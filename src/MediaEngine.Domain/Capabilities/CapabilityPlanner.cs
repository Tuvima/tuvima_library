using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

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
            if (string.Equals(definition.Id, CapabilityId.EnrichmentStructuredDiscoveryMetadata, StringComparison.OrdinalIgnoreCase))
            {
                var parsedMediaType = Enum.TryParse<MediaType>(mediaType, true, out var parsed)
                    ? parsed
                    : MediaType.Unknown;
                foreach (var field in StructuredDiscoveryFieldCatalog.Fields
                    .Where(field => field.Source == DiscoveryFactSource.StructuredProvider))
                {
                    var fieldApplicable = IsApplicable(definition, entityKind, mediaType)
                        && (parsedMediaType == MediaType.Unknown || field.IsApplicable(parsedMediaType));
                    await _states.EnsureAsync(new EntityCapabilityState
                    {
                        EntityId = entityId,
                        EntityKind = entityKind,
                        MediaType = mediaType,
                        CapabilityId = definition.Id,
                        CapabilityKind = definition.Kind,
                        CapabilityVersion = StructuredDiscoveryFieldCatalog.CapabilityVersion,
                        SubKey = field.Key,
                        Status = fieldApplicable ? EntityCapabilityStatus.Pending : EntityCapabilityStatus.NotApplicable,
                        Requiredness = definition.DefaultRequiredness,
                        MissingReason = fieldApplicable ? null : "Discovery field is not applicable to this media type."
                    }, ct);
                }
                continue;
            }

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
