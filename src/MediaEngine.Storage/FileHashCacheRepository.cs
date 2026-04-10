using System.Globalization;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IFileHashCacheRepository"/> backed by
/// the <c>file_hash_cache</c> table (migration M-085).
/// </summary>
public sealed class FileHashCacheRepository : IFileHashCacheRepository
{
    private readonly IDatabaseConnection _db;

    public FileHashCacheRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<string?> TryGetAsync(
        string absolutePath,
        long sizeBytes,
        DateTimeOffset mtimeUtc,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<(long SizeBytes, string MtimeUtc, string Sha256)?>("""
            SELECT size_bytes AS SizeBytes,
                   mtime_utc  AS MtimeUtc,
                   sha256     AS Sha256
            FROM   file_hash_cache
            WHERE  absolute_path = @path
            LIMIT  1;
            """, new { path = absolutePath });

        if (row is null) return Task.FromResult<string?>(null);

        // Stale-row detection: any mismatch on size or mtime means the file
        // has changed since we last hashed it — the caller must re-hash.
        if (row.Value.SizeBytes != sizeBytes)
            return Task.FromResult<string?>(null);

        if (!DateTimeOffset.TryParse(
                row.Value.MtimeUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var storedMtime))
        {
            return Task.FromResult<string?>(null);
        }

        // Round to seconds — filesystem mtime precision varies across platforms.
        if (Math.Abs((storedMtime - mtimeUtc).TotalSeconds) > 1.0)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(row.Value.Sha256);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(
        string absolutePath,
        long sizeBytes,
        DateTimeOffset mtimeUtc,
        string sha256,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO file_hash_cache (absolute_path, size_bytes, mtime_utc, sha256, cached_at)
            VALUES (@path, @size, @mtime, @sha256, @now)
            ON CONFLICT(absolute_path) DO UPDATE SET
                size_bytes = excluded.size_bytes,
                mtime_utc  = excluded.mtime_utc,
                sha256     = excluded.sha256,
                cached_at  = excluded.cached_at;
            """,
            new
            {
                path   = absolutePath,
                size   = sizeBytes,
                mtime  = mtimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                sha256,
                now    = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string absolutePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        using var conn = _db.CreateConnection();
        conn.Execute(
            "DELETE FROM file_hash_cache WHERE absolute_path = @path;",
            new { path = absolutePath });

        return Task.CompletedTask;
    }
}
