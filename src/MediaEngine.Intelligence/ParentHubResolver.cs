using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Intelligence;

/// <summary>
/// Examines Hub relationships to detect franchise-level groupings and promote
/// sibling Hubs under a common Parent Hub.
///
/// A Parent Hub is created when 2+ Hubs share a <see cref="HubRelationship"/>
/// with the same <c>rel_qid</c> and a <c>rel_type</c> in
/// ("franchise", "fictional_universe").
///
/// The Parent Hub is itself a Hub row with <c>parent_hub_id = NULL</c>,
/// its <see cref="Hub.DisplayName"/> taken from the relationship's
/// <c>rel_label</c>, and child Hubs pointing to it via
/// <c>parent_hub_id</c>.
/// </summary>
public sealed class ParentHubResolver : IParentHubResolver
{
    private static readonly HashSet<string> FranchiseRelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "franchise",
        "fictional_universe",
    };

    private readonly IHubRepository _hubRepo;
    private readonly ILogger<ParentHubResolver> _logger;

    public ParentHubResolver(IHubRepository hubRepo, ILogger<ParentHubResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(hubRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _hubRepo = hubRepo;
        _logger  = logger;
    }

    /// <inheritdoc />
    public async Task ResolveParentHubAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Load the Hub row (lightweight — no Works, no relationships).
        var hub = await _hubRepo.GetByIdAsync(hubId, ct).ConfigureAwait(false);
        if (hub is null)
        {
            _logger.LogWarning("ParentHubResolver: Hub {HubId} not found — skipping.", hubId);
            return;
        }

        // 2. Idempotency guard: already has a parent.
        if (hub.ParentHubId is not null)
        {
            _logger.LogDebug(
                "ParentHubResolver: Hub {HubId} already has parent {ParentHubId} — no-op.",
                hubId, hub.ParentHubId);
            return;
        }

        // 3. Load this Hub's relationships.
        var relationships = await _hubRepo.GetRelationshipsAsync(hubId, ct).ConfigureAwait(false);

        // 4. Filter for franchise / fictional_universe relationships.
        var franchiseRels = relationships
            .Where(r => FranchiseRelTypes.Contains(r.RelType))
            .ToList();

        if (franchiseRels.Count == 0)
        {
            _logger.LogDebug(
                "ParentHubResolver: Hub {HubId} has no franchise relationships — no-op.",
                hubId);
            return;
        }

        // 5. Process each franchise QID.
        foreach (var rel in franchiseRels)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessFranchiseAsync(hubId, rel, ct).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task ProcessFranchiseAsync(
        Guid hubId,
        HubRelationship rel,
        CancellationToken ct)
    {
        string qid   = rel.RelQid;
        string label = rel.RelLabel ?? qid;

        // a) Check if a Parent Hub already exists for this franchise QID.
        var existing = await _hubRepo.FindParentHubByRelationshipAsync(qid, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            // Parent Hub exists — assign this Hub as a child if not already done.
            // (FindParentHubByRelationshipAsync returns hubs with parent_hub_id IS NULL,
            //  so existing is always a proper Parent Hub row.)
            _logger.LogInformation(
                "ParentHubResolver: Assigning Hub {HubId} to existing Parent Hub {ParentHubId} (franchise QID {Qid}).",
                hubId, existing.Id, qid);

            await _hubRepo.SetParentHubAsync(hubId, existing.Id, ct).ConfigureAwait(false);
            return;
        }

        // b) No Parent Hub yet — check whether 2+ sibling Hubs share this QID.
        var siblingIds = await _hubRepo.FindHubIdsByFranchiseQidAsync(qid, ct).ConfigureAwait(false);

        // Filter out hubs that already have a parent (they've been processed before).
        // We collect IDs to re-parent after creating the new Parent Hub.
        // Note: We include the current hub in the sibling set — it will be set too.
        if (siblingIds.Count < 2)
        {
            // Only one Hub (this one) carries this franchise QID — not enough to form a group.
            _logger.LogDebug(
                "ParentHubResolver: Only {Count} Hub(s) share franchise QID {Qid} — not enough to form a Parent Hub.",
                siblingIds.Count, qid);
            return;
        }

        // c) Create the new Parent Hub.
        var parentHub = new Hub
        {
            Id             = Guid.NewGuid(),
            DisplayName    = label,
            CreatedAt      = DateTimeOffset.UtcNow,
            UniverseStatus = "Unknown",
            // ParentHubId remains null — Parent Hubs are top-level.
        };

        await _hubRepo.UpsertAsync(parentHub, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "ParentHubResolver: Created Parent Hub {ParentHubId} '{Label}' for franchise QID {Qid} " +
            "({SiblingCount} sibling(s)).",
            parentHub.Id, label, qid, siblingIds.Count);

        // d) Add a franchise relationship to the Parent Hub so future calls to
        //    FindParentHubByRelationshipAsync can locate it.
        var parentRel = new HubRelationship
        {
            Id           = Guid.NewGuid(),
            HubId        = parentHub.Id,
            RelType      = rel.RelType,
            RelQid       = qid,
            RelLabel     = rel.RelLabel,
            Confidence   = rel.Confidence,
            DiscoveredAt = DateTimeOffset.UtcNow,
        };

        await _hubRepo.InsertRelationshipsAsync([parentRel], ct).ConfigureAwait(false);

        // e) Assign all sibling Hubs (that don't yet have a parent) to the new Parent Hub.
        foreach (var siblingId in siblingIds)
        {
            ct.ThrowIfCancellationRequested();

            // Skip hubs that already have a parent assigned from a previous resolution pass.
            var sibling = await _hubRepo.GetByIdAsync(siblingId, ct).ConfigureAwait(false);
            if (sibling is null || sibling.ParentHubId is not null)
                continue;

            await _hubRepo.SetParentHubAsync(siblingId, parentHub.Id, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "ParentHubResolver: Hub {HubId} assigned to Parent Hub {ParentHubId} (franchise QID {Qid}).",
                siblingId, parentHub.Id, qid);
        }
    }
}
