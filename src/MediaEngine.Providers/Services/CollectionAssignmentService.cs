using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Assigns works to ContentGroup collections based on stable shelf identities.
/// QIDs are preferred when Stage 2 has resolved them; provider-backed keys such
/// as TMDB movie collections and TV show IDs keep shelves stable before Wikidata
/// catches up. Broader franchise/universe relationships remain rollup inputs.
///
/// Uses QID/rule-hash lookups to avoid duplicates and
/// <see cref="ICollectionRepository.AssignWorkToCollectionAsync"/> to set the FK.
/// Idempotent — skips works that already have a collection_id.
/// </summary>
public sealed class CollectionAssignmentService
{
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IWorkRepository _workRepo;
    private readonly ILogger<CollectionAssignmentService> _logger;

    // Per-shelf semaphores serialise concurrent find-or-create calls so two
    // workers cannot race past lookup and both UpsertAsync the same ContentGroup
    // collection. The service is a singleton, so entries are process-lifetime.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShelfLocks = new();

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
        var lookup = canonicals.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        var mediaType = lineage?.MediaType ?? ResolveMediaType(lookup);

        // Try to find an immediate shelf identity. Broader franchise/universe
        // values are relationships on the shelf and become Collections only
        // when multiple shelves share them.
        var shelf = ResolveShelfIdentity(lookup, mediaType);

        if (shelf is null)
        {
            _logger.LogDebug("CollectionAssignment: no shelf identity for work {WorkId}; standalone", workId);
            return;
        }

        if (existingCollectionId is not null)
        {
            var existingCollection = await _collectionRepo.GetByIdAsync(existingCollectionId.Value, ct);
            if (existingCollection is not null && CollectionMatchesShelf(existingCollection, shelf))
            {
                await UpgradeCollectionIdentityAsync(existingCollection, shelf, ct);
                await EnsureCollectionRelationshipsAsync(existingCollection.Id, lookup, ct);

                _logger.LogDebug(
                    "CollectionAssignment: work {WorkId} already assigned to collection {CollectionId}",
                    workId,
                    existingCollectionId);
                return;
            }

            if (existingCollection is not null)
            {
                _logger.LogInformation(
                    "CollectionAssignment: work {WorkId} is assigned to broader collection '{CollectionName}' ({ExistingQid}) but immediate shelf is {ParentQid}; reassigning",
                    workId,
                    existingCollection.DisplayName,
                    existingCollection.WikidataQid,
                    shelf.Qid ?? shelf.ProviderKey);
            }
        }

        // Find or create a ContentGroup collection for this parent QID.
        // Serialise on the QID so two workers cannot race past FindByQidAsync
        // and both Upsert a duplicate collection for the same album/show/series.
        var gate = ShelfLocks.GetOrAdd(shelf.LockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        Collection collection;
        try
        {
            var existing = await FindShelfCollectionAsync(shelf, ct);
            if (existing is not null)
            {
                collection = existing;
                await UpgradeCollectionIdentityAsync(collection, shelf, ct);
            }
            else
            {
                // Sanitize the label — fall back to QID if label is just a QID or empty
                var displayName = SanitizeShelfLabel(shelf.Label, shelf.Qid, shelf.ProviderKey);

                collection = new Collection
                {
                    Id = Guid.NewGuid(),
                    DisplayName = displayName,
                    WikidataQid = shelf.Qid,
                    CollectionType = "ContentGroup",
                    Resolution = "materialized",
                    RuleHash = shelf.ProviderKey,
                    GroupByField = GetGroupByField(shelf.MediaType),
                    UniverseStatus = "Unknown",
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                await _collectionRepo.UpsertAsync(collection, ct);

                _logger.LogInformation(
                    "CollectionAssignment: created ContentGroup collection '{Name}' ({Identity}) for work {WorkId}",
                    collection.DisplayName, shelf.Qid ?? shelf.ProviderKey, workId);
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
            "CollectionAssignment: assigned work {WorkId} to collection '{CollectionName}' ({Identity})",
            workId, collection.DisplayName, shelf.Qid ?? shelf.ProviderKey);
    }

    private async Task<Collection?> FindShelfCollectionAsync(ShelfIdentity shelf, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(shelf.Qid))
        {
            var byQid = await _collectionRepo.FindByQidAsync(shelf.Qid, ct);
            if (byQid is not null)
                return byQid;
        }

        return string.IsNullOrWhiteSpace(shelf.ProviderKey)
            ? null
            : await _collectionRepo.FindByRuleHashAsync(shelf.ProviderKey, ct);
    }

    private async Task UpgradeCollectionIdentityAsync(Collection collection, ShelfIdentity shelf, CancellationToken ct)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(shelf.Qid)
            && !string.Equals(collection.WikidataQid, shelf.Qid, StringComparison.OrdinalIgnoreCase))
        {
            collection.WikidataQid = shelf.Qid;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(shelf.ProviderKey)
            && !string.Equals(collection.RuleHash, shelf.ProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            collection.RuleHash = shelf.ProviderKey;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(collection.DisplayName) || IsQidLike(collection.DisplayName))
        {
            collection.DisplayName = SanitizeShelfLabel(shelf.Label, shelf.Qid, shelf.ProviderKey);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(collection.GroupByField))
        {
            collection.GroupByField = GetGroupByField(shelf.MediaType);
            changed = true;
        }

        if (changed)
            await _collectionRepo.UpsertAsync(collection, ct);
    }

