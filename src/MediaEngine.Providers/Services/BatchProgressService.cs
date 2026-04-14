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
            var snapshot = await conn.QueryFirstOrDefaultAsync<BatchRunSnapshot>("""
                WITH latest_jobs AS (
                    SELECT
                        entity_id,
                        state,
                        ROW_NUMBER() OVER (
                            PARTITION BY entity_id
                            ORDER BY updated_at DESC, created_at DESC
                        ) AS rn
                    FROM identity_jobs
                    WHERE ingestion_run_id = @batchId
                ),
                job_states AS (
                    SELECT entity_id, state
                    FROM latest_jobs
                    WHERE rn = 1
                ),
                pending_reviews AS (
                    SELECT DISTINCT entity_id
                    FROM review_queue
                    WHERE status = 'Pending'
                )
                SELECT
                    COALESCE(SUM(CASE WHEN js.state = 'Completed' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesIdentified,
                    COALESCE(SUM(CASE
                        WHEN js.state = 'QidNeedsReview' THEN 1
                        WHEN pr.entity_id IS NOT NULL
                             AND js.state NOT IN (
                                 'Queued',
                                 'RetailSearching',
                                 'RetailMatched',
                                 'RetailMatchedNeedsReview',
                                 'BridgeSearching',
                                 'QidResolved',
                                 'Hydrating'
                             )
                            THEN 1
                        ELSE 0
                    END), 0) AS FilesReview,
                    COALESCE(SUM(CASE WHEN js.state IN ('RetailNoMatch', 'QidNoMatch') THEN 1 ELSE 0 END), 0) AS FilesNoMatch,
                    COALESCE(SUM(CASE WHEN js.state = 'Failed' THEN 1 ELSE 0 END), 0) AS PipelineFailed,
                    COALESCE(SUM(CASE WHEN js.state IN (
                        'RetailSearching',
                        'RetailMatched',
                        'RetailMatchedNeedsReview',
                        'BridgeSearching',
                        'QidResolved',
                        'Hydrating'
                    ) THEN 1 ELSE 0 END), 0) AS FilesActive,
                    COALESCE(SUM(CASE WHEN js.state = 'Queued' THEN 1 ELSE 0 END), 0) AS QueuedJobs,
                    COALESCE(SUM(CASE WHEN js.state = 'RetailSearching' THEN 1 ELSE 0 END), 0) AS RetailSearching,
                    COALESCE(SUM(CASE WHEN js.state = 'RetailMatched' THEN 1 ELSE 0 END), 0) AS RetailMatched,
                    COALESCE(SUM(CASE WHEN js.state = 'RetailMatchedNeedsReview' THEN 1 ELSE 0 END), 0) AS RetailMatchedNeedsReview,
                    COALESCE(SUM(CASE WHEN js.state = 'BridgeSearching' THEN 1 ELSE 0 END), 0) AS BridgeSearching,
                    COALESCE(SUM(CASE WHEN js.state = 'QidResolved' THEN 1 ELSE 0 END), 0) AS QidResolved,
                    COALESCE(SUM(CASE WHEN js.state = 'Hydrating' THEN 1 ELSE 0 END), 0) AS Hydrating
                FROM job_states js
                LEFT JOIN pending_reviews pr ON pr.entity_id = js.entity_id;
                """, new { batchId = batchId.ToString() }).ConfigureAwait(false) ?? new BatchRunSnapshot();

            var total = batch.FilesTotal;
            var failed = batch.FilesFailed + snapshot.PipelineFailed;
            var review = snapshot.FilesReview;
            var identified = snapshot.FilesIdentified;
            var noMatch = snapshot.FilesNoMatch;
            var active = snapshot.FilesActive;
            var queued = Math.Max(0, total - identified - review - noMatch - failed - active);
            var progressed = Math.Max(0, total - queued);
            var pct = total > 0 ? (int)Math.Round(progressed * 100.0 / total) : 0;
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
                    CurrentStage: ResolveStageLabel(snapshot, queued, completed),
                    FilesQueued: queued,
                    FilesActive: active),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch progress emission failed for {BatchId}", batchId);
        }
    }

    private static string ResolveStageLabel(BatchRunSnapshot snapshot, int queued, bool completed) =>
        completed ? "Complete" :
        snapshot.Hydrating > 0 || snapshot.QidResolved > 0 ? "Hydrating" :
        snapshot.BridgeSearching > 0 ? "Resolving universes" :
        snapshot.RetailSearching > 0 ? "Identifying" :
        snapshot.RetailMatched > 0 || snapshot.RetailMatchedNeedsReview > 0 ? "Preparing universe lookup" :
        queued > 0 ? "Queued" :
        "Processing";

    private sealed class BatchRunSnapshot
    {
        public int FilesIdentified { get; init; }
        public int FilesReview { get; init; }
        public int FilesNoMatch { get; init; }
        public int PipelineFailed { get; init; }
        public int FilesActive { get; init; }
        public int QueuedJobs { get; init; }
        public int RetailSearching { get; init; }
        public int RetailMatched { get; init; }
        public int RetailMatchedNeedsReview { get; init; }
        public int BridgeSearching { get; init; }
        public int QidResolved { get; init; }
        public int Hydrating { get; init; }
    }
}
