using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IImageCacheRepository"/>.
///
/// Tracks downloaded image content hashes to prevent redundant re-downloads
/// when the same image URL (or identical image content) appears across
/// multiple entities.
///
/// Uses Dapper for type-safe column-to-property mapping.
/// </summary>
public sealed class ImageCacheRepository : IImageCacheRepository
{
    private readonly IDatabaseConnection _db;

    public ImageCacheRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<string?> FindByHashAsync(string contentHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<string>("""
            SELECT file_path
            FROM   image_cache
            WHERE  content_hash = @contentHash;
            """, new { contentHash });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task InsertAsync(
        string contentHash,
        string filePath,
        string? sourceUrl = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var conn = _db.CreateConnection();
        using var transaction = conn.BeginTransaction();
        conn.Execute("""
            INSERT OR IGNORE INTO image_cache
                (content_hash, file_path, source_url, downloaded_at)
            VALUES
                (@contentHash, @filePath, @sourceUrl, @downloadedAt);
            """,
            new
            {
                contentHash,
                filePath,
                sourceUrl = NormalizeSourceUrl(sourceUrl),
                downloadedAt = DateTimeOffset.UtcNow.ToString("O"),
            }, transaction);

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            conn.Execute("""
                INSERT INTO image_cache_sources
                    (source_url, content_hash, first_seen_at)
                VALUES
                    (@sourceUrl, @contentHash, @firstSeenAt)
                ON CONFLICT(source_url) DO UPDATE SET
                    content_hash = excluded.content_hash;
                """,
                new
                {
                    sourceUrl = NormalizeSourceUrl(sourceUrl),
                    contentHash,
                    firstSeenAt = DateTimeOffset.UtcNow.ToString("O"),
                },
                transaction);
        }

        transaction.Commit();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> IsUserOverrideAsync(string contentHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<long?>("""
            SELECT is_user_override
            FROM   image_cache
            WHERE  content_hash = @contentHash;
            """, new { contentHash });

        return Task.FromResult(result.HasValue && result.Value != 0);
    }

    /// <inheritdoc/>
    public Task<string?> FindBySourceUrlAsync(string sourceUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<string>("""
            SELECT cached.file_path
            FROM image_cache_sources source
            JOIN image_cache cached ON cached.content_hash = source.content_hash
            WHERE source.source_url = @normalizedSourceUrl
            UNION ALL
            SELECT file_path
            FROM image_cache
            WHERE source_url IN (@sourceUrl, @normalizedSourceUrl)
            LIMIT 1;
            """,
            new
            {
                sourceUrl,
                normalizedSourceUrl = NormalizeSourceUrl(sourceUrl),
            });

        return Task.FromResult(result);
    }

    private static string? NormalizeSourceUrl(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        var trimmed = sourceUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed;

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
        };

        return builder.Uri.AbsoluteUri;
    }

    /// <inheritdoc/>
    public Task SetUserOverrideAsync(string contentHash, bool isOverride, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE image_cache
            SET    is_user_override = @isOverride
            WHERE  content_hash = @contentHash;
            """,
            new
            {
                isOverride = isOverride ? 1 : 0,
                contentHash,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetPerceptualHashAsync(string contentHash, ulong phash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        // SQLite stores INTEGER as 64-bit signed; cast ulong → long for storage.
        long storedValue = (long)phash;

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE image_cache
            SET    phash = @phash
            WHERE  content_hash = @contentHash;
            """,
            new { phash = storedValue, contentHash });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ulong?> GetPerceptualHashAsync(string contentHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<long?>("""
            SELECT phash
            FROM   image_cache
            WHERE  content_hash = @contentHash;
            """, new { contentHash });

        // Cast long → ulong on read; null if no hash stored.
        return Task.FromResult(result.HasValue ? (ulong?)((ulong)result.Value) : null);
    }
}
