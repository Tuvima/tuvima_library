using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IProviderResponseCacheRepository"/>.
///
/// Caches raw JSON responses from metadata provider API calls to prevent
/// redundant HTTP requests for the same query.  Entries have a per-provider
/// TTL and support ETag-based conditional revalidation.
/// </summary>
public sealed class ProviderResponseCacheRepository : IProviderResponseCacheRepository
{
    private readonly IDatabaseConnection _db;

    public ProviderResponseCacheRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<CachedResponse?> FindAsync(string cacheKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<(string ResponseJson, string? Etag)>("""
            SELECT response_json AS ResponseJson,
                   etag          AS Etag
            FROM   provider_response_cache
            WHERE  cache_key  = @cacheKey
              AND  expires_at > @now;
            """, new { cacheKey, now = DateTimeOffset.UtcNow.ToString("O") });

        if (row == default)
            return Task.FromResult<CachedResponse?>(null);

        return Task.FromResult<CachedResponse?>(new CachedResponse(row.ResponseJson, row.Etag));
    }

    /// <inheritdoc/>
    public Task UpsertAsync(
        string cacheKey,
        string providerId,
        string queryHash,
        string responseJson,
        string? etag,
        int ttlHours,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseJson);

        var now       = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(ttlHours);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO provider_response_cache
                (cache_key, provider_id, query_hash, response_json, etag, fetched_at, expires_at)
            VALUES
                (@cacheKey, @providerId, @queryHash, @responseJson, @etag, @fetchedAt, @expiresAt)
            ON CONFLICT(cache_key) DO UPDATE SET
                response_json = excluded.response_json,
                etag          = excluded.etag,
                fetched_at    = excluded.fetched_at,
                expires_at    = excluded.expires_at;
            """, new
        {
            cacheKey,
            providerId,
            queryHash,
            responseJson,
            etag,
            fetchedAt = now.ToString("O"),
            expiresAt = expiresAt.ToString("O"),
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string?> FindExpiredEtagAsync(string cacheKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<string?>("""
            SELECT etag
            FROM   provider_response_cache
            WHERE  cache_key = @cacheKey
              AND  etag IS NOT NULL;
            """, new { cacheKey });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task RefreshExpiryAsync(string cacheKey, int ttlHours, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(ttlHours);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE provider_response_cache
            SET    expires_at = @expiresAt
            WHERE  cache_key  = @cacheKey;
            """, new { cacheKey, expiresAt = expiresAt.ToString("O") });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var count = conn.Execute("""
            DELETE FROM provider_response_cache
            WHERE  expires_at <= @now;
            """, new { now = DateTimeOffset.UtcNow.ToString("O") });

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<int> ClearAllAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var count = conn.Execute("DELETE FROM provider_response_cache;");
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<StatsRow>("""
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN expires_at > @now THEN 1 ELSE 0 END) AS Active,
                MIN(fetched_at) AS OldestFetchedAt
            FROM provider_response_cache;
            """, new { now = DateTimeOffset.UtcNow.ToString("O") });

        if (row is null)
            return Task.FromResult(new CacheStats(0, 0, null));

        return Task.FromResult(new CacheStats(row.Total, row.Active, row.OldestFetchedAt));
    }

    // ── Private DTO ─────────────────────────────────────────────────────────

    private sealed class StatsRow
    {
        public int     Total          { get; set; }
        public int     Active         { get; set; }
        public string? OldestFetchedAt { get; set; }
    }
}
