using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Async-first persistence contract for the system activity ledger.
///
/// All writes are non-blocking — callers should fire-and-forget in background
/// contexts (e.g. after ingestion completes) to avoid slowing the critical path.
/// </summary>
public interface ISystemActivityRepository
{
    /// <summary>
    /// Appends a single activity entry to the ledger.
    /// Must never throw on duplicate or constraint violations.
    /// </summary>
    Task LogAsync(SystemActivityEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent activity entries, ordered newest-first.
    /// </summary>
    /// <param name="limit">Maximum entries to return (default 50).</param>
    Task<IReadOnlyList<SystemActivityEntry>> GetRecentAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all entries older than <paramref name="retentionDays"/> days.
    /// Returns the number of rows deleted.
    /// </summary>
    Task<int> PruneOlderThanAsync(int retentionDays, CancellationToken ct = default);

    /// <summary>
    /// Returns the total number of entries in the ledger.
    /// </summary>
    Task<long> CountAsync(CancellationToken ct = default);
}
