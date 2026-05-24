using System.Text.Json.Serialization;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/operations")
            .WithTags("Operations");

        group.MapGet("/", async (
            string? queueName,
            int? limit,
            IMediaOperationRepository repository,
            CancellationToken ct) =>
        {
            var operations = await repository.GetQueueAsync(queueName, limit ?? 200, ct);
            return Results.Ok(operations.Select((op, index) => OperationDto.From(op, index + 1)).ToList());
        })
        .WithName("ListMediaOperations")
        .WithSummary("List durable media operations by queue order.")
        .RequireAdminOrCurator();

        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediaOperationRepository repository,
            IMediaOperationEventRepository events,
            CancellationToken ct) =>
        {
            var operation = await repository.GetByIdAsync(id, ct);
            if (operation is null)
                return Results.NotFound();

            var timeline = await events.GetByOperationAsync(id, ct);
            return Results.Ok(new OperationDetailDto(
                OperationDto.From(operation, null),
                timeline.Select(OperationEventDto.From).ToList()));
        })
        .WithName("GetMediaOperation")
        .WithSummary("Get one durable media operation and its event timeline.")
        .RequireAdminOrCurator();

        group.MapGet("/summary", async (
            IMediaOperationRepository repository,
            CancellationToken ct) =>
        {
            var summary = await repository.GetSummaryAsync(ct);
            return Results.Ok(summary);
        })
        .WithName("GetMediaOperationsSummary")
        .WithSummary("Get media operation counts by status.")
        .RequireAdminOrCurator();

        group.MapPost("/{id:guid}/retry", async (
            Guid id,
            IMediaOperationRepository repository,
            CancellationToken ct) =>
        {
            if (await repository.GetByIdAsync(id, ct) is null)
                return Results.NotFound();

            await repository.RequeueAsync(id, ct);
            return Results.Accepted($"/operations/{id}");
        })
        .WithName("RetryMediaOperation")
        .WithSummary("Requeue a durable media operation for another attempt.")
        .RequireAdminOrCurator();

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            IMediaOperationRepository repository,
            CancellationToken ct) =>
        {
            if (await repository.GetByIdAsync(id, ct) is null)
                return Results.NotFound();

            await repository.MarkCancelledAsync(id, "Cancelled by user.", ct);
            return Results.Accepted($"/operations/{id}");
        })
        .WithName("CancelMediaOperation")
        .WithSummary("Cancel a durable media operation.")
        .RequireAdminOrCurator();

        return app;
    }
}

public sealed record OperationDetailDto(
    [property: JsonPropertyName("operation")] OperationDto Operation,
    [property: JsonPropertyName("events")] IReadOnlyList<OperationEventDto> Events);

