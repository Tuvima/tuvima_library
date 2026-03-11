using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IReaderBookmarkRepository"/>.
/// </summary>
public sealed class ReaderBookmarkRepository : IReaderBookmarkRepository
{
    private readonly IDatabaseConnection _db;

    public ReaderBookmarkRepository(IDatabaseConnection db) => _db = db;

    public Task<IReadOnlyList<ReaderBookmark>> ListByAssetAsync(string userId, Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, asset_id, chapter_index, cfi_position, label, created_at
            FROM   reader_bookmarks
            WHERE  user_id = @user_id AND asset_id = @asset_id
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@asset_id", assetId.ToString());

        using var reader = cmd.ExecuteReader();
        var results = new List<ReaderBookmark>();
        while (reader.Read())
            results.Add(MapBookmark(reader));

        return Task.FromResult<IReadOnlyList<ReaderBookmark>>(results);
    }

    public Task<ReaderBookmark?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, asset_id, chapter_index, cfi_position, label, created_at
            FROM   reader_bookmarks
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapBookmark(reader) : null;
        return Task.FromResult(result);
    }

    public Task InsertAsync(ReaderBookmark bookmark, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reader_bookmarks (id, user_id, asset_id, chapter_index, cfi_position, label, created_at)
            VALUES (@id, @user_id, @asset_id, @chapter_index, @cfi_position, @label, @created_at);
            """;
        cmd.Parameters.AddWithValue("@id", bookmark.Id.ToString());
        cmd.Parameters.AddWithValue("@user_id", bookmark.UserId);
        cmd.Parameters.AddWithValue("@asset_id", bookmark.AssetId.ToString());
        cmd.Parameters.AddWithValue("@chapter_index", bookmark.ChapterIndex);
        cmd.Parameters.AddWithValue("@cfi_position", (object?)bookmark.CfiPosition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@label", (object?)bookmark.Label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", bookmark.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM reader_bookmarks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static ReaderBookmark MapBookmark(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id           = Guid.Parse(r.GetString(0)),
        UserId       = r.GetString(1),
        AssetId      = Guid.Parse(r.GetString(2)),
        ChapterIndex = r.GetInt32(3),
        CfiPosition  = r.IsDBNull(4) ? null : r.GetString(4),
        Label        = r.IsDBNull(5) ? null : r.GetString(5),
        CreatedAt    = DateTime.Parse(r.GetString(6)),
    };
}
