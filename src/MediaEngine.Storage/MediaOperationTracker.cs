using System.Text.Json;
using MediaEngine.Domain;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage;

public sealed class MediaOperationTracker : IMediaOperationTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMediaOperationRepository _operations;
    private readonly IMediaOperationEventRepository _events;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<MediaOperationTracker> _logger;

    public MediaOperationTracker(
        IMediaOperationRepository operations,
        IMediaOperationEventRepository events,
        IEventPublisher publisher,
        ILogger<MediaOperationTracker> logger)
    {
        _operations = operations;
        _events = events;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<MediaOperation> EnsureQueuedAsync(MediaOperation operation, CancellationToken ct = default)
    {
        var ensured = await _operations.EnsureAsync(operation, ct);
        await _events.AddAsync(new MediaOperationEvent
        {
            OperationId = ensured.Id,
            EntityId = ensured.EntityId,
            BatchId = ensured.BatchId,
            EventType = "ensured",
            NewStatus = ensured.Status,
            NewStage = ensured.Stage,
            Message = "Operation ensured."
        }, ct);
        await PublishOperationChangedAsync(ensured, ct);
        return ensured;
    }

    public async Task UpdateStageAsync(Guid operationId, string stage, int? progressPercent = null, string? message = null, object? detail = null, CancellationToken ct = default)
    {
        var before = await _operations.GetByIdAsync(operationId, ct);
        await _operations.UpdateStageAsync(operationId, stage, progressPercent, ct);
        var after = await _operations.GetByIdAsync(operationId, ct);
        if (after is not null)
            await AddTransitionAsync("stage_changed", before, after, message, detail, ct);
    }

    public async Task MarkSucceededAsync(Guid operationId, string? resultSummary = null, object? detail = null, CancellationToken ct = default)
    {
        var before = await _operations.GetByIdAsync(operationId, ct);
        await _operations.MarkSucceededAsync(operationId, resultSummary, ct);
        var after = await _operations.GetByIdAsync(operationId, ct);
        if (after is not null)
            await AddTransitionAsync("succeeded", before, after, resultSummary, detail, ct);
    }

    public async Task MarkNoResultAsync(Guid operationId, string? reason = null, object? detail = null, CancellationToken ct = default)
    {
        var before = await _operations.GetByIdAsync(operationId, ct);
        await _operations.MarkNoResultAsync(operationId, reason, reason, ct);
        var after = await _operations.GetByIdAsync(operationId, ct);
        if (after is not null)
            await AddTransitionAsync("no_result", before, after, reason, detail, ct);
    }

    public async Task MarkBlockedAsync(Guid operationId, string reason, object? detail = null, CancellationToken ct = default)
    {
        var before = await _operations.GetByIdAsync(operationId, ct);
        await _operations.MarkBlockedAsync(operationId, reason, ct);
        var after = await _operations.GetByIdAsync(operationId, ct);
        if (after is not null)
            await AddTransitionAsync("blocked", before, after, reason, detail, ct);
    }

    public async Task MarkFailedAsync(Guid operationId, Exception exception, bool terminal, CancellationToken ct = default)
    {
        var before = await _operations.GetByIdAsync(operationId, ct);
        if (terminal)
            await _operations.MarkFailedTerminalAsync(operationId, exception.Message, ct);
        else
            await _operations.MarkFailedRetryableAsync(operationId, exception.Message, DateTimeOffset.UtcNow.AddMinutes(5), ct);

        var after = await _operations.GetByIdAsync(operationId, ct);
        if (after is not null)
            await AddTransitionAsync(terminal ? "failed_terminal" : "failed_retryable", before, after, exception.Message, new
            {
                exception = exception.GetType().FullName,
                exception.Message
            }, ct);
    }

    private async Task AddTransitionAsync(
        string eventType,
        MediaOperation? before,
        MediaOperation after,
        string? message,
        object? detail,
        CancellationToken ct)
    {
        await _events.AddAsync(new MediaOperationEvent
        {
            OperationId = after.Id,
            EntityId = after.EntityId,
            BatchId = after.BatchId,
            EventType = eventType,
            OldStatus = before?.Status,
            NewStatus = after.Status,
            OldStage = before?.Stage,
            NewStage = after.Stage,
            Message = message,
            DetailJson = detail is null ? null : JsonSerializer.Serialize(detail, JsonOptions)
        }, ct);
        await PublishOperationChangedAsync(after, ct);
    }

    private async Task PublishOperationChangedAsync(MediaOperation operation, CancellationToken ct)
    {
        try
        {
            await _publisher.PublishAsync(
                SignalREvents.MediaOperationChanged,
                new MediaOperationChangedPayload(
                    operation.Id,
                    operation.OperationType,
                    operation.OperationKind,
                    operation.Status,
                    operation.Stage,
                    operation.ProgressPercent,
                    operation.ItemsTotal,
                    operation.ItemsCompleted,
                    operation.UpdatedAt),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not publish activity for media operation {OperationId}.", operation.Id);
        }
    }

    private sealed record MediaOperationChangedPayload(
        Guid Id,
        string OperationType,
        string OperationKind,
        string Status,
        string? Stage,
        int ProgressPercent,
        int ItemsTotal,
        int ItemsCompleted,
        DateTimeOffset UpdatedAt);
}
