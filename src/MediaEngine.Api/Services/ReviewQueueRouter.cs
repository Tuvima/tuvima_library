using MediaEngine.Domain.Capabilities;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Services;

public sealed class ReviewQueueRouter : IReviewQueueRouter
{
    private readonly IMediaOperationRepository _operations;
    private readonly IEntityCapabilityStateRepository _capabilityStates;
    private readonly IReviewQueueRepository _reviewQueue;
    private readonly CapabilityRegistry _registry;

    public ReviewQueueRouter(
        IMediaOperationRepository operations,
        IEntityCapabilityStateRepository capabilityStates,
        IReviewQueueRepository reviewQueue,
        CapabilityRegistry registry)
    {
        _operations = operations;
        _capabilityStates = capabilityStates;
        _reviewQueue = reviewQueue;
        _registry = registry;
    }

    public async Task EvaluateOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await _operations.GetByIdAsync(operationId, ct);
        if (operation is null || operation.EntityId is null || !ReviewEligibility.IsReviewEligible(operation))
            return;

        await SendIfAbsentAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = operation.EntityId.Value,
            EntityType = operation.EntityKind ?? "MediaAsset",
            Trigger = TriggerForOperation(operation),
            Detail = operation.LastError ?? operation.MissingReason ?? operation.ResultSummary ?? "Automation finished in an actionable terminal state.",
            SourceOperationId = operation.Id,
            SourceCapabilityId = operation.CapabilityId,
            SourceCapabilitySubKey = operation.SubKey,
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = operation.CompletedAt ?? DateTimeOffset.UtcNow
        }, ct);
    }

    public async Task EvaluateCapabilityAsync(Guid entityId, string capabilityId, string? subKey = null, CancellationToken ct = default)
    {
        var state = await _capabilityStates.GetAsync(entityId, capabilityId, subKey, ct);
        if (state is null)
            return;

        var definition = _registry.Find(capabilityId);
        if (!ReviewEligibility.IsReviewEligible(state, definition))
            return;

        await SendIfAbsentAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = state.EntityId,
            EntityType = state.EntityKind,
            Trigger = TriggerForCapability(state),
            Detail = state.LastError ?? state.MissingReason ?? state.ResultSummary ?? "Capability finished in an actionable terminal state.",
            ConfidenceScore = state.Confidence,
            SourceOperationId = state.LastOperationId,
            SourceCapabilityId = state.CapabilityId,
            SourceCapabilitySubKey = state.SubKey,
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = state.UpdatedAt
        }, ct);
    }

    public Task SendManualAsync(Guid entityId, string entityType, string trigger, string detail, CancellationToken ct = default)
        => SendIfAbsentAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = entityType,
            Trigger = trigger,
            Detail = detail,
            ReviewReadyAt = DateTimeOffset.UtcNow
        }, ct);

    private async Task SendIfAbsentAsync(ReviewQueueEntry entry, CancellationToken ct)
    {
        var existing = await _reviewQueue.GetPendingByEntityAsync(entry.EntityId, ct);
        if (existing.Count > 0)
            return;

        await _reviewQueue.InsertAsync(entry, ct);
    }

    private static string TriggerForOperation(MediaOperation operation)
        => operation.OperationType switch
        {
            MediaOperationType.IdentityWikidataBridge => ReviewTrigger.WikidataBridgeFailed,
            MediaOperationType.IdentityRetailMatch => ReviewTrigger.RetailMatchFailed,
            MediaOperationType.WritebackMetadata => ReviewTrigger.WritebackFailed,
            _ => ReviewTrigger.LowConfidence
        };

    private static string TriggerForCapability(EntityCapabilityState state)
        => state.CapabilityId switch
        {
            CapabilityId.IdentityMediaTypeClassification => ReviewTrigger.AmbiguousMediaType,
            CapabilityId.IdentityWikidataBridge => ReviewTrigger.WikidataBridgeFailed,
            CapabilityId.IdentityRetailMatch => ReviewTrigger.RetailMatchFailed,
            CapabilityId.WritebackMetadata => ReviewTrigger.WritebackFailed,
            _ => ReviewTrigger.LowConfidence
        };
}
