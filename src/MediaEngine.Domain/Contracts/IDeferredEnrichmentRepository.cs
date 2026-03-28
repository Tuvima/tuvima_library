using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="DeferredEnrichmentRequest"/> records.
///
/// The <c>deferred_enrichment_queue</c> table stores Pass 2 enrichment requests
/// that are processed when the system is idle or on a nightly schedule.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface IDeferredEnrichmentRepository
{
    /// <summary>Inserts a new deferred enrichment request.</summary>
    Task InsertAsync(DeferredEnrichmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns pending requests ordered by creation date (oldest first).
    /// Oldest-first ensures FIFO processing.
    /// </summary>
    Task<IReadOnlyList<DeferredEnrichmentRequest>> GetPendingAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns pending requests older than <paramref name="threshold"/>,
    /// ordered by creation date (oldest first). Used by the nightly sweep.
    /// </summary>
    Task<IReadOnlyList<DeferredEnrichmentRequest>> GetStaleAsync(
        TimeSpan threshold,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>Marks a single request as processed.</summary>
    Task MarkProcessedAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Marks all pending requests for a given entity as processed.
    /// Called when the user triggers full hydration manually (both passes
    /// run synchronously), making any pending Pass 2 request redundant.
    /// </summary>
    Task MarkProcessedByEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Returns the number of pending requests.</summary>
    Task<int> CountPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all pending items that failed because a specific provider was down.
    /// </summary>
    Task<IReadOnlyList<DeferredEnrichmentRequest>> GetByFailedProviderAsync(
        string providerName, int limit = 50, CancellationToken ct = default);
}
