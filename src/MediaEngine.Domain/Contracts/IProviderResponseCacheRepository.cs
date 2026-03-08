namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the provider HTTP response cache.
///
/// The <c>provider_response_cache</c> table stores raw JSON responses from
/// metadata provider API calls, keyed by a hash of the full request URL.
/// This prevents redundant API calls when:
/// <list type="bullet">
///   <item>Multiple files in the same series/album/volume share metadata.</item>
///   <item>The same entity is re-queried within the cache TTL window.</item>
/// </list>
///
/// The cache is a <b>performance optimization only</b>; it is not a source of
/// truth. The database can be wiped and rebuilt from <c>library.xml</c> sidecars
/// without any data loss — the cache simply repopulates on next hydration.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface IProviderResponseCacheRepository
{
    /// <summary>
    /// Looks up a cached response by its cache key (provider_id + URL hash).
    /// Returns the raw JSON response body if found and not expired, or <c>null</c>
    /// if the entry is missing or expired.
    /// </summary>
    /// <param name="cacheKey">The cache key (provider_id + strategy + query hash).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of (responseJson, etag) if a valid cache entry exists,
    /// or <c>null</c> if not found or expired.
    /// </returns>
    Task<CachedResponse?> FindAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces a cached response entry.
    /// </summary>
    /// <param name="cacheKey">The cache key (provider_id + strategy + query hash).</param>
    /// <param name="providerId">The provider GUID.</param>
    /// <param name="queryHash">The SHA-256 hash of the request URL.</param>
    /// <param name="responseJson">The raw JSON response body.</param>
    /// <param name="etag">The HTTP ETag header value, if returned (nullable).</param>
    /// <param name="ttlHours">Time-to-live in hours for this entry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertAsync(
        string cacheKey,
        string providerId,
        string queryHash,
        string responseJson,
        string? etag,
        int ttlHours,
        CancellationToken ct = default);

    /// <summary>
    /// Finds an expired entry that still has an ETag for conditional revalidation.
    /// Returns the ETag if the entry exists (even if expired), or <c>null</c>.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> FindExpiredEtagAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the expiry on an existing entry (used when a 304 Not Modified
    /// response is received, indicating the cached data is still valid).
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="ttlHours">New TTL in hours from now.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshExpiryAsync(string cacheKey, int ttlHours, CancellationToken ct = default);

    /// <summary>
    /// Deletes all expired entries.
    /// Returns the number of entries removed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes all entries (full cache clear).
    /// Returns the number of entries removed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<int> ClearAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns total number of cached entries and number of non-expired entries.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<CacheStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// A cached provider response with its JSON body and optional ETag.
/// </summary>
/// <param name="ResponseJson">The raw JSON response body.</param>
/// <param name="Etag">The HTTP ETag header value, if available.</param>
public sealed record CachedResponse(string ResponseJson, string? Etag);

/// <summary>
/// Cache statistics for display in the Maintenance tab.
/// </summary>
/// <param name="TotalEntries">Total entries (including expired).</param>
/// <param name="ActiveEntries">Non-expired entries.</param>
/// <param name="OldestEntryAt">Timestamp of the oldest entry (nullable if empty).</param>
public sealed record CacheStats(int TotalEntries, int ActiveEntries, string? OldestEntryAt);