public sealed record OperationDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("operation_type")] public string OperationType { get; init; } = "";
    [JsonPropertyName("operation_kind")] public string OperationKind { get; init; } = "";
    [JsonPropertyName("entity_id")] public Guid? EntityId { get; init; }
    [JsonPropertyName("entity_kind")] public string? EntityKind { get; init; }
    [JsonPropertyName("batch_id")] public Guid? BatchId { get; init; }
    [JsonPropertyName("source_path")] public string? SourcePath { get; init; }
    [JsonPropertyName("capability_id")] public string? CapabilityId { get; init; }
    [JsonPropertyName("capability_version")] public string? CapabilityVersion { get; init; }
    [JsonPropertyName("sub_key")] public string? SubKey { get; init; }
    [JsonPropertyName("plugin_id")] public string? PluginId { get; init; }
    [JsonPropertyName("plugin_version")] public string? PluginVersion { get; init; }
    [JsonPropertyName("provider_id")] public string? ProviderId { get; init; }
    [JsonPropertyName("model_id")] public string? ModelId { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("stage")] public string? Stage { get; init; }
    [JsonPropertyName("priority")] public int Priority { get; init; }
    [JsonPropertyName("queue_name")] public string QueueName { get; init; } = "";
    [JsonPropertyName("queue_position")] public int? QueuePosition { get; init; }
    [JsonPropertyName("attempt_count")] public int AttemptCount { get; init; }
    [JsonPropertyName("lease_owner")] public string? LeaseOwner { get; init; }
    [JsonPropertyName("lease_expires_at")] public DateTimeOffset? LeaseExpiresAt { get; init; }
    [JsonPropertyName("heartbeat_at")] public DateTimeOffset? HeartbeatAt { get; init; }
    [JsonPropertyName("next_retry_at")] public DateTimeOffset? NextRetryAt { get; init; }
    [JsonPropertyName("progress_percent")] public int ProgressPercent { get; init; }
    [JsonPropertyName("items_total")] public int ItemsTotal { get; init; }
    [JsonPropertyName("items_completed")] public int ItemsCompleted { get; init; }
    [JsonPropertyName("items_failed")] public int ItemsFailed { get; init; }
    [JsonPropertyName("result_summary")] public string? ResultSummary { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
    [JsonPropertyName("missing_reason")] public string? MissingReason { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; init; }

    public static OperationDto From(MediaOperation operation, int? queuePosition) => new()
    {
        Id = operation.Id,
        OperationType = operation.OperationType,
        OperationKind = operation.OperationKind,
        EntityId = operation.EntityId,
        EntityKind = operation.EntityKind,
        BatchId = operation.BatchId,
        SourcePath = operation.SourcePath,
        CapabilityId = operation.CapabilityId,
        CapabilityVersion = operation.CapabilityVersion,
        SubKey = operation.SubKey,
        PluginId = operation.PluginId,
        PluginVersion = operation.PluginVersion,
        ProviderId = operation.ProviderId,
        ModelId = operation.ModelId,
        Status = operation.Status,
        Stage = operation.Stage,
        Priority = operation.Priority,
        QueueName = operation.QueueName,
        QueuePosition = queuePosition,
        AttemptCount = operation.AttemptCount,
        LeaseOwner = operation.LeaseOwner,
        LeaseExpiresAt = operation.LeaseExpiresAt,
        HeartbeatAt = operation.HeartbeatAt,
        NextRetryAt = operation.NextRetryAt,
        ProgressPercent = operation.ProgressPercent,
        ItemsTotal = operation.ItemsTotal,
        ItemsCompleted = operation.ItemsCompleted,
        ItemsFailed = operation.ItemsFailed,
        ResultSummary = operation.ResultSummary,
        LastError = operation.LastError,
        MissingReason = operation.MissingReason,
        CreatedAt = operation.CreatedAt,
        UpdatedAt = operation.UpdatedAt,
        CompletedAt = operation.CompletedAt
    };
}

public sealed record OperationEventDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("operation_id")] public Guid OperationId { get; init; }
    [JsonPropertyName("entity_id")] public Guid? EntityId { get; init; }
    [JsonPropertyName("batch_id")] public Guid? BatchId { get; init; }
    [JsonPropertyName("event_type")] public string EventType { get; init; } = "";
    [JsonPropertyName("old_status")] public string? OldStatus { get; init; }
    [JsonPropertyName("new_status")] public string? NewStatus { get; init; }
    [JsonPropertyName("old_stage")] public string? OldStage { get; init; }
    [JsonPropertyName("new_stage")] public string? NewStage { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("detail_json")] public string? DetailJson { get; init; }
    [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; init; }

    public static OperationEventDto From(MediaOperationEvent evt) => new()
    {
        Id = evt.Id,
        OperationId = evt.OperationId,
        EntityId = evt.EntityId,
        BatchId = evt.BatchId,
        EventType = evt.EventType,
        OldStatus = evt.OldStatus,
        NewStatus = evt.NewStatus,
        OldStage = evt.OldStage,
        NewStage = evt.NewStage,
        Message = evt.Message,
        DetailJson = evt.DetailJson,
        OccurredAt = evt.OccurredAt
    };
}
