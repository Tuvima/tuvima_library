namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the fan-out search results cache.
/// Stores serialized search results per entity so the Edit panel can show
/// previously fetched candidates without re-querying providers.
/// </summary>
public interface ISearchResultsCacheRepository
{
    /// <summary>
    /// Returns cached search results for the given entity, or null if not found or expired.
    /// </summary>
    /// <param name="entityId">The entity (work) ID.</param>
    /// <param name="maxAgeDays">Maximum age in days before the entry is considered stale.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> FindAsync(Guid entityId, int maxAgeDays = 30, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces cached search results for the given entity.
    /// </summary>
    /// <param name="entityId">The entity (work) ID.</param>
    /// <param name="resultsJson">Serialized JSON of the search results list.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertAsync(Guid entityId, string resultsJson, CancellationToken ct = default);

    /// <summary>
    /// Deletes entries older than the specified number of days.
    /// Returns the count of deleted entries.
    /// </summary>
    Task<int> PurgeExpiredAsync(int maxAgeDays = 30, CancellationToken ct = default);
}
