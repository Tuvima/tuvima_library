namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Contract for the deferred Pass 2 (Universe Lookup) enrichment service.
///
/// The service runs as a background worker that processes pending Pass 2
/// requests when the ingestion pipeline is idle, on a configurable nightly
/// schedule, or when manually triggered via the Dashboard.
///
/// Implementations live in <c>MediaEngine.Providers</c>.
/// </summary>
public interface IDeferredEnrichmentService
{
    /// <summary>
    /// Signals the background worker to start processing pending Pass 2
    /// requests immediately, bypassing the idle detection check.
    /// Returns the number of pending items at the time of triggering.
    /// </summary>
    Task<int> TriggerImmediateProcessingAsync(CancellationToken ct = default);

    /// <summary>
    /// The approximate number of pending Pass 2 requests.
    /// </summary>
    int PendingCount { get; }
}
