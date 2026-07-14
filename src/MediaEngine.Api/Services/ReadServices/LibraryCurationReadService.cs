using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface ILibraryCurationReadService
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, Guid>>> ResolveBatchEditTargetsAsync(
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<string> fieldKeys,
        CancellationToken ct = default);

    Task<IReadOnlyList<UniverseCandidateDto>> GetUniverseCandidatesAsync(CancellationToken ct = default);
    Task<Guid?> FindOwnedAssetIdForWorkAsync(Guid workId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, string>> GetBestUniverseCandidateQidsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default);
    Task<IReadOnlyList<UnlinkedWorkDto>> GetUniverseUnlinkedAsync(CancellationToken ct = default);
}

/// <summary>
/// Typed read boundary for library curation. It owns GUID-BLOB SQL shape and
/// batches selected items so endpoint handlers do not perform N+1 lookups.
/// </summary>
public sealed class LibraryCurationReadService(IDatabaseConnection db) : ILibraryCurationReadService
{
    private const int MaxParametersPerQuery = 400;

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, Guid>>> ResolveBatchEditTargetsAsync(
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<string> fieldKeys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        ArgumentNullException.ThrowIfNull(fieldKeys);
        ct.ThrowIfCancellationRequested();

        var ids = entityIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var keys = fieldKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0 || keys.Length == 0)
            return new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>();

        using var conn = db.CreateConnection();
        var rows = new List<BatchEditLineageRow>();
        foreach (var batch in ids.Chunk(MaxParametersPerQuery))
        {
            ct.ThrowIfCancellationRequested();
            var parameters = new DynamicParameters();
            var values = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                var name = $"entityId{i}";
                values[i] = $"(@{name})";
                parameters.Add(name, GuidSql.ToBlob(batch[i]));
            }

