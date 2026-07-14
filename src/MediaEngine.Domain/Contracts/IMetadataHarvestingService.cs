using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Admission-only contract for the metadata harvesting queue. Services that
/// discover follow-up work depend on this narrow interface instead of the
/// hosted worker that processes the queue.
/// </summary>
public interface IMetadataHarvestQueueAdmission
{
    /// <summary>
    /// Waits until the bounded queue accepts the request. Caller cancellation is
    /// observed and accepted requests are never silently discarded.
    /// </summary>
    ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default);

    /// <summary>Approximate waiting, queued, and executing request count.</summary>
    int PendingCount { get; }
}

/// <summary>
/// Contract for the background metadata harvesting queue.
///
/// The service accepts <see cref="HarvestRequest"/> items from the ingestion
/// pipeline and processes them asynchronously on a background channel, keeping
/// ingestion non-blocking. Each request is routed to the appropriate external
/// provider adapters based on media type and entity type.
///
/// Implementations live in <c>MediaEngine.Providers</c>.
/// Spec: Phase 9 – Non-Blocking Harvesting.
/// </summary>
public interface IMetadataHarvestingService : IMetadataHarvestQueueAdmission
{
    /// <summary>
    /// Processes a single harvest request synchronously, bypassing the background
    /// channel. Used when person enrichment must complete before the caller returns
    /// (e.g. during review queue resolution).
    /// </summary>
    /// <param name="request">The harvest request to process immediately.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ProcessSynchronousAsync(HarvestRequest request, CancellationToken ct = default);
}
