using Dapper;
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
        var row = conn.QueryFirstOrDefault<AlignmentJobRow>("""
            SELECT id             AS Id,
                   ebook_asset_id    AS EbookAssetId,
                   audiobook_asset_id AS AudiobookAssetId,
                   status            AS Status,
                   alignment_data    AS AlignmentData,
                   error_message     AS ErrorMessage,
                   created_at        AS CreatedAt,
                   completed_at      AS CompletedAt
            FROM   alignment_jobs
            WHERE  id = @id
            LIMIT  1;
            """, new { id });
        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task<IReadOnlyList<AlignmentJob>> ListByAssetAsync(Guid ebookAssetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<AlignmentJobRow>("""
            SELECT id             AS Id,
                   ebook_asset_id    AS EbookAssetId,
                   audiobook_asset_id AS AudiobookAssetId,
                   status            AS Status,
                   alignment_data    AS AlignmentData,
                   error_message     AS ErrorMessage,
                   created_at        AS CreatedAt,
                   completed_at      AS CompletedAt
            FROM   alignment_jobs
            WHERE  ebook_asset_id = @ebookAssetId
            ORDER BY created_at DESC;
            """, new { ebookAssetId });
        return Task.FromResult<IReadOnlyList<AlignmentJob>>(rows.Select(MapRow).ToList());
    }

    public Task<AlignmentJob?> FindPendingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<AlignmentJobRow>("""
            SELECT id             AS Id,
                   ebook_asset_id    AS EbookAssetId,
                   audiobook_asset_id AS AudiobookAssetId,
                   status            AS Status,
                   alignment_data    AS AlignmentData,
                   error_message     AS ErrorMessage,
                   created_at        AS CreatedAt,
                   completed_at      AS CompletedAt
            FROM   alignment_jobs
            WHERE  status = 'Pending'
            ORDER BY created_at ASC
            LIMIT  1;
            """);
        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task InsertAsync(AlignmentJob job, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO alignment_jobs
                (id, ebook_asset_id, audiobook_asset_id, status,
                 alignment_data, error_message, created_at, completed_at)
            VALUES
                (@Id, @EbookAssetId, @AudiobookAssetId, @Status,
                 @AlignmentData, @ErrorMessage, @CreatedAt, @CompletedAt);
            """,
            new
            {
                Id               = job.Id,
                EbookAssetId     = job.EbookAssetId,
                AudiobookAssetId = job.AudiobookAssetId,
                Status           = job.Status.ToString(),
                job.AlignmentData,
                job.ErrorMessage,
                CreatedAt        = job.CreatedAt.ToString("O"),
                CompletedAt      = job.CompletedAt.HasValue
                                       ? job.CompletedAt.Value.ToString("O")
                                       : (string?)null,
            });
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(Guid id, AlignmentJobStatus status, string? alignmentData, string? errorMessage, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE alignment_jobs
            SET    status         = @Status,
                   alignment_data = @AlignmentData,
                   error_message  = @ErrorMessage,
                   completed_at   = CASE WHEN @Status IN ('Completed', 'Failed')
                                         THEN @CompletedAt
                                         ELSE completed_at END
            WHERE  id = @Id;
            """,
            new
            {
                Id             = id,
                Status         = status.ToString(),
                AlignmentData  = alignmentData,
                ErrorMessage   = errorMessage,
                CompletedAt    = DateTime.UtcNow.ToString("O"),
            });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM alignment_jobs WHERE id = @id;", new { id });
        return Task.CompletedTask;
    }

    // ── Private intermediate row type and mapper ──────────────────────────────

    /// <summary>
    /// Intermediate row type for Dapper mapping.
    /// Status and date columns are kept as strings because Dapper cannot
    /// automatically convert TEXT → enum or TEXT → DateTime without a type handler.
    /// </summary>
    private sealed class AlignmentJobRow
    {
        public Guid    Id                { get; set; }
        public Guid    EbookAssetId      { get; set; }
        public Guid    AudiobookAssetId  { get; set; }
        public string  Status            { get; set; } = string.Empty;
        public string? AlignmentData     { get; set; }
        public string? ErrorMessage      { get; set; }
        public string  CreatedAt         { get; set; } = string.Empty;
        public string? CompletedAt       { get; set; }
    }

    private static AlignmentJob MapRow(AlignmentJobRow r) => new()
    {
        Id               = r.Id,
        EbookAssetId     = r.EbookAssetId,
        AudiobookAssetId = r.AudiobookAssetId,
        Status           = Enum.Parse<AlignmentJobStatus>(r.Status),
        AlignmentData    = r.AlignmentData,
        ErrorMessage     = r.ErrorMessage,
        CreatedAt        = DateTime.Parse(r.CreatedAt),
        CompletedAt      = r.CompletedAt is null ? null : DateTime.Parse(r.CompletedAt),
    };
}
