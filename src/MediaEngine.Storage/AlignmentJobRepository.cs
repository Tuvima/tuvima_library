using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IAlignmentJobRepository"/>.
/// Manages WhisperSync alignment jobs for ebook-to-audiobook synchronisation.
/// </summary>
public sealed class AlignmentJobRepository : IAlignmentJobRepository
{
    private readonly IDatabaseConnection _db;

    public AlignmentJobRepository(IDatabaseConnection db) => _db = db;

    public Task<AlignmentJob?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, ebook_asset_id, audiobook_asset_id, status,
                   alignment_data, error_message, created_at, completed_at
            FROM   alignment_jobs
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapJob(reader) : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<AlignmentJob>> ListByAssetAsync(Guid ebookAssetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, ebook_asset_id, audiobook_asset_id, status,
                   alignment_data, error_message, created_at, completed_at
            FROM   alignment_jobs
            WHERE  ebook_asset_id = @ebook_asset_id
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("@ebook_asset_id", ebookAssetId.ToString());

        using var reader = cmd.ExecuteReader();
        var results = new List<AlignmentJob>();
        while (reader.Read())
            results.Add(MapJob(reader));

        return Task.FromResult<IReadOnlyList<AlignmentJob>>(results);
    }

    public Task<AlignmentJob?> FindPendingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, ebook_asset_id, audiobook_asset_id, status,
                   alignment_data, error_message, created_at, completed_at
            FROM   alignment_jobs
            WHERE  status = 'Pending'
            ORDER BY created_at ASC
            LIMIT  1;
            """;

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapJob(reader) : null;
        return Task.FromResult(result);
    }

    public Task InsertAsync(AlignmentJob job, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alignment_jobs
                (id, ebook_asset_id, audiobook_asset_id, status,
                 alignment_data, error_message, created_at, completed_at)
            VALUES
                (@id, @ebook_asset_id, @audiobook_asset_id, @status,
                 @alignment_data, @error_message, @created_at, @completed_at);
            """;
        cmd.Parameters.AddWithValue("@id", job.Id.ToString());
        cmd.Parameters.AddWithValue("@ebook_asset_id", job.EbookAssetId.ToString());
        cmd.Parameters.AddWithValue("@audiobook_asset_id", job.AudiobookAssetId.ToString());
        cmd.Parameters.AddWithValue("@status", job.Status.ToString());
        cmd.Parameters.AddWithValue("@alignment_data", (object?)job.AlignmentData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", (object?)job.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", job.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completed_at", job.CompletedAt.HasValue
            ? (object)job.CompletedAt.Value.ToString("O")
            : DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(Guid id, AlignmentJobStatus status, string? alignmentData, string? errorMessage, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE alignment_jobs
            SET    status         = @status,
                   alignment_data = @alignment_data,
                   error_message  = @error_message,
                   completed_at   = CASE WHEN @status IN ('Completed', 'Failed')
                                         THEN @completed_at
                                         ELSE completed_at END
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@alignment_data", (object?)alignmentData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", (object?)errorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@completed_at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM alignment_jobs WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static AlignmentJob MapJob(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id                = Guid.Parse(r.GetString(0)),
        EbookAssetId      = Guid.Parse(r.GetString(1)),
        AudiobookAssetId  = Guid.Parse(r.GetString(2)),
        Status            = Enum.Parse<AlignmentJobStatus>(r.GetString(3)),
        AlignmentData     = r.IsDBNull(4) ? null : r.GetString(4),
        ErrorMessage      = r.IsDBNull(5) ? null : r.GetString(5),
        CreatedAt         = DateTime.Parse(r.GetString(6)),
        CompletedAt       = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)),
    };
}
