using Dapper;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage.Services;

public sealed class WorkIdentityReconciliationService
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

    public Task<int> MergeDuplicateReadWorksByQidAsync(CancellationToken ct = default)
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
                  AND w.media_type IN ('Books', 'Audiobooks', 'Comics')
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
                merged += MergeWorkInto(conn, source.WorkId, target.WorkId, target.IdentityQid);
            }
        }

        if (merged > 0)
        {
            _logger?.LogInformation(
                "Merged {Count} duplicate Read work(s) by media type and Wikidata QID.",
                merged);
        }

        return Task.FromResult(merged);
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

    private static int MergeWorkInto(System.Data.IDbConnection conn, Guid sourceWorkId, Guid targetWorkId, string qid)
    {
        using var tx = conn.BeginTransaction();
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

        tx.Commit();
        return 1;
    }

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
}
