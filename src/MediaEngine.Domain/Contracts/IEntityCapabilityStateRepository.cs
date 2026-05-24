using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IEntityCapabilityStateRepository
{
    Task<EntityCapabilityState> EnsureAsync(EntityCapabilityState state, CancellationToken ct = default);
    Task<EntityCapabilityState?> GetAsync(Guid entityId, string capabilityId, string? subKey = null, CancellationToken ct = default);
    Task<IReadOnlyList<EntityCapabilityState>> GetByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, int>> GetSummaryAsync(CancellationToken ct = default);
    Task MarkQueuedAsync(Guid entityId, string capabilityId, string? subKey, Guid operationId, CancellationToken ct = default);
    Task MarkRunningAsync(Guid entityId, string capabilityId, string? subKey, Guid operationId, CancellationToken ct = default);
    Task MarkSucceededAsync(Guid entityId, string capabilityId, string? subKey, CapabilityStateResult result, CancellationToken ct = default);
    Task MarkNoResultAsync(Guid entityId, string capabilityId, string? subKey, string reason, CancellationToken ct = default);
    Task MarkBlockedAsync(Guid entityId, string capabilityId, string? subKey, string reason, CancellationToken ct = default);
    Task MarkFailedAsync(Guid entityId, string capabilityId, string? subKey, string error, bool terminal, CancellationToken ct = default);
    Task MarkNotApplicableAsync(Guid entityId, string capabilityId, string? subKey, string reason, CancellationToken ct = default);
    Task InvalidateForCapabilityVersionAsync(string capabilityId, string newVersion, CancellationToken ct = default);
}
