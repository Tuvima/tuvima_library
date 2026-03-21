using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IIngestionBatchRepository"/>.
/// Tracks ingestion batches — grouped runs of file processing — from start to completion.
/// Uses Dapper for type-safe column-to-property mapping.
/// </summary>
public sealed class IngestionBatchRepository : IIngestionBatchRepository
{
    private readonly IDatabaseConnection _db;

    // Reusable SELECT list with column aliases for Dapper mapping.
    private const string SelectColumns = """
        id               AS Id,
        status           AS Status,
        source_path      AS SourcePath,
        category         AS Category,
        files_total      AS FilesTotal,
        files_processed  AS FilesProcessed,
        files_registered AS FilesRegistered,
        files_review     AS FilesReview,
        files_no_match   AS FilesNoMatch,
        files_failed     AS FilesFailed,
        started_at       AS StartedAt,
        completed_at     AS CompletedAt,
        created_at       AS CreatedAt,
        updated_at       AS UpdatedAt
        """;

    public IngestionBatchRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task CreateAsync(IngestionBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO ingestion_batches
                (id, status, source_path, category,
                 files_total, files_processed, files_registered,
                 files_review, files_no_match, files_failed,
                 started_at, completed_at, created_at, updated_at)
            VALUES
                (@id, @status, @sourcePath, @category,
                 @filesTotal, @filesProcessed, @filesRegistered,
                 @filesReview, @filesNoMatch, @filesFailed,
                 @startedAt, @completedAt, @createdAt, @updatedAt);
            """,
            new
            {
                id               = batch.Id.ToString(),
                status           = batch.Status,
                sourcePath       = batch.SourcePath,
                category         = batch.Category,
                filesTotal       = batch.FilesTotal,
                filesProcessed   = batch.FilesProcessed,
                filesRegistered  = batch.FilesRegistered,
                filesReview      = batch.FilesReview,
                filesNoMatch     = batch.FilesNoMatch,
                filesFailed      = batch.FilesFailed,
                startedAt        = batch.StartedAt.ToString("O"),
                completedAt      = batch.CompletedAt?.ToString("O"),
                createdAt        = batch.CreatedAt.ToString("O"),
                updatedAt        = batch.UpdatedAt.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateCountsAsync(
        Guid id,
        int filesTotal,
        int filesProcessed,
        int filesRegistered,
        int filesReview,
        int filesNoMatch,
        int filesFailed,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE ingestion_batches
            SET files_total      = @filesTotal,
                files_processed  = @filesProcessed,
                files_registered = @filesRegistered,
                files_review     = @filesReview,
                files_no_match   = @filesNoMatch,
                files_failed     = @filesFailed,
                updated_at       = @updatedAt
            WHERE id = @id;
            """,
            new
            {
                id               = id.ToString(),
                filesTotal       = filesTotal,
                filesProcessed   = filesProcessed,
                filesRegistered  = filesRegistered,
                filesReview      = filesReview,
                filesNoMatch     = filesNoMatch,
                filesFailed      = filesFailed,
                updatedAt        = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompleteAsync(Guid id, string status, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE ingestion_batches
            SET status       = @status,
                completed_at = @completedAt,
                updated_at   = @updatedAt
            WHERE id = @id;
            """,
            new
            {
                id          = id.ToString(),
                status      = status,
                completedAt = DateTimeOffset.UtcNow.ToString("O"),
                updatedAt   = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IngestionBatch?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<IngestionBatch>($"""
            SELECT {SelectColumns}
            FROM   ingestion_batches
            WHERE  id = @id;
            """, new { id = id.ToString() });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IngestionBatch>> GetRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var results = conn.Query<IngestionBatch>($"""
            SELECT {SelectColumns}
            FROM   ingestion_batches
            ORDER BY created_at DESC
            LIMIT  @limit;
            """, new { limit }).AsList();

        return Task.FromResult<IReadOnlyList<IngestionBatch>>(results);
    }

    /// <inheritdoc/>
    public Task<int> GetNeedsAttentionCountAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var count = conn.QueryFirstOrDefault<int>("""
            SELECT COALESCE(SUM(files_review + files_no_match), 0)
            FROM   ingestion_batches
            WHERE  status = 'completed';
            """);

        return Task.FromResult(count);
    }
}
