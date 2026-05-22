using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Intelligence;

/// <summary>
/// Examines Collection relationships to detect broader rollup groupings and promote
/// sibling shelf Collections under a common Parent Collection.
///
/// A Parent Collection is created when 2+ shelf Collections share a
/// <see cref="CollectionRelationship"/> with the same <c>rel_qid</c> and a
/// <c>rel_type</c> in ("series", "franchise", "fictional_universe").
///
/// The Parent Collection is itself a Collection row with <c>parent_collection_id = NULL</c>,
/// its <see cref="Collection.DisplayName"/> taken from the relationship's
/// <c>rel_label</c>, and child Collections pointing to it via
/// <c>parent_collection_id</c>.
/// </summary>
public sealed class ParentCollectionResolver : IParentCollectionResolver
{
    private static readonly HashSet<string> RollupRelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "series",
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
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ResolveParentCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var collection = await _collectionRepo.GetByIdAsync(collectionId, ct).ConfigureAwait(false);
        if (collection is null)
        {
            _logger.LogWarning("ParentCollectionResolver: Collection {CollectionId} not found; skipping.", collectionId);
            return;
        }

        if (collection.ParentCollectionId is not null)
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Collection {CollectionId} already has parent {ParentCollectionId}; no-op.",
                collectionId, collection.ParentCollectionId);
            return;
        }

        var relationships = await _collectionRepo.GetRelationshipsAsync(collectionId, ct).ConfigureAwait(false);
        var rollupRels = relationships
            .Where(r => RollupRelTypes.Contains(r.RelType))
            .ToList();

        if (rollupRels.Count == 0)
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Collection {CollectionId} has no rollup relationships; no-op.",
                collectionId);
            return;
        }

        foreach (var rel in rollupRels)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessRollupAsync(collection, rel, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessRollupAsync(
        Collection collection,
        CollectionRelationship rel,
        CancellationToken ct)
    {
        var collectionId = collection.Id;
        var qid = rel.RelQid;
        var label = rel.RelLabel ?? qid;

        if (string.Equals(collection.WikidataQid, qid, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Ignoring self-match for Collection {CollectionId} (rollup QID {Qid}).",
                collectionId,
                qid);
            return;
        }

        var existing = await _collectionRepo.FindParentCollectionByRelationshipAsync(qid, ct).ConfigureAwait(false);
        if (existing is not null && existing.Id != collectionId)
        {
            _logger.LogInformation(
                "ParentCollectionResolver: Assigning Collection {CollectionId} to existing Parent Collection {ParentCollectionId} (rollup QID {Qid}).",
                collectionId, existing.Id, qid);

            await _collectionRepo.SetParentCollectionAsync(collectionId, existing.Id, ct).ConfigureAwait(false);
            return;
        }

        var siblingIds = await _collectionRepo.FindCollectionIdsByFranchiseQidAsync(qid, ct).ConfigureAwait(false);
        if (siblingIds.Count < 2)
        {
            _logger.LogDebug(
                "ParentCollectionResolver: Only {Count} shelf Collection(s) share rollup QID {Qid}; no parent created.",
                siblingIds.Count, qid);
            return;
        }

        var parentCollection = new Collection
        {
            Id = Guid.NewGuid(),
            DisplayName = label,
            CreatedAt = DateTimeOffset.UtcNow,
            UniverseStatus = "Unknown",
            CollectionType = "Universe",
            Resolution = "materialized",
            WikidataQid = qid,
        };

        await _collectionRepo.UpsertAsync(parentCollection, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "ParentCollectionResolver: Created Parent Collection {ParentCollectionId} '{Label}' for rollup QID {Qid} ({SiblingCount} sibling(s)).",
            parentCollection.Id, label, qid, siblingIds.Count);

        var parentRel = new CollectionRelationship
        {
            Id = Guid.NewGuid(),
            CollectionId = parentCollection.Id,
            RelType = rel.RelType,
            RelQid = qid,
            RelLabel = rel.RelLabel,
            Confidence = rel.Confidence,
            DiscoveredAt = DateTimeOffset.UtcNow,
        };

        await _collectionRepo.InsertRelationshipsAsync([parentRel], ct).ConfigureAwait(false);

        foreach (var siblingId in siblingIds)
        {
            ct.ThrowIfCancellationRequested();

            var sibling = await _collectionRepo.GetByIdAsync(siblingId, ct).ConfigureAwait(false);
            if (sibling is null || sibling.ParentCollectionId is not null)
                continue;

            await _collectionRepo.SetParentCollectionAsync(siblingId, parentCollection.Id, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "ParentCollectionResolver: Collection {CollectionId} assigned to Parent Collection {ParentCollectionId} (rollup QID {Qid}).",
                siblingId, parentCollection.Id, qid);
        }
    }
}