    private static bool CollectionMatchesShelf(Collection collection, ShelfIdentity shelf)
    {
        if (!string.IsNullOrWhiteSpace(shelf.Qid)
            && string.Equals(collection.WikidataQid, shelf.Qid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(shelf.ProviderKey)
            && string.Equals(collection.RuleHash, shelf.ProviderKey, StringComparison.OrdinalIgnoreCase);
    }

    private static ShelfIdentity? ResolveShelfIdentity(
        Dictionary<string, string> lookup,
        MediaType mediaType)
    {
        var relationshipKeys = BuildRelationshipKeys(lookup);

        if (TryGetQid(lookup, "series", out var qid, out var label))
        {
            return new ShelfIdentity(
                mediaType,
                label,
                qid,
                ResolveProviderKey(lookup, mediaType),
                relationshipKeys);
        }

        var providerKey = ResolveProviderKey(lookup, mediaType);
        var providerLabel = ResolveProviderLabel(lookup, mediaType);
        if (!string.IsNullOrWhiteSpace(providerKey) && !string.IsNullOrWhiteSpace(providerLabel))
            return new ShelfIdentity(mediaType, providerLabel, null, providerKey, relationshipKeys);

        var localLabel = ResolveLocalShelfLabel(lookup, mediaType);
        if (string.IsNullOrWhiteSpace(localLabel))
            return null;

        var localKey = $"local:{mediaType.ToString().ToLowerInvariant()}:{NormalizeKey(localLabel)}";
        return new ShelfIdentity(mediaType, localLabel, null, localKey, relationshipKeys);
    }

    private static string? ResolveProviderKey(Dictionary<string, string> lookup, MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Movies when TryGetValue(lookup, "tmdb_collection_id", out var tmdbCollectionId)
                => $"tmdb:collection:{tmdbCollectionId}",
            MediaType.TV when TryGetValue(lookup, BridgeIdKeys.TmdbId, out var tmdbTvId)
                => $"tmdb:tv:{tmdbTvId}",
            MediaType.TV when TryGetValue(lookup, BridgeIdKeys.TvdbId, out var tvdbId)
                => $"tvdb:tv:{tvdbId}",
            MediaType.Music when TryGetValue(lookup, BridgeIdKeys.AppleMusicCollectionId, out var appleCollectionId)
                => $"applemusic:album:{appleCollectionId}",
            MediaType.Music when TryGetValue(lookup, BridgeIdKeys.MusicBrainzReleaseGroupId, out var releaseGroupId)
                => $"musicbrainz:release-group:{releaseGroupId}",
            MediaType.Comics when TryGetValue(lookup, BridgeIdKeys.ComicVineId, out var comicVineId)
                => $"comicvine:series:{comicVineId}",
            _ => null,
        };
    }

    private static string? ResolveProviderLabel(Dictionary<string, string> lookup, MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Movies => FirstNonBlank(
                ValueOrNull(lookup, "tmdb_collection_name"),
                ValueOrNull(lookup, MetadataFieldConstants.Series)),
            MediaType.TV => FirstNonBlank(
                ValueOrNull(lookup, MetadataFieldConstants.ShowName),
                ValueOrNull(lookup, MetadataFieldConstants.Title),
                ValueOrNull(lookup, MetadataFieldConstants.Series)),
            MediaType.Music => FirstNonBlank(
                ValueOrNull(lookup, MetadataFieldConstants.Album),
                ValueOrNull(lookup, MetadataFieldConstants.Title)),
            MediaType.Comics => ValueOrNull(lookup, MetadataFieldConstants.Series),
            _ => null,
        };
    }

