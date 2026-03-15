namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Caches identity resolution decisions (normalized_title + media_type → QID + confidence)
/// to avoid redundant 4-tier Wikidata resolution for the same logical entity.
///
/// Different from <see cref="IProviderResponseCacheRepository"/> which caches raw HTTP responses.
/// This cache captures the DECISION made by the resolver (which QID won and at what confidence),
/// eliminating the need to re-run multi-tier resolution logic for common queries.
///
/// Primary use case: 22 TV episodes or 12 album tracks sharing the same series/album title.
/// </summary>
public interface IResolverCacheRepository
{
    /// <summary>
    /// Looks up a cached resolution by the SHA-256 hash of (normalized_title + media_type).
    /// Returns null if not found or expired.
    /// </summary>
    Task<ResolverCacheEntry?> FindAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a cached resolution.
    /// </summary>
    Task UpsertAsync(ResolverCacheEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Deletes all entries whose <c>expires_at</c> is in the past.
    /// Returns the number of rows purged.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// A cached identity resolution decision.
/// </summary>
/// <param name="CacheKey">SHA-256 hash of (normalized_title + "|" + media_type).</param>
/// <param name="NormalizedTitle">The cleaned title used for lookup.</param>
/// <param name="MediaType">The media type used for lookup.</param>
/// <param name="WikidataQid">Resolved QID (may be null if resolution failed).</param>
/// <param name="Confidence">Confidence of the resolution (0.0–1.0).</param>
/// <param name="EntityLabel">Human-readable label from Wikidata.</param>
/// <param name="CreatedAt">When this cache entry was created.</param>
/// <param name="ExpiresAt">When this cache entry expires (default: 7 days).</param>
public record ResolverCacheEntry(
    string CacheKey,
    string NormalizedTitle,
    string MediaType,
    string? WikidataQid,
    double Confidence,
    string? EntityLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
