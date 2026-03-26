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
                sourceUrl,
                downloadedAt = DateTimeOffset.UtcNow.ToString("O"),
            });

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
            SELECT file_path
            FROM   image_cache
            WHERE  source_url = @sourceUrl
            LIMIT  1;
            """, new { sourceUrl });

        return Task.FromResult(result);
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
