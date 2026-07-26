using System.Collections.Concurrent;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Contracts.Realtime;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Manages batch counter adjustments and SignalR progress emission.
/// Extracted from HydrationPipelineService for single-responsibility and reuse by pipeline workers.
/// </summary>
public sealed class BatchProgressService
{
    private static readonly TimeSpan MinimumEmitInterval = TimeSpan.FromSeconds(1);

    private readonly IIngestionBatchRepository _batchRepo;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<BatchProgressService> _logger;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastProgressEmitUtc = new();

    public BatchProgressService(
        IIngestionBatchRepository batchRepo,
        IEventPublisher eventPublisher,
        ILogger<BatchProgressService> logger)
    {
        _batchRepo = batchRepo;
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
            var now = DateTimeOffset.UtcNow;
            if (!isFinal
                && _lastProgressEmitUtc.TryGetValue(batchId, out var lastEmit)
                && now - lastEmit < MinimumEmitInterval)
            {
                return;
            }

            _lastProgressEmitUtc[batchId] = now;

            var batch = await _batchRepo.GetByIdAsync(batchId, ct).ConfigureAwait(false);
            if (batch is null) return;

            var snapshot = await _batchRepo.GetProgressSnapshotAsync(batchId, ct).ConfigureAwait(false);

            var total = Math.Max(batch.FilesTotal, snapshot.TotalJobs);
            var failed = snapshot.TotalJobs > 0
                ? Math.Max(0, snapshot.PipelineFailed)
                : Math.Max(0, batch.FilesFailed);
            var ready = snapshot.FilesReady;
            var readyWithoutUniverse = snapshot.FilesReadyWithoutUniverse;
            var identified = ready + readyWithoutUniverse;
            var review = snapshot.FilesReview;
            var noMatch = snapshot.FilesNoMatch;
            var active = snapshot.RetailSearching
                + snapshot.BridgeSearching
                + snapshot.Hydrating
                + snapshot.UniverseEnriching;
            var terminal = identified + review + noMatch + failed;
            if (total > 0)
            {
                terminal = Math.Clamp(terminal, 0, total);
                active = Math.Clamp(active, 0, Math.Max(0, total - terminal));
            }

            var queued = total > 0
                ? Math.Max(0, total - terminal - active)
                : snapshot.QueuedJobs + snapshot.RetailMatched + snapshot.QidResolved;

            var progressed = terminal;
            var pct = total > 0 ? (int)Math.Round(Math.Clamp(progressed * 100d / total, 0, 100)) : 0;
            var completed = total > 0 && terminal >= total && active == 0;

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
                    CurrentFileTitle: snapshot.CurrentFileTitle,
                    LifecycleStage: lifecycleStage,
                    WorkUnitsTotal: total,
                    WorkUnitsCompleted: progressed),
                ct).ConfigureAwait(false);

            if (isFinal || completed)
                _lastProgressEmitUtc.TryRemove(batchId, out _);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch progress emission failed for {BatchId}", batchId);
        }
    }

    private static string ResolveLifecycleStage(
        IngestionBatchProgressSnapshot snapshot,
        int queued,
        int review,
        bool completed)
    {
        if (completed)
            return "Complete";

        if (snapshot.UniverseEnriching > 0)
            return "Enriching";

        if (snapshot.Hydrating > 0)
            return "Hydrating";

        if (snapshot.BridgeSearching > 0 || snapshot.QidResolved > 0)
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
            "ResolvingUniverse" => "Resolving universe",
            "Hydrating" => "Retail Match",
            "Enriching" => "Universe enrichment",
            "Identifying" => "Identifying",
            "Review" => "Review",
            "Queued" => "Queued",
            _ => "Processing",
        };

}
