using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IMediaOperationRepository
{
    Task<MediaOperation> EnsureAsync(MediaOperation operation, CancellationToken ct = default);
    Task<MediaOperation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<MediaOperation?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<MediaOperation?> GetActiveBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task<MediaOperation?> GetLatestBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperation>> GetByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperation>> GetByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperation>> GetByPluginAsync(string pluginId, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperation>> GetQueueAsync(string? queueName, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperation>> LeaseNextAsync(string workerName, IReadOnlyList<string> operationTypes, int batchSize, TimeSpan leaseDuration, CancellationToken ct = default);
    Task UpdateStageAsync(Guid id, string stage, int? progressPercent = null, CancellationToken ct = default);
    Task HeartbeatAsync(Guid id, int? progressPercent = null, CancellationToken ct = default);
    Task MarkSucceededAsync(Guid id, string? resultSummary = null, CancellationToken ct = default);
    Task MarkNoResultAsync(Guid id, string? missingReason = null, string? resultSummary = null, CancellationToken ct = default);
    Task MarkMissingConfirmedAsync(Guid id, string? missingReason = null, CancellationToken ct = default);
    Task MarkBlockedAsync(Guid id, string reason, CancellationToken ct = default);
    Task MarkFailedRetryableAsync(Guid id, string error, DateTimeOffset nextRetryAt, CancellationToken ct = default);
    Task MarkFailedTerminalAsync(Guid id, string error, CancellationToken ct = default);
    Task MarkDeadLetteredAsync(Guid id, string error, CancellationToken ct = default);
    Task MarkCancelledAsync(Guid id, string? reason = null, CancellationToken ct = default);
    Task MarkInterruptedAsync(Guid id, string? reason = null, CancellationToken ct = default);
    Task RequeueAsync(Guid id, CancellationToken ct = default);
    Task<int> ReclaimStuckAsync(TimeSpan stuckThreshold, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, int>> GetSummaryAsync(CancellationToken ct = default);
}
