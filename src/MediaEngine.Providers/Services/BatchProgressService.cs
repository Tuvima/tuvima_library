using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Events;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Manages batch counter adjustments and SignalR progress emission.
/// Extracted from HydrationPipelineService for single-responsibility and reuse by pipeline workers.
/// </summary>
public sealed class BatchProgressService
{
    private static readonly string[] ActiveStates =
    [
        "RetailSearching",
        "RetailMatched",
        "RetailMatchedNeedsReview",
        "BridgeSearching",
        "QidResolved",
        "Hydrating",
        "UniverseEnriching",
    ];

    private readonly IIngestionBatchRepository _batchRepo;
    private readonly IDatabaseConnection _db;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<BatchProgressService> _logger;

    public BatchProgressService(
        IIngestionBatchRepository batchRepo,
        IDatabaseConnection db,
        IEventPublisher eventPublisher,
        ILogger<BatchProgressService> logger)
    {
        _batchRepo = batchRepo;
        _db = db;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes the live batch snapshot after a file settles into review.
    /// </summary>
    public async Task ShiftToReviewAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is null) return;
        try
        {
            await EmitProgressAsync(batchId.Value, isFinal: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch review adjustment failed for {BatchId}", batchId);
        }
    }

    /// <summary>
    /// Refreshes the live batch snapshot after a file leaves review.
    /// </summary>
    public async Task ShiftToIdentifiedAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is null) return;
        try
        {
            await EmitProgressAsync(batchId.Value, isFinal: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch resolve adjustment failed for {BatchId}", batchId);
        }
    }

    /// <summary>
    /// Fetches the current batch counters and broadcasts a BatchProgress SignalR event.
    /// Best-effort — never throws.
    /// </summary>
    public async Task EmitProgressAsync(Guid batchId, bool isFinal, CancellationToken ct)
    {
        try
        {
            var batch = await _batchRepo.GetByIdAsync(batchId, ct).ConfigureAwait(false);
            if (batch is null) return;

            using var conn = _db.CreateConnection();
            var snapshot = await conn.QueryFirstOrDefaultAsync<BatchRunSnapshot>(
                """
                WITH latest_jobs AS (
                    SELECT
                        entity_id,
                        state,
                        updated_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY entity_id
                            ORDER BY updated_at DESC, created_at DESC
                        ) AS rn
                    FROM identity_jobs
                    WHERE ingestion_run_id = @batchId
                ),
                job_states AS (
                    SELECT entity_id, state, updated_at
                    FROM latest_jobs
                    WHERE rn = 1
                ),
                pending_reviews AS (
                    SELECT DISTINCT entity_id
                    FROM review_queue
                    WHERE status = 'Pending'
                )
                SELECT
                    COALESCE(SUM(CASE WHEN js.state IN ('Ready', 'Completed') AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesReady,
                    COALESCE(SUM(CASE WHEN js.state = 'ReadyWithoutUniverse' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesReadyWithoutUniverse,
                    COALESCE(SUM(CASE
                        WHEN js.state = 'QidNeedsReview' THEN 1
                        WHEN pr.entity_id IS NOT NULL
                             AND js.state IN ('Ready', 'ReadyWithoutUniverse', 'Completed', 'RetailNoMatch', 'QidNoMatch', 'Failed')
                            THEN 1
                        ELSE 0
                    END), 0) AS FilesReview,
                    COALESCE(SUM(CASE WHEN js.state IN ('RetailNoMatch', 'QidNoMatch') AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesNoMatch,
                    COALESCE(SUM(CASE WHEN js.state = 'Failed' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS PipelineFailed,
                    COALESCE(SUM(CASE WHEN js.state = 'Queued' THEN 1 ELSE 0 END), 0) AS QueuedJobs,
                    COALESCE(SUM(CASE WHEN js.state = 'RetailSearching' THEN 1 ELSE 0 END), 0) AS RetailSearching,
                    COALESCE(SUM(CASE WHEN js.state = 'RetailMatched' THEN 1 ELSE 0 END), 0) AS RetailMatched,
                    COALESCE(SUM(CASE WHEN js.state = 'RetailMatchedNeedsReview' THEN 1 ELSE 0 END), 0) AS RetailMatchedNeedsReview,
                    COALESCE(SUM(CASE WHEN js.state = 'BridgeSearching' THEN 1 ELSE 0 END), 0) AS BridgeSearching,
                    COALESCE(SUM(CASE WHEN js.state = 'QidResolved' THEN 1 ELSE 0 END), 0) AS QidResolved,
                    COALESCE(SUM(CASE WHEN js.state = 'Hydrating' THEN 1 ELSE 0 END), 0) AS Hydrating,
                    COALESCE(SUM(CASE WHEN js.state = 'UniverseEnriching' THEN 1 ELSE 0 END), 0) AS UniverseEnriching
                FROM job_states js
                LEFT JOIN pending_reviews pr ON pr.entity_id = js.entity_id;
                """,
                new { batchId = batchId.ToString() }).ConfigureAwait(false) ?? new BatchRunSnapshot();

            var currentFileTitle = await conn.QueryFirstOrDefaultAsync<string?>(
                """
                WITH latest_jobs AS (
                    SELECT
                        entity_id,
                        state,
                        updated_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY entity_id
                            ORDER BY updated_at DESC, created_at DESC
                        ) AS rn
                    FROM identity_jobs
                    WHERE ingestion_run_id = @batchId
                ),
                active_job AS (
                    SELECT entity_id, state, updated_at
                    FROM latest_jobs
                    WHERE rn = 1
                      AND state IN @activeStates
                    ORDER BY updated_at DESC
                    LIMIT 1
                )
                SELECT cv.value
                FROM active_job aj
                INNER JOIN canonical_values cv ON cv.entity_id = aj.entity_id
                WHERE cv.key IN ('title', 'show_name')
                ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END
                LIMIT 1;
                """,
                new { batchId = batchId.ToString(), activeStates = ActiveStates }).ConfigureAwait(false);

            var work = await conn.QueryFirstOrDefaultAsync<BatchWorkSnapshot>(
                """
                WITH latest_jobs AS (
                    SELECT
                        entity_id,
                        state,
                        updated_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY entity_id
                            ORDER BY updated_at DESC, created_at DESC
                        ) AS rn
                    FROM identity_jobs
                    WHERE ingestion_run_id = @batchId
                ),
                job_states AS (
                    SELECT entity_id, state, updated_at
                    FROM latest_jobs
                    WHERE rn = 1
                ),
                person_counts AS (
                    SELECT
                        pml.media_asset_id AS entity_id,
                        COUNT(DISTINCT p.id) AS total_people,
                        COUNT(DISTINCT CASE WHEN p.enriched_at IS NOT NULL THEN p.id END) AS enriched_people
                    FROM person_media_links pml
                    INNER JOIN persons p ON p.id = pml.person_id
                    GROUP BY pml.media_asset_id
                ),
                canonical_flags AS (
                    SELECT
                        entity_id,
                        MAX(CASE WHEN key = 'stage3_enriched_at' THEN 1 ELSE 0 END) AS stage3_core_done,
                        MAX(CASE WHEN key = 'stage3_enhanced_at' THEN 1 ELSE 0 END) AS stage3_enhancers_done
                    FROM canonical_values
                    WHERE key IN ('stage3_enriched_at', 'stage3_enhanced_at')
                    GROUP BY entity_id
                ),
                item_progress AS (
                    SELECT
                        js.entity_id,
                        CASE
                            WHEN js.state IN ('Ready', 'ReadyWithoutUniverse', 'Completed', 'RetailNoMatch', 'QidNoMatch', 'QidNeedsReview', 'Failed') THEN 100.0
                            WHEN js.state = 'UniverseEnriching' THEN
                                80.0
                                + CASE WHEN COALESCE(cf.stage3_core_done, 0) = 1 THEN 10.0 ELSE 0.0 END
                                + CASE WHEN COALESCE(cf.stage3_enhancers_done, 0) = 1 THEN 10.0 ELSE 0.0 END
                            WHEN js.state = 'Hydrating' THEN
                                50.0
                                + CASE
                                    WHEN COALESCE(pc.total_people, 0) = 0 THEN 20.0
                                    ELSE 20.0 * COALESCE(pc.enriched_people, 0) / pc.total_people
                                  END
                            WHEN js.state = 'QidResolved' THEN 45.0
                            WHEN js.state = 'BridgeSearching' THEN 35.0
                            WHEN js.state IN ('RetailMatched', 'RetailMatchedNeedsReview') THEN 30.0
                            WHEN js.state = 'RetailSearching' THEN 20.0
                            WHEN js.state = 'Queued' THEN 10.0
                            ELSE 0.0
                        END AS progress_percent,
                        CASE
                            WHEN js.state = 'Hydrating' THEN MAX(COALESCE(pc.total_people, 0), 1)
                            WHEN js.state = 'UniverseEnriching' THEN 2
                            ELSE 1
                        END AS work_units_total,
                        CASE
                            WHEN js.state IN ('Ready', 'ReadyWithoutUniverse', 'Completed', 'RetailNoMatch', 'QidNoMatch', 'QidNeedsReview', 'Failed') THEN
                                CASE
                                    WHEN js.state = 'UniverseEnriching' THEN 2
                                    ELSE 1
                                END
                            WHEN js.state = 'Hydrating' THEN
                                CASE
                                    WHEN COALESCE(pc.total_people, 0) = 0 THEN 1
                                    ELSE COALESCE(pc.enriched_people, 0)
                                END
                            WHEN js.state = 'UniverseEnriching' THEN
                                COALESCE(cf.stage3_core_done, 0) + COALESCE(cf.stage3_enhancers_done, 0)
                            WHEN js.state IN ('RetailSearching', 'RetailMatched', 'RetailMatchedNeedsReview', 'BridgeSearching', 'QidResolved') THEN 0
                            ELSE 0
                        END AS work_units_completed
                    FROM job_states js
                    LEFT JOIN person_counts pc ON pc.entity_id = js.entity_id
                    LEFT JOIN canonical_flags cf ON cf.entity_id = js.entity_id
                )
                SELECT
                    COALESCE(AVG(progress_percent), 0) AS AverageProgressPercent,
                    COALESCE(SUM(work_units_total), 0) AS WorkUnitsTotal,
                    COALESCE(SUM(work_units_completed), 0) AS WorkUnitsCompleted
                FROM item_progress;
                """,
                new { batchId = batchId.ToString() }).ConfigureAwait(false) ?? new BatchWorkSnapshot();

            var total = batch.FilesTotal;
            var failed = batch.FilesFailed + snapshot.PipelineFailed;
            var ready = snapshot.FilesReady;
            var readyWithoutUniverse = snapshot.FilesReadyWithoutUniverse;
            var identified = ready + readyWithoutUniverse;
            var review = snapshot.FilesReview;
            var noMatch = snapshot.FilesNoMatch;
            var queued = snapshot.QueuedJobs;
            var active = snapshot.RetailSearching
                + snapshot.RetailMatched
                + snapshot.RetailMatchedNeedsReview
                + snapshot.BridgeSearching
                + snapshot.QidResolved
                + snapshot.Hydrating
                + snapshot.UniverseEnriching;

            var progressed = Math.Max(0, total - queued);
            var pct = total > 0 ? (int)Math.Round(Math.Clamp(work.AverageProgressPercent, 0, 100)) : 0;
            var completed = total > 0 && queued == 0 && active == 0;

            int? etaSecs = null;
            if (progressed > 0 && queued > 0)
            {
                var elapsed = (DateTimeOffset.UtcNow - batch.StartedAt).TotalSeconds;
                var rate = elapsed > 0 ? progressed / elapsed : 0;
                if (rate > 0) etaSecs = (int)Math.Round(queued / rate);
            }

            if (completed && !string.Equals(batch.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                await _batchRepo.CompleteAsync(batchId, "completed", ct).ConfigureAwait(false);
            }

            var lifecycleStage = ResolveLifecycleStage(snapshot, queued, review, completed);
            var currentStage = ResolveStageLabel(lifecycleStage, completed);

            await _eventPublisher.PublishAsync(
                SignalREvents.BatchProgress,
                new BatchProgressEvent(
                    batch.Id,
                    total,
                    progressed,
                    identified,
                    review,
                    noMatch,
                    failed,
                    pct,
                    etaSecs,
                    isFinal || completed,
                    CurrentStage: currentStage,
                    FilesQueued: queued,
                    FilesActive: active,
                    FilesReady: ready,
                    FilesReadyWithoutUniverse: readyWithoutUniverse,
                    CurrentFileTitle: currentFileTitle,
                    LifecycleStage: lifecycleStage,
                    WorkUnitsTotal: work.WorkUnitsTotal,
                    WorkUnitsCompleted: work.WorkUnitsCompleted),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch progress emission failed for {BatchId}", batchId);
        }
    }

    private static string ResolveLifecycleStage(BatchRunSnapshot snapshot, int queued, int review, bool completed)
    {
        if (completed)
            return "Complete";

        if (snapshot.UniverseEnriching > 0)
            return "UniverseEnriching";

        if (snapshot.BridgeSearching > 0 || snapshot.QidResolved > 0 || snapshot.Hydrating > 0)
            return "ResolvingUniverse";

        if (snapshot.RetailSearching > 0 || snapshot.RetailMatched > 0 || snapshot.RetailMatchedNeedsReview > 0)
            return "Identifying";

        if (review > 0)
            return "Review";

        if (queued > 0)
            return "Queued";

        return "Processing";
    }

    private static string ResolveStageLabel(string lifecycleStage, bool completed) =>
        completed ? "Complete" :
        lifecycleStage switch
        {
            "UniverseEnriching" => "Enriching universe",
            "ResolvingUniverse" => "Resolving universe",
            "Identifying" => "Identifying",
            "Review" => "Review",
            "Queued" => "Queued",
            _ => "Processing",
        };

    private sealed class BatchRunSnapshot
    {
        public int FilesReady { get; init; }
        public int FilesReadyWithoutUniverse { get; init; }
        public int FilesReview { get; init; }
        public int FilesNoMatch { get; init; }
        public int PipelineFailed { get; init; }
        public int QueuedJobs { get; init; }
        public int RetailSearching { get; init; }
        public int RetailMatched { get; init; }
        public int RetailMatchedNeedsReview { get; init; }
        public int BridgeSearching { get; init; }
        public int QidResolved { get; init; }
        public int Hydrating { get; init; }
        public int UniverseEnriching { get; init; }
    }

    private sealed class BatchWorkSnapshot
    {
        public double AverageProgressPercent { get; init; }
        public int WorkUnitsTotal { get; init; }
        public int WorkUnitsCompleted { get; init; }
    }
}
