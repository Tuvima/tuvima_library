using System.Collections.Concurrent;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Assigns works to ContentGroup collections based on Wikidata QID relationships.
/// After Stage 2 resolves a QID, this service reads the work's canonical values
/// (series_qid, franchise_qid, fictional_universe_qid) and creates or finds
/// a Collection for the parent entity (album, TV show, book series, etc.).
///
/// Uses <see cref="ICollectionRepository.FindByQidAsync"/> to avoid duplicates and
/// <see cref="ICollectionRepository.AssignWorkToCollectionAsync"/> to set the FK.
/// Idempotent — skips works that already have a collection_id.
/// </summary>
public sealed class CollectionAssignmentService
{
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IWorkRepository _workRepo;
    private readonly ILogger<CollectionAssignmentService> _logger;

    // Per-QID semaphores serialise concurrent find-or-create calls so two
    // workers cannot race past FindByQidAsync and both UpsertAsync the same
    // ContentGroup collection. The service is a singleton, so the dictionary lives
    // for the process lifetime; entries are cheap (one SemaphoreSlim each).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> QidLocks = new();

    public CollectionAssignmentService(
        ICollectionRepository collectionRepo,
        ICanonicalValueRepository canonicalRepo,
        IWorkRepository workRepo,
        ILogger<CollectionAssignmentService> logger)
    {
        _collectionRepo = collectionRepo;
        _canonicalRepo = canonicalRepo;
        _workRepo = workRepo;
        _logger = logger;
    }

    /// <summary>
    /// Finds or creates a ContentGroup collection for the work's parent entity
    /// (album, series, show) and assigns the work to it via collection_id FK.
    /// </summary>
    public async Task AssignAsync(Guid entityId, CancellationToken ct = default)
    {
        var lineage = await _workRepo.GetLineageByAssetAsync(entityId, ct);

        Guid? workId = lineage?.WorkId;
        if (workId is null)
            workId = await _collectionRepo.GetWorkIdByMediaAssetAsync(entityId, ct);

        if (workId is null)
        {
            _logger.LogDebug("CollectionAssignment: no work found for entity {EntityId}", entityId);
            return;
        }

        var existingCollectionId = await _collectionRepo.GetCollectionIdByWorkIdAsync(workId.Value, ct);

        // Stage 3 hierarchy claims are parent-scoped, so TV/music/book items need
        // the root work's canonicals instead of the file asset's canonicals.
        var canonicalEntityId = lineage?.TargetForParentScope ?? workId.Value;
        var canonicals = await _canonicalRepo.GetByEntityAsync(canonicalEntityId, ct);
        if (canonicals.Count == 0 && canonicalEntityId != entityId)
            canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);

        var lookup = canonicals.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

        // Try to find a parent QID from Wikidata relationship properties.
        // Priority: series_qid (P179) > franchise_qid (P8345) > fictional_universe_qid (P1434)
        // We use the most specific level for collection assignment — an album or TV show,
        // not the broader franchise/universe.
        var (parentQid, parentLabel) = ResolveParentQid(lookup);

        if (string.IsNullOrWhiteSpace(parentQid))
        {
            _logger.LogDebug("CollectionAssignment: no parent QID for work {WorkId} — standalone", workId);
            return;
        }

        if (existingCollectionId is not null)
        {
            var existingCollection = await _collectionRepo.GetByIdAsync(existingCollectionId.Value, ct);
            if (existingCollection is not null)
            {
                await EnsureCollectionRelationshipsAsync(existingCollection.Id, lookup, ct);

                _logger.LogDebug(
                    "CollectionAssignment: work {WorkId} already assigned to collection {CollectionId}",
                    workId,
                    existingCollectionId);
                return;
            }
        }

