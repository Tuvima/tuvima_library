using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IProviderResponseCacheRepository"/>.
///
/// Caches raw JSON responses from metadata provider API calls to prevent
/// redundant HTTP requests for the same query.  Entries have a per-provider
/// TTL and support ETag-based conditional revalidation.
///
/// ORM-less: all SQL is executed via <see cref="SqliteCommand"/>.
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT response_json, etag
            FROM   provider_response_cache
            WHERE  cache_key  = @key
              AND  expires_at > @now;
            """;
        cmd.Parameters.AddWithValue("@key", cacheKey);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var json = reader.GetString(0);
            var etag = reader.IsDBNull(1) ? null : reader.GetString(1);
            return Task.FromResult<CachedResponse?>(new CachedResponse(json, etag));
        }

        return Task.FromResult<CachedResponse?>(null);
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

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(ttlHours);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO provider_response_cache
                (cache_key, provider_id, query_hash, response_json, etag, fetched_at, expires_at)
            VALUES
                (@key, @pid, @qhash, @json, @etag, @fetched, @expires)
            ON CONFLICT(cache_key) DO UPDATE SET
                response_json = excluded.response_json,
                etag          = excluded.etag,
                fetched_at    = excluded.fetched_at,
                expires_at    = excluded.expires_at;
            """;

        cmd.Parameters.AddWithValue("@key",     cacheKey);
        cmd.Parameters.AddWithValue("@pid",     providerId);
        cmd.Parameters.AddWithValue("@qhash",   queryHash);
        cmd.Parameters.AddWithValue("@json",    responseJson);
        cmd.Parameters.AddWithValue("@etag",    (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fetched", now.ToString("O"));
        cmd.Parameters.AddWithValue("@expires", expiresAt.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string?> FindExpiredEtagAsync(string cacheKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT etag
            FROM   provider_response_cache
            WHERE  cache_key = @key
              AND  etag IS NOT NULL;
            """;
        cmd.Parameters.AddWithValue("@key", cacheKey);

        var result = cmd.ExecuteScalar();
        return Task.FromResult(result as string);
    }

    /// <inheritdoc/>
    public Task RefreshExpiryAsync(string cacheKey, int ttlHours, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(ttlHours);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE provider_response_cache
            SET    expires_at = @expires
            WHERE  cache_key  = @key;
            """;
        cmd.Parameters.AddWithValue("@key",     cacheKey);
        cmd.Parameters.AddWithValue("@expires", expiresAt.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM provider_response_cache
            WHERE  expires_at <= @now;
            """;
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        var count = cmd.ExecuteNonQuery();
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<int> ClearAllAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM provider_response_cache;";

        var count = cmd.ExecuteNonQuery();
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*),
                SUM(CASE WHEN expires_at > @now THEN 1 ELSE 0 END),
                MIN(fetched_at)
            FROM provider_response_cache;
            """;
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var total   = reader.GetInt32(0);
            var active  = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            var oldest  = reader.IsDBNull(2) ? null : reader.GetString(2);
            return Task.FromResult(new CacheStats(total, active, oldest));
        }

        return Task.FromResult(new CacheStats(0, 0, null));
    }
}
