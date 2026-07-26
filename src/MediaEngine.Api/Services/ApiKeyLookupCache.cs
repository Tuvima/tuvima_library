using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace MediaEngine.Api.Services;

/// <summary>
/// Caches <see cref="IApiKeyRepository.FindByHashedKeyAsync"/> lookups so that
/// <c>ApiKeyMiddleware</c> — which runs this lookup on EVERY authenticated
/// request — does not open a SQLite connection and run a query per request.
/// </summary>
/// <summary>
/// In-memory, short-TTL implementation of <see cref="IApiKeyLookupCache"/>.
///
/// <c>ApiKeyMiddleware</c> calls <see cref="FindByHashedKeyAsync"/> on every
/// authenticated request. Without this cache, that means a blocking SQLite
/// connection open plus a query per request (see
/// <c>MediaEngine.Storage.ApiKeyRepository.FindByHashedKeyAsync</c>). This wraps
/// <see cref="IApiKeyRepository"/> with a private, service-owned
/// <see cref="MemoryCache"/> — deliberately not the shared app <c>IMemoryCache</c>
/// registered via <c>AddMemoryCache()</c> — so that <see cref="InvalidateAll"/> can
/// discard this cache's entries without touching unrelated cached data elsewhere
/// in the app.
///
/// Cache keys are the SHA-256 hashed key values already used everywhere else in
/// this subsystem — never the plaintext key.
///
/// Both found and "not found" results are cached. Caching negative results is
/// deliberate: it stops a flood of invalid-key guesses from forcing a database
/// round trip on every single request.
///
/// SECURITY:
/// • Hashed keys are used as cache keys but MUST NOT be logged anywhere.
/// • The 30-second absolute TTL bounds how long a newly revoked (or re-created)
///   key can remain stale in the cache — worst-case revocation propagation is
///   30 seconds for any path that does not call <see cref="InvalidateAll"/>.
///   <c>AdminEndpoints</c> calls <see cref="InvalidateAll"/> on every key
///   create/revoke, so the common case is near-instant; the TTL is only the
///   ceiling.
/// • <see cref="MemoryCacheOptions.SizeLimit"/> is capped at 1024 entries so a
///   flood of random/invalid keys cannot grow this cache without bound.
/// </summary>
public sealed class ApiKeyLookupCache : IApiKeyLookupCache, IDisposable
{
    private const int TtlSeconds = 30;
    private const int SizeLimit  = 1024;

    private readonly IApiKeyRepository _repo;
    private readonly MemoryCache _cache;

    /// <summary>
    /// "Generation" token used for bulk invalidation. Every cache entry is tagged
    /// with an expiration token derived from the CURRENT generation's
    /// <see cref="CancellationTokenSource"/>. <see cref="InvalidateAll"/> swaps in a
    /// fresh generation and cancels the old one, which lazily expires every entry
    /// tagged with it — without disposing or replacing the <see cref="MemoryCache"/>
    /// instance itself, and without a "clear everything" API that would need to be
    /// verified for availability across target frameworks.
    /// </summary>
    private CancellationTokenSource _generation = new();

    public ApiKeyLookupCache(IApiKeyRepository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        _repo  = repo;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = SizeLimit });
    }

    /// <inheritdoc/>
    public async Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hashedKey);

        if (_cache.TryGetValue(hashedKey, out CachedLookup? cached) && cached is not null)
            return cached.Value;

        // Read the current generation BEFORE the (potentially slow) repository call.
        // If an InvalidateAll() fires while the query is in flight, the row we read
        // may predate the key mutation — but because the entry is tagged with the
        // OLD generation's token (which that InvalidateAll just cancelled), the
        // cache discards it immediately instead of serving stale data for the TTL.
        var generation = Volatile.Read(ref _generation);

        var result = await _repo.FindByHashedKeyAsync(hashedKey, ct).ConfigureAwait(false);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(TtlSeconds),
            Size = 1,
        };
        options.AddExpirationToken(new CancellationChangeToken(generation.Token));

        // Cache both hits and misses — see class remarks.
        _cache.Set(hashedKey, new CachedLookup(result), options);

        return result;
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        // Swap in a fresh generation and cancel the old one. Any FindByHashedKeyAsync
        // call already in flight may still hold a reference to the OLD
        // CancellationTokenSource; it is deliberately NOT disposed here — only
        // cancelled — so that reading its `.Token` can never throw
        // ObjectDisposedException on a racing caller. Cancel() is the only thing
        // ever called on it (never CancelAfter), so the old instance holds no
        // timer/unmanaged resource and is safe to leave for the garbage collector.
        var old = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        old.Cancel();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _generation.Dispose();
    }

    // Wrapper so a cached "not found" (null ApiKey) is still a distinct, non-null
    // cache entry — keeps TryGetValue's null-vs-missing semantics unambiguous.
    private sealed record CachedLookup(ApiKey? Value);
}
