using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface IReviewQueueReadService
{
    Task<IReadOnlyList<ReviewItemDto>> GetPendingAsync(int limit, CancellationToken ct = default);

    Task<ReviewItemDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<int> GetPendingCountAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReviewReasonCount>> GetPendingReasonCountsAsync(CancellationToken ct = default);
}

public sealed record ReviewReasonCount(string? Trigger, string? Detail, int Count);

public sealed class ReviewQueueReadService : IReviewQueueReadService
{
    private static readonly string[] BridgeIdentifierKeys =
    [
        "isbn",
        "isbn_13",
        "isbn_10",
        "asin",
        "audible_id",
        "apple_books_id",
        "tmdb_id",
        "imdb_id",
        "tvdb_id",
        "musicbrainz_release_group_id",
        "musicbrainz_release_id",
        "musicbrainz_recording_id",
        "wikidata_qid",
    ];

    private readonly IDatabaseConnection _db;

    public ReviewQueueReadService(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<IReadOnlyList<ReviewItemDto>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ReviewDisplayRow>("""
            WITH pending AS (
                SELECT
                    rq.id,
                    rq.entity_id,
                    rq.entity_type,
                    rq.trigger,
                    rq.status,
                    rq.proposed_collection_id,
                    rq.confidence_score,
                    rq.candidates_json,
                    rq.detail,
                    rq.created_at,
                    rq.resolved_at,
                    rq.resolved_by,
                    rq.review_ready_at,
                    rq.automation_completed_at,
                    CASE
                        WHEN rq.entity_type = 'MediaAsset' THEN rq.entity_id
                        WHEN rq.entity_type = 'Work' THEN (
                            SELECT ma.id
                            FROM editions e
                            JOIN media_assets ma ON ma.edition_id = e.id
                            WHERE e.work_id = rq.entity_id
                            ORDER BY ma.file_path_root
                            LIMIT 1
                        )
                        ELSE NULL
                    END AS asset_id,
                    CASE
                        WHEN rq.entity_type = 'Work' THEN rq.entity_id
                        WHEN rq.entity_type = 'MediaAsset' THEN (
                            SELECT e.work_id
                            FROM media_assets ma
                            JOIN editions e ON e.id = ma.edition_id
                            WHERE ma.id = rq.entity_id
                            LIMIT 1
                        )
                        ELSE NULL
                    END AS work_id
                FROM review_queue rq
                WHERE rq.status = @status
                  AND rq.review_ready_at IS NOT NULL
            )
            SELECT
                p.id AS Id,
                p.entity_id AS EntityId,
                p.entity_type AS EntityType,
                p.trigger AS Trigger,
                p.status AS Status,
                p.proposed_collection_id AS ProposedCollectionId,
                p.confidence_score AS ConfidenceScore,
                p.candidates_json AS CandidatesJson,
                p.detail AS Detail,
                p.created_at AS CreatedAt,
                p.resolved_at AS ResolvedAt,
                p.resolved_by AS ResolvedBy,
                p.review_ready_at AS ReviewReadyAt,
                p.automation_completed_at AS AutomationCompletedAt,
                p.asset_id AS AssetId,
                p.work_id AS WorkId,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = p.work_id AND cv.key IN ('title', 'episode_title') AND cv.value <> '' ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = p.asset_id AND cv.key IN ('title', 'episode_title', 'file_name') AND cv.value <> '' ORDER BY CASE cv.key WHEN 'title' THEN 0 WHEN 'episode_title' THEN 1 ELSE 2 END LIMIT 1),
                    p.detail,
                    ma.file_path_root
                ) AS EntityTitle,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = p.asset_id AND cv.key IN ('media_type', 'media_type_detected') AND cv.value <> '' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = p.work_id AND cv.key IN ('media_type', 'media_type_detected') AND cv.value <> '' LIMIT 1),
                    w.media_type
                ) AS MediaType,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = p.asset_id AND cv.key IN ('cover_url', 'cover', 'poster_url') AND cv.value <> '' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = p.work_id AND cv.key IN ('cover_url', 'cover', 'poster_url') AND cv.value <> '' LIMIT 1)
                ) AS CoverUrl
            FROM pending p
            LEFT JOIN media_assets ma ON ma.id = p.asset_id
            LEFT JOIN works w ON w.id = p.work_id
            WHERE (p.entity_type = 'MediaAsset' AND ma.id IS NOT NULL)
               OR (p.entity_type = 'Work' AND w.id IS NOT NULL)
               OR (p.entity_type NOT IN ('MediaAsset', 'Work'))
            ORDER BY p.created_at DESC
            LIMIT @limit;
            """, new { status = ReviewStatus.Pending, limit })).AsList();

