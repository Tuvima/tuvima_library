using MediaEngine.Domain.Capabilities;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Workers;

public sealed partial class WikidataBridgeWorker
{
    private async Task MarkStructuredDiscoveryObservedAsync(
        Guid assetId,
        WorkLineage? lineage,
        MediaType mediaType,
        IReadOnlyList<ProviderClaim> claims,
        CancellationToken ct)
    {
        if (_capabilityStates is null)
            return;

        foreach (var field in StructuredDiscoveryFieldCatalog.Fields
            .Where(field => field.Source == DiscoveryFactSource.StructuredProvider && field.IsApplicable(mediaType)))
        {
            var scope = ClaimScopeCatalog.GetScope(field.Key, mediaType);
            var targetId = lineage is null
                ? assetId
                : scope switch
                {
                    ClaimScope.Parent => lineage.TargetForParentScope,
                    ClaimScope.Edition => lineage.EditionId,
                    ClaimScope.Asset => lineage.AssetId,
                    ClaimScope.Work => lineage.WorkId,
                    _ => lineage.TargetForSelfScope,
                };
            var entityKind = scope switch
            {
                ClaimScope.Edition => "edition",
                ClaimScope.Asset => "asset",
                _ when lineage is not null => "work",
                _ => "asset",
            };
            var matching = claims
                .Where(claim => string.Equals(claim.Key, field.Key, StringComparison.OrdinalIgnoreCase))
                .Where(claim => !string.IsNullOrWhiteSpace(claim.Value))
                .ToArray();

            await _capabilityStates.EnsureAsync(new EntityCapabilityState
            {
                EntityId = targetId,
                EntityKind = entityKind,
                MediaType = mediaType.ToString(),
                CapabilityId = CapabilityId.EnrichmentStructuredDiscoveryMetadata,
                CapabilityKind = "enrichment",
                CapabilityVersion = StructuredDiscoveryFieldCatalog.CapabilityVersion,
                SubKey = field.Key,
                Status = EntityCapabilityStatus.Pending,
                Requiredness = CapabilityRequiredness.Optional,
            }, ct).ConfigureAwait(false);

            if (matching.Length > 0)
            {
                await _capabilityStates.MarkSucceededAsync(
                    targetId,
                    CapabilityId.EnrichmentStructuredDiscoveryMetadata,
                    field.Key,
                    new CapabilityStateResult(
                        Source: "wikidata",
                        Confidence: matching.Max(claim => claim.Confidence),
                        ArtifactCount: matching.Length,
                        ArtifactSummary: field.Label,
                        ResultSummary: $"Observed {matching.Length} {field.Label.ToLowerInvariant()} value(s)."),
                    ct).ConfigureAwait(false);
            }
            else
            {
                await _capabilityStates.MarkNoResultAsync(
                    targetId,
                    CapabilityId.EnrichmentStructuredDiscoveryMetadata,
                    field.Key,
                    $"Wikidata returned no current {field.Label.ToLowerInvariant()} statement.",
                    ct).ConfigureAwait(false);
            }

            if (targetId != assetId)
            {
                await _capabilityStates.MarkNotApplicableAsync(
                    assetId,
                    CapabilityId.EnrichmentStructuredDiscoveryMetadata,
                    field.Key,
                    "Discovery knowledge state is recorded on the field's canonical scope.",
                    ct).ConfigureAwait(false);
            }
        }
    }
}
