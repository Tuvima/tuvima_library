using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IReaderHighlightRepository"/>.
/// </summary>
public sealed class ReaderHighlightRepository : IReaderHighlightRepository
{
    private readonly IDatabaseConnection _db;

    public ReaderHighlightRepository(IDatabaseConnection db) => _db = db;

    public Task<IReadOnlyList<ReaderHighlight>> ListByAssetAsync(string userId, Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, asset_id, chapter_index, start_offset, end_offset,
                   selected_text, color, note_text, created_at
            FROM   reader_highlights
            WHERE  user_id = @user_id AND asset_id = @asset_id
            ORDER BY chapter_index, start_offset;
            """;
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@asset_id", assetId.ToString());

        using var reader = cmd.ExecuteReader();
        var results = new List<ReaderHighlight>();
        while (reader.Read())
            results.Add(MapHighlight(reader));

        return Task.FromResult<IReadOnlyList<ReaderHighlight>>(results);
    }

    public Task<ReaderHighlight?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, asset_id, chapter_index, start_offset, end_offset,
                   selected_text, color, note_text, created_at
            FROM   reader_highlights
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapHighlight(reader) : null;
        return Task.FromResult(result);
    }

    public Task InsertAsync(ReaderHighlight highlight, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reader_highlights
                (id, user_id, asset_id, chapter_index, start_offset, end_offset,
                 selected_text, color, note_text, created_at)
            VALUES
                (@id, @user_id, @asset_id, @chapter_index, @start_offset, @end_offset,
                 @selected_text, @color, @note_text, @created_at);
            """;
        cmd.Parameters.AddWithValue("@id", highlight.Id.ToString());
        cmd.Parameters.AddWithValue("@user_id", highlight.UserId);
        cmd.Parameters.AddWithValue("@asset_id", highlight.AssetId.ToString());
        cmd.Parameters.AddWithValue("@chapter_index", highlight.ChapterIndex);
        cmd.Parameters.AddWithValue("@start_offset", highlight.StartOffset);
        cmd.Parameters.AddWithValue("@end_offset", highlight.EndOffset);
        cmd.Parameters.AddWithValue("@selected_text", highlight.SelectedText);
        cmd.Parameters.AddWithValue("@color", highlight.Color);
        cmd.Parameters.AddWithValue("@note_text", (object?)highlight.NoteText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", highlight.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Guid id, string? color, string? noteText, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE reader_highlights
            SET    color     = COALESCE(@color, color),
                   note_text = @note_text
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@color", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note_text", (object?)noteText ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM reader_highlights WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static ReaderHighlight MapHighlight(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id           = Guid.Parse(r.GetString(0)),
        UserId       = r.GetString(1),
        AssetId      = Guid.Parse(r.GetString(2)),
        ChapterIndex = r.GetInt32(3),
        StartOffset  = r.GetInt32(4),
        EndOffset    = r.GetInt32(5),
        SelectedText = r.GetString(6),
        Color        = r.GetString(7),
        NoteText     = r.IsDBNull(8) ? null : r.GetString(8),
        CreatedAt    = DateTime.Parse(r.GetString(9)),
    };
}
