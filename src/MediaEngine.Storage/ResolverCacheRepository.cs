using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IResolverCacheRepository"/>.
/// Caches identity resolution decisions to avoid redundant 4-tier Wikidata resolution.
/// </summary>
public sealed class ResolverCacheRepository : IResolverCacheRepository
{
    private readonly IDatabaseConnection _db;

    public ResolverCacheRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<ResolverCacheEntry?> FindAsync(string cacheKey, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cache_key, normalized_title, media_type, wikidata_qid,
                   confidence, entity_label, created_at, expires_at
            FROM resolver_cache
            WHERE cache_key = @key
              AND expires_at > @now;
            """;
        cmd.Parameters.AddWithValue("@key", cacheKey);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<ResolverCacheEntry?>(null);

        var entry = new ResolverCacheEntry(
            CacheKey:        reader.GetString(0),
            NormalizedTitle: reader.GetString(1),
            MediaType:       reader.GetString(2),
            WikidataQid:     reader.IsDBNull(3) ? null : reader.GetString(3),
            Confidence:      reader.GetDouble(4),
            EntityLabel:     reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt:       DateTimeOffset.Parse(reader.GetString(6)),
            ExpiresAt:       DateTimeOffset.Parse(reader.GetString(7)));

        return Task.FromResult<ResolverCacheEntry?>(entry);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(ResolverCacheEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO resolver_cache
                (cache_key, normalized_title, media_type, wikidata_qid,
                 confidence, entity_label, created_at, expires_at)
            VALUES
                (@key, @title, @mediaType, @qid,
                 @confidence, @label, @created, @expires)
            ON CONFLICT(cache_key) DO UPDATE SET
                wikidata_qid = excluded.wikidata_qid,
                confidence   = excluded.confidence,
                entity_label = excluded.entity_label,
                created_at   = excluded.created_at,
                expires_at   = excluded.expires_at;
            """;

        cmd.Parameters.AddWithValue("@key",        entry.CacheKey);
        cmd.Parameters.AddWithValue("@title",      entry.NormalizedTitle);
        cmd.Parameters.AddWithValue("@mediaType",  entry.MediaType);
        cmd.Parameters.AddWithValue("@qid",        (object?)entry.WikidataQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", entry.Confidence);
        cmd.Parameters.AddWithValue("@label",      (object?)entry.EntityLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created",    entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@expires",    entry.ExpiresAt.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM resolver_cache WHERE expires_at <= @now;";
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        int deleted = cmd.ExecuteNonQuery();
        return Task.FromResult(deleted);
    }
}