    private static string? ResolveLocalShelfLabel(Dictionary<string, string> lookup, MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.TV => FirstNonBlank(
                ValueOrNull(lookup, MetadataFieldConstants.ShowName),
                ValueOrNull(lookup, MetadataFieldConstants.Series)),
            MediaType.Music => FirstNonBlank(
                ValueOrNull(lookup, MetadataFieldConstants.Album),
                ValueOrNull(lookup, MetadataFieldConstants.Title)),
            MediaType.Books or MediaType.Audiobooks or MediaType.Comics => ValueOrNull(lookup, MetadataFieldConstants.Series),
            _ => null,
        };
    }

    private static IReadOnlyList<string> BuildRelationshipKeys(Dictionary<string, string> lookup)
    {
        var keys = new List<string>();
        foreach (var claimKey in new[] { "series", "franchise", "fictional_universe" })
        {
            if (TryGetQid(lookup, claimKey, out var qid, out _))
                keys.Add($"{claimKey}:{qid}");
        }

        return keys;
    }

    private static MediaType ResolveMediaType(Dictionary<string, string> lookup)
        => Enum.TryParse<MediaType>(ValueOrNull(lookup, MetadataFieldConstants.MediaTypeField), ignoreCase: true, out var mediaType)
            ? mediaType
            : MediaType.Unknown;

    private static string SanitizeShelfLabel(string label, string? qid, string? providerKey)
        => string.IsNullOrWhiteSpace(label) || IsQidLike(label)
            ? FirstNonBlank(qid, providerKey, "Untitled shelf")!
            : label.Trim();

    private static string? GetGroupByField(MediaType mediaType) => mediaType switch
    {
        MediaType.TV => MetadataFieldConstants.ShowName,
        MediaType.Music => MetadataFieldConstants.Album,
        MediaType.Books or MediaType.Audiobooks or MediaType.Comics or MediaType.Movies => MetadataFieldConstants.Series,
        _ => null,
    };

    private static bool TryGetValue(Dictionary<string, string> lookup, string key, out string value)
    {
        value = string.Empty;
        if (!lookup.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return true;
    }

    private static string? ValueOrNull(Dictionary<string, string> lookup, string key)
        => TryGetValue(lookup, key, out var value) ? value : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool IsQidLike(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length > 1
           && value[0] is 'Q'
           && char.IsDigit(value[1]);

    private static string NormalizeKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSeparator = false;

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// Extracts the immediate shelf QID from canonical values.
    /// Series (P179) drives lane shelves; franchise/universe values are rollup relationships.
    /// Returns the QID and human-readable label.
    /// </summary>
    private static (string? Qid, string? Label) ResolveParentQid(
        Dictionary<string, string> lookup)
    {
        // Try series first (most specific: album, TV show, book series)
        if (TryGetQid(lookup, "series", out var qid, out var label))
            return (qid, label);

        return (null, null);
    }

    /// <summary>
    /// Extracts a clean QID and label from canonical values for a given claim key.
    /// Handles URI prefixes and :: suffixes.
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

        // Strip ::Label suffix
        if (qid.Contains("::"))
            qid = qid.Split("::", 2)[0];

        if (string.IsNullOrWhiteSpace(qid))
            return false;

        // Get the label
        if (lookup.TryGetValue(claimKey, out var rawLabel) && !string.IsNullOrWhiteSpace(rawLabel))
        {
            label = rawLabel.Contains("::") ? rawLabel.Split("::", 2)[^1].Trim() : rawLabel.Trim();
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
