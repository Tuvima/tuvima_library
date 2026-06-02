using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

public interface IIngestionBatchResponseService
{
    Task<IReadOnlyList<IngestionBatchResponse>> GetRecentAsync(int limit, CancellationToken ct = default);

    Task<IngestionBatchResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public sealed class IngestionBatchResponseService : IIngestionBatchResponseService
{
    private readonly IIngestionBatchRepository _batchRepository;
    private readonly IDatabaseConnection _db;

    public IngestionBatchResponseService(
        IIngestionBatchRepository batchRepository,
        IDatabaseConnection db)
    {
        _batchRepository = batchRepository;
        _db = db;
    }

    public async Task<IReadOnlyList<IngestionBatchResponse>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        var batches = await _batchRepository.GetRecentAsync(limit, ct);
        var responses = new List<IngestionBatchResponse>();
        foreach (var batch in batches.Where(IngestionBatchEndpointMapper.ShouldShowInRecentBatches))
        {
            responses.Add(await ToResponseAsync(batch, ct));
        }

        return responses;
    }

    public async Task<IngestionBatchResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _batchRepository.GetByIdAsync(id, ct);
        return batch is null ? null : await ToResponseAsync(batch, ct);
    }

    private async Task<IngestionBatchResponse> ToResponseAsync(
        IngestionBatch batch,
        CancellationToken ct = default)
    {
        var counters = await ReadTerminalSnapshotAsync(batch.Id, ct);
        var hasCurrentLifecycleRows = counters.HasRows;
        var total = batch.FilesTotal > 0
            ? batch.FilesTotal
            : Math.Max(counters.TotalJobs + counters.OperationOnlyTerminal, counters.FileOperationsTerminal);
        var identified = hasCurrentLifecycleRows ? counters.Identified : batch.FilesIdentified;
        var review = hasCurrentLifecycleRows ? counters.Review : batch.FilesReview;
        var noMatch = hasCurrentLifecycleRows ? counters.NoMatch : batch.FilesNoMatch;
        var failed = hasCurrentLifecycleRows ? counters.Failed : batch.FilesFailed;
        var terminal = identified + review + noMatch + failed + counters.OperationOnlyTerminal;
        if (total > 0)
        {
            terminal = Math.Clamp(terminal, 0, total);
        }

        return new IngestionBatchResponse
        {
            Id              = batch.Id,
            Status          = batch.Status,
            SourcePath      = batch.SourcePath,
            Category        = batch.Category,
            FilesTotal      = total,
            FilesProcessed  = terminal,
            FilesIdentified = Math.Max(0, identified),
            FilesReview     = Math.Max(0, review),
            FilesNoMatch    = Math.Max(0, noMatch),
            FilesFailed     = Math.Max(0, failed),
            StartedAt       = batch.StartedAt,
            CompletedAt     = batch.CompletedAt,
            CreatedAt       = batch.CreatedAt,
        };
    }

    private async Task<IngestionBatchTerminalCounters> ReadTerminalSnapshotAsync(
        Guid batchId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<IngestionBatchTerminalCounters>(
            """
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            ),
            job_states AS (
                SELECT entity_id, state
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
                COALESCE(SUM(CASE
                    WHEN js.state IN ('Ready', 'ReadyWithoutUniverse') AND pr.entity_id IS NULL THEN 1
                    ELSE 0
                END), 0) AS Identified,
                COALESCE(SUM(CASE
                    WHEN pr.entity_id IS NOT NULL THEN 1
                    WHEN js.state IN ('QidNeedsReview', 'RetailMatchedNeedsReview') THEN 1
                    ELSE 0
                END), 0) AS Review,
                COALESCE(SUM(CASE WHEN js.state IN ('RetailNoMatch', 'QidNoMatch') AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS NoMatch,
                COALESCE(SUM(CASE WHEN js.state = 'Failed' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS Failed,
                COUNT(js.entity_id) AS TotalJobs,
                (
                    SELECT COUNT(*)
                    FROM media_operations mo
                    WHERE mo.batch_id = @batchId
                      AND mo.operation_type = 'ingestion.file'
                      AND mo.status IN ('succeeded', 'no_result', 'missing_confirmed', 'not_applicable', 'skipped', 'blocked', 'failed_terminal', 'dead_lettered', 'cancelled')
                ) AS FileOperationsTerminal
            FROM job_states js
            LEFT JOIN pending_reviews pr ON pr.entity_id = js.entity_id;
            """,
            new { batchId = GuidSql.ToBlob(batchId) }) ?? new IngestionBatchTerminalCounters();
    }

    private sealed class IngestionBatchTerminalCounters
    {
        public int Identified { get; init; }
        public int Review { get; init; }
        public int NoMatch { get; init; }
        public int Failed { get; init; }
        public int TotalJobs { get; init; }
        public int FileOperationsTerminal { get; init; }
        public int OperationOnlyTerminal => Math.Max(0, FileOperationsTerminal - TotalJobs);
        public bool HasRows => TotalJobs > 0 || FileOperationsTerminal > 0;
    }
}