            rows.AddRange(await conn.QueryAsync<BatchEditLineageRow>(new CommandDefinition(
                $"""
                WITH RECURSIVE
                requested(input_id) AS (
                    VALUES {string.Join(", ", values)}
                ),
                requested_work AS (
                    SELECT r.input_id AS RequestedEntityId, w.id AS WorkId,
                           w.media_type AS MediaType
                    FROM requested r
                    JOIN works w ON w.id = r.input_id
                    UNION
                    SELECT r.input_id AS RequestedEntityId, w.id AS WorkId,
                           w.media_type AS MediaType
                    FROM requested r
                    JOIN media_assets ma ON ma.id = r.input_id
                    JOIN editions e ON e.id = ma.edition_id
                    JOIN works w ON w.id = e.work_id
                ),
                descendants(RequestedEntityId, WorkId, CandidateWorkId, Depth) AS (
                    SELECT RequestedEntityId, WorkId, WorkId, 0
                    FROM requested_work
                    UNION ALL
                    SELECT d.RequestedEntityId, d.WorkId, child.id, d.Depth + 1
                    FROM descendants d
                    JOIN works child ON child.parent_work_id = d.CandidateWorkId
                ),
                ranked AS (
                    SELECT rw.RequestedEntityId,
                           rw.WorkId,
                           ma.id AS AssetId,
                           rw.MediaType,
                           COALESCE(gp.id, p.id, rw.WorkId) AS RootParentWorkId,
                           ROW_NUMBER() OVER (
                               PARTITION BY rw.RequestedEntityId
                               ORDER BY d.Depth, ma.id
                           ) AS AssetRank
                    FROM requested_work rw
                    LEFT JOIN works target ON target.id = rw.WorkId
                    LEFT JOIN works p ON p.id = target.parent_work_id
                    LEFT JOIN works gp ON gp.id = p.parent_work_id
                    JOIN descendants d
                      ON d.RequestedEntityId = rw.RequestedEntityId
                     AND d.WorkId = rw.WorkId
                    JOIN editions e ON e.work_id = d.CandidateWorkId
                    JOIN media_assets ma ON ma.edition_id = e.id
                )
                SELECT RequestedEntityId, WorkId, AssetId, MediaType, RootParentWorkId
                FROM ranked
                WHERE AssetRank = 1;
                """,
                parameters,
                cancellationToken: ct)).ConfigureAwait(false));
        }

        var result = new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>();
        foreach (var row in rows)
        {
            var mediaType = Enum.TryParse<MediaType>(row.MediaType, ignoreCase: true, out var parsed)
                ? parsed
                : MediaType.Unknown;
            result[row.RequestedEntityId] = keys.ToDictionary(
                key => key,
                key => ClaimScopeCatalog.IsParentScoped(key, mediaType)
                    ? row.RootParentWorkId
                    : row.AssetId,
                StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    public async Task<IReadOnlyList<UniverseCandidateDto>> GetUniverseCandidatesAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<UniverseCandidateDto>(new CommandDefinition(
            """
            SELECT DISTINCT
                w.id AS WorkId,
                ma.id AS EntityId,
                COALESCE(cv_title.value, 'Unknown') AS Title,
                COALESCE(cv_mt.value, '') AS MediaType,
                COALESCE(cv_sq.value, cv_fq.value, cv_uq.value) AS CandidateQid,
                CASE
                    WHEN cv_sq.value IS NOT NULL THEN 'series'
                    WHEN cv_fq.value IS NOT NULL THEN 'franchise'
                    ELSE 'universe'
                END AS CandidateType,
                COALESCE(cv_sq.value, cv_fq.value, cv_uq.value) AS CandidateLabel
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values cv_title ON cv_title.entity_id = ma.id AND cv_title.key = 'title'
            LEFT JOIN canonical_values cv_mt ON cv_mt.entity_id = ma.id AND cv_mt.key = 'media_type'
            LEFT JOIN canonical_values cv_sq ON cv_sq.entity_id = ma.id AND cv_sq.key = 'series_qid'
                AND cv_sq.value IS NOT NULL AND cv_sq.value != ''
            LEFT JOIN canonical_values cv_fq ON cv_fq.entity_id = ma.id AND cv_fq.key = 'franchise_qid'
                AND cv_fq.value IS NOT NULL AND cv_fq.value != ''
            LEFT JOIN canonical_values cv_uq ON cv_uq.entity_id = ma.id AND cv_uq.key = 'fictional_universe_qid'
                AND cv_uq.value IS NOT NULL AND cv_uq.value != ''
            LEFT JOIN canonical_values cv_review ON cv_review.entity_id = ma.id AND cv_review.key = 'universe_review_status'
            WHERE w.collection_id IS NULL
              AND (cv_sq.value IS NOT NULL OR cv_fq.value IS NOT NULL OR cv_uq.value IS NOT NULL)
              AND (cv_review.value IS NULL OR cv_review.value != 'rejected')
            ORDER BY cv_title.value
            LIMIT 200;
            """,
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<Guid?> FindOwnedAssetIdForWorkAsync(
        Guid workId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT ma.id
            FROM media_assets ma
            INNER JOIN editions e ON e.id = ma.edition_id
            WHERE e.work_id = @workId
            ORDER BY ma.id
            LIMIT 1;
            """,
            new { workId = GuidSql.ToBlob(workId) },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetBestUniverseCandidateQidsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workIds);
        ct.ThrowIfCancellationRequested();
        var ids = workIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, string>();

        using var conn = db.CreateConnection();
        var rows = new List<UniverseCandidateQidRow>();
        foreach (var batch in ids.Chunk(MaxParametersPerQuery))
        {
            ct.ThrowIfCancellationRequested();
            rows.AddRange(await conn.QueryAsync<UniverseCandidateQidRow>(new CommandDefinition(
                """
                SELECT w.id AS WorkId,
                       ma.id AS AssetId,
                       COALESCE(cv_sq.value, cv_fq.value, cv_uq.value) AS CandidateQid
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN canonical_values cv_sq ON cv_sq.entity_id = ma.id AND cv_sq.key = 'series_qid'
                    AND cv_sq.value IS NOT NULL AND cv_sq.value != ''
                LEFT JOIN canonical_values cv_fq ON cv_fq.entity_id = ma.id AND cv_fq.key = 'franchise_qid'
                    AND cv_fq.value IS NOT NULL AND cv_fq.value != ''
                LEFT JOIN canonical_values cv_uq ON cv_uq.entity_id = ma.id AND cv_uq.key = 'fictional_universe_qid'
                    AND cv_uq.value IS NOT NULL AND cv_uq.value != ''
                WHERE w.id IN @workIds
                  AND (cv_sq.value IS NOT NULL OR cv_fq.value IS NOT NULL OR cv_uq.value IS NOT NULL)
                ORDER BY w.id, ma.id;
                """,
                new { workIds = batch.Select(GuidSql.ToBlob).ToArray() },
                cancellationToken: ct)).ConfigureAwait(false));
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.CandidateQid))
            .GroupBy(row => row.WorkId)
            .ToDictionary(
                group => group.Key,
                group => NormalizeQid(group.First().CandidateQid));
    }

    public async Task<IReadOnlyList<UnlinkedWorkDto>> GetUniverseUnlinkedAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<UnlinkedWorkDto>(new CommandDefinition(
            """
            SELECT DISTINCT
                w.id AS WorkId,
                ma.id AS EntityId,
                COALESCE(cv_title.value, 'Unknown') AS Title,
                COALESCE(cv_mt.value, '') AS MediaType,
                cv_qid.value AS WikidataQid
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            INNER JOIN canonical_values cv_qid ON cv_qid.entity_id = ma.id
                AND cv_qid.key = 'wikidata_qid'
                AND cv_qid.value IS NOT NULL AND cv_qid.value != ''
                AND cv_qid.value NOT LIKE 'NF%'
            LEFT JOIN canonical_values cv_title ON cv_title.entity_id = ma.id AND cv_title.key = 'title'
            LEFT JOIN canonical_values cv_mt ON cv_mt.entity_id = ma.id AND cv_mt.key = 'media_type'
            LEFT JOIN canonical_values cv_sq ON cv_sq.entity_id = ma.id AND cv_sq.key = 'series_qid'
                AND cv_sq.value IS NOT NULL AND cv_sq.value != ''
            LEFT JOIN canonical_values cv_fq ON cv_fq.entity_id = ma.id AND cv_fq.key = 'franchise_qid'
                AND cv_fq.value IS NOT NULL AND cv_fq.value != ''
            LEFT JOIN canonical_values cv_uq ON cv_uq.entity_id = ma.id AND cv_uq.key = 'fictional_universe_qid'
                AND cv_uq.value IS NOT NULL AND cv_uq.value != ''
            LEFT JOIN canonical_values cv_review ON cv_review.entity_id = ma.id AND cv_review.key = 'universe_review_status'
            WHERE w.collection_id IS NULL
              AND cv_sq.value IS NULL AND cv_fq.value IS NULL AND cv_uq.value IS NULL
              AND (cv_review.value IS NULL OR cv_review.value != 'rejected')
            ORDER BY cv_title.value
            LIMIT 200;
            """,
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    private static string NormalizeQid(string value)
    {
        var qid = value;
        if (qid.Contains('/'))
            qid = qid.Split('/').Last();
        if (qid.Contains("::", StringComparison.Ordinal))
            qid = qid.Split("::", StringSplitOptions.None)[0];
        return qid;
    }

    private sealed class BatchEditLineageRow
    {
        public Guid RequestedEntityId { get; init; }
        public Guid WorkId { get; init; }
        public Guid AssetId { get; init; }
        public string MediaType { get; init; } = string.Empty;
        public Guid RootParentWorkId { get; init; }
    }

    private sealed class UniverseCandidateQidRow
    {
        public Guid WorkId { get; init; }
        public Guid AssetId { get; init; }
        public string CandidateQid { get; init; } = string.Empty;
    }
}
