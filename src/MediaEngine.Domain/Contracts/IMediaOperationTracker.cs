using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IMediaOperationTracker
{
    Task<MediaOperation> EnsureQueuedAsync(MediaOperation operation, CancellationToken ct = default);
    Task UpdateStageAsync(Guid operationId, string stage, int? progressPercent = null, string? message = null, object? detail = null, CancellationToken ct = default);
    Task MarkSucceededAsync(Guid operationId, string? resultSummary = null, object? detail = null, CancellationToken ct = default);
    Task MarkNoResultAsync(Guid operationId, string? reason = null, object? detail = null, CancellationToken ct = default);
    Task MarkBlockedAsync(Guid operationId, string reason, object? detail = null, CancellationToken ct = default);
    Task MarkFailedAsync(Guid operationId, Exception exception, bool terminal, CancellationToken ct = default);
}
