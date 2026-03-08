using Microsoft.Data.Sqlite;
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
/// ORM-less: all SQL is executed via <see cref="SqliteCommand"/>.
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT file_path
            FROM   image_cache
            WHERE  content_hash = @hash;
            """;
        cmd.Parameters.AddWithValue("@hash", contentHash);

        var result = cmd.ExecuteScalar();
        return Task.FromResult(result as string);
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO image_cache
                (content_hash, file_path, source_url, downloaded_at)
            VALUES
                (@hash, @path, @url, @at);
            """;

        cmd.Parameters.AddWithValue("@hash", contentHash);
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@url",  (object?)sourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at",   DateTimeOffset.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }
}
