using System.Globalization;
using Dapper;
using MediaEngine.Application.Services;
using MediaEngine.Contracts.Ingestion;
using MediaEngine.Contracts.Paging;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

/// <summary>
/// Converts durable ingestion records into media-centric administration models.
/// Pipeline vocabulary is intentionally contained here rather than leaked into Razor.
/// </summary>
public sealed class IngestionPresentationReadService : IIngestionPresentationReadService
{
    private static readonly string[] ActiveStatuses =
        ["queued", "running", "processing", "active", "retry_waiting", "failed_retryable", "interrupted"];

    private readonly IDatabaseConnection _db;

    public IngestionPresentationReadService(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IngestionPresentationSnapshotDto> GetSnapshotAsync(
        int currentLimit = 8,
        int recentDayLimit = 3,
        int recentItemsPerDay = 9,
        CancellationToken ct = default)
    {
        currentLimit = Math.Clamp(currentLimit, 1, 20);
        recentDayLimit = Math.Clamp(recentDayLimit, 1, 7);
        recentItemsPerDay = Math.Clamp(recentItemsPerDay, 1, 12);

        var current = await LoadGroupsAsync(GroupScope.Current, null, ct).ConfigureAwait(false);
        var recent = await LoadGroupsAsync(GroupScope.History, null, ct).ConfigureAwait(false);
        var batchFacts = await ReadCurrentBatchFactsAsync(ct).ConfigureAwait(false);
        var operationFacts = await ReadCurrentOperationFactsAsync(ct).ConfigureAwait(false);
        var reviewCount = await ReadPendingReviewCountAsync(ct).ConfigureAwait(false);

        var recentDays = recent
            .GroupBy(item => DateOnly.FromDateTime(item.AddedAt.ToLocalTime().DateTime))
            .OrderByDescending(group => group.Key)
            .Take(recentDayLimit)
            .Select(group => new IngestionRecentDayDto
            {
                Date = group.Key,
                TotalCount = group.Count(),
                Items = group.Take(recentItemsPerDay).ToList(),
            })
            .ToList();

        var attention = new List<IngestionAttentionItemDto>();
        if (reviewCount > 0)
        {
            attention.Add(new IngestionAttentionItemDto
            {
                Kind = "review",
                Count = reviewCount,
                Label = $"{reviewCount:N0} {Pluralize("title", reviewCount)} need review",
                Description = "Missing or uncertain metadata",
                Route = "/settings/review",
            });
        }

        if (operationFacts.RetryWaiting > 0)
        {
            attention.Add(new IngestionAttentionItemDto
            {
                Kind = "provider",
                Count = operationFacts.RetryWaiting,
                Label = $"{operationFacts.RetryWaiting:N0} {Pluralize("item", operationFacts.RetryWaiting)} waiting on provider data",
                Description = "Tuvima will retry automatically",
                Route = batchFacts.BatchId is { } batchId ? $"/settings/activity?runId={batchId:D}" : "/settings/activity",
            });
        }

        if (operationFacts.TextTrackWaiting > 0)
        {
            attention.Add(new IngestionAttentionItemDto
            {
                Kind = "textTracks",
                Count = operationFacts.TextTrackWaiting,
                Label = $"{operationFacts.TextTrackWaiting:N0} lyrics or subtitles in queue",
                Description = "Fetching from providers",
                Route = batchFacts.BatchId is { } batchId ? $"/settings/activity?runId={batchId:D}" : "/settings/activity",
            });
        }

        var isRunning = batchFacts.IsRunning || operationFacts.Active + operationFacts.Queued + operationFacts.RetryWaiting > 0;
        var ready = current.Count(item => item.Availability == "ready");
        var finishing = current.Count(item => item.Availability == "finishing");
        var review = current.Count(item => item.Availability == "review");

        return new IngestionPresentationSnapshotDto
        {
            IsRunning = isRunning,
            Status = isRunning ? "active" : "idle",
            FilesDiscovered = batchFacts.FilesTotal,
            FilesProcessed = Math.Min(batchFacts.FilesProcessed, Math.Max(batchFacts.FilesTotal, batchFacts.FilesProcessed)),
            LibraryGroups = current.Count,
            ReadyGroups = ready,
            FinishingGroups = finishing,
            ReviewGroups = review,
            ActiveOperations = operationFacts.Active,
            QueuedOperations = operationFacts.Queued,
            RetryWaitingOperations = operationFacts.RetryWaiting,
            StartedAt = batchFacts.StartedAt,
            LastActivityAt = batchFacts.LastActivityAt ?? recent.FirstOrDefault()?.AddedAt,
            CurrentMedia = current.Take(currentLimit).ToList(),
            CurrentMediaTotal = current.Count,
            Attention = attention,
            RecentDays = recentDays,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    public async Task<PagedResponse<IngestionMediaGroupDto>> GetCurrentMediaAsync(
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var request = PagedRequest.From(offset, limit, 50, 100);
        var groups = await LoadGroupsAsync(GroupScope.Current, null, ct).ConfigureAwait(false);
        return Page(groups, request);
    }

    public async Task<PagedResponse<IngestionMediaGroupDto>> GetRecentAdditionsAsync(
        string? search,
        string? lane,
        DateTimeOffset? start,
        DateTimeOffset? end,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var request = PagedRequest.From(offset, limit, 50, 100);
        IEnumerable<IngestionMediaGroupDto> groups = await LoadGroupsAsync(GroupScope.History, null, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(search))
        {
            groups = groups.Where(item =>
                item.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (item.Subtitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(lane) && !lane.Equals("all", StringComparison.OrdinalIgnoreCase))
            groups = groups.Where(item => LaneFor(item.MediaType).Equals(lane, StringComparison.OrdinalIgnoreCase));
        if (start.HasValue)
            groups = groups.Where(item => item.AddedAt >= start.Value);
        if (end.HasValue)
            groups = groups.Where(item => item.AddedAt <= end.Value);

        return Page(groups.ToList(), request);
    }

    public async Task<IngestionMediaGroupDto?> GetMediaGroupAsync(
        Guid batchId,
        Guid groupId,
        CancellationToken ct = default)
    {
        var groups = await LoadGroupsAsync(GroupScope.Batch, batchId, ct).ConfigureAwait(false);
        return groups.FirstOrDefault(item => item.GroupId == groupId);
    }

    public async Task<PagedResponse<IngestionMediaChildDto>> GetChildrenAsync(
        Guid batchId,
        Guid groupId,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var request = PagedRequest.From(offset, limit, 50, 100);
        var rows = await LoadRowsAsync(GroupScope.Batch, batchId, ct).ConfigureAwait(false);
        var children = rows
            .Where(row => PresentationGroupId(row) == groupId)
            .GroupBy(row => row.WorkId)
            .Select(group =>
            {
                var row = group.OrderByDescending(item => item.UpdatedAt).First();
                return new IngestionMediaChildDto
                {
                    Id = row.WorkId,
                    Title = FirstNonBlank(row.LeafTitle, row.DetectedTitle, "Untitled"),
                    SequenceLabel = BuildSequenceLabel(row),
                    Status = row.ReviewCount > 0
                        ? "review"
                        : row.PresentedAt.HasValue ? "complete" : FriendlyOperationState(row.OperationStatus),
                    DurationLabel = row.DurationLabel,
                };
            })
            .OrderBy(item => SequenceSort(item.SequenceLabel))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Page(children, request);
    }

    public async Task<ActivityHistorySummaryDto> GetActivitySummaryAsync(CancellationToken ct = default)
    {
        var recent = await LoadGroupsAsync(GroupScope.History, null, ct).ConfigureAwait(false);
        using var conn = _db.CreateConnection();
        var row = await conn.QuerySingleAsync<ActivitySummaryRow>(new CommandDefinition("""
            SELECT
                COUNT(CASE WHEN status = 'completed' AND completed_at >= @weekStart THEN 1 END) AS CompletedRunsThisWeek,
                MAX(COALESCE(completed_at, updated_at, started_at)) AS LastActivityAt
            FROM ingestion_batches;
            """, new { weekStart = StartOfWeek(DateTimeOffset.UtcNow).ToString("O") }, cancellationToken: ct)).ConfigureAwait(false);

        var latestBatch = await conn.QueryFirstOrDefaultAsync<LatestBatchRow>(new CommandDefinition("""
            SELECT files_processed AS FilesProcessed
            FROM ingestion_batches
            WHERE status = 'completed'
            ORDER BY COALESCE(completed_at, updated_at, started_at) DESC
            LIMIT 1;
            """, cancellationToken: ct)).ConfigureAwait(false);
        var pending = await ReadPendingReviewCountAsync(ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(DateTime.Now);

        return new ActivityHistorySummaryDto
        {
            CompletedRunsThisWeek = row.CompletedRunsThisWeek,
            ItemsAddedToday = recent.Count(item => DateOnly.FromDateTime(item.AddedAt.ToLocalTime().DateTime) == today),
            ItemsNeedingFollowUp = pending,
            LastActivityAt = row.LastActivityAt,
            LastFilesProcessed = latestBatch?.FilesProcessed,
            LastGroupsAdded = recent.GroupBy(item => item.BatchId).OrderByDescending(group => group.Max(item => item.AddedAt)).FirstOrDefault()?.Count(),
        };
    }

    public async Task<ActivityBatchPresentationDto?> GetBatchPresentationAsync(
        Guid batchId,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var batch = await conn.QueryFirstOrDefaultAsync<BatchPresentationRow>(new CommandDefinition("""
            SELECT
                id AS BatchId,
                status AS Status,
                source_path AS Source,
                category AS Category,
                files_processed AS FilesProcessed,
                files_review AS ReviewCount,
                files_failed AS FailureCount,
                started_at AS StartedAt,
                completed_at AS CompletedAt
            FROM ingestion_batches
            WHERE id = @batchId;
            """, new { batchId }, cancellationToken: ct)).ConfigureAwait(false);
        if (batch is null)
            return null;

        var groups = await LoadGroupsAsync(GroupScope.BatchAdditions, batchId, ct).ConfigureAwait(false);
        var metrics = await conn.QuerySingleAsync<BatchMetricRow>(new CommandDefinition("""
            SELECT
                COUNT(DISTINCT CASE WHEN iba.artifact_type = 'person' THEN iba.artifact_id END) AS PeopleUpdated,
                COUNT(DISTINCT CASE WHEN iba.artifact_type IN ('artwork', 'image') THEN iba.artifact_id END) AS ArtworkAdded,
                COUNT(DISTINCT CASE WHEN iba.artifact_type IN ('lyrics', 'subtitle', 'text_track') THEN iba.artifact_id END) AS TextTracksAdded
            FROM ingestion_batch_artifacts iba
            WHERE iba.batch_id = @batchId;
            """, new { batchId }, cancellationToken: ct)).ConfigureAwait(false);

        var followUpCount = Math.Max(0, batch.ReviewCount + batch.FailureCount);
        var followUp = new List<IngestionAttentionItemDto>();
        if (batch.ReviewCount > 0)
        {
            followUp.Add(new IngestionAttentionItemDto
            {
                Kind = "review",
                Count = batch.ReviewCount,
                Label = $"{batch.ReviewCount:N0} {Pluralize("item", batch.ReviewCount)} need review",
                Route = "/settings/review",
            });
        }
        if (batch.FailureCount > 0)
        {
            followUp.Add(new IngestionAttentionItemDto
            {
                Kind = "failure",
                Count = batch.FailureCount,
                Label = $"{batch.FailureCount:N0} {Pluralize("item", batch.FailureCount)} failed",
                Route = $"/settings/activity?runId={batchId:D}&detail=technical",
            });
        }

        return new ActivityBatchPresentationDto
        {
            BatchId = batchId,
            DisplayName = DisplayBatchName(batch.Category, batch.Source, groups),
            Summary = BatchSummary(batch.Status, groups, followUpCount),
            FilesProcessed = batch.FilesProcessed,
            GroupsAdded = groups.Count,
            PeopleUpdated = metrics.PeopleUpdated > 0 ? metrics.PeopleUpdated : null,
            ArtworkAdded = metrics.ArtworkAdded > 0 ? metrics.ArtworkAdded : null,
            TextTracksAdded = metrics.TextTracksAdded > 0 ? metrics.TextTracksAdded : null,
            ItemsNeedingFollowUp = followUpCount > 0 ? followUpCount : null,
            AddedPreview = groups.Take(6).ToList(),
            AddedTotal = groups.Count,
            FollowUp = followUp,
            Timeline = await BuildMilestonesAsync(conn, batch, ct).ConfigureAwait(false),
        };
    }

    public async Task<PagedResponse<IngestionMediaGroupDto>> GetBatchMediaAsync(
        Guid batchId,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var request = PagedRequest.From(offset, limit, 50, 100);
        var groups = await LoadGroupsAsync(GroupScope.BatchAdditions, batchId, ct).ConfigureAwait(false);
        return Page(groups, request);
    }

    public async Task<IReadOnlyDictionary<Guid, PagedResponse<IngestionMediaGroupDto>>> GetBatchMediaPreviewsAsync(
        IReadOnlyCollection<Guid> batchIds,
        int limitPerBatch,
        CancellationToken ct = default)
    {
        if (batchIds.Count == 0)
            return new Dictionary<Guid, PagedResponse<IngestionMediaGroupDto>>();

        var request = PagedRequest.From(0, limitPerBatch, 6, 12);
        var groups = await LoadGroupsAsync(GroupScope.BatchAdditionsSet, null, ct, batchIds).ConfigureAwait(false);
        return groups
            .GroupBy(item => item.BatchId)
            .ToDictionary(group => group.Key, group => Page(group.ToList(), request));
    }

    private async Task<List<IngestionMediaGroupDto>> LoadGroupsAsync(
        GroupScope scope,
        Guid? batchId,
        CancellationToken ct,
        IReadOnlyCollection<Guid>? batchIds = null)
    {
        var rows = await LoadRowsAsync(scope, batchId, ct, batchIds).ConfigureAwait(false);
        var operations = await LoadOperationsAsync(rows.Select(row => row.BatchId).Distinct().ToList(), ct).ConfigureAwait(false);

        return rows
            .GroupBy(row => new { row.BatchId, GroupId = PresentationGroupId(row) })
            .Select(group => BuildGroup(group.Key.BatchId, group.Key.GroupId, group.ToList(), operations))
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<MediaPresentationRow>> LoadRowsAsync(
        GroupScope scope,
        Guid? batchId,
        CancellationToken ct,
        IReadOnlyCollection<Guid>? batchIds = null)
    {
        var where = scope switch
        {
            GroupScope.Current => """
                AND (
                    LOWER(b.status) IN ('running', 'processing', 'active', 'queued')
                    OR EXISTS (
                        SELECT 1 FROM media_operations active_mo
                        WHERE active_mo.batch_id = b.id
                          AND active_mo.status IN ('queued', 'running', 'processing', 'active', 'retry_waiting', 'failed_retryable', 'interrupted')
                    )
                )
                """,
            GroupScope.History => $"""
                AND LOWER(b.status) NOT IN ('running', 'processing', 'active', 'queued')
                AND {AdditionBatchPredicate}
                """,
            GroupScope.BatchAdditions => $"AND b.id = @batchId AND {AdditionBatchPredicate}",
            GroupScope.BatchAdditionsSet => $"AND b.id IN @batchIds AND {AdditionBatchPredicate}",
            _ => "AND b.id = @batchId",
        };

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<MediaPresentationRow>(new CommandDefinition($"""
            WITH latest_logs AS (
                SELECT
                    il.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY il.ingestion_run_id, il.media_asset_id
                        ORDER BY il.updated_at DESC, il.created_at DESC
                    ) AS rn
                FROM ingestion_log il
                WHERE il.ingestion_run_id IS NOT NULL
                  AND il.media_asset_id IS NOT NULL
            ),
            latest_file_operations AS (
                SELECT *
                FROM (
                    SELECT
                        mo.*,
                        ROW_NUMBER() OVER (
                            PARTITION BY mo.batch_id, mo.entity_id
                            ORDER BY COALESCE(mo.updated_at, mo.completed_at, mo.started_at, mo.created_at) DESC
                        ) AS rn
                    FROM media_operations mo
                    WHERE mo.operation_type = 'ingestion.file'
                )
                WHERE rn = 1
            )
            SELECT
                ll.id AS LogId,
                ll.ingestion_run_id AS BatchId,
                ll.media_asset_id AS AssetId,
                e.id AS EditionId,
                w.id AS WorkId,
                p.id AS ParentWorkId,
                gp.id AS RootWorkId,
                COALESCE(NULLIF(w.media_type, ''), NULLIF(ll.media_type, ''), 'Unknown') AS MediaType,
                ll.detected_title AS DetectedTitle,
                (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('title','episode_title','issue_title') ORDER BY CASE key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1) AS LeafTitle,
                (SELECT value FROM canonical_values WHERE entity_id = p.id AND key IN ('title','album','show_name','series','book_title') ORDER BY CASE key WHEN 'title' THEN 0 WHEN 'album' THEN 1 ELSE 2 END LIMIT 1) AS ParentTitle,
                (SELECT value FROM canonical_values WHERE entity_id = gp.id AND key IN ('title','show_name','series') ORDER BY CASE key WHEN 'title' THEN 0 ELSE 1 END LIMIT 1) AS RootTitle,
                COALESCE(
                    (SELECT group_concat(value, '; ') FROM (SELECT value FROM canonical_value_arrays WHERE entity_id = COALESCE(gp.id,p.id,w.id) AND key IN ('artist','album_artist','author','creator','narrator') ORDER BY ordinal)),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gp.id,p.id,w.id) AND key IN ('artist','album_artist','author','creator','narrator') LIMIT 1)
                ) AS Creator,
                (SELECT value FROM canonical_values WHERE entity_id IN (ll.media_asset_id,w.id,p.id,gp.id) AND key = 'season_number' LIMIT 1) AS SeasonNumber,
                (SELECT value FROM canonical_values WHERE entity_id IN (ll.media_asset_id,w.id) AND key = 'episode_number' LIMIT 1) AS EpisodeNumber,
                (SELECT value FROM canonical_values WHERE entity_id IN (ll.media_asset_id,w.id) AND key = 'track_number' LIMIT 1) AS TrackNumber,
                (SELECT value FROM canonical_values WHERE entity_id IN (ll.media_asset_id,w.id) AND key IN ('issue_number','series_position') LIMIT 1) AS IssueNumber,
                (SELECT value FROM canonical_values WHERE entity_id IN (ll.media_asset_id,w.id,p.id) AND key = 'audiobook_part_count' LIMIT 1) AS AudiobookPartCount,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id IN (p.id,gp.id,w.id,ll.media_asset_id) AND key = 'track_count' AND CAST(value AS INTEGER) > 0 LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id IN (p.id,gp.id,w.id,ll.media_asset_id) AND key = 'episode_count' AND CAST(value AS INTEGER) > 0 LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id IN (p.id,gp.id,w.id,ll.media_asset_id) AND key = 'issue_count' AND CAST(value AS INTEGER) > 0 LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id IN (p.id,gp.id,w.id,ll.media_asset_id) AND key = 'audiobook_part_count' AND CAST(value AS INTEGER) > 0 LIMIT 1),
                    (SELECT total.value FROM canonical_values total
                     WHERE total.entity_id IN (p.id,gp.id,w.id)
                       AND total.key = 'sequence_total'
                       AND CAST(total.value AS INTEGER) > 0
                       AND EXISTS (SELECT 1 FROM canonical_values total_scope WHERE total_scope.entity_id = total.entity_id AND total_scope.key = 'sequence_total_scope' AND total_scope.value IN ('MainSequence','Album','Season'))
                     LIMIT 1)
                ) AS ExpectedCount,
                (SELECT value FROM canonical_values WHERE entity_id IN (ll.media_asset_id,w.id) AND key IN ('duration','runtime') LIMIT 1) AS DurationLabel,
                ll.status AS LogStatus,
                lfo.status AS OperationStatus,
                lfo.stage AS OperationStage,
                ma.presented_at AS PresentedAt,
                ll.created_at AS AddedAt,
                COALESCE(lfo.updated_at, ll.updated_at, ll.created_at) AS UpdatedAt,
                (SELECT COUNT(*) FROM review_queue rq WHERE rq.entity_id IN (ll.media_asset_id,w.id,p.id,gp.id) AND rq.status = 'Pending' AND rq.review_ready_at IS NOT NULL) AS ReviewCount,
                (SELECT COUNT(*) FROM person_media_links pml WHERE pml.media_asset_id = ll.media_asset_id) AS PeopleCount,
                (SELECT COUNT(*) FROM text_tracks tt WHERE tt.asset_id = ll.media_asset_id) AS TextTrackCount,
                (SELECT ea.id FROM entity_assets ea
                 WHERE ea.entity_id IN (ll.media_asset_id,w.id,p.id,gp.id)
                   AND ea.asset_type IN ('CoverArt','SeasonPoster','EpisodeStill')
                 ORDER BY CASE ea.asset_type WHEN 'CoverArt' THEN 0 WHEN 'SeasonPoster' THEN 1 ELSE 2 END, COALESCE(ea.is_preferred,0) DESC, COALESCE(ea.updated_at,ea.created_at) DESC
                 LIMIT 1) AS CoverAssetId
            FROM latest_logs ll
            JOIN ingestion_batches b ON b.id = ll.ingestion_run_id
            JOIN media_assets ma ON ma.id = ll.media_asset_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN latest_file_operations lfo ON lfo.batch_id = ll.ingestion_run_id AND lfo.entity_id = ll.media_asset_id
            WHERE ll.rn = 1
              {where}
            ORDER BY COALESCE(lfo.updated_at, ll.updated_at, ll.created_at) DESC;
            """, new { batchId, batchIds }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        return rows;
    }

    private async Task<List<PresentationOperationRow>> LoadOperationsAsync(IReadOnlyList<Guid> batchIds, CancellationToken ct)
    {
        if (batchIds.Count == 0)
            return [];
        using var conn = _db.CreateConnection();
        return (await conn.QueryAsync<PresentationOperationRow>(new CommandDefinition("""
            SELECT batch_id AS BatchId, entity_id AS EntityId, operation_type AS OperationType,
                   capability_id AS CapabilityId, status AS Status, stage AS Stage,
                   COALESCE(updated_at, completed_at, started_at, created_at) AS UpdatedAt
            FROM media_operations
            WHERE batch_id IN @batchIds;
            """, new { batchIds }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
    }

    private static IngestionMediaGroupDto BuildGroup(
        Guid batchId,
        Guid groupId,
        IReadOnlyList<MediaPresentationRow> rows,
        IReadOnlyList<PresentationOperationRow> operations)
    {
        var newest = rows.OrderByDescending(row => row.UpdatedAt).First();
        var mediaType = NormalizeMediaType(newest.MediaType);
        var memberIds = rows.SelectMany(row => new Guid?[] { row.AssetId, row.WorkId, row.ParentWorkId, row.RootWorkId })
            .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var scopedOps = operations.Where(operation => operation.BatchId == batchId && operation.EntityId is { } id && memberIds.Contains(id)).ToList();
        var completed = rows.Count(row => row.PresentedAt.HasValue || IsTerminalSuccess(row.LogStatus, row.OperationStatus));
        var expected = rows.Select(row => ParsePositiveInt(row.ExpectedCount)).FirstOrDefault(value => value.HasValue);
        var needsReview = rows.Any(row => row.ReviewCount > 0);
        var terminalFailure = scopedOps.Any(operation => operation.Status is "failed_terminal" or "dead_lettered")
                              || rows.Any(row => row.OperationStatus is "failed_terminal" or "dead_lettered");
        var hasBackgroundWork = scopedOps.Any(operation => IsActive(operation.Status) && !IsFileIntake(operation.OperationType));
        var intakeComplete = rows.All(row => row.PresentedAt.HasValue || IsTerminalSuccess(row.LogStatus, row.OperationStatus));
        var availability = needsReview ? "review" : terminalFailure ? "failed" : intakeComplete ? hasBackgroundWork ? "finishing" : "ready" : "adding";

        return new IngestionMediaGroupDto
        {
            GroupId = groupId,
            BatchId = batchId,
            WorkId = groupId,
            EditionId = rows.Count == 1 ? newest.EditionId : null,
            MediaType = mediaType,
            Title = GroupTitle(mediaType, newest),
            Subtitle = GroupSubtitle(mediaType, rows),
            CoverUrl = newest.CoverAssetId is { } coverId ? $"/stream/artwork/{coverId:D}" : null,
            Availability = availability,
            StatusLabel = GroupStatus(availability, scopedOps),
            ChildCompleted = completed,
            ChildExpected = expected,
            ChildUnit = ChildUnit(mediaType, rows),
            DetailRoute = DetailRoute(mediaType, groupId),
            People = Facet("People", "people", scopedOps, rows.Any(row => row.PeopleCount > 0), applies: true),
            Artwork = Facet("Artwork", "artwork", scopedOps, newest.CoverAssetId.HasValue, applies: true),
            Metadata = Facet("Metadata", "metadata", scopedOps, intakeComplete, applies: true),
            Relationships = Facet("Relationships", "relationship", scopedOps, false, applies: true),
            TextTracks = Facet(mediaType == "Music" ? "Lyrics" : "Subtitles", "text", scopedOps, rows.Any(row => row.TextTrackCount > 0), applies: mediaType is "Music" or "Movies" or "TV"),
            AddedAt = rows.Min(row => row.PresentedAt ?? row.AddedAt),
            UpdatedAt = rows.Max(row => row.UpdatedAt),
        };
    }

    private static IngestionFacetStateDto Facet(
        string label,
        string facet,
        IReadOnlyList<PresentationOperationRow> operations,
        bool hasResult,
        bool applies)
    {
        if (!applies)
            return IngestionFacetStateDto.NotApplicable(label);
        var matching = operations.Where(operation => OperationFacet(operation) == facet).ToList();
        var state = matching.Any(operation => operation.Status is "failed_terminal" or "dead_lettered")
            ? "failed"
            : matching.Any(operation => operation.Status is "retry_waiting" or "failed_retryable" or "interrupted")
                ? "attention"
                : matching.Any(operation => IsActive(operation.Status))
                    ? "active"
                    : hasResult || matching.Any(operation => IsTerminalSuccess(operation.Status, operation.Status))
                        ? "complete"
                        : "pending";
        return new IngestionFacetStateDto { State = state, Label = $"{label} {FacetStateLabel(state)}" };
    }

    private static string OperationFacet(PresentationOperationRow operation)
    {
        var value = $"{operation.OperationType} {operation.CapabilityId} {operation.Stage}".ToLowerInvariant();
        if (value.Contains("artwork") || value.Contains("image") || value.Contains("poster") || value.Contains("cover")) return "artwork";
        if (value.Contains("people") || value.Contains("person") || value.Contains("cast") || value.Contains("credit")) return "people";
        if (value.Contains("lyric") || value.Contains("subtitle") || value.Contains("text_track") || value.Contains("text track")) return "text";
        if (value.Contains("relationship") || value.Contains("universe") || value.Contains("collection") || value.Contains("series")) return "relationship";
        if (value.Contains("metadata") || value.Contains("identity") || value.Contains("retail") || value.Contains("wikidata") || value.Contains("hydrat")) return "metadata";
        return "other";
    }

    private static Guid PresentationGroupId(MediaPresentationRow row)
    {
        var mediaType = NormalizeMediaType(row.MediaType);
        return mediaType switch
        {
            "Music" => row.ParentWorkId ?? row.WorkId,
            "TV" => row.RootWorkId ?? row.ParentWorkId ?? row.WorkId,
            "Comics" => row.ParentWorkId ?? row.WorkId,
            "Audiobooks" when row.ParentWorkId.HasValue && ParsePositiveInt(row.AudiobookPartCount).HasValue => row.ParentWorkId.Value,
            _ => row.WorkId,
        };
    }

    private static string GroupTitle(string mediaType, MediaPresentationRow row) => mediaType switch
    {
        "Music" or "Comics" => FirstNonBlank(row.ParentTitle, row.LeafTitle, row.DetectedTitle, "Identifying media"),
        "TV" => FirstNonBlank(row.RootTitle, row.ParentTitle, row.LeafTitle, row.DetectedTitle, "Identifying show"),
        "Audiobooks" when row.ParentWorkId.HasValue && ParsePositiveInt(row.AudiobookPartCount).HasValue => FirstNonBlank(row.ParentTitle, row.LeafTitle, row.DetectedTitle, "Identifying audiobook"),
        _ => FirstNonBlank(row.LeafTitle, row.DetectedTitle, "Identifying media"),
    };

    private static string? GroupSubtitle(string mediaType, IReadOnlyList<MediaPresentationRow> rows)
    {
        var newest = rows.OrderByDescending(row => row.UpdatedAt).First();
        if (mediaType == "TV")
        {
            var seasons = rows.Select(row => ParsePositiveInt(row.SeasonNumber)).Where(value => value.HasValue).Select(value => value!.Value).Distinct().Order().ToList();
            return seasons.Count switch
            {
                1 => $"Season {seasons[0]}",
                > 1 => $"{seasons.Count:N0} seasons · {rows.Count:N0} episodes added",
                _ => "TV Show",
            };
        }
        return FirstNonBlankOrNull(newest.Creator, mediaType);
    }

    private static string GroupStatus(string availability, IReadOnlyList<PresentationOperationRow> operations)
    {
        if (availability == "review") return "Needs review";
        if (availability == "failed") return "Failed";
        var active = operations.Where(operation => IsActive(operation.Status)).OrderByDescending(operation => operation.UpdatedAt).FirstOrDefault();
        if (active is not null)
        {
            return OperationFacet(active) switch
            {
                "people" => "Adding people",
                "artwork" => "Finishing artwork",
                "relationship" => "Building relationships",
                "text" when $"{active.OperationType} {active.CapabilityId}".Contains("lyric", StringComparison.OrdinalIgnoreCase) => "Adding lyrics",
                "text" => "Adding subtitles",
                "metadata" => availability == "finishing" ? "Finishing details" : "Identifying",
                _ => availability == "finishing" ? "Finishing details" : "Adding",
            };
        }
        return availability switch { "ready" => "Ready", "finishing" => "Finishing details", _ => "Adding" };
    }

    private static string? ChildUnit(string mediaType, IReadOnlyList<MediaPresentationRow> rows) => mediaType switch
    {
        "Music" => "tracks",
        "TV" => "episodes",
        "Comics" => "issues",
        "Audiobooks" when rows.Count > 1 => "files",
        _ => null,
    };

    private static string? DetailRoute(string mediaType, Guid groupId) => mediaType switch
    {
        "TV" => $"/details/tvshow/{groupId:D}?context=watch",
        "Music" => $"/details/musicalbum/{groupId:D}?context=listen",
        "Movies" => $"/details/work/{groupId:D}?context=watch",
        "Audiobooks" => $"/details/work/{groupId:D}?context=listen",
        "Books" => $"/details/work/{groupId:D}?context=read",
        "Comics" => $"/details/work/{groupId:D}?context=comics",
        _ => $"/details/work/{groupId:D}",
    };

    private static string BuildSequenceLabel(MediaPresentationRow row)
    {
        var mediaType = NormalizeMediaType(row.MediaType);
        return mediaType switch
        {
            "TV" when ParsePositiveInt(row.EpisodeNumber) is { } episode => $"E{episode:00}",
            "Music" when ParsePositiveInt(row.TrackNumber) is { } track => track.ToString(CultureInfo.InvariantCulture),
            "Comics" when !string.IsNullOrWhiteSpace(row.IssueNumber) => $"#{row.IssueNumber}",
            _ => "",
        };
    }

    private static double SequenceSort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return double.MaxValue;
        var numeric = new string(value.Where(character => char.IsDigit(character) || character == '.').ToArray());
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.MaxValue;
    }

    private async Task<CurrentBatchFacts> ReadCurrentBatchFactsAsync(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrentBatchFacts>(new CommandDefinition("""
            SELECT
                b.id AS BatchId,
                CASE WHEN LOWER(b.status) IN ('running','processing','active','queued')
                       OR EXISTS (SELECT 1 FROM media_operations mo WHERE mo.batch_id = b.id AND mo.status IN ('queued','running','processing','active','retry_waiting','failed_retryable','interrupted'))
                     THEN 1 ELSE 0 END AS IsRunning,
                b.files_total AS FilesTotal,
                b.files_processed AS FilesProcessed,
                b.started_at AS StartedAt,
                COALESCE(b.completed_at,b.updated_at,b.started_at) AS LastActivityAt
            FROM ingestion_batches b
            ORDER BY IsRunning DESC, COALESCE(b.completed_at,b.updated_at,b.started_at) DESC
            LIMIT 1;
            """, cancellationToken: ct)).ConfigureAwait(false) ?? new CurrentBatchFacts();
    }

    private async Task<CurrentOperationFacts> ReadCurrentOperationFactsAsync(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleAsync<CurrentOperationFacts>(new CommandDefinition("""
            SELECT
                COUNT(CASE WHEN status IN ('running','processing','active') THEN 1 END) AS Active,
                COUNT(CASE WHEN status = 'queued' THEN 1 END) AS Queued,
                COUNT(CASE WHEN status IN ('retry_waiting','failed_retryable','interrupted') THEN 1 END) AS RetryWaiting,
                COUNT(CASE WHEN status IN ('queued','running','processing','active','retry_waiting','failed_retryable','interrupted')
                             AND (LOWER(operation_type) LIKE '%lyric%' OR LOWER(operation_type) LIKE '%subtitle%' OR LOWER(COALESCE(capability_id,'')) LIKE '%lyric%' OR LOWER(COALESCE(capability_id,'')) LIKE '%subtitle%') THEN 1 END) AS TextTrackWaiting
            FROM media_operations;
            """, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<int> ReadPendingReviewCountAsync(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM review_queue
            WHERE status = 'Pending' AND review_ready_at IS NOT NULL;
            """, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<List<ActivityBatchMilestoneDto>> BuildMilestonesAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        BatchPresentationRow batch,
        CancellationToken ct)
    {
        var operationRows = (await conn.QueryAsync<MilestoneRow>(new CommandDefinition("""
            SELECT operation_type AS OperationType, capability_id AS CapabilityId, stage AS Stage,
                   status AS Status, MIN(COALESCE(started_at,created_at)) AS StartedAt,
                   MAX(COALESCE(completed_at,updated_at,started_at,created_at)) AS CompletedAt
            FROM media_operations
            WHERE batch_id = @batchId
            GROUP BY operation_type, capability_id, stage, status;
            """, new { batchId = batch.BatchId }, cancellationToken: ct)).ConfigureAwait(false)).AsList();

        var milestones = new List<ActivityBatchMilestoneDto>();
        if (batch.FilesProcessed > 0)
        {
            milestones.Add(new ActivityBatchMilestoneDto
            {
                Label = "Files scanned",
                State = batch.FailureCount > 0 ? "attention" : "complete",
                Detail = $"{batch.FilesProcessed:N0} files processed",
                OccurredAt = batch.CompletedAt ?? batch.StartedAt,
            });
        }

        foreach (var facet in new[] { "metadata", "artwork", "people", "text", "relationship" })
        {
            var matching = operationRows.Where(row => OperationFacet(new PresentationOperationRow
            {
                OperationType = row.OperationType,
                CapabilityId = row.CapabilityId,
                Stage = row.Stage,
            }) == facet).ToList();
            if (matching.Count == 0) continue;
            var state = matching.Any(row => row.Status is "failed_terminal" or "dead_lettered") ? "failed"
                : matching.Any(row => row.Status is "retry_waiting" or "failed_retryable" or "interrupted") ? "attention"
                : matching.Any(row => IsActive(row.Status)) ? "active" : "complete";
            var started = matching.Min(row => row.StartedAt);
            var completed = matching.Max(row => row.CompletedAt);
            milestones.Add(new ActivityBatchMilestoneDto
            {
                Label = facet switch
                {
                    "metadata" => "Media identified",
                    "artwork" => "Artwork downloaded",
                    "people" => "People and credits added",
                    "text" => "Lyrics and subtitles added",
                    _ => "Relationships built",
                },
                State = state,
                Detail = state == "complete" && started.HasValue && completed.HasValue
                    ? $"Completed in {FormatDuration(completed.Value - started.Value)}"
                    : FacetStateLabel(state),
                OccurredAt = completed,
            });
        }
        return milestones.OrderBy(item => item.OccurredAt).ToList();
    }

    private static string DisplayBatchName(string? category, string? source, IReadOnlyList<IngestionMediaGroupDto> groups)
    {
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
            return category.EndsWith("import", StringComparison.OrdinalIgnoreCase) ? category : $"{category} import";
        var lanes = groups.Select(group => LaneFor(group.MediaType)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (lanes.Count == 1) return $"{lanes[0]} import";
        if (!string.IsNullOrWhiteSpace(source))
        {
            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(name)) return $"{name} scan";
        }
        return "Mixed media scan";
    }

    private static string BatchSummary(string status, IReadOnlyList<IngestionMediaGroupDto> groups, int followUpCount)
    {
        var lane = groups.Select(group => LaneFor(group.MediaType)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var destination = lane.Count == 1 ? $" in {lane[0]}" : " in the library";
        if (status.Equals("failed", StringComparison.OrdinalIgnoreCase)) return "This run failed before all media could be added.";
        if (status is "abandoned" or "interrupted") return "This run was interrupted. Completed items remain available.";
        return followUpCount > 0
            ? $"This run completed. Most items are now available{destination}; a small number still need follow-up."
            : $"This run completed successfully. Its items are now available{destination}.";
    }

    private static PagedResponse<T> Page<T>(IReadOnlyList<T> items, PagedRequest request)
    {
        var page = items.Skip(request.Offset).Take(request.Limit + 1).ToList();
        return PagedResponse<T>.FromPage(page, request, items.Count);
    }

    private static bool IsActive(string? status) => !string.IsNullOrWhiteSpace(status) && ActiveStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);
    private static bool IsFileIntake(string? operationType) => operationType?.Equals("ingestion.file", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsTerminalSuccess(string? first, string? second) => new[] { first, second }.Any(value => value is not null && value.ToLowerInvariant() is "complete" or "completed" or "succeeded" or "ready" or "readywithoutuniverse" or "registered");
    private static string FriendlyOperationState(string? status) => status switch { "running" or "processing" or "active" => "active", "failed_terminal" or "dead_lettered" => "failed", "retry_waiting" or "failed_retryable" => "waiting", _ => "pending" };
    private static string FacetStateLabel(string state) => state switch { "complete" => "complete", "active" => "in progress", "attention" => "waiting to retry", "failed" => "failed", "pending" => "queued", _ => state };

    private static string NormalizeMediaType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Contains("audio") && normalized.Contains("book")) return "Audiobooks";
        if (normalized.Contains("comic")) return "Comics";
        if (normalized is "tv" or "television" or "tv shows" or "show" or "shows") return "TV";
        if (normalized is "movie" or "movies" or "film" or "films") return "Movies";
        if (normalized is "music" or "album" or "albums" or "track" or "tracks" or "song" or "songs") return "Music";
        if (normalized is "book" or "books" or "ebook" or "ebooks" or "epub" or "pdf") return "Books";
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }

    private static string LaneFor(string mediaType) => NormalizeMediaType(mediaType) switch
    {
        "Books" or "Comics" => "Read",
        "Movies" or "TV" => "Watch",
        "Music" or "Audiobooks" => "Listen",
        _ => "Other",
    };

    private static int? ParsePositiveInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : null;
    private static string FirstNonBlank(params string?[] values) => values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    private static string? FirstNonBlankOrNull(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    private static string Pluralize(string word, int count) => count == 1 ? word : $"{word}s";
    private static DateTimeOffset StartOfWeek(DateTimeOffset value) => value.Date.AddDays(-((7 + (int)value.DayOfWeek - (int)DayOfWeek.Monday) % 7));
    private static string FormatDuration(TimeSpan value) => value.TotalMinutes >= 1 ? $"{(int)value.TotalMinutes}m {value.Seconds}s" : $"{Math.Max(0, value.Seconds)}s";

    private const string AdditionBatchPredicate = """
        ma.presented_at IS NOT NULL
        AND b.id = (
            SELECT prior_log.ingestion_run_id
            FROM ingestion_log prior_log
            WHERE prior_log.media_asset_id = ma.id
              AND prior_log.ingestion_run_id IS NOT NULL
              AND julianday(prior_log.created_at) <= julianday(ma.presented_at)
            ORDER BY julianday(prior_log.created_at) DESC, julianday(prior_log.updated_at) DESC
            LIMIT 1
        )
        """;

    private enum GroupScope { Current, History, Batch, BatchAdditions, BatchAdditionsSet }

    private sealed class MediaPresentationRow
    {
        public Guid LogId { get; set; }
        public Guid BatchId { get; set; }
        public Guid AssetId { get; set; }
        public Guid EditionId { get; set; }
        public Guid WorkId { get; set; }
        public Guid? ParentWorkId { get; set; }
        public Guid? RootWorkId { get; set; }
        public string MediaType { get; set; } = "Unknown";
        public string? DetectedTitle { get; set; }
        public string? LeafTitle { get; set; }
        public string? ParentTitle { get; set; }
        public string? RootTitle { get; set; }
        public string? Creator { get; set; }
        public string? SeasonNumber { get; set; }
        public string? EpisodeNumber { get; set; }
        public string? TrackNumber { get; set; }
        public string? IssueNumber { get; set; }
        public string? AudiobookPartCount { get; set; }
        public string? ExpectedCount { get; set; }
        public string? DurationLabel { get; set; }
        public string? LogStatus { get; set; }
        public string? OperationStatus { get; set; }
        public string? OperationStage { get; set; }
        public DateTimeOffset? PresentedAt { get; set; }
        public DateTimeOffset AddedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int ReviewCount { get; set; }
        public int PeopleCount { get; set; }
        public int TextTrackCount { get; set; }
        public Guid? CoverAssetId { get; set; }
    }

    private sealed class PresentationOperationRow
    {
        public Guid BatchId { get; set; }
        public Guid? EntityId { get; set; }
        public string OperationType { get; set; } = "";
        public string? CapabilityId { get; set; }
        public string Status { get; set; } = "";
        public string? Stage { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class CurrentBatchFacts
    {
        public Guid? BatchId { get; set; }
        public bool IsRunning { get; set; }
        public int FilesTotal { get; set; }
        public int FilesProcessed { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
    }

    private sealed class CurrentOperationFacts { public int Active { get; set; } public int Queued { get; set; } public int RetryWaiting { get; set; } public int TextTrackWaiting { get; set; } }
    private sealed class ActivitySummaryRow { public int CompletedRunsThisWeek { get; set; } public DateTimeOffset? LastActivityAt { get; set; } }
    private sealed class LatestBatchRow { public int FilesProcessed { get; set; } }
    private sealed class BatchPresentationRow { public Guid BatchId { get; set; } public string Status { get; set; } = ""; public string? Source { get; set; } public string? Category { get; set; } public int FilesProcessed { get; set; } public int ReviewCount { get; set; } public int FailureCount { get; set; } public DateTimeOffset StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } }
    private sealed class BatchMetricRow { public int PeopleUpdated { get; set; } public int ArtworkAdded { get; set; } public int TextTracksAdded { get; set; } }
    private sealed class MilestoneRow { public string OperationType { get; set; } = ""; public string? CapabilityId { get; set; } public string? Stage { get; set; } public string Status { get; set; } = ""; public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } }
}
