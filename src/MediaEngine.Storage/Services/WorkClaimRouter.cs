using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Services;

/// <summary>
/// Decides which Work id should receive a metadata claim or external
/// identifier given the asset's lineage. The router is the single
/// point at which "is this a track-level fact or an album-level fact?"
/// gets answered.
///
/// Used by:
///   • <c>RetailMatchWorker</c> — routes provider bridge IDs to the
///     parent Work (album, show, series) or the child Work (track,
///     episode, issue) before calling <c>WriteExternalIdentifiersAsync</c>.
///   • <c>WikidataBridgeWorker</c> — splits a Wikidata claim batch into
///     two batches (parent-scoped and self-scoped) and persists each
///     under the correct entity id, eliminating the conflation that
///     used to overwrite an album's identity with each track's claims.
///
/// The router is purely a routing service: it never reads or writes the
/// database itself. It is stateless and has no constructor dependencies.
/// </summary>
public sealed class WorkClaimRouter
{
    /// <summary>
    /// Returns the target Work id for the given claim key under the
    /// given lineage. Looks up the scope from <see cref="ClaimScopeRegistry"/>
    /// and routes to the appropriate Work id.
    /// </summary>
    public Guid Route(WorkLineage lineage, string claimKey)
    {
        ArgumentNullException.ThrowIfNull(lineage);

        var scope = ClaimScopeRegistry.GetScope(claimKey, lineage.MediaType);
        return scope switch
        {
            ClaimScope.Parent => lineage.TargetForParentScope,
            _                 => lineage.TargetForSelfScope,
        };
    }

    /// <summary>
    /// Splits a dictionary of bridge IDs (or any string→string map) into
    /// two buckets: identifiers that belong to the parent Work and
    /// identifiers that belong to the asset's own Work.
    /// </summary>
    public (Dictionary<string, string> ForParent, Dictionary<string, string> ForSelf)
        SplitBridgeIds(WorkLineage lineage, IReadOnlyDictionary<string, string> bridgeIds)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(bridgeIds);

        var forParent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var forSelf   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in bridgeIds)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var scope = ClaimScopeRegistry.GetScope(key, lineage.MediaType);
            if (scope == ClaimScope.Parent)
                forParent[key] = value;
            else
                forSelf[key] = value;
        }

        return (forParent, forSelf);
    }

    /// <summary>
    /// Splits a list of <see cref="MetadataClaim"/> records into two lists:
    /// claims that belong to the parent Work and claims that belong to the
    /// asset's own Work. The returned claims are clones with their
    /// <see cref="MetadataClaim.EntityId"/> rewritten to point at the
    /// appropriate Work id.
    ///
    /// Callers persist each list with two separate batch inserts —
    /// no schema discriminator is needed because Work and MediaAsset GUIDs
    /// occupy different namespaces.
    /// </summary>
    public (List<MetadataClaim> ForParent, List<MetadataClaim> ForSelf)
        SplitClaims(WorkLineage lineage, IReadOnlyList<MetadataClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(claims);

        var forParent = new List<MetadataClaim>(claims.Count);
        var forSelf   = new List<MetadataClaim>(claims.Count);

        var parentTarget = lineage.TargetForParentScope;
        var selfTarget   = lineage.TargetForSelfScope;

        foreach (var claim in claims)
        {
            var scope = ClaimScopeRegistry.GetScope(claim.ClaimKey, lineage.MediaType);
            var rerouted = new MetadataClaim
            {
                Id           = claim.Id,
                EntityId     = scope == ClaimScope.Parent ? parentTarget : selfTarget,
                ProviderId   = claim.ProviderId,
                ClaimKey     = claim.ClaimKey,
                ClaimValue   = claim.ClaimValue,
                Confidence   = claim.Confidence,
                ClaimedAt    = claim.ClaimedAt,
                IsUserLocked = claim.IsUserLocked,
            };

            if (scope == ClaimScope.Parent)
                forParent.Add(rerouted);
            else
                forSelf.Add(rerouted);
        }

        return (forParent, forSelf);
    }
}