        // Find or create a ContentGroup collection for this parent QID.
        // Serialise on the QID so two workers cannot race past FindByQidAsync
        // and both Upsert a duplicate collection for the same album/show/series.
        var gate = QidLocks.GetOrAdd(parentQid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        Collection collection;
        try
        {
            var existing = await _collectionRepo.FindByQidAsync(parentQid, ct);
            if (existing is not null)
            {
                collection = existing;
            }
            else
            {
                // Sanitize the label — fall back to QID if label is just a QID or empty
                if (string.IsNullOrWhiteSpace(parentLabel) ||
                    (parentLabel.Length > 1 && parentLabel[0] is 'Q' && char.IsDigit(parentLabel[1])))
                {
                    parentLabel = parentQid;
                }

                collection = new Collection
                {
                    Id = Guid.NewGuid(),
                    DisplayName = parentLabel,
                    WikidataQid = parentQid,
                    CollectionType = "ContentGroup",
                    Resolution = "materialized",
                    UniverseStatus = "Unknown",
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                await _collectionRepo.UpsertAsync(collection, ct);

                _logger.LogInformation(
                    "CollectionAssignment: created ContentGroup collection '{Name}' ({Qid}) for work {WorkId}",
                    collection.DisplayName, parentQid, workId);
            }

            await EnsureCollectionRelationshipsAsync(collection.Id, lookup, ct);
        }
        finally
        {
            gate.Release();
        }

        // Assign the work to the collection
        await _collectionRepo.AssignWorkToCollectionAsync(workId.Value, collection.Id, ct);

        _logger.LogInformation(
            "CollectionAssignment: assigned work {WorkId} to collection '{CollectionName}' ({Qid})",
            workId, collection.DisplayName, parentQid);
    }

    /// <summary>
    /// Extracts the most specific parent QID from canonical values.
    /// Priority: series (P179) → franchise (P8345) → fictional_universe (P1434).
    /// Returns the QID and human-readable label.
    /// </summary>
    private static (string? Qid, string? Label) ResolveParentQid(
        Dictionary<string, string> lookup)
    {
        // Try series first (most specific: album, TV show, book series)
        if (TryGetQid(lookup, "series", out var qid, out var label))
            return (qid, label);

        // Then franchise
        if (TryGetQid(lookup, "franchise", out qid, out label))
            return (qid, label);

        // Then fictional universe (broadest)
        if (TryGetQid(lookup, "fictional_universe", out qid, out label))
            return (qid, label);

        return (null, null);
    }

    /// <summary>
    /// Extracts a clean QID and label from canonical values for a given claim key.
    /// Handles URI prefixes, :: suffixes, and ||| legacy separators.
    /// </summary>
    private static bool TryGetQid(
        Dictionary<string, string> lookup,
        string claimKey,
        out string qid,
        out string label)
    {
        qid = string.Empty;
        label = string.Empty;

        if (!lookup.TryGetValue($"{claimKey}_qid", out var rawQid) ||
            string.IsNullOrWhiteSpace(rawQid))
            return false;

        // Strip entity URI prefix
        qid = rawQid.Contains('/') ? rawQid.Split('/')[^1] : rawQid;

        // Strip ||| legacy separator
        if (qid.Contains("|||"))
            qid = qid.Split("|||")[0].Trim();

        // Strip ::Label suffix
        if (qid.Contains("::"))
            qid = qid.Split("::", 2)[0];

        if (string.IsNullOrWhiteSpace(qid))
            return false;

        // Get the label
        if (lookup.TryGetValue(claimKey, out var rawLabel) && !string.IsNullOrWhiteSpace(rawLabel))
        {
            var cleaned = rawLabel.Contains("|||") ? rawLabel.Split("|||")[0].Trim() : rawLabel;
            label = cleaned.Contains("::") ? cleaned.Split("::", 2)[^1].Trim() : cleaned;
        }
        else
        {
            label = qid;
        }

        return true;
    }

    private async Task EnsureCollectionRelationshipsAsync(
        Guid collectionId,
        Dictionary<string, string> lookup,
        CancellationToken ct)
    {
        var desiredRelationships = BuildCollectionRelationships(collectionId, lookup);
        if (desiredRelationships.Count == 0)
            return;

        var existingRelationships = await _collectionRepo.GetRelationshipsAsync(collectionId, ct);
        var missingRelationships = desiredRelationships
            .Where(candidate => !existingRelationships.Any(existing =>
                string.Equals(existing.RelType, candidate.RelType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.RelQid, candidate.RelQid, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingRelationships.Count == 0)
            return;

        await _collectionRepo.InsertRelationshipsAsync(missingRelationships, ct);

        _logger.LogInformation(
            "CollectionAssignment: added {Count} relationship(s) to collection {CollectionId}",
            missingRelationships.Count,
            collectionId);
    }

    private static IReadOnlyList<CollectionRelationship> BuildCollectionRelationships(
        Guid collectionId,
        Dictionary<string, string> lookup)
    {
        var relationships = new List<CollectionRelationship>();
        foreach (var claimKey in new[] { "series", "franchise", "fictional_universe" })
        {
            if (!TryGetQid(lookup, claimKey, out var qid, out var label))
                continue;

            relationships.Add(new CollectionRelationship
            {
                Id = Guid.NewGuid(),
                CollectionId = collectionId,
                RelType = claimKey,
                RelQid = qid,
                RelLabel = label,
                Confidence = 1.0,
                DiscoveredAt = DateTimeOffset.UtcNow,
            });
        }

        return relationships;
    }
}
