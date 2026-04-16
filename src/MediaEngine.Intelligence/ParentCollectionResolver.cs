using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Intelligence;

/// <summary>
/// Examines Collection relationships to detect franchise-level groupings and promote
/// sibling Collections under a common Parent Collection.
///
/// A Parent Collection is created when either:
/// 1. 2+ Collections share a <see cref="CollectionRelationship"/> with the same
///    <c>rel_qid</c> and a <c>rel_type</c> in ("franchise", "fictional_universe"), or
/// 2. A single ContentGroup Collection exposes a broader franchise/universe QID that is
///    different from the Collection's own <c>wikidata_qid</c>.
///
/// The Parent Collection is itself a Collection row with <c>parent_collection_id = NULL</c>,
/// its <see cref="Collection.DisplayName"/> taken from the relationship's
/// <c>rel_label</c>, and child Collections pointing to it via
/// <c>parent_collection_id</c>.
/// </summary>
public sealed class ParentCollectionResolver : IParentCollectionResolver
{
    private static readonly HashSet<string> FranchiseRelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "franchise",
        "fictional_universe",
    };

    private readonly ICollectionRepository _collectionRepo;
    private readonly ILogger<ParentCollectionResolver> _logger;

    public ParentCollectionResolver(ICollectionRepository collectionRepo, ILogger<ParentCollectionResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(collectionRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _collectionRepo = collectionRepo;
        _logger  = logger;
    }

    /// <inheritdoc />
    public async Task ResolveParentCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Load the Collection row (lightweight — no Works, no relationships).
        var collection = await _collectionRepo.GetByIdAsync(collectionId, ct).ConfigureAwait(false);
        if (collection is null)
        {
            _logger.LogWarning("ParentCollectionResolver: Hub {CollectionId} not found — skipping.", collectionId);
            return;
        }

        // 2. Idempotency guard: already has a parent.
        if (collection.ParentCollectionId is not null)
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Hub {CollectionId} already has parent {ParentCollectionId} — no-op.",
                collectionId, collection.ParentCollectionId);
            return;
        }

        // 3. Load this Collection's relationships.
        var relationships = await _collectionRepo.GetRelationshipsAsync(collectionId, ct).ConfigureAwait(false);

        // 4. Filter for franchise / fictional_universe relationships.
        var franchiseRels = relationships
            .Where(r => FranchiseRelTypes.Contains(r.RelType))
            .ToList();

        if (franchiseRels.Count == 0)
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Hub {CollectionId} has no franchise relationships — no-op.",
                collectionId);
            return;
        }

        // 5. Process each franchise QID.
        foreach (var rel in franchiseRels)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessFranchiseAsync(collection, rel, ct).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task ProcessFranchiseAsync(
        Collection collection,
        CollectionRelationship rel,
        CancellationToken ct)
    {
        var collectionId = collection.Id;
        string qid   = rel.RelQid;
        string label = rel.RelLabel ?? qid;

        // a) Check if a Parent Collection already exists for this franchise QID.
        var existing = await _collectionRepo.FindParentCollectionByRelationshipAsync(qid, ct).ConfigureAwait(false);

        if (existing is not null && existing.Id != collectionId)
        {
            // Parent Collection exists — assign this Collection as a child if not already done.
            // (FindParentCollectionByRelationshipAsync returns collections with parent_collection_id IS NULL,
            //  so existing is always a proper Parent Collection row.)
            _logger.LogInformation(
                "ParentCollectionResolver: Assigning Collection {CollectionId} to existing Parent Collection {ParentCollectionId} (franchise QID {Qid}).",
                collectionId, existing.Id, qid);

            await _collectionRepo.SetParentCollectionAsync(collectionId, existing.Id, ct).ConfigureAwait(false);
            return;
        }

        if (existing is not null)
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Ignoring self-match for Collection {CollectionId} (franchise QID {Qid}).",
                collectionId,
                qid);
        }

        // b) No Parent Collection yet — check whether 2+ sibling Collections share this QID.
        var siblingIds = await _collectionRepo.FindCollectionIdsByFranchiseQidAsync(qid, ct).ConfigureAwait(false);
        var hasBroaderUniverseQid =
            !string.IsNullOrWhiteSpace(collection.WikidataQid)
            && !string.Equals(collection.WikidataQid, qid, StringComparison.OrdinalIgnoreCase);

        // Filter out collections that already have a parent (they've been processed before).
        // We collect IDs to re-parent after creating the new Parent Collection.
        // Note: We include the current collection in the sibling set — it will be set too.
        if (siblingIds.Count < 2 && !hasBroaderUniverseQid)
        {
            // Only one Collection (this one) carries this franchise QID — not enough to form a group.
            _logger.LogDebug(
                "ParentCollectionResolver: Only {Count} Collection(s) share franchise QID {Qid} — not enough to form a Parent Collection.",
                siblingIds.Count, qid);
            return;
        }

        // c) Create the new Parent Collection.
        var parentCollection = new Collection
        {
            Id             = Guid.NewGuid(),
            DisplayName    = label,
            CreatedAt      = DateTimeOffset.UtcNow,
            UniverseStatus = "Unknown",
            CollectionType = "Universe",
            Resolution     = "materialized",
            // ParentCollectionId remains null — Parent Collections are top-level.
        };

        await _collectionRepo.UpsertAsync(parentCollection, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "ParentCollectionResolver: Created Parent Collection {ParentCollectionId} '{Label}' for franchise QID {Qid} " +
            "({SiblingCount} sibling(s), broaderMatch={HasBroaderUniverseQid}).",
            parentCollection.Id, label, qid, siblingIds.Count, hasBroaderUniverseQid);

        // d) Add a franchise relationship to the Parent Collection so future calls to
        //    FindParentCollectionByRelationshipAsync can locate it.
        var parentRel = new CollectionRelationship
        {
            Id           = Guid.NewGuid(),
            CollectionId        = parentCollection.Id,
            RelType      = rel.RelType,
            RelQid       = qid,
            RelLabel     = rel.RelLabel,
            Confidence   = rel.Confidence,
            DiscoveredAt = DateTimeOffset.UtcNow,
        };

        await _collectionRepo.InsertRelationshipsAsync([parentRel], ct).ConfigureAwait(false);

        // e) Assign all sibling Collections (that don't yet have a parent) to the new Parent Collection.
        foreach (var siblingId in siblingIds)
        {
            ct.ThrowIfCancellationRequested();

            // Skip collections that already have a parent assigned from a previous resolution pass.
            var sibling = await _collectionRepo.GetByIdAsync(siblingId, ct).ConfigureAwait(false);
            if (sibling is null || sibling.ParentCollectionId is not null)
                continue;

            await _collectionRepo.SetParentCollectionAsync(siblingId, parentCollection.Id, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "ParentCollectionResolver: Hub {CollectionId} assigned to Parent Collection {ParentCollectionId} (franchise QID {Qid}).",
                siblingId, parentCollection.Id, qid);
        }
    }
}
