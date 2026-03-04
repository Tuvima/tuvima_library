using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

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
public interface IMetadataHarvestingService
{
    /// <summary>
    /// Enqueues a harvest request for asynchronous processing.
    /// Returns immediately — the caller does not wait for the harvest to complete.
    ///
    /// The underlying channel is bounded (capacity 500, DropOldest policy) to
    /// prevent memory growth during heavy ingestion bursts.
    /// </summary>
    /// <param name="request">The harvest request to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default);

    /// <summary>
    /// The approximate number of harvest requests currently waiting to be processed.
    /// Useful for monitoring and diagnostics only; not guaranteed to be exact.
    /// </summary>
    int PendingCount { get; }
}