        var result = new List<ReviewItemDto>(rows.Count);
        foreach (var row in rows)
        {
            var entry = ToEntry(row);
            var bridgeIds = await ReadBridgeIdentifiersAsync(conn, row, ct).ConfigureAwait(false);
            result.Add(ReviewItemDto.FromDomain(entry, row.MediaType, row.EntityTitle, row.CoverUrl, bridgeIds));
        }

        return result;
    }

    public async Task<ReviewItemDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<ReviewDisplayRow>("""
            WITH item AS (
                SELECT
                    rq.id,
                    rq.entity_id,
                    rq.entity_type,
                    rq.trigger,
                    rq.status,
                    rq.proposed_collection_id,
                    rq.confidence_score,
                    rq.candidates_json,
                    rq.detail,
                    rq.created_at,
                    rq.resolved_at,
                    rq.resolved_by,
                    rq.review_ready_at,
                    rq.automation_completed_at,
                    CASE
                        WHEN rq.entity_type = 'MediaAsset' THEN rq.entity_id
                        WHEN rq.entity_type = 'Work' THEN (
                            SELECT ma.id
                            FROM editions e
                            JOIN media_assets ma ON ma.edition_id = e.id
                            WHERE e.work_id = rq.entity_id
                            ORDER BY ma.file_path_root
                            LIMIT 1
                        )
                        ELSE NULL
                    END AS asset_id,
                    CASE
                        WHEN rq.entity_type = 'Work' THEN rq.entity_id
                        WHEN rq.entity_type = 'MediaAsset' THEN (
                            SELECT e.work_id
                            FROM media_assets ma
                            JOIN editions e ON e.id = ma.edition_id
                            WHERE ma.id = rq.entity_id
                            LIMIT 1
                        )
                        ELSE NULL
                    END AS work_id
                FROM review_queue rq
                WHERE rq.id = @id
            )
            SELECT
                i.id AS Id,
                i.entity_id AS EntityId,
                i.entity_type AS EntityType,
                i.trigger AS Trigger,
                i.status AS Status,
                i.proposed_collection_id AS ProposedCollectionId,
                i.confidence_score AS ConfidenceScore,
                i.candidates_json AS CandidatesJson,
                i.detail AS Detail,
                i.created_at AS CreatedAt,
                i.resolved_at AS ResolvedAt,
                i.resolved_by AS ResolvedBy,
                i.review_ready_at AS ReviewReadyAt,
                i.automation_completed_at AS AutomationCompletedAt,
                i.asset_id AS AssetId,
                i.work_id AS WorkId,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = i.work_id AND cv.key IN ('title', 'episode_title') AND cv.value <> '' ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = i.asset_id AND cv.key IN ('title', 'episode_title', 'file_name') AND cv.value <> '' ORDER BY CASE cv.key WHEN 'title' THEN 0 WHEN 'episode_title' THEN 1 ELSE 2 END LIMIT 1),
                    i.detail,
                    ma.file_path_root
                ) AS EntityTitle,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = i.asset_id AND cv.key IN ('media_type', 'media_type_detected') AND cv.value <> '' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = i.work_id AND cv.key IN ('media_type', 'media_type_detected') AND cv.value <> '' LIMIT 1),
                    w.media_type
                ) AS MediaType,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = i.asset_id AND cv.key IN ('cover_url', 'cover', 'poster_url') AND cv.value <> '' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = i.work_id AND cv.key IN ('cover_url', 'cover', 'poster_url') AND cv.value <> '' LIMIT 1)
                ) AS CoverUrl
            FROM item i
            LEFT JOIN media_assets ma ON ma.id = i.asset_id
            LEFT JOIN works w ON w.id = i.work_id
            WHERE (i.entity_type = 'MediaAsset' AND ma.id IS NOT NULL)
               OR (i.entity_type = 'Work' AND w.id IS NOT NULL)
               OR (i.entity_type NOT IN ('MediaAsset', 'Work'));
            """, new { id });

        if (row is null)
            return null;

        var entry = ToEntry(row);
        var bridgeIds = await ReadBridgeIdentifiersAsync(conn, row, ct).ConfigureAwait(false);
        return ReviewItemDto.FromDomain(entry, row.MediaType, row.EntityTitle, row.CoverUrl, bridgeIds);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(DISTINCT rq.id)
            FROM review_queue rq
            WHERE rq.status = @status
              AND rq.review_ready_at IS NOT NULL
              AND (
                    (rq.entity_type = 'MediaAsset' AND EXISTS (
                        SELECT 1 FROM media_assets ma WHERE ma.id = rq.entity_id
                    ))
                 OR (rq.entity_type = 'Work' AND EXISTS (
                        SELECT 1 FROM works w WHERE w.id = rq.entity_id
                    ))
                 OR (rq.entity_type NOT IN ('MediaAsset', 'Work'))
              );
            """, new { status = ReviewStatus.Pending });
    }

    public async Task<IReadOnlyList<ReviewReasonCount>> GetPendingReasonCountsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<ReviewReasonCount>("""
            SELECT rq.trigger AS Trigger, rq.detail AS Detail, COUNT(DISTINCT rq.id) AS Count
            FROM review_queue rq
            WHERE rq.status = @status
              AND rq.review_ready_at IS NOT NULL
              AND (
                    (rq.entity_type = 'MediaAsset' AND EXISTS (
                        SELECT 1 FROM media_assets ma WHERE ma.id = rq.entity_id
                    ))
                 OR (rq.entity_type = 'Work' AND EXISTS (
                        SELECT 1 FROM works w WHERE w.id = rq.entity_id
                    ))
                 OR (rq.entity_type NOT IN ('MediaAsset', 'Work'))
              )
            GROUP BY rq.trigger, rq.detail;
            """, new { status = ReviewStatus.Pending })).AsList();
    }

    private static ReviewQueueEntry ToEntry(ReviewDisplayRow row) => new()
    {
        Id = row.Id,
        EntityId = row.EntityId,
        EntityType = row.EntityType,
        Trigger = row.Trigger,
        Status = row.Status,
        ProposedCollectionId = row.ProposedCollectionId,
        ConfidenceScore = row.ConfidenceScore,
        CandidatesJson = row.CandidatesJson,
        Detail = row.Detail,
        CreatedAt = ParseDate(row.CreatedAt) ?? DateTimeOffset.UtcNow,
        ResolvedAt = ParseDate(row.ResolvedAt),
        ResolvedBy = row.ResolvedBy,
        ReviewReadyAt = ParseDate(row.ReviewReadyAt),
        AutomationCompletedAt = ParseDate(row.AutomationCompletedAt),
    };

    private static async Task<Dictionary<string, string>> ReadBridgeIdentifiersAsync(
        System.Data.IDbConnection conn,
        ReviewDisplayRow row,
        CancellationToken ct)
    {
        var entityIds = new[] { row.EntityId, row.AssetId, row.WorkId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        if (entityIds.Length == 0)
            return [];

        var command = new CommandDefinition("""
            SELECT key AS Key, value AS Value
            FROM canonical_values
            WHERE entity_id IN @entityIds
              AND key IN @keys
              AND value IS NOT NULL
              AND value <> ''
            ORDER BY
                CASE key
                    WHEN 'wikidata_qid' THEN 0
                    WHEN 'isbn_13' THEN 1
                    WHEN 'isbn' THEN 2
                    ELSE 3
                END;
            """, new { entityIds = entityIds.Select(GuidSql.ToBlob).ToArray(), keys = BridgeIdentifierKeys }, cancellationToken: ct);

        var rows = await conn.QueryAsync<KeyValueRow>(command).ConfigureAwait(false);
        return rows
            .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class ReviewDisplayRow
    {
        public Guid Id { get; init; }
        public Guid EntityId { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public string Trigger { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? ProposedCollectionId { get; init; }
        public double? ConfidenceScore { get; init; }
        public string? CandidatesJson { get; init; }
        public string? Detail { get; init; }
        public string? CreatedAt { get; init; }
        public string? ResolvedAt { get; init; }
        public string? ResolvedBy { get; init; }
        public string? ReviewReadyAt { get; init; }
        public string? AutomationCompletedAt { get; init; }
        public Guid? AssetId { get; init; }
        public Guid? WorkId { get; init; }
        public string? MediaType { get; init; }
        public string? EntityTitle { get; init; }
        public string? CoverUrl { get; init; }
    }

    private sealed record KeyValueRow(string Key, string Value);
}
