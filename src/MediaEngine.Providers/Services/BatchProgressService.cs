using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Events;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Manages batch counter adjustments and SignalR progress emission.
/// Extracted from HydrationPipelineService for single-responsibility and reuse by pipeline workers.
/// </summary>
public sealed class BatchProgressService
{
    private readonly IIngestionBatchRepository _batchRepo;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<BatchProgressService> _logger;

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
    /// Shifts one file from Identified to Review in the batch counters.
    /// Called when a review item is created for an already-counted file.
    /// </summary>
    public async Task ShiftToReviewAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is null) return;
        try
        {
            var batch = await _batchRepo.GetByIdAsync(batchId.Value, ct).ConfigureAwait(false);
            if (batch is null || batch.FilesIdentified <= 0) return;

            await _batchRepo.UpdateCountsAsync(
                batchId.Value,
                batch.FilesTotal,
                batch.FilesProcessed,
                batch.FilesIdentified - 1,
                batch.FilesReview + 1,
                batch.FilesNoMatch,
                batch.FilesFailed,
                ct).ConfigureAwait(false);

            await EmitProgressAsync(batchId.Value, isFinal: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch review adjustment failed for {BatchId}", batchId);
        }
    }

    /// <summary>
    /// Shifts one file from Review to Identified in the batch counters.
    /// Called when a review item is auto-resolved.
    /// </summary>
    public async Task ShiftToIdentifiedAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is null) return;
        try
        {
            var batch = await _batchRepo.GetByIdAsync(batchId.Value, ct).ConfigureAwait(false);
            if (batch is null || batch.FilesReview <= 0) return;

            await _batchRepo.UpdateCountsAsync(
                batchId.Value,
                batch.FilesTotal,
                batch.FilesProcessed,
                batch.FilesIdentified + 1,
                batch.FilesReview - 1,
                batch.FilesNoMatch,
                batch.FilesFailed,
                ct).ConfigureAwait(false);

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

            var processed = batch.FilesProcessed;
            var total     = batch.FilesTotal;
            var pct       = total > 0 ? (int)Math.Round(processed * 100.0 / total) : 0;

            int? etaSecs = null;
            if (processed > 0 && total > processed)
            {
                var elapsed = (DateTimeOffset.UtcNow - batch.StartedAt).TotalSeconds;
                var rate    = elapsed > 0 ? processed / elapsed : 0;
                if (rate > 0) etaSecs = (int)Math.Round((total - processed) / rate);
            }

            await _eventPublisher.PublishAsync(
                SignalREvents.BatchProgress,
                new BatchProgressEvent(
                    batch.Id, total, processed,
                    batch.FilesIdentified, batch.FilesReview,
                    batch.FilesNoMatch, batch.FilesFailed,
                    pct, etaSecs, isFinal),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch progress emission failed for {BatchId}", batchId);
        }
    }
}
