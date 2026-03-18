using Dapper;
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

        var row = conn.QueryFirstOrDefault<(string CacheKey, string NormalizedTitle, string MediaType,
            string? WikidataQid, double Confidence, string? EntityLabel,
            string CreatedAt, string ExpiresAt)>("""
            SELECT cache_key AS CacheKey, normalized_title AS NormalizedTitle, media_type AS MediaType,
                   wikidata_qid AS WikidataQid, confidence AS Confidence, entity_label AS EntityLabel,
                   created_at AS CreatedAt, expires_at AS ExpiresAt
            FROM resolver_cache
            WHERE cache_key = @cacheKey
              AND expires_at > @now
            """, new { cacheKey, now = DateTimeOffset.UtcNow.ToString("O") });

        if (row == default)
            return Task.FromResult<ResolverCacheEntry?>(null);

        var entry = new ResolverCacheEntry(
            CacheKey:        row.CacheKey,
            NormalizedTitle: row.NormalizedTitle,
            MediaType:       row.MediaType,
            WikidataQid:     row.WikidataQid,
            Confidence:      row.Confidence,
            EntityLabel:     row.EntityLabel,
            CreatedAt:       DateTimeOffset.Parse(row.CreatedAt),
            ExpiresAt:       DateTimeOffset.Parse(row.ExpiresAt));

        return Task.FromResult<ResolverCacheEntry?>(entry);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(ResolverCacheEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = _db.CreateConnection();
        conn.Execute("""
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
                expires_at   = excluded.expires_at
            """, new
        {
            key        = entry.CacheKey,
            title      = entry.NormalizedTitle,
            mediaType  = entry.MediaType,
            qid        = entry.WikidataQid,
            confidence = entry.Confidence,
            label      = entry.EntityLabel,
            created    = entry.CreatedAt.ToString("O"),
            expires    = entry.ExpiresAt.ToString("O"),
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var deleted = conn.Execute(
            "DELETE FROM resolver_cache WHERE expires_at <= @now",
            new { now = DateTimeOffset.UtcNow.ToString("O") });

        return Task.FromResult(deleted);
    }
}
