using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage.Services;

public sealed class WorkIdentityReconciliationService : IWorkIdentityReconciliationService
{
    private readonly IDatabaseConnection _db;
    private readonly ILogger<WorkIdentityReconciliationService>? _logger;

    public WorkIdentityReconciliationService(
        IDatabaseConnection db,
        ILogger<WorkIdentityReconciliationService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> MergeDuplicateReadWorksByQidAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<ReadWorkIdentityRow>("""
            WITH work_assets AS (
                SELECT
                    w.id AS WorkId,
                    w.collection_id AS CollectionId,
                    w.media_type AS MediaType,
                    w.work_kind AS WorkKind,
                    w.parent_work_id AS ParentWorkId,
                    w.ordinal AS Ordinal,
                    COALESCE(
                        NULLIF(TRIM(w.wikidata_qid), ''),
                        NULLIF(TRIM((SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'wikidata_qid' LIMIT 1)), ''),
                        NULLIF(TRIM((SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'wikidata_qid' LIMIT 1)), '')
                    ) AS IdentityQid,
                    MIN(mc.claimed_at) AS CreatedAt,
                    COUNT(DISTINCT ma.id) AS AssetCount
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN metadata_claims mc ON mc.entity_id = ma.id
                WHERE w.work_kind IN ('standalone', 'child')
                  AND w.media_type IN ('Books', 'Audiobooks')
                GROUP BY w.id
            )
            SELECT WorkId, CollectionId, MediaType, WorkKind, ParentWorkId, Ordinal,
                   IdentityQid, CreatedAt, AssetCount
            FROM work_assets
            WHERE IdentityQid IS NOT NULL
              AND IdentityQid <> ''
              AND IdentityQid NOT LIKE 'NF%';
            """).AsList();

        var groups = rows
            .GroupBy(row => (MediaType: NormalizeMediaType(row.MediaType), Qid: row.IdentityQid.ToUpperInvariant()))
            .Where(group => group.Count() > 1)
            .ToList();

        var merged = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var siblings = group.ToList();
            var target = ChooseCanonical(siblings);
            foreach (var source in siblings.Where(row => row.WorkId != target.WorkId))
            {
                // Each merge runs through its own ExecuteWriteAsync call so the
                // (non-reentrant) global write lock is acquired and released once per
                // merge, sequentially. This method's own connection (`conn`, above) is
                // read-only and holds no lock, so there is no nesting/deadlock risk here.
                merged += await MergeWorkIntoAsync(source.WorkId, target.WorkId, target.IdentityQid, ct)
                    .ConfigureAwait(false);
            }
        }

        if (merged > 0)
        {
            _logger?.LogInformation(
                "Merged {Count} duplicate Read work(s) by media type and Wikidata QID.",
                merged);
        }

