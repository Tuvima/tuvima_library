using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class IngestionBatchReadService : IIngestionBatchReadService
{
    private readonly IDatabaseConnection _db;

    public IngestionBatchReadService(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<IngestionBatchItemResponse>> GetItemsAsync(
        Guid batchId,
        int offset,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Query;
        cmd.Parameters.Add("@batchId", SqliteType.Blob).Value = GuidSql.ToBlob(batchId);
        cmd.Parameters.AddWithValue("@offset", Math.Max(0, offset));
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 501));

        using var reader = cmd.ExecuteReader();
        var items = new List<IngestionBatchItemResponse>();
        while (reader.Read())
        {
            var filePath = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var status = reader.GetString(4);
            var stage = reader.IsDBNull(5) ? ResolveItemStage(status) : reader.GetString(5);
            var progressPercent = reader.GetInt32(6);
            var workUnitsTotal = reader.GetInt32(7);
            var workUnitsCompleted = reader.GetInt32(8);
            var lastError = reader.IsDBNull(10) ? null : reader.GetString(10);
            var missingReason = reader.IsDBNull(11) ? null : reader.GetString(11);

            items.Add(new IngestionBatchItemResponse
            {
                Id = GuidSql.FromDb(reader.GetValue(0)),
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                MediaAssetId = reader.IsDBNull(2) ? null : GuidSql.FromDb(reader.GetValue(2)),
                ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
                Status = status,
                ErrorDetail = lastError ?? missingReason,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(12)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(13)),
                Stage = stage,
                StageOrder = ResolveItemStageOrder(stage),
                ProgressPercent = progressPercent,
                WorkUnitsTotal = workUnitsTotal <= 0 ? 1 : workUnitsTotal,
                WorkUnitsCompleted = workUnitsCompleted,
                IsTerminal = IsTerminal(status),
            });
        }

        return Task.FromResult<IReadOnlyList<IngestionBatchItemResponse>>(items);
    }

    private static string ResolveItemStage(string status) => status switch
    {
        "succeeded" => "completed",
        "no_result" or "missing_confirmed" => "failed",
        "blocked" => "blocked",
        "failed_retryable" or "retry_waiting" => "retrying",
        "failed_terminal" or "dead_lettered" => "failed",
        _ => status,
    };

    private static int ResolveItemStageOrder(string stage) => stage switch
    {
        "discovered" => 0,
        "settling" => 1,
        "waiting_for_lock" => 2,
        "queued" => 3,
        "hashing" => 4,
        "parsing" or "processed" => 5,
        "scoring" or "scored" => 6,
        "registered" => 7,
        "queued_identity" => 8,
        "identifying" => 9,
        "hydrating" => 10,
        "universe_enriching" => 11,
        "completed" or "complete" or "needs_review" or "failed" => 12,
        _ => 0,
    };

    private static bool IsTerminal(string status) => status is
        "succeeded" or
        "no_result" or
        "missing_confirmed" or
        "not_applicable" or
        "blocked" or
        "failed_terminal" or
        "dead_lettered" or
        "cancelled" or
        "skipped";

    private const string Query = """
        SELECT
            id,
            source_path,
            entity_id,
            content_hash,
            status,
            stage,
            progress_percent,
            items_total,
            items_completed,
            result_summary,
            last_error,
            missing_reason,
            created_at,
            updated_at
        FROM media_operations
        WHERE batch_id = @batchId
          AND operation_type = 'ingestion.file'
        ORDER BY priority ASC, position_key ASC
        LIMIT @limit OFFSET @offset;
        """;
}
