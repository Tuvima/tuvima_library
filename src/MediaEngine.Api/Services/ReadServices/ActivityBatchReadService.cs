using System.Text;
using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Contracts.Paging;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface IActivityBatchReadService
{
    Task<PagedResponse<ActivityBatchSummaryDto>> GetBatchesAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<ActivityMediaTypeGroupDto>> GetGroupsAsync(
        Guid batchId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityBatchItemDto>> GetItemsAsync(
        Guid batchId,
        string? mediaType,
        int offset,
        int limit,
        string? sort,
        string? sortDirection,
        CancellationToken ct = default);

    Task<ActivityBatchItemDetailDto?> GetItemDetailAsync(
        Guid batchId,
        Guid assetId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityPersonAuditDto>> GetPeopleAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default);
}

public sealed record ActivityBatchQuery(
    string? Search,
    string? MediaType,
    string? Status,
    string? Source,
    string? EventType,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    int Offset,
    int Limit,
    string? Sort = null,
    string? SortDirection = null);

public sealed class ActivityBatchReadService : IActivityBatchReadService
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;
    private const string ReviewGroupMediaType = "Needs Review";
    private readonly IDatabaseConnection _db;

    public ActivityBatchReadService(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<PagedResponse<ActivityBatchSummaryDto>> GetBatchesAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var page = PagedRequest.From(query.Offset, query.Limit, DefaultLimit, MaxLimit);
        var (whereSql, parameters) = BuildBatchWhere(query);
        var orderBy = BuildBatchOrderBy(query.Sort, query.SortDirection);
        parameters.Add("offset", page.Offset);
        parameters.Add("limitPlusOne", page.Limit + 1);

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ActivityBatchSummaryRow>($"""
            WITH page_batches AS (
                SELECT b.*
                FROM ingestion_batches b
                {whereSql}
                ORDER BY {orderBy}
                LIMIT @limitPlusOne OFFSET @offset
            ),
            latest_jobs AS (
                SELECT
                    ij.ingestion_run_id AS BatchId,
                    COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown') AS MediaType,
                    ij.entity_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY ij.ingestion_run_id, ij.entity_id
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                    ) AS rn
                FROM identity_jobs ij
                JOIN page_batches pb ON pb.id = ij.ingestion_run_id
                LEFT JOIN media_assets ma ON ma.id = ij.entity_id
                LEFT JOIN editions e ON e.id = ma.edition_id
                LEFT JOIN works w ON w.id = e.work_id
                WHERE ij.entity_id IS NOT NULL
            ),
            latest_job_rollups AS (
                SELECT
                    BatchId,
                    COUNT(DISTINCT CanonicalMediaType) AS MediaTypeCount,
                    COUNT(DISTINCT entity_id) AS TitleCount
                FROM (
                    SELECT
                        BatchId,
                        entity_id,
                        CASE
                            WHEN LOWER(MediaType) LIKE '%audio%book%' THEN 'Audiobooks'
                            WHEN LOWER(MediaType) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                            WHEN LOWER(MediaType) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                            WHEN LOWER(MediaType) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                            WHEN LOWER(MediaType) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                            WHEN LOWER(MediaType) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                            ELSE MediaType
                        END AS CanonicalMediaType
                    FROM latest_jobs
                    WHERE rn = 1
                )
                GROUP BY BatchId
            ),
            operation_rollups AS (
                SELECT
                    mo.batch_id AS BatchId,
                    COUNT(*) AS OperationItemCount,
                    MAX(mo.updated_at) AS LastOperationAt
                FROM media_operations mo
                JOIN page_batches pb ON pb.id = mo.batch_id
                WHERE mo.operation_type = 'ingestion.file'
                GROUP BY mo.batch_id
            ),
            ingestion_log_rollups AS (
                SELECT
                    il.ingestion_run_id AS BatchId,
                    COUNT(*) AS LogItemCount
                FROM ingestion_log il
                JOIN page_batches pb ON pb.id = il.ingestion_run_id
                GROUP BY il.ingestion_run_id
            ),
            activity_rollups AS (
                SELECT
                    sa.ingestion_run_id AS BatchId,
                    COUNT(*) AS EventCount,
                    MAX(sa.occurred_at) AS LastActivityAt
                FROM system_activity sa
                JOIN page_batches pb ON pb.id = sa.ingestion_run_id
                GROUP BY sa.ingestion_run_id
            ),
            people_rollups AS (
                SELECT
                    lj.BatchId,
                    COUNT(DISTINCT pml.person_id) AS PeopleCount
                FROM latest_jobs lj
                JOIN person_media_links pml ON pml.media_asset_id = lj.entity_id
                WHERE lj.rn = 1
                GROUP BY lj.BatchId
            ),
            review_rollups AS (
                SELECT
                    lj.BatchId,
                    COUNT(DISTINCT rq.id) AS ReviewCount
                FROM latest_jobs lj
                JOIN review_queue rq ON rq.entity_id = lj.entity_id
                WHERE lj.rn = 1
                  AND rq.status = 'Pending'
                  AND rq.review_ready_at IS NOT NULL
                GROUP BY lj.BatchId
            )
            SELECT
                pb.id AS BatchId,
                pb.status AS Status,
                pb.source_path AS Source,
                pb.category AS Category,
                pb.started_at AS StartedAt,
                pb.completed_at AS CompletedAt,
                COALESCE(
                    ar.LastActivityAt,
                    mor.LastOperationAt,
                    pb.updated_at
                ) AS LastActivityAt,
                CASE
                    WHEN pb.started_at IS NOT NULL THEN
                        MAX(0, CAST(strftime('%s', COALESCE(pb.completed_at, pb.updated_at, CURRENT_TIMESTAMP)) AS INTEGER)
                            - CAST(strftime('%s', pb.started_at) AS INTEGER))
                    ELSE NULL
                END AS DurationSeconds,
                NULL AS DurationLabel,
                COALESCE(ljr.MediaTypeCount, 0) AS MediaTypeCount,
                COALESCE(ljr.TitleCount, 0) AS TitleCount,
                CASE
                    WHEN pb.files_total > 0 THEN pb.files_total
                    ELSE MAX(COALESCE(mor.OperationItemCount, 0), COALESCE(ilr.LogItemCount, 0))
                END AS ItemCount,
                COALESCE(ar.EventCount, 0) AS EventCount,
                COALESCE(pr.PeopleCount, 0) AS PeopleCount,
                MAX(
                    pb.files_review,
                    COALESCE(rr.ReviewCount, 0)
                ) AS ReviewCount,
                pb.files_failed + pb.files_no_match + MAX(
                    pb.files_review,
                    COALESCE(rr.ReviewCount, 0)
                ) AS AlertCount
            FROM page_batches pb
            LEFT JOIN latest_job_rollups ljr ON ljr.BatchId = pb.id
            LEFT JOIN operation_rollups mor ON mor.BatchId = pb.id
            LEFT JOIN ingestion_log_rollups ilr ON ilr.BatchId = pb.id
            LEFT JOIN activity_rollups ar ON ar.BatchId = pb.id
            LEFT JOIN people_rollups pr ON pr.BatchId = pb.id
            LEFT JOIN review_rollups rr ON rr.BatchId = pb.id
            ORDER BY {orderBy.Replace("b.", "pb.", StringComparison.Ordinal)};
            """, parameters).ConfigureAwait(false)).AsList();

        var batchIds = rows.Select(row => row.BatchId).ToArray();
        var mediaCounts = await ReadMediaTypeCountsAsync(conn, batchIds).ConfigureAwait(false);
        var items = rows.Select(row =>
        {
            var mediaTypes = mediaCounts.GetValueOrDefault(row.BatchId, []);
            return new ActivityBatchSummaryDto
            {
                BatchId = row.BatchId,
                Status = row.Status,
                Source = row.Source,
                Category = row.Category,
                StartedAt = row.StartedAt,
                CompletedAt = row.CompletedAt,
                LastActivityAt = row.LastActivityAt,
                DurationSeconds = row.DurationSeconds,
                DurationLabel = FormatDurationLabel(row.DurationSeconds, row.DurationLabel),
                MediaTypeCount = Math.Max(row.MediaTypeCount, mediaTypes.Count),
                TitleCount = row.TitleCount,
                ItemCount = row.ItemCount,
                EventCount = row.EventCount,
                PeopleCount = row.PeopleCount,
                ReviewCount = row.ReviewCount,
                AlertCount = row.AlertCount,
                MediaTypes = mediaTypes,
            };
        }).ToList();

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM ingestion_batches b {whereSql};",
            parameters).ConfigureAwait(false);

        return PagedResponse<ActivityBatchSummaryDto>.FromPage(items, page, total);
    }

    public async Task<IReadOnlyList<ActivityMediaTypeGroupDto>> GetGroupsAsync(
        Guid batchId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ActivityMediaTypeGroupDto>("""
            WITH latest_jobs AS (
                SELECT
                    ij.entity_id,
                    CASE
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                        ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')
                    END AS media_type,
                    ij.state,
                    ij.updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY ij.entity_id
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                    ) AS rn
                FROM identity_jobs ij
                LEFT JOIN media_assets ma ON ma.id = ij.entity_id
                LEFT JOIN editions e ON e.id = ma.edition_id
                LEFT JOIN works w ON w.id = e.work_id
                WHERE ij.ingestion_run_id = @batchId
                  AND ij.entity_id IS NOT NULL
            ),
            review_flags AS (
                SELECT entity_id, COUNT(*) AS review_count
                FROM review_queue
                WHERE status = 'Pending'
                  AND review_ready_at IS NOT NULL
                GROUP BY entity_id
            ),
            scoped AS (
                SELECT
                    lj.entity_id,
                    lj.media_type,
                    lj.updated_at,
                    COALESCE(rf.review_count, 0) AS review_count,
                    CASE
                        WHEN COALESCE(rf.review_count, 0) > 0 THEN 1
                        WHEN LOWER(COALESCE(lj.state, '')) LIKE '%review%' THEN 1
                        WHEN LOWER(COALESCE(lj.state, '')) IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence') THEN 1
                        ELSE 0
                    END AS needs_review
                FROM latest_jobs lj
                LEFT JOIN review_flags rf ON rf.entity_id = lj.entity_id
                WHERE lj.rn = 1
            )
            SELECT
                @batchId AS BatchId,
                CASE WHEN scoped.needs_review = 1 THEN @reviewGroup ELSE scoped.media_type END AS MediaType,
                COUNT(DISTINCT scoped.entity_id) AS TitleCount,
                COUNT(DISTINCT mo.id) AS ItemCount,
                COUNT(DISTINCT sa.id) AS EventCount,
                COUNT(DISTINCT pml.person_id) AS PeopleCount,
                COUNT(DISTINCT CASE WHEN scoped.needs_review = 1 THEN scoped.entity_id END) AS ReviewCount,
                COUNT(DISTINCT CASE
                    WHEN scoped.needs_review = 1 THEN scoped.entity_id
                    WHEN mo.status IN ('blocked', 'failed_terminal', 'dead_lettered', 'cancelled', 'no_result', 'missing_confirmed') THEN mo.id
                END) AS AlertCount,
                MAX(COALESCE(sa.occurred_at, mo.updated_at, scoped.updated_at)) AS LastActivityAt
            FROM scoped
            LEFT JOIN media_operations mo
                ON mo.batch_id = @batchId
               AND mo.entity_id = scoped.entity_id
               AND mo.operation_type = 'ingestion.file'
            LEFT JOIN system_activity sa
                ON sa.ingestion_run_id = @batchId
               AND sa.entity_id = scoped.entity_id
            LEFT JOIN person_media_links pml ON pml.media_asset_id = scoped.entity_id
            GROUP BY CASE WHEN scoped.needs_review = 1 THEN @reviewGroup ELSE scoped.media_type END
            ORDER BY CASE WHEN MediaType = @reviewGroup THEN 0 ELSE 1 END, TitleCount DESC, MediaType ASC;
            """, new { batchId, reviewGroup = ReviewGroupMediaType }).ConfigureAwait(false)).AsList();

        if (rows.Count > 0)
            return rows;

        var fallback = await conn.QueryFirstOrDefaultAsync<ActivityMediaTypeGroupDto>("""
            SELECT
                b.id AS BatchId,
                CASE WHEN b.files_review > 0 THEN @reviewGroup ELSE COALESCE(NULLIF(b.category, ''), 'Unknown') END AS MediaType,
                0 AS TitleCount,
                CASE WHEN b.files_total > 0 THEN b.files_total ELSE b.files_processed END AS ItemCount,
                0 AS EventCount,
                0 AS PeopleCount,
                b.files_review AS ReviewCount,
                b.files_failed + b.files_no_match + b.files_review AS AlertCount,
                b.updated_at AS LastActivityAt
            FROM ingestion_batches b
            WHERE b.id = @batchId;
            """, new { batchId, reviewGroup = ReviewGroupMediaType }).ConfigureAwait(false);

        return fallback is null ? [] : [fallback];
    }

    public async Task<PagedResponse<ActivityBatchItemDto>> GetItemsAsync(
        Guid batchId,
        string? mediaType,
        int offset,
        int limit,
        string? sort,
        string? sortDirection,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var page = PagedRequest.From(offset, limit, DefaultLimit, MaxLimit);
        var filterMediaType = string.IsNullOrWhiteSpace(mediaType) ? null : mediaType.Trim();
        var orderBy = BuildItemOrderBy(sort, sortDirection);

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ActivityBatchItemDto>($"""
            WITH latest_jobs AS (
                SELECT
                    ij.id AS JobId,
                    ij.entity_id,
                    CASE
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                        ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')
                    END AS media_type,
                    ij.resolved_qid,
                    ij.state,
                    ij.updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY ij.entity_id
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                    ) AS rn
                FROM identity_jobs ij
                LEFT JOIN media_assets ma_job ON ma_job.id = ij.entity_id
                LEFT JOIN editions e_job ON e_job.id = ma_job.edition_id
                LEFT JOIN works w ON w.id = e_job.work_id
                WHERE ij.ingestion_run_id = @batchId
                  AND ij.entity_id IS NOT NULL
            ),
            review_flags AS (
                SELECT entity_id, COUNT(*) AS review_count
                FROM review_queue
                WHERE status = 'Pending'
                  AND review_ready_at IS NOT NULL
                GROUP BY entity_id
            ),
            scoped AS (
                SELECT
                    lj.*,
                    COALESCE(rf.review_count, 0) AS review_count,
                    CASE
                        WHEN COALESCE(rf.review_count, 0) > 0 THEN 1
                        WHEN LOWER(COALESCE(lj.state, '')) LIKE '%review%' THEN 1
                        WHEN LOWER(COALESCE(lj.state, '')) IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence') THEN 1
                        ELSE 0
                    END AS needs_review
                FROM latest_jobs lj
                LEFT JOIN review_flags rf ON rf.entity_id = lj.entity_id
                WHERE rn = 1
                  AND (
                      @mediaType IS NULL
                      OR (@mediaType = @reviewGroup AND (
                          COALESCE(rf.review_count, 0) > 0
                          OR LOWER(COALESCE(lj.state, '')) LIKE '%review%'
                          OR LOWER(COALESCE(lj.state, '')) IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence')
                      ))
                      OR (@mediaType <> @reviewGroup
                          AND LOWER(COALESCE(media_type, 'Unknown')) = LOWER(@mediaType)
                          AND COALESCE(rf.review_count, 0) = 0
                          AND LOWER(COALESCE(lj.state, '')) NOT LIKE '%review%'
                          AND LOWER(COALESCE(lj.state, '')) NOT IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence'))
                  )
            ),
            people_rollup AS (
                SELECT
                    pml.media_asset_id AS entity_id,
                    COUNT(DISTINCT pml.person_id) AS PeopleCount
                FROM person_media_links pml
                JOIN scoped scoped_people ON scoped_people.entity_id = pml.media_asset_id
                GROUP BY pml.media_asset_id
            ),
            review_rollup AS (
                SELECT
                    rq.entity_id,
                    COUNT(*) AS ReviewCount
                FROM review_queue rq
                JOIN scoped scoped_review ON scoped_review.entity_id = rq.entity_id
                WHERE rq.status = 'Pending'
                  AND rq.review_ready_at IS NOT NULL
                GROUP BY rq.entity_id
            ),
            event_rollup AS (
                SELECT
                    sa.entity_id,
                    COUNT(*) AS EventCount,
                    MAX(sa.occurred_at) AS LastActivityAt
                FROM system_activity sa
                JOIN scoped scoped_event ON scoped_event.entity_id = sa.entity_id
                WHERE sa.ingestion_run_id = @batchId
                GROUP BY sa.entity_id
            ),
            artwork_rollup AS (
                SELECT
                    scoped_art.entity_id,
                    COUNT(DISTINCT ea.id) AS ArtworkCount
                FROM scoped scoped_art
                LEFT JOIN media_assets ma_art ON ma_art.id = scoped_art.entity_id
                LEFT JOIN editions e_art ON e_art.id = ma_art.edition_id
                LEFT JOIN works w_art ON w_art.id = e_art.work_id
                LEFT JOIN works p_art ON p_art.id = w_art.parent_work_id
                LEFT JOIN works gp_art ON gp_art.id = p_art.parent_work_id
                JOIN entity_assets ea ON ea.entity_id IN (scoped_art.entity_id, w_art.id, p_art.id, gp_art.id)
                GROUP BY scoped_art.entity_id
            ),
            cover_candidates AS (
                SELECT
                    scoped_cover.entity_id,
                    ea.id AS CoverAssetId,
                    ROW_NUMBER() OVER (
                        PARTITION BY scoped_cover.entity_id
                        ORDER BY
                            CASE ea.asset_type
                                WHEN 'CoverArt' THEN 0
                                WHEN 'SeasonPoster' THEN 1
                                WHEN 'EpisodeStill' THEN 2
                                ELSE 3
                            END,
                            COALESCE(ea.is_preferred, 0) DESC,
                            COALESCE(ea.updated_at, ea.created_at) DESC
                    ) AS rn
                FROM scoped scoped_cover
                LEFT JOIN media_assets ma_cover ON ma_cover.id = scoped_cover.entity_id
                LEFT JOIN editions e_cover ON e_cover.id = ma_cover.edition_id
                LEFT JOIN works w_cover ON w_cover.id = e_cover.work_id
                LEFT JOIN works p_cover ON p_cover.id = w_cover.parent_work_id
                LEFT JOIN works gp_cover ON gp_cover.id = p_cover.parent_work_id
                JOIN entity_assets ea
                  ON ea.entity_id IN (scoped_cover.entity_id, w_cover.id, p_cover.id, gp_cover.id)
                 AND ea.asset_type IN ('CoverArt', 'SeasonPoster', 'EpisodeStill', 'SquareArt')
            ),
            provider_candidates AS (
                SELECT
                    scoped_provider.entity_id,
                    iba.provider_id AS Provider,
                    ROW_NUMBER() OVER (
                        PARTITION BY scoped_provider.entity_id
                        ORDER BY iba.occurred_at DESC
                    ) AS rn
                FROM scoped scoped_provider
                LEFT JOIN media_assets ma_provider ON ma_provider.id = scoped_provider.entity_id
                LEFT JOIN editions e_provider ON e_provider.id = ma_provider.edition_id
                LEFT JOIN works w_provider ON w_provider.id = e_provider.work_id
                LEFT JOIN works p_provider ON p_provider.id = w_provider.parent_work_id
                LEFT JOIN works gp_provider ON gp_provider.id = p_provider.parent_work_id
                JOIN ingestion_batch_artifacts iba
                  ON iba.batch_id = @batchId
                 AND iba.artifact_type = 'provider_match'
                 AND iba.provider_id IS NOT NULL
                 AND (
                    iba.artifact_id = scoped_provider.entity_id
                    OR iba.parent_entity_id IN (scoped_provider.entity_id, w_provider.id, p_provider.id, gp_provider.id)
                 )
            )
            SELECT
                @batchId AS BatchId,
                scoped.entity_id AS AssetId,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = scoped.entity_id AND cv.key IN ('title', 'episode_title') ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'title' LIMIT 1),
                    NULLIF(mo.result_summary, ''),
                    CASE WHEN mo.source_path IS NOT NULL THEN replace(substr(mo.source_path, length(rtrim(mo.source_path, replace(mo.source_path, '\', ''))) + 1), '.', ' ') END,
                    'Unknown title'
                ) AS Title,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = scoped.entity_id AND cv.key IN ('year', 'release_year') LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('year', 'release_year') LIMIT 1)
                ) AS Subtitle,
                COALESCE(NULLIF(scoped.media_type, ''), NULLIF(w.media_type, ''), 'Unknown') AS MediaType,
                COALESCE(mo.source_path, ma.file_path_root) AS SourcePath,
                CASE
                    WHEN scoped.needs_review = 1 THEN 'Needs review'
                    WHEN COALESCE(mo.status, scoped.state, '') IN ('succeeded', 'completed', 'Ready', 'ReadyWithoutUniverse') THEN 'Complete'
                    ELSE COALESCE(mo.status, scoped.state, '')
                END AS Status,
                COALESCE(mo.status, scoped.state, '') AS ProcessingStatus,
                CASE WHEN scoped.needs_review = 1 THEN 'NeedsReview' ELSE 'Complete' END AS AuditStatus,
                COALESCE(mo.stage, scoped.state) AS Stage,
                pc.Provider AS Provider,
                COALESCE(
                    NULLIF(scoped.resolved_qid, ''),
                    NULLIF(w.wikidata_qid, ''),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = scoped.entity_id AND cv.key = 'wikidata_qid' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'wikidata_qid' LIMIT 1)
                ) AS WikidataQid,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = scoped.entity_id AND cv.key IN ('duration', 'runtime', 'runtime_minutes', 'duration_seconds') ORDER BY CASE cv.key WHEN 'duration' THEN 0 WHEN 'runtime' THEN 1 WHEN 'runtime_minutes' THEN 2 ELSE 3 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('duration', 'runtime', 'runtime_minutes', 'duration_seconds') ORDER BY CASE cv.key WHEN 'duration' THEN 0 WHEN 'runtime' THEN 1 WHEN 'runtime_minutes' THEN 2 ELSE 3 END LIMIT 1)
                ) AS DurationLabel,
                CAST(COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = scoped.entity_id AND cv.key = 'duration_seconds' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'duration_seconds' LIMIT 1)
                ) AS REAL) AS DurationSeconds,
                COALESCE(NULLIF(w.media_type, ''), NULLIF(scoped.media_type, ''), 'Unknown') AS LibraryEntityType,
                w.id AS LibraryEntityId,
                cc.CoverAssetId AS CoverAssetId,
                COALESCE(pr.PeopleCount, 0) AS PeopleCount,
                COALESCE(ar.ArtworkCount, 0) AS ArtworkCount,
                COALESCE(rr.ReviewCount, 0) AS ReviewCount,
                COALESCE(rr.ReviewCount, 0)
                + CASE WHEN mo.status IN ('blocked', 'failed_terminal', 'dead_lettered', 'cancelled', 'no_result', 'missing_confirmed') THEN 1 ELSE 0 END AS AlertCount,
                COALESCE(er.EventCount, 0) AS EventCount,
                COALESCE(
                    er.LastActivityAt,
                    mo.updated_at,
                    scoped.updated_at
                ) AS LastActivityAt
            FROM scoped
            LEFT JOIN media_operations mo
                ON mo.batch_id = @batchId
               AND mo.entity_id = scoped.entity_id
               AND mo.operation_type = 'ingestion.file'
            LEFT JOIN media_assets ma ON ma.id = scoped.entity_id
            LEFT JOIN editions e ON e.id = ma.edition_id
            LEFT JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN people_rollup pr ON pr.entity_id = scoped.entity_id
            LEFT JOIN review_rollup rr ON rr.entity_id = scoped.entity_id
            LEFT JOIN event_rollup er ON er.entity_id = scoped.entity_id
            LEFT JOIN artwork_rollup ar ON ar.entity_id = scoped.entity_id
            LEFT JOIN cover_candidates cc ON cc.entity_id = scoped.entity_id AND cc.rn = 1
            LEFT JOIN provider_candidates pc ON pc.entity_id = scoped.entity_id AND pc.rn = 1
            ORDER BY {orderBy}
            LIMIT @limitPlusOne OFFSET @offset;
            """, new
            {
                batchId,
                mediaType = filterMediaType,
                reviewGroup = ReviewGroupMediaType,
                offset = page.Offset,
                limitPlusOne = page.Limit + 1,
            }).ConfigureAwait(false)).AsList();

        foreach (var row in rows)
        {
            if (row.CoverAssetId is { } coverAssetId)
                row.CoverUrl = $"/stream/artwork/{coverAssetId:D}";

            row.DurationLabel = FormatDurationLabel(row.DurationSeconds, row.DurationLabel);
        }

        var total = await conn.ExecuteScalarAsync<int>("""
            WITH latest_jobs AS (
                SELECT
                    ij.entity_id,
                    CASE
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                        ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')
                    END AS media_type,
                    ij.state,
                    ROW_NUMBER() OVER (
                        PARTITION BY ij.entity_id
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                    ) AS rn
                FROM identity_jobs ij
                LEFT JOIN media_assets ma ON ma.id = ij.entity_id
                LEFT JOIN editions e ON e.id = ma.edition_id
                LEFT JOIN works w ON w.id = e.work_id
                WHERE ij.ingestion_run_id = @batchId
                  AND ij.entity_id IS NOT NULL
            ),
            review_flags AS (
                SELECT entity_id, COUNT(*) AS review_count
                FROM review_queue
                WHERE status = 'Pending'
                  AND review_ready_at IS NOT NULL
                GROUP BY entity_id
            )
            SELECT COUNT(*)
            FROM latest_jobs lj
            LEFT JOIN review_flags rf ON rf.entity_id = lj.entity_id
            WHERE lj.rn = 1
              AND (
                  @mediaType IS NULL
                  OR (@mediaType = @reviewGroup AND (
                      COALESCE(rf.review_count, 0) > 0
                      OR LOWER(COALESCE(lj.state, '')) LIKE '%review%'
                      OR LOWER(COALESCE(lj.state, '')) IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence')
                  ))
                  OR (@mediaType <> @reviewGroup
                      AND LOWER(COALESCE(lj.media_type, 'Unknown')) = LOWER(@mediaType)
                      AND COALESCE(rf.review_count, 0) = 0
                      AND LOWER(COALESCE(lj.state, '')) NOT LIKE '%review%'
                      AND LOWER(COALESCE(lj.state, '')) NOT IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence'))
              );
            """, new { batchId, mediaType = filterMediaType, reviewGroup = ReviewGroupMediaType }).ConfigureAwait(false);

        return PagedResponse<ActivityBatchItemDto>.FromPage(rows, page, total);
    }

    public async Task<ActivityBatchItemDetailDto?> GetItemDetailAsync(
        Guid batchId,
        Guid assetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<ActivityBatchItemDto>("""
            WITH latest_job AS (
                SELECT
                    ij.entity_id,
                    CASE
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                        ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')
                    END AS media_type,
                    ij.resolved_qid,
                    ij.state,
                    ij.updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY ij.entity_id
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                    ) AS rn
                FROM identity_jobs ij
                LEFT JOIN media_assets ma_job ON ma_job.id = ij.entity_id
                LEFT JOIN editions e_job ON e_job.id = ma_job.edition_id
                LEFT JOIN works w ON w.id = e_job.work_id
                WHERE ij.ingestion_run_id = @batchId
                  AND ij.entity_id = @assetId
            ),
            review_flags AS (
                SELECT entity_id, COUNT(*) AS review_count
                FROM review_queue
                WHERE entity_id = @assetId
                  AND status = 'Pending'
                  AND review_ready_at IS NOT NULL
                GROUP BY entity_id
            )
            SELECT
                @batchId AS BatchId,
                @assetId AS AssetId,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = @assetId AND cv.key IN ('title', 'episode_title') ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'title' LIMIT 1),
                    NULLIF(mo.result_summary, ''),
                    'Unknown title'
                ) AS Title,
                NULL AS Subtitle,
                CASE
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                    ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')
                END AS MediaType,
                COALESCE(mo.source_path, ma.file_path_root) AS SourcePath,
                CASE
                    WHEN COALESCE(rf.review_count, 0) > 0
                        OR LOWER(COALESCE(lj.state, '')) LIKE '%review%'
                        OR LOWER(COALESCE(lj.state, '')) IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence') THEN 'Needs review'
                    WHEN COALESCE(mo.status, lj.state, '') IN ('succeeded', 'completed', 'Ready', 'ReadyWithoutUniverse') THEN 'Complete'
                    ELSE COALESCE(mo.status, lj.state, '')
                END AS Status,
                COALESCE(mo.status, lj.state, '') AS ProcessingStatus,
                CASE
                    WHEN COALESCE(rf.review_count, 0) > 0
                        OR LOWER(COALESCE(lj.state, '')) LIKE '%review%'
                        OR LOWER(COALESCE(lj.state, '')) IN ('retailmatchambiguous', 'qidneedsreview', 'retailmatchedneedsreview', 'lowconfidence') THEN 'NeedsReview'
                    ELSE 'Complete'
                END AS AuditStatus,
                COALESCE(mo.stage, lj.state) AS Stage,
                (
                    SELECT iba.provider_id
                    FROM ingestion_batch_artifacts iba
                    WHERE iba.batch_id = @batchId
                      AND iba.artifact_type = 'provider_match'
                      AND iba.parent_entity_id IN (@assetId, w.id, p.id, gp.id)
                      AND iba.provider_id IS NOT NULL
                    ORDER BY iba.occurred_at DESC
                    LIMIT 1
                ) AS Provider,
                COALESCE(NULLIF(lj.resolved_qid, ''), NULLIF(w.wikidata_qid, '')) AS WikidataQid,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = @assetId AND cv.key IN ('duration', 'runtime', 'runtime_minutes', 'duration_seconds') ORDER BY CASE cv.key WHEN 'duration' THEN 0 WHEN 'runtime' THEN 1 WHEN 'runtime_minutes' THEN 2 ELSE 3 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('duration', 'runtime', 'runtime_minutes', 'duration_seconds') ORDER BY CASE cv.key WHEN 'duration' THEN 0 WHEN 'runtime' THEN 1 WHEN 'runtime_minutes' THEN 2 ELSE 3 END LIMIT 1)
                ) AS DurationLabel,
                CAST(COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = @assetId AND cv.key = 'duration_seconds' LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'duration_seconds' LIMIT 1)
                ) AS REAL) AS DurationSeconds,
                COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown') AS LibraryEntityType,
                w.id AS LibraryEntityId,
                0 AS PeopleCount,
                0 AS ArtworkCount,
                0 AS ReviewCount,
                0 AS AlertCount,
                0 AS EventCount,
                COALESCE(mo.updated_at, lj.updated_at) AS LastActivityAt
            FROM latest_job lj
            LEFT JOIN media_operations mo
                ON mo.batch_id = @batchId
               AND mo.entity_id = lj.entity_id
               AND mo.operation_type = 'ingestion.file'
            LEFT JOIN media_assets ma ON ma.id = lj.entity_id
            LEFT JOIN editions e ON e.id = ma.edition_id
            LEFT JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN review_flags rf ON rf.entity_id = lj.entity_id
            WHERE lj.rn = 1;
            """, new { batchId, assetId }).ConfigureAwait(false);

        if (item is null)
            return null;

        item.DurationLabel = FormatDurationLabel(item.DurationSeconds, item.DurationLabel);
        var fileDetails = BuildFileDetails(item);
        var timeline = (await conn.QueryAsync<ActivityTimelineEventDto>("""
            SELECT
                occurred_at AS OccurredAt,
                action_type AS EventType,
                action_type AS Label,
                detail AS Detail,
                entity_type AS Source,
                CASE
                    WHEN action_type IN ('MediaFailed', 'BatchFailed', 'FileQuarantined') THEN 'danger'
                    WHEN action_type IN ('ReviewItemCreated') THEN 'warning'
                    WHEN action_type IN ('FileIngested', 'MediaAdded', 'BatchCompleted') THEN 'success'
                    ELSE 'neutral'
                END AS Tone
            FROM system_activity
            WHERE ingestion_run_id = @batchId
              AND entity_id = @assetId
            UNION ALL
            SELECT
                occurred_at AS OccurredAt,
                event_type AS EventType,
                event_type AS Label,
                message AS Detail,
                new_stage AS Source,
                CASE
                    WHEN new_status IN ('failed_terminal', 'dead_lettered', 'blocked') THEN 'danger'
                    WHEN new_status IN ('succeeded') THEN 'success'
                    ELSE 'neutral'
                END AS Tone
            FROM media_operation_events
            WHERE batch_id = @batchId
              AND entity_id = @assetId
            ORDER BY OccurredAt ASC;
            """, new { batchId, assetId }).ConfigureAwait(false)).AsList();

        var people = (await conn.QueryAsync<ActivityPersonAuditDto>("""
            SELECT
                p.id AS PersonId,
                p.name AS PersonName,
                pml.role AS Role,
                p.wikidata_qid AS WikidataQid,
                @batchId AS BatchId,
                b.started_at AS BatchStartedAt,
                @assetId AS AssetId,
                @title AS Title,
                @mediaType AS MediaType,
                COALESCE(sa.detail, 'Linked from Wikidata claims') AS Source,
                CASE WHEN p.wikidata_qid IS NOT NULL AND p.wikidata_qid <> '' THEN 'wikidata' ELSE NULL END AS ProviderId,
                sa.occurred_at AS HydratedAt,
                CASE
                    WHEN p.local_headshot_path IS NOT NULL AND p.local_headshot_path <> '' THEN 'Stored'
                    WHEN p.headshot_url IS NOT NULL AND p.headshot_url <> '' THEN 'Remote'
                    ELSE 'Missing'
                END AS HeadshotStatus,
                CASE
                    WHEN p.local_headshot_path IS NOT NULL AND p.local_headshot_path <> '' THEN 1
                    WHEN p.headshot_url IS NOT NULL AND p.headshot_url <> '' THEN 1
                    ELSE 0
                END AS HasHeadshot
            FROM person_media_links pml
            JOIN persons p ON p.id = pml.person_id
            LEFT JOIN ingestion_batches b ON b.id = @batchId
            LEFT JOIN system_activity sa
                ON sa.ingestion_run_id = @batchId
               AND sa.entity_id = p.id
               AND sa.action_type IN ('PersonHydrated', 'PersonMerged')
            WHERE pml.media_asset_id = @assetId
            ORDER BY pml.role ASC, p.name ASC;
            """, new
            {
                batchId,
                assetId,
                title = item.Title,
                mediaType = item.MediaType,
            }).ConfigureAwait(false)).AsList();

        foreach (var person in people)
        {
            if (!string.Equals(person.HeadshotStatus, "Missing", StringComparison.OrdinalIgnoreCase))
                person.HeadshotUrl = $"/persons/{person.PersonId:D}/headshot";
        }

        var evidence = (await conn.QueryAsync<ActivityEvidenceDto>("""
            WITH lineage AS (
                SELECT @assetId AS id
                UNION
                SELECT w.id
                FROM media_assets ma
                JOIN editions e ON e.id = ma.edition_id
                JOIN works w ON w.id = e.work_id
                WHERE ma.id = @assetId
                UNION
                SELECT p.id
                FROM media_assets ma
                JOIN editions e ON e.id = ma.edition_id
                JOIN works w ON w.id = e.work_id
                JOIN works p ON p.id = w.parent_work_id
                WHERE ma.id = @assetId
            )
            SELECT
                artifact_type AS Kind,
                action AS Label,
                display_name AS Value,
                provider_id AS ProviderId,
                source AS Source,
                detail_json AS Detail,
                occurred_at AS OccurredAt
            FROM ingestion_batch_artifacts
            WHERE batch_id = @batchId
              AND (parent_entity_id IN (SELECT id FROM lineage) OR artifact_id = @assetId)
            ORDER BY occurred_at ASC;
            """, new { batchId, assetId }).ConfigureAwait(false)).AsList();

        return new ActivityBatchItemDetailDto
        {
            BatchId = batchId,
            AssetId = assetId,
            Title = item.Title,
            MediaType = item.MediaType,
            SourcePath = item.SourcePath,
            Status = item.Status,
            ProcessingStatus = item.ProcessingStatus,
            AuditStatus = item.AuditStatus,
            Stage = item.Stage,
            WikidataQid = item.WikidataQid,
            DurationSeconds = item.DurationSeconds,
            DurationLabel = item.DurationLabel,
            LibraryEntityType = item.LibraryEntityType,
            LibraryEntityId = item.LibraryEntityId,
            FileDetails = fileDetails,
            Timeline = timeline,
            People = people,
            Evidence = evidence,
        };
    }

    public async Task<PagedResponse<ActivityPersonAuditDto>> GetPeopleAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var page = PagedRequest.From(query.Offset, query.Limit, DefaultLimit, MaxLimit);
        var (whereSql, parameters) = BuildPeopleWhere(query);
        parameters.Add("offset", page.Offset);
        parameters.Add("limitPlusOne", page.Limit + 1);

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ActivityPersonAuditDto>($"""
            WITH latest_jobs AS (
                SELECT
                    ingestion_run_id,
                    entity_id,
                    media_type,
                    updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY ingestion_run_id, entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE entity_id IS NOT NULL
            )
            SELECT
                p.id AS PersonId,
                p.name AS PersonName,
                pml.role AS Role,
                p.wikidata_qid AS WikidataQid,
                b.id AS BatchId,
                b.started_at AS BatchStartedAt,
                ma.id AS AssetId,
                COALESCE(
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('title', 'episode_title') ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1),
                    (SELECT cv.value FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'title' LIMIT 1),
                    'Unknown title'
                ) AS Title,
                CASE
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                    ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')
                END AS MediaType,
                COALESCE(sa.detail, 'Linked from Wikidata claims') AS Source,
                CASE WHEN p.wikidata_qid IS NOT NULL AND p.wikidata_qid <> '' THEN 'wikidata' ELSE NULL END AS ProviderId,
                sa.occurred_at AS HydratedAt,
                CASE
                    WHEN p.local_headshot_path IS NOT NULL AND p.local_headshot_path <> '' THEN 'Stored'
                    WHEN p.headshot_url IS NOT NULL AND p.headshot_url <> '' THEN 'Remote'
                    ELSE 'Missing'
                END AS HeadshotStatus
            FROM latest_jobs lj
            JOIN ingestion_batches b ON b.id = lj.ingestion_run_id
            JOIN media_assets ma ON ma.id = lj.entity_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            JOIN person_media_links pml ON pml.media_asset_id = ma.id
            JOIN persons p ON p.id = pml.person_id
            LEFT JOIN system_activity sa
                ON sa.ingestion_run_id = b.id
               AND sa.entity_id = p.id
               AND sa.action_type IN ('PersonHydrated', 'PersonMerged')
            WHERE lj.rn = 1
            {whereSql}
            ORDER BY COALESCE(sa.occurred_at, b.started_at) DESC, p.name ASC
            LIMIT @limitPlusOne OFFSET @offset;
            """, parameters).ConfigureAwait(false)).AsList();

        foreach (var row in rows)
        {
            if (!string.Equals(row.HeadshotStatus, "Missing", StringComparison.OrdinalIgnoreCase))
                row.HeadshotUrl = $"/persons/{row.PersonId:D}/headshot";
        }

        var total = await conn.ExecuteScalarAsync<int>($"""
            WITH latest_jobs AS (
                SELECT
                    ingestion_run_id,
                    entity_id,
                    media_type,
                    ROW_NUMBER() OVER (
                        PARTITION BY ingestion_run_id, entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE entity_id IS NOT NULL
            )
            SELECT COUNT(*)
            FROM latest_jobs lj
            JOIN ingestion_batches b ON b.id = lj.ingestion_run_id
            JOIN media_assets ma ON ma.id = lj.entity_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            JOIN person_media_links pml ON pml.media_asset_id = ma.id
            JOIN persons p ON p.id = pml.person_id
            LEFT JOIN system_activity sa
                ON sa.ingestion_run_id = b.id
               AND sa.entity_id = p.id
               AND sa.action_type IN ('PersonHydrated', 'PersonMerged')
            WHERE lj.rn = 1
            {whereSql};
            """, parameters).ConfigureAwait(false);

        return PagedResponse<ActivityPersonAuditDto>.FromPage(rows, page, total);
    }

    private static List<ActivityDetailFieldDto> BuildFileDetails(ActivityBatchItemDto item)
    {
        var details = new List<ActivityDetailFieldDto>
        {
            new() { Label = "Path", Value = item.SourcePath },
            new() { Label = "Media", Value = item.MediaType },
            new() { Label = "Status", Value = item.Status },
            new() { Label = "Processing", Value = item.ProcessingStatus },
            new() { Label = "Duration", Value = item.DurationLabel },
            new() { Label = "Wikidata QID", Value = item.WikidataQid },
        };

        if (!string.IsNullOrWhiteSpace(item.SourcePath))
            details.Add(new ActivityDetailFieldDto { Label = "File Name", Value = Path.GetFileName(item.SourcePath) });

        return details
            .Where(detail => !string.IsNullOrWhiteSpace(detail.Value))
            .ToList();
    }

    private static async Task<Dictionary<Guid, List<ActivityMediaTypeCountDto>>> ReadMediaTypeCountsAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        IReadOnlyList<Guid> batchIds)
    {
        if (batchIds.Count == 0)
            return [];

        var rows = (await conn.QueryAsync<ActivityMediaTypeCountRow>("""
            WITH latest_jobs AS (
                SELECT
                    ij.ingestion_run_id AS BatchId,
                    CASE
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                        WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                        ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')
                    END AS MediaType,
                    ij.entity_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY ij.ingestion_run_id, ij.entity_id
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                    ) AS rn
                FROM identity_jobs ij
                LEFT JOIN media_assets ma ON ma.id = ij.entity_id
                LEFT JOIN editions e ON e.id = ma.edition_id
                LEFT JOIN works w ON w.id = e.work_id
                WHERE ij.ingestion_run_id IN @batchIds
                  AND ij.entity_id IS NOT NULL
            )
            SELECT BatchId, MediaType, COUNT(*) AS Count
            FROM latest_jobs
            WHERE rn = 1
            GROUP BY BatchId, MediaType
            ORDER BY Count DESC, MediaType ASC;
            """, new { batchIds = batchIds.Select(GuidSql.ToBlob).ToArray() }).ConfigureAwait(false)).AsList();

        return rows
            .GroupBy(row => row.BatchId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => new ActivityMediaTypeCountDto
                    {
                        MediaType = row.MediaType,
                        Count = row.Count,
                    })
                    .ToList());
    }

    private static string BuildBatchOrderBy(string? sort, string? direction)
    {
        var column = (sort ?? "started").Trim().ToLowerInvariant() switch
        {
            "batch" or "batchid" or "batch_id" => "b.id",
            "started" or "startedat" or "started_at" => "b.started_at",
            "media" => "b.category",
            "total" or "items" => "b.files_total",
            "duration" => "(CAST(strftime('%s', COALESCE(b.completed_at, b.updated_at, CURRENT_TIMESTAMP)) AS INTEGER) - CAST(strftime('%s', b.started_at) AS INTEGER))",
            "status" => "b.status",
            _ => "b.started_at",
        };

        var dir = IsAscending(direction) ? "ASC" : "DESC";
        return $"{column} {dir}, b.started_at DESC, b.created_at DESC";
    }

    private static string BuildItemOrderBy(string? sort, string? direction)
    {
        var dir = IsAscending(direction) ? "ASC" : "DESC";
        var column = (sort ?? "title").Trim().ToLowerInvariant() switch
        {
            "title" => "Title",
            "provider" => "Provider",
            "qid" or "wikidata" or "wikidataqid" => "WikidataQid",
            "people" or "peoplecount" => "PeopleCount",
            "duration" => "COALESCE(DurationSeconds, 0)",
            _ => "Title",
        };

        return column == "Title"
            ? $"Title COLLATE NOCASE {dir}, LastActivityAt DESC"
            : $"{column} {dir}, Title COLLATE NOCASE ASC";
    }

    private static bool IsAscending(string? direction) =>
        string.Equals(direction?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

    private static string? FormatDurationLabel(double? durationSeconds, string? rawLabel)
    {
        if (durationSeconds is > 0)
            return FormatDuration(TimeSpan.FromSeconds(durationSeconds.Value));

        if (string.IsNullOrWhiteSpace(rawLabel))
            return null;

        var trimmed = rawLabel.Trim();
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
        {
            if (numeric > 600)
                return FormatDuration(TimeSpan.FromSeconds(numeric));

            if (numeric > 0)
                return FormatDuration(TimeSpan.FromMinutes(numeric));
        }

        return trimmed;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        duration = duration.Duration();
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";

        return $"{Math.Max(0, duration.Seconds)}s";
    }

    private static (string Sql, DynamicParameters Parameters) BuildBatchWhere(ActivityBatchQuery query)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            clauses.Add("""
                (
                    LOWER(COALESCE(b.source_path, '')) LIKE @search
                    OR LOWER(COALESCE(b.category, '')) LIKE @search
                    OR LOWER(hex(b.id)) LIKE @searchCompact
                    OR EXISTS (
                        SELECT 1
                        FROM system_activity sa
                        WHERE sa.ingestion_run_id = b.id
                          AND (
                              LOWER(COALESCE(sa.collection_name, '')) LIKE @search
                              OR LOWER(COALESCE(sa.detail, '')) LIKE @search
                          )
                    )
                )
                """);
            parameters.Add("search", $"%{search.ToLowerInvariant()}%");
            parameters.Add("searchCompact", $"%{search.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            clauses.Add("LOWER(b.status) = LOWER(@status)");
            parameters.Add("status", query.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            clauses.Add("LOWER(COALESCE(b.source_path, '')) LIKE @source");
            parameters.Add("source", $"%{query.Source.Trim().ToLowerInvariant()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            clauses.Add("""
                EXISTS (
                    SELECT 1
                    FROM identity_jobs ij
                    LEFT JOIN media_assets ma ON ma.id = ij.entity_id
                    LEFT JOIN editions e ON e.id = ma.edition_id
                    LEFT JOIN works w ON w.id = e.work_id
                    WHERE ij.ingestion_run_id = b.id
                      AND LOWER(CASE
                          WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                          WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                          WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                          WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                          WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                          WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                          ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(ij.media_type, ''), 'Unknown')
                      END) = LOWER(@mediaType)
                )
                """);
            parameters.Add("mediaType", query.MediaType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            clauses.Add("""
                EXISTS (
                    SELECT 1
                    FROM system_activity sa
                    WHERE sa.ingestion_run_id = b.id
                      AND sa.action_type = @eventType
                )
                """);
            parameters.Add("eventType", query.EventType.Trim());
        }

        if (query.Start.HasValue)
        {
            clauses.Add("b.started_at >= @start");
            parameters.Add("start", query.Start.Value.ToString("O"));
        }

        if (query.End.HasValue)
        {
            clauses.Add("b.started_at <= @end");
            parameters.Add("end", query.End.Value.ToString("O"));
        }

        return (clauses.Count == 0 ? "" : $"WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static (string Sql, DynamicParameters Parameters) BuildPeopleWhere(ActivityBatchQuery query)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            clauses.Add("""
                AND (
                    LOWER(p.name) LIKE @search
                    OR LOWER(COALESCE(p.wikidata_qid, '')) LIKE @search
                    OR EXISTS (
                        SELECT 1
                        FROM canonical_values cv
                        WHERE cv.entity_id IN (ma.id, w.id)
                          AND cv.key IN ('title', 'episode_title')
                          AND LOWER(cv.value) LIKE @search
                    )
                    OR LOWER(COALESCE(b.source_path, '')) LIKE @search
                )
                """);
            parameters.Add("search", $"%{query.Search.Trim().ToLowerInvariant()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            clauses.Add("""
                AND LOWER(CASE
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) LIKE '%audio%book%' THEN 'Audiobooks'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('book', 'books', 'ebook', 'ebooks', 'epub', 'pdf') THEN 'Books'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('comic', 'comics', 'cbz', 'cbr') THEN 'Comics'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('movie', 'movies', 'film', 'films') THEN 'Movies'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('tv', 'tv shows', 'television', 'show', 'shows') THEN 'TV'
                    WHEN LOWER(COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')) IN ('music', 'album', 'albums', 'audio') THEN 'Music'
                    ELSE COALESCE(NULLIF(w.media_type, ''), NULLIF(lj.media_type, ''), 'Unknown')
                END) = LOWER(@mediaType)
                """);
            parameters.Add("mediaType", query.MediaType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            clauses.Add("AND LOWER(COALESCE(b.source_path, '')) LIKE @source");
            parameters.Add("source", $"%{query.Source.Trim().ToLowerInvariant()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            clauses.Add("AND LOWER(b.status) = LOWER(@status)");
            parameters.Add("status", query.Status.Trim());
        }

        if (query.Start.HasValue)
        {
            clauses.Add("AND b.started_at >= @start");
            parameters.Add("start", query.Start.Value.ToString("O"));
        }

        if (query.End.HasValue)
        {
            clauses.Add("AND b.started_at <= @end");
            parameters.Add("end", query.End.Value.ToString("O"));
        }

        return (string.Join(Environment.NewLine, clauses), parameters);
    }

    private sealed class ActivityBatchSummaryRow
    {
        public Guid BatchId { get; set; }
        public string Status { get; set; } = "";
        public string? Source { get; set; }
        public string? Category { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
        public double? DurationSeconds { get; set; }
        public string? DurationLabel { get; set; }
        public int MediaTypeCount { get; set; }
        public int TitleCount { get; set; }
        public int ItemCount { get; set; }
        public int EventCount { get; set; }
        public int PeopleCount { get; set; }
        public int ReviewCount { get; set; }
        public int AlertCount { get; set; }
    }

    private sealed class ActivityMediaTypeCountRow
    {
        public Guid BatchId { get; set; }
        public string MediaType { get; set; } = "Unknown";
        public int Count { get; set; }
    }
}
