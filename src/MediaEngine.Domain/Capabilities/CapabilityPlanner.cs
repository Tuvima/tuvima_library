using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Capabilities;

public sealed class CapabilityPlanner
{
    private readonly CapabilityRegistry _registry;
    private readonly IEntityCapabilityStateRepository _states;

    public CapabilityPlanner(CapabilityRegistry registry, IEntityCapabilityStateRepository states)
    {
        _registry = registry;
        _states = states;
    }

    public async Task EnsureForAssetAsync(
        Guid entityId,
        string entityKind,
        string? mediaType,
        CancellationToken ct = default)
    {
        foreach (var definition in _registry.All)
        {
            var applicable = IsApplicable(definition, entityKind, mediaType);
            var status = applicable ? EntityCapabilityStatus.Pending : EntityCapabilityStatus.NotApplicable;
            await _states.EnsureAsync(new EntityCapabilityState
            {
                EntityId = entityId,
                EntityKind = entityKind,
                MediaType = mediaType,
                CapabilityId = definition.Id,
                CapabilityKind = definition.Kind,
                CapabilityVersion = definition.CurrentVersion,
                Status = status,
                Requiredness = definition.DefaultRequiredness,
                MissingReason = applicable ? null : "Capability is not applicable to this entity or media type."
            }, ct);
        }
    }

    public Task MarkVersionChangedAsync(string capabilityId, string newVersion, CancellationToken ct = default)
        => _states.InvalidateForCapabilityVersionAsync(capabilityId, newVersion, ct);

    private static bool IsApplicable(CapabilityDefinition definition, string entityKind, string? mediaType)
    {
        var entityMatches = definition.EntityKinds.Count == 0 || definition.EntityKinds.Contains(entityKind);
        var mediaMatches = definition.MediaTypes.Count == 0
            || (mediaType is not null && definition.MediaTypes.Contains(mediaType));
        return entityMatches && mediaMatches;
    }
}
