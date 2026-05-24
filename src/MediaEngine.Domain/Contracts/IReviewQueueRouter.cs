namespace MediaEngine.Domain.Contracts;

public interface IReviewQueueRouter
{
    Task EvaluateOperationAsync(Guid operationId, CancellationToken ct = default);
    Task EvaluateCapabilityAsync(Guid entityId, string capabilityId, string? subKey = null, CancellationToken ct = default);
    Task SendManualAsync(Guid entityId, string entityType, string trigger, string detail, CancellationToken ct = default);
}
