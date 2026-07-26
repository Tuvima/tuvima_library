using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
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
        files_registered AS FilesIdentified,
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
                id               = batch.Id,
                status           = batch.Status,
                sourcePath       = batch.SourcePath,
                category         = batch.Category,
                filesTotal       = batch.FilesTotal,
                filesProcessed   = batch.FilesProcessed,
                filesRegistered  = batch.FilesIdentified,
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
        int filesIdentified,
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
                files_registered = @filesIdentified,
                files_review     = @filesReview,
                files_no_match   = @filesNoMatch,
                files_failed     = @filesFailed,
                updated_at       = @updatedAt
            WHERE id = @id;
            """,
            new
            {
                id,
                filesTotal       = filesTotal,
                filesProcessed   = filesProcessed,
                filesIdentified  = filesIdentified,
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
                id,
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
            """, new { id });

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

    /// <inheritdoc/>
    public Task IncrementCounterAsync(Guid id, BatchCounterColumn column, CancellationToken ct = default)
    {
        // Map enum to the exact SQLite column name.
        var colName = column switch
        {
            BatchCounterColumn.FilesTotal      => "files_total",
            BatchCounterColumn.FilesProcessed  => "files_processed",
            BatchCounterColumn.FilesIdentified => "files_registered",
            BatchCounterColumn.FilesReview     => "files_review",
            BatchCounterColumn.FilesNoMatch    => "files_no_match",
            BatchCounterColumn.FilesFailed     => "files_failed",
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown BatchCounterColumn value"),
        };

        // Use a raw SQL string built from a fixed switch — colName is never user-supplied, so no injection risk.
        using var conn = _db.CreateConnection();
        conn.Execute($"""
            UPDATE ingestion_batches
            SET {colName}   = {colName} + 1,
                updated_at  = @updatedAt
            WHERE id = @id;
            """,
            new
            {
                id,
                updatedAt = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> AbandonRunningAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var affected = conn.Execute("""
            UPDATE ingestion_batches
            SET    status       = 'abandoned',
                   completed_at = @completedAt,
                   updated_at   = @updatedAt
            WHERE  status = 'running';
            """,
            new
            {
                completedAt = DateTimeOffset.UtcNow.ToString("O"),
                updatedAt   = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.FromResult(affected);
    }

    /// <inheritdoc/>
    public async Task<IngestionBatchProgressSnapshot> GetProgressSnapshotAsync(
        Guid batchId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var snapshot = await conn.QueryFirstOrDefaultAsync<IngestionBatchProgressSnapshot>(
            """
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            ),
            job_states AS (
                SELECT entity_id, state, updated_at
                FROM latest_jobs
                WHERE rn = 1
            ),
            pending_reviews AS (
                SELECT DISTINCT entity_id
                FROM review_queue
                WHERE status = 'Pending'
                  AND review_ready_at IS NOT NULL
            )
            SELECT
                COUNT(js.entity_id) AS TotalJobs,
                COALESCE(SUM(CASE WHEN js.state = 'Ready' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesReady,
                COALESCE(SUM(CASE WHEN js.state = 'ReadyWithoutUniverse' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesReadyWithoutUniverse,
                COALESCE(SUM(CASE
                    WHEN js.state IN ('QidNeedsReview', 'RetailMatchedNeedsReview') THEN 1
                    WHEN pr.entity_id IS NOT NULL
                         AND js.state IN ('Ready', 'ReadyWithoutUniverse', 'RetailNoMatch', 'QidNoMatch', 'Failed')
                        THEN 1
                    ELSE 0
                END), 0) AS FilesReview,
                COALESCE(SUM(CASE WHEN js.state IN ('RetailNoMatch', 'QidNoMatch') AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS FilesNoMatch,
                COALESCE(SUM(CASE WHEN js.state = 'Failed' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS PipelineFailed,
                COALESCE(SUM(CASE WHEN js.state = 'Queued' THEN 1 ELSE 0 END), 0) AS QueuedJobs,
                COALESCE(SUM(CASE WHEN js.state = 'RetailSearching' THEN 1 ELSE 0 END), 0) AS RetailSearching,
                COALESCE(SUM(CASE WHEN js.state = 'RetailMatched' THEN 1 ELSE 0 END), 0) AS RetailMatched,
                0 AS RetailMatchedNeedsReview,
                COALESCE(SUM(CASE WHEN js.state = 'BridgeSearching' THEN 1 ELSE 0 END), 0) AS BridgeSearching,
                COALESCE(SUM(CASE WHEN js.state = 'QidResolved' THEN 1 ELSE 0 END), 0) AS QidResolved,
                COALESCE(SUM(CASE WHEN js.state = 'Hydrating' THEN 1 ELSE 0 END), 0) AS Hydrating,
                COALESCE(SUM(CASE WHEN js.state = 'UniverseEnriching' THEN 1 ELSE 0 END), 0) AS UniverseEnriching
            FROM job_states js
            LEFT JOIN pending_reviews pr ON pr.entity_id = js.entity_id;
            """,
            new { batchId }).ConfigureAwait(false) ?? new IngestionBatchProgressSnapshot();

        var currentFileTitle = await conn.QueryFirstOrDefaultAsync<string?>(
            """
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            ),
            active_job AS (
                SELECT entity_id, state, updated_at
                FROM latest_jobs
                WHERE rn = 1
                  AND state IN ('RetailSearching', 'BridgeSearching', 'Hydrating', 'UniverseEnriching')
                ORDER BY updated_at DESC
                LIMIT 1
            )
            SELECT cv.value
            FROM active_job aj
            INNER JOIN canonical_values cv ON cv.entity_id = aj.entity_id
            WHERE cv.key IN ('title', 'show_name')
            ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END
            LIMIT 1;
            """,
            new { batchId }).ConfigureAwait(false);

        return new IngestionBatchProgressSnapshot
        {
            TotalJobs = snapshot.TotalJobs,
            FilesReady = snapshot.FilesReady,
            FilesReadyWithoutUniverse = snapshot.FilesReadyWithoutUniverse,
            FilesReview = snapshot.FilesReview,
            FilesNoMatch = snapshot.FilesNoMatch,
            PipelineFailed = snapshot.PipelineFailed,
            QueuedJobs = snapshot.QueuedJobs,
            RetailSearching = snapshot.RetailSearching,
            RetailMatched = snapshot.RetailMatched,
            RetailMatchedNeedsReview = snapshot.RetailMatchedNeedsReview,
            BridgeSearching = snapshot.BridgeSearching,
            QidResolved = snapshot.QidResolved,
            Hydrating = snapshot.Hydrating,
            UniverseEnriching = snapshot.UniverseEnriching,
            CurrentFileTitle = currentFileTitle,
        };
    }
}