        return merged;
    }

    public async Task<int> AlignAudiobookAuthorsWithBooksByQidAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var variants = conn.Query<ReadVariantAuthorScopeRow>(
            """
            SELECT DISTINCT
                   w.media_type AS MediaType,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   w.id AS WorkId,
                   ma.id AS AssetId,
                   COALESCE(
                       NULLIF(TRIM(w.wikidata_qid), ''),
                       NULLIF(TRIM((SELECT value
                                   FROM canonical_values
                                   WHERE entity_id = w.id
                                     AND key = 'wikidata_qid'
                                   LIMIT 1)), ''),
                       NULLIF(TRIM((SELECT value
                                   FROM canonical_values
                                   WHERE entity_id = ma.id
                                     AND key = 'wikidata_qid'
                                   LIMIT 1)), '')
                   ) AS IdentityQid
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.work_kind IN ('standalone', 'child')
              AND w.media_type IN ('Books', 'Audiobooks')
              AND w.is_catalog_only = 0
              AND ma.file_path_root IS NOT NULL
              AND TRIM(ma.file_path_root) <> ''
              AND ma.status <> 'Orphaned';
            """).AsList();

        var crossFormatGroups = variants
            .Where(row => !string.IsNullOrWhiteSpace(row.IdentityQid)
                          && !row.IdentityQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.IdentityQid, StringComparer.OrdinalIgnoreCase)
            .Where(group =>
                group.Any(row => string.Equals(row.MediaType, "Books", StringComparison.OrdinalIgnoreCase))
                && group.Any(row => string.Equals(row.MediaType, "Audiobooks", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (crossFormatGroups.Count == 0)
            return 0;

        var bookAuthorScopeIds = crossFormatGroups
            .SelectMany(group => group)
            .Where(row => string.Equals(row.MediaType, "Books", StringComparison.OrdinalIgnoreCase))
            .SelectMany(row => new[] { row.RootWorkId, row.WorkId, row.AssetId })
            .Distinct()
            .ToList();
        var authorRows = conn.Query<CanonicalAuthorRow>(
            """
            SELECT entity_id AS EntityId,
                   ordinal AS Ordinal,
                   value AS Value,
                   value_qid AS ValueQid
            FROM canonical_value_arrays
            WHERE entity_id IN @BookAuthorScopeIds
              AND key = 'author'
              AND NULLIF(TRIM(value), '') IS NOT NULL
              AND NULLIF(TRIM(value_qid), '') IS NOT NULL
            ORDER BY entity_id, ordinal;
            """,
            new { BookAuthorScopeIds = bookAuthorScopeIds.Select(GuidSql.ToBlob).ToArray() }).AsList();
        var authorsByEntity = authorRows
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var desiredByAudiobookScope = new Dictionary<Guid, List<CanonicalAuthorRow>>();
        foreach (var group in crossFormatGroups)
        {
            var sourceAuthors = group
                .Where(row => string.Equals(row.MediaType, "Books", StringComparison.OrdinalIgnoreCase))
                .SelectMany(row => PreferredAuthorRows(row, authorsByEntity))
                .GroupBy(row => row.ValueQid ?? row.Value, StringComparer.OrdinalIgnoreCase)
                .Select(authorGroup => authorGroup.First())
                .ToList();
            if (sourceAuthors.Count == 0)
                continue;

            foreach (var audiobook in group
                         .Where(row => string.Equals(row.MediaType, "Audiobooks", StringComparison.OrdinalIgnoreCase))
                         .DistinctBy(row => row.WorkId))
            {
                AddDesiredAuthors(audiobook.WorkId, sourceAuthors);
                AddDesiredAuthors(audiobook.RootWorkId, sourceAuthors);
            }

            void AddDesiredAuthors(Guid targetId, IReadOnlyList<CanonicalAuthorRow> authors)
            {
                if (!desiredByAudiobookScope.TryGetValue(targetId, out var desired))
                {
                    desired = [];
                    desiredByAudiobookScope[targetId] = desired;
                }

                foreach (var author in authors)
                {
                    if (!desired.Any(existing =>
                            string.Equals(
                                existing.ValueQid ?? existing.Value,
                                author.ValueQid ?? author.Value,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        desired.Add(author);
                    }
                }
            }
        }

        if (desiredByAudiobookScope.Count == 0)
            return 0;

        var targetIds = desiredByAudiobookScope.Keys.ToList();
        var currentRows = conn.Query<CanonicalAuthorRow>(
            """
            SELECT entity_id AS EntityId,
                   ordinal AS Ordinal,
                   value AS Value,
                   value_qid AS ValueQid
            FROM canonical_value_arrays
            WHERE entity_id IN @TargetIds
              AND key = 'author'
            ORDER BY entity_id, ordinal;
            """,
            new { TargetIds = targetIds.Select(GuidSql.ToBlob).ToArray() }).AsList();
        var currentByTarget = currentRows
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var changes = desiredByAudiobookScope
            .Where(pair => !AuthorRowsEqual(
                currentByTarget.GetValueOrDefault(pair.Key) ?? [],
                pair.Value))
            .ToList();
        if (changes.Count == 0)
            return 0;

        await _db.ExecuteWriteAsync((writeConnection, transaction, innerCt) =>
        {
            foreach (var (targetId, authors) in changes)
            {
                innerCt.ThrowIfCancellationRequested();
                writeConnection.Execute(
                    """
                    DELETE FROM canonical_value_arrays
                    WHERE entity_id = @TargetId
                      AND key = 'author';
                    """,
                    new { TargetId = targetId },
                    transaction);

                writeConnection.Execute(
                    """
                    INSERT INTO canonical_value_arrays
                        (entity_id, key, ordinal, value, value_qid)
                    VALUES
                        (@EntityId, 'author', @Ordinal, @Value, @ValueQid);
                    """,
                    authors.Select((author, ordinal) => new
                    {
                        EntityId = targetId,
                        Ordinal = ordinal,
                        author.Value,
                        author.ValueQid,
                    }),
                    transaction);
            }

            return changes.Count;
        }, ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "Aligned canonical book author identities onto {Count} audiobook work scope(s).",
            changes.Count);
        return changes.Count;
    }

    private static bool AuthorRowsEqual(
        IReadOnlyList<CanonicalAuthorRow> current,
        IReadOnlyList<CanonicalAuthorRow> desired)
    {
        if (current.Count != desired.Count)
            return false;

        return current
            .OrderBy(row => row.Ordinal)
            .Zip(desired, (left, right) =>
                string.Equals(left.Value, right.Value, StringComparison.Ordinal)
                && string.Equals(left.ValueQid, right.ValueQid, StringComparison.OrdinalIgnoreCase))
            .All(matches => matches);
    }

    private static IReadOnlyList<CanonicalAuthorRow> PreferredAuthorRows(
        ReadVariantAuthorScopeRow row,
        IReadOnlyDictionary<Guid, List<CanonicalAuthorRow>> authorsByEntity)
    {
        foreach (var entityId in new[] { row.RootWorkId, row.WorkId, row.AssetId }.Distinct())
        {
            if (authorsByEntity.TryGetValue(entityId, out var authors) && authors.Count > 0)
                return authors;
        }

        return [];
    }

    private static string NormalizeMediaType(string mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
            ? string.Empty
            : mediaType.Trim().ToUpperInvariant();

    private static ReadWorkIdentityRow ChooseCanonical(IReadOnlyList<ReadWorkIdentityRow> rows) =>
        rows
            .OrderBy(row => string.Equals(row.WorkKind, "child", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(row => row.CollectionId.HasValue ? 0 : 1)
            .ThenBy(row => string.Equals(row.MediaType, "Audiobooks", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(row => row.AssetCount)
            .ThenBy(row => row.CreatedAt ?? string.Empty, StringComparer.Ordinal)
            .First();

    private Task<int> MergeWorkIntoAsync(Guid sourceWorkId, Guid targetWorkId, string qid, CancellationToken ct) =>
        _db.ExecuteWriteAsync((conn, tx, innerCt) =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            var args = new
            {
                source = sourceWorkId,
                target = targetWorkId,
                qid,
                now,
            };

            conn.Execute("""
                UPDATE works
                SET wikidata_qid = COALESCE(NULLIF(TRIM(wikidata_qid), ''), @qid)
                WHERE id = @target;

                UPDATE editions
                SET work_id = @target
                WHERE work_id = @source;

                INSERT OR IGNORE INTO canonical_values
                    (entity_id, key, value, last_scored_at, is_conflicted,
                     winning_provider_id, needs_review)
                SELECT @target, key, value, last_scored_at, is_conflicted,
                       winning_provider_id, needs_review
                FROM canonical_values
                WHERE entity_id = @source;

                DELETE FROM canonical_values
                WHERE entity_id = @source;

                INSERT OR IGNORE INTO canonical_value_arrays
                    (entity_id, key, ordinal, value, value_qid)
                SELECT @target, key, ordinal, value, value_qid
                FROM canonical_value_arrays
                WHERE entity_id = @source;

                DELETE FROM canonical_value_arrays
                WHERE entity_id = @source;

                UPDATE metadata_claims
                SET entity_id = @target
                WHERE entity_id = @source;

                UPDATE OR IGNORE bridge_ids
                SET entity_id = @target
                WHERE entity_id = @source;

                DELETE FROM bridge_ids
                WHERE entity_id = @source;

                UPDATE entity_assets
                SET entity_id = @target
                WHERE entity_type = 'Work'
                  AND entity_id = @source;

                UPDATE collection_items
                SET work_id = @target
                WHERE work_id = @source;

                UPDATE series_manifest_items
                SET linked_work_id = @target
                WHERE linked_work_id = @source;

                UPDATE review_queue
                SET status = 'Resolved',
                    resolved_at = @now,
                    resolved_by = 'system:work-identity-merge'
                WHERE entity_type = 'Work'
                  AND entity_id = @source
                  AND status = 'Pending';

                UPDATE review_queue
                SET entity_id = @target
                WHERE entity_type = 'Work'
                  AND entity_id = @source;

                DELETE FROM works
                WHERE id = @source
                  AND NOT EXISTS (
                      SELECT 1 FROM editions e WHERE e.work_id = @source
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM works child WHERE child.parent_work_id = @source
                  );
                """, args, tx);

            return 1;
        }, ct);

    private sealed class ReadWorkIdentityRow
    {
        public Guid WorkId { get; set; }
        public Guid? CollectionId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string WorkKind { get; set; } = string.Empty;
        public Guid? ParentWorkId { get; set; }
        public int? Ordinal { get; set; }
        public string IdentityQid { get; set; } = string.Empty;
        public string? CreatedAt { get; set; }
        public int AssetCount { get; set; }
    }

    private sealed class ReadVariantAuthorScopeRow
    {
        public string MediaType { get; set; } = string.Empty;
        public Guid RootWorkId { get; set; }
        public Guid WorkId { get; set; }
        public Guid AssetId { get; set; }
        public string IdentityQid { get; set; } = string.Empty;
    }

    private sealed class CanonicalAuthorRow
    {
        public Guid EntityId { get; set; }
        public int Ordinal { get; set; }
        public string Value { get; set; } = string.Empty;
        public string? ValueQid { get; set; }
    }
}
