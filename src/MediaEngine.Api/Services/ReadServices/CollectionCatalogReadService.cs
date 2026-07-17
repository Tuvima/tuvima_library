using System.Globalization;
using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services.Display;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed record CollectionItemReadResult(bool Found, bool Forbidden, List<CollectionItemDto> Items);

public sealed class CollectionCatalogReadService(
    ICollectionRepository collectionRepo,
    ISeriesManifestRepository manifestRepo,
    IArtworkPaletteService artworkPaletteService,
    ICollectionMediaLookupReadService mediaLookupReadService,
    IDatabaseConnection db)
{
    public async Task<List<ManagedCollectionDto>> GetManagedAsync(
        Profile? activeProfile,
        CancellationToken ct = default)
    {
        var collections = (await collectionRepo.GetManagedCollectionsAsync(ct).ConfigureAwait(false))
            .Where(collection => CollectionAccessPolicy.CanAccess(collection, activeProfile))
            .ToList();
        var materializedCounts = await collectionRepo.GetCollectionItemCountsAsync(
            collections
                .Where(collection => !string.Equals(collection.Resolution, CollectionResolutionNames.Query, StringComparison.OrdinalIgnoreCase))
                .Select(collection => collection.Id),
            ct).ConfigureAwait(false);

        var results = new List<ManagedCollectionDto>(collections.Count);
        foreach (var collection in collections)
        {
            var count = string.Equals(collection.Resolution, CollectionResolutionNames.Query, StringComparison.OrdinalIgnoreCase)
                ? (await GetCollectionWorkIdsAsync(collection, ct).ConfigureAwait(false)).Count
                : GetManagedCollectionItemCount(collection, materializedCounts, []);
            results.Add(ManagedCollectionDto.FromDomain(collection, count, activeProfile));
        }

        return results;
    }

    public Task<IReadOnlyList<Guid>> GetDisplayWorkIdsAsync(
        IEnumerable<Guid> sourceWorkIds,
        CancellationToken ct = default) =>
        GetCollectionCatalogDisplayWorkIdsAsync(sourceWorkIds, ct);

    public async Task<Guid> ResolveMembershipWorkIdAsync(Guid sourceWorkId, CancellationToken ct = default)
    {
        var displayIds = await GetCollectionCatalogDisplayWorkIdsAsync([sourceWorkId], ct).ConfigureAwait(false);
        return displayIds.Count == 0 ? sourceWorkId : displayIds[0];
    }

    public async Task<List<CollectionManagementCatalogDto>> GetCatalogAsync(
        Profile? activeProfile,
        CancellationToken ct = default)
    {
        var collections = await GetAccessibleCollectionsAsync(activeProfile, ct).ConfigureAwait(false);
        var candidates = new List<CollectionManagementCatalogCandidate>();
        foreach (var collection in collections)
        {
            var classification = ClassifyCollectionForCatalog(collection);
            var sourceWorkIds = await GetCollectionCatalogSourceWorkIdsAsync(collection, collections, ct).ConfigureAwait(false);
            var workIds = await GetOwnedCollectionCatalogDisplayWorkIdsAsync(sourceWorkIds, ct).ConfigureAwait(false);
            var itemCount = workIds.Count;
            var mediaCounts = await GetCollectionMediaCountsAsync(workIds, ct).ConfigureAwait(false);
            var hasKnownSeriesManifest = await HasKnownSeriesManifestAsync(collection, ct).ConfigureAwait(false);
            if (!ShouldIncludeInManagementCatalog(collection, classification, mediaCounts, hasKnownSeriesManifest))
            {
                continue;
            }

            candidates.Add(new CollectionManagementCatalogCandidate(
                collection,
                classification,
                GetCollectionCatalogAggregation(collection),
                workIds,
                itemCount,
                mediaCounts));
        }

        var dtos = new List<CollectionManagementCatalogDto>();
        foreach (var group in candidates.GroupBy(candidate => candidate.Grouping?.Key ?? candidate.Collection.Id.ToString("D"), StringComparer.OrdinalIgnoreCase))
        {
            var entries = group.ToList();
            if (!ShouldIncludeCatalogGroup(entries))
            {
                continue;
            }

            var representative = SelectCatalogRepresentative(entries);
            var workIds = entries
                .SelectMany(entry => entry.WorkIds)
                .Distinct()
                .ToList();
            var mediaCounts = await GetCollectionMediaCountsAsync(workIds, ct).ConfigureAwait(false);
            if (IsGeneratedTvShowContainer(representative.Collection, mediaCounts))
            {
                continue;
            }

            var artworkItems = await GetCollectionArtworkItemsAsync(workIds, 12, ct).ConfigureAwait(false);
            var artworkPalette = await artworkPaletteService.GeneratePaletteAsync(
                artworkItems
                    .Select(item => new ArtworkPaletteSource
                    {
                        Id = item.WorkId.ToString("D"),
                        ImageUrl = item.CoverUrl ?? string.Empty,
                        LocalPath = item.LocalImagePath,
                        MediaType = TryParseMediaType(item.MediaType),
                        Shape = TryParseArtworkShape(item.ArtworkShape),
                    })
                    .ToList(),
                new ArtworkPaletteOptions
                {
                    StableSeed = representative.Collection.Id.ToString("D"),
                    MaxImagesToAnalyze = 5,
                },
                ct).ConfigureAwait(false);

            dtos.Add(CollectionManagementCatalogDto.FromDomain(
                representative.Collection,
                workIds.Count,
                activeProfile,
                representative.Classification,
                mediaCounts,
                artworkItems,
                artworkPalette,
                entries.Count > 1 ? representative.Grouping?.Label : null));
        }

        return dtos
            .OrderBy(dto => dto.Family == "System" ? 0 : dto.Family == "Global" ? 1 : dto.Family == "User" ? 2 : 3)
            .ThenByDescending(dto => dto.IsFeatured)
            .ThenBy(dto => dto.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CollectionManagementCatalogDto?> GetSummaryAsync(
        Guid collectionId,
        Profile? activeProfile,
        CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(activeProfile, ct).ConfigureAwait(false);
        return catalog.FirstOrDefault(collection => collection.Id == collectionId);
    }

    public async Task<CollectionItemReadResult> GetItemsAsync(
        Guid collectionId,
        Profile? activeProfile,
        int limit,
        CancellationToken ct = default)
    {
        var collection = await collectionRepo.GetByIdAsync(collectionId, ct).ConfigureAwait(false);
        if (collection is null)
        {
            return new CollectionItemReadResult(false, false, []);
        }

        if (!CollectionAccessPolicy.CanAccess(collection, activeProfile))
        {
            return new CollectionItemReadResult(true, true, []);
        }

        var take = Math.Max(1, limit);
        List<CollectionItemDto> dtos;
        if (IsGeneratedSeriesCollection(collection))
        {
            var workIds = (await GetAggregatedCollectionWorkIdsAsync(collectionId, activeProfile, ct).ConfigureAwait(false))
                .Distinct()
                .Take(take)
                .ToList();
            dtos = await ResolveCollectionWorkIdsToItemsAsync(collectionId, workIds, ct).ConfigureAwait(false);
        }
        else if (string.Equals(collection.Resolution, CollectionResolutionNames.Materialized, StringComparison.OrdinalIgnoreCase))
        {
            var items = await collectionRepo.GetCollectionItemsAsync(collectionId, take, ct).ConfigureAwait(false);
            dtos = await mediaLookupReadService.ResolveItemsAsync(collectionId, items, ct).ConfigureAwait(false);
        }
        else
        {
            var workIds = (await GetCollectionWorkIdsAsync(collection, ct).ConfigureAwait(false))
                .Distinct()
                .Take(take)
                .ToList();
            dtos = await ResolveCollectionWorkIdsToItemsAsync(collectionId, workIds, ct).ConfigureAwait(false);
        }

        return new CollectionItemReadResult(true, false, dtos);
    }

    private async Task<List<Collection>> GetAccessibleCollectionsAsync(Profile? activeProfile, CancellationToken ct)
    {
        var collections = await collectionRepo.GetAllAsync(ct).ConfigureAwait(false);
        return collections
            .Where(collection => CollectionAccessPolicy.CanAccess(collection, activeProfile))
            .ToList();
    }

    private static int GetManagedCollectionItemCount(
        Collection collection,
        IReadOnlyDictionary<Guid, int> curatedCountByCollection,
        IReadOnlyList<Guid> workIds)
    {
        if (!string.Equals(collection.Resolution, CollectionResolutionNames.Query, StringComparison.OrdinalIgnoreCase)
            && curatedCountByCollection.TryGetValue(collection.Id, out var count))
        {
            return Math.Max(count, workIds.Count);
        }

        return workIds.Count;
    }

    private static CollectionCatalogClassification ClassifyCollectionForCatalog(Collection collection)
    {
        var systemKey = GetSystemCollectionKey(collection);
        if (systemKey is not null)
        {
            return new CollectionCatalogClassification("System", CollectionTypeNames.System, systemKey, true, SystemLaneForKey(systemKey));
        }

        var family = string.Equals(collection.Scope, CollectionScopeNames.Library, StringComparison.OrdinalIgnoreCase)
            ? "Global"
            : "User";
        return new CollectionCatalogClassification(family, collection.CollectionType, null, false);
    }

    private static string? GetSystemCollectionKey(Collection collection)
    {
        var normalizedName = (collection.DisplayName ?? string.Empty).Trim();
        if (normalizedName.Length == 0)
        {
            return null;
        }

        if (string.Equals(collection.CollectionType, CollectionTypeNames.System, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedName.ToLowerInvariant().Replace(' ', '-');
        }

        return normalizedName switch
        {
            "Watchlist" => "watchlist",
            "Favorites" => "favorites",
            "Reading List" => "reading-list",
            "Listening Queue" => "listening-queue",
            "Currently Watching" => "currently-watching",
            _ => null,
        };
    }

    private static string? SystemLaneForKey(string systemKey) => systemKey switch
    {
        "favorites" or "listening-queue" => "Listen",
        "watchlist" or "currently-watching" => "Watch",
        "reading-list" => "Read",
        _ => null,
    };

    private async Task<bool> HasKnownSeriesManifestAsync(Collection collection, CancellationToken ct)
    {
        if (!IsGeneratedSeriesCollection(collection))
        {
            return false;
        }

        var manifest = await manifestRepo.GetViewByCollectionIdAsync(collection.Id, ct).ConfigureAwait(false);
        return manifest?.TotalCount > 1;
    }

    private static bool ShouldIncludeInManagementCatalog(
        Collection collection,
        CollectionCatalogClassification classification,
        CollectionMediaCounts mediaCounts,
        bool hasKnownSeriesManifest)
    {
        if (IsPlaylistCatalogCollection(collection))
        {
            return false;
        }

        if (classification.IsSystem || string.Equals(classification.Family, "User", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
        {
            return true;
        }

        if (IsGeneratedSeriesCollection(collection) && GetCollectionCatalogAggregation(collection) is null)
        {
            return false;
        }

        return mediaCounts.TotalCount >= 2 || hasKnownSeriesManifest;
    }

    private static bool IsPlaylistCatalogCollection(Collection collection)
        => string.Equals(collection.CollectionType, CollectionTypeNames.Playlist, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, CollectionTypeNames.PlaylistFolder, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, CollectionTypeNames.Smart, StringComparison.OrdinalIgnoreCase);

    private static CollectionManagementCatalogCandidate SelectCatalogRepresentative(
        IReadOnlyList<CollectionManagementCatalogCandidate> entries)
        => entries
            .OrderByDescending(entry => entry.MediaCounts.TotalCount)
            .ThenByDescending(entry => entry.ItemCount)
            .ThenBy(entry => entry.Collection.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();

    private static CollectionCatalogAggregation? GetCollectionCatalogAggregation(Collection collection)
    {
        if (!IsGeneratedSeriesCollection(collection))
        {
            return null;
        }

        return TryGetRelationshipAggregation(collection, "fictional_universe", out var aggregation)
            || TryGetRelationshipAggregation(collection, "franchise", out aggregation)
            || TryGetRelationshipAggregation(collection, "series", out aggregation)
            ? aggregation
            : null;
    }

    private static bool ShouldIncludeCatalogGroup(IReadOnlyList<CollectionManagementCatalogCandidate> entries)
    {
        var generatedEntries = entries
            .Where(entry => IsGeneratedSeriesCollection(entry.Collection))
            .ToList();

        if (generatedEntries.Count == 0)
        {
            return true;
        }

        return generatedEntries
            .Where(entry => entry.Collection.Works.Count > 0)
            .Select(entry => entry.Collection.Id)
            .Distinct()
            .Count() >= 2;
    }

    private static bool TryGetRelationshipAggregation(
        Collection collection,
        string relationshipType,
        out CollectionCatalogAggregation aggregation)
    {
        var relationship = collection.Relationships
            .FirstOrDefault(candidate => string.Equals(candidate.RelType, relationshipType, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(candidate.RelQid));
        if (relationship is null)
        {
            aggregation = default!;
            return false;
        }

        aggregation = new CollectionCatalogAggregation(
            $"{relationshipType}:{NormalizeCatalogQid(relationship.RelQid)}",
            FirstNonBlank(relationship.RelLabel, collection.DisplayName));
        return true;
    }

    private static string NormalizeCatalogQid(string qid)
    {
        var value = qid.Contains('/') ? qid.Split('/')[^1] : qid;
        if (value.Contains("::", StringComparison.Ordinal))
        {
            value = value.Split("::", 2)[0];
        }

        return value.Trim();
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ToNullableText(object? value) =>
        value switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };

    private static bool IsGeneratedSeriesCollection(Collection collection)
        => string.Equals(collection.CollectionType, CollectionTypeNames.Universe, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, CollectionTypeNames.Series, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, CollectionTypeNames.ContentGroup, StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedTvShowContainer(Collection collection, CollectionMediaCounts mediaCounts)
        => IsGeneratedSeriesCollection(collection)
            && mediaCounts.TvCount > 0
            && mediaCounts.WatchCount == mediaCounts.TvCount
            && mediaCounts.ListenCount == 0
            && mediaCounts.ReadCount == 0
            && mediaCounts.OtherCount == 0;

    private async Task<CollectionMediaCounts> GetCollectionMediaCountsAsync(
        IReadOnlyList<Guid> workIds,
        CancellationToken ct)
    {
        if (workIds.Count == 0)
        {
            return new CollectionMediaCounts(0, 0, 0, 0);
        }

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string MediaType, int Count)>(new CommandDefinition(
            """
            SELECT media_type AS MediaType, COUNT(*) AS Count
            FROM works
            WHERE id IN @WorkIds
            GROUP BY media_type
            """,
            new { WorkIds = workIds.Select(GuidSql.ToBlob).ToArray() },
            cancellationToken: ct)).ConfigureAwait(false);

        var watch = 0;
        var listen = 0;
        var read = 0;
        var other = 0;
        var tv = 0;
        foreach (var row in rows)
        {
            if (string.Equals(row.MediaType, "TV", StringComparison.OrdinalIgnoreCase))
            {
                tv += row.Count;
            }

            if (IsWatchMediaType(row.MediaType))
            {
                watch += row.Count;
            }
            else if (IsListenMediaType(row.MediaType))
            {
                listen += row.Count;
            }
            else if (IsReadMediaType(row.MediaType))
            {
                read += row.Count;
            }
            else
            {
                other += row.Count;
            }
        }

        return new CollectionMediaCounts(watch, listen, read, other, tv);
    }

    private async Task<IReadOnlyList<Guid>> GetCollectionWorkIdsAsync(Collection collection, CancellationToken ct)
    {
        if (string.Equals(collection.Resolution, CollectionResolutionNames.Query, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(collection.RuleJson))
        {
            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0)
            {
                return [];
            }

            var evaluator = new CollectionRuleEvaluator(db);
            return evaluator.Evaluate(predicates, collection.MatchMode, collection.SortField, collection.SortDirection, 0);
        }

        var items = await collectionRepo.GetCollectionItemsAsync(collection.Id, 5000, ct).ConfigureAwait(false);
        if (items.Count > 0)
        {
            return items.Select(item => item.WorkId).Distinct().ToList();
        }

        var collectionWithWorks = await collectionRepo.GetCollectionWithWorksAsync(collection.Id, ct).ConfigureAwait(false);
        return collectionWithWorks?.Works.Select(work => work.Id).Distinct().ToList() ?? [];
    }

    private async Task<IReadOnlyList<Guid>> GetAggregatedCollectionWorkIdsAsync(
        Guid collectionId,
        Profile? activeProfile,
        CancellationToken ct)
    {
        var accessibleCollections = await GetAccessibleCollectionsAsync(activeProfile, ct).ConfigureAwait(false);
        var target = accessibleCollections.FirstOrDefault(collection => collection.Id == collectionId);
        if (target is null)
        {
            return [];
        }

        var targetGrouping = GetCollectionCatalogAggregation(target);
        var siblingCollections = targetGrouping is null
            ? new List<Collection> { target }
            : accessibleCollections
                .Where(collection => IsGeneratedSeriesCollection(collection)
                    && string.Equals(GetCollectionCatalogAggregation(collection)?.Key, targetGrouping.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var workIds = new List<Guid>();
        foreach (var collection in ExpandWithChildCollections(siblingCollections, accessibleCollections))
        {
            workIds.AddRange(await GetCollectionWorkIdsAsync(collection, ct).ConfigureAwait(false));
        }

        return await GetOwnedCollectionCatalogDisplayWorkIdsAsync(workIds, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Guid>> GetCollectionCatalogSourceWorkIdsAsync(
        Collection collection,
        IReadOnlyList<Collection> accessibleCollections,
        CancellationToken ct)
    {
        var workIds = new List<Guid>();
        foreach (var sourceCollection in ExpandWithChildCollections([collection], accessibleCollections))
        {
            workIds.AddRange(await GetCollectionWorkIdsAsync(sourceCollection, ct).ConfigureAwait(false));
        }

        return workIds.Distinct().ToList();
    }

    private static IReadOnlyList<Collection> ExpandWithChildCollections(
        IReadOnlyList<Collection> collections,
        IReadOnlyList<Collection> accessibleCollections)
    {
        var result = new List<Collection>();
        var queue = new Queue<Collection>(collections);
        var seen = new HashSet<Guid>();
        while (queue.Count > 0)
        {
            var collection = queue.Dequeue();
            if (!seen.Add(collection.Id))
            {
                continue;
            }

            result.Add(collection);
            foreach (var child in accessibleCollections.Where(candidate => candidate.ParentCollectionId == collection.Id))
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<Guid>> GetCollectionCatalogDisplayWorkIdsAsync(
        IEnumerable<Guid> sourceWorkIds,
        CancellationToken ct)
    {
        var workIds = sourceWorkIds.Distinct().ToList();
        if (workIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionDisplayWorkRow>(new CommandDefinition(
            """
            SELECT DISTINCT
                   CASE
                       WHEN w.work_kind = 'child' THEN COALESCE(gp.id, p.id, w.id)
                       WHEN w.work_kind = 'parent' AND p.id IS NOT NULL THEN COALESCE(gp.id, p.id, w.id)
                       ELSE w.id
                   END AS WorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id IN @WorkIds;
            """,
            new { WorkIds = workIds.Select(GuidSql.ToBlob).ToArray() },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows
            .Select(row => row.WorkId)
            .Distinct()
            .ToList();
    }

    private async Task<IReadOnlyList<Guid>> GetOwnedCollectionCatalogDisplayWorkIdsAsync(
        IEnumerable<Guid> sourceWorkIds,
        CancellationToken ct)
    {
        var displayWorkIds = await GetCollectionCatalogDisplayWorkIdsAsync(sourceWorkIds, ct).ConfigureAwait(false);
        if (displayWorkIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = await conn.QueryAsync<CollectionDisplayWorkRow>(new CommandDefinition(
            $"""
            WITH RECURSIVE work_tree(RootWorkId, WorkId) AS (
                SELECT w.id AS RootWorkId,
                       w.id AS WorkId
                FROM works w
                WHERE w.id IN @WorkIds
                UNION
                SELECT work_tree.RootWorkId,
                       child.id AS WorkId
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.WorkId
            )
            SELECT DISTINCT work_tree.RootWorkId AS WorkId
            FROM work_tree
            INNER JOIN editions e ON e.work_id = work_tree.WorkId
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE {visibleAssetPredicate};
            """,
            new { WorkIds = displayWorkIds.Select(GuidSql.ToBlob).ToArray() },
            cancellationToken: ct)).ConfigureAwait(false);

        var ownedIds = rows.Select(row => row.WorkId).ToHashSet();
        return displayWorkIds.Where(ownedIds.Contains).ToList();
    }

    private async Task<List<CollectionItemDto>> ResolveCollectionWorkIdsToItemsAsync(
        Guid collectionId,
        IReadOnlyList<Guid> workIds,
        CancellationToken ct)
    {
        var displayWorkIds = await GetCollectionCatalogDisplayWorkIdsAsync(workIds, ct).ConfigureAwait(false);
        if (displayWorkIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<GeneratedCollectionItemRow>(new CommandDefinition(
            $"""
            WITH RECURSIVE work_tree(RootWorkId, WorkId) AS (
                SELECT w.id AS RootWorkId,
                       w.id AS WorkId
                FROM works w
                WHERE w.id IN @WorkIds
                UNION ALL
                SELECT work_tree.RootWorkId,
                       child.id AS WorkId
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.WorkId
            ),
            representative_assets AS (
                SELECT work_tree.RootWorkId AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM work_tree
                INNER JOIN editions e ON e.work_id = work_tree.WorkId
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE {visibleAssetPredicate}
                GROUP BY work_tree.RootWorkId
            )
            SELECT w.id AS WorkId,
                   COALESCE(
                       NULLIF(title_work.value, ''),
                       NULLIF(episode_title.value, ''),
                       NULLIF(show_name.value, ''),
                       NULLIF(series_item.item_label, ''),
                       'Untitled'
                   ) AS Title,
                   COALESCE(
                       NULLIF(CAST(author_work.value AS TEXT), ''),
                       NULLIF(CAST(artist_work.value AS TEXT), ''),
                       NULLIF(CAST(director_work.value AS TEXT), '')
                   ) AS Creator,
                   w.media_type AS MediaType,
                   COALESCE(
                       NULLIF(cover_asset.value, ''),
                       NULLIF(cover_work.value, ''),
                       CASE WHEN ra.AssetId IS NOT NULL THEN '/stream/' || ra.AssetId || '/cover' END
                   ) AS CoverUrl,
                   COALESCE(w.ordinal, series_item.sort_order, 999999) AS SortOrder
            FROM works w
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = w.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values show_name ON show_name.entity_id = w.id AND show_name.key = 'show_name'
            LEFT JOIN canonical_values author_work ON author_work.entity_id = w.id AND author_work.key = 'author'
            LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values director_work ON director_work.entity_id = w.id AND director_work.key = 'director'
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ra.AssetId AND cover_asset.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN series_manifest_items series_item ON series_item.linked_work_id = w.id AND series_item.collection_id = @CollectionId
            WHERE w.id IN @WorkIds
              AND ({visibleWorkPredicate} OR ra.AssetId IS NOT NULL)
            ORDER BY SortOrder, Title COLLATE NOCASE, w.id;
            """,
            new
            {
                CollectionId = collectionId,
                WorkIds = displayWorkIds.Select(GuidSql.ToBlob).ToArray(),
            },
            cancellationToken: ct))).ToList();

        return rows
            .GroupBy(row => row.WorkId)
            .Select(group => group.OrderBy(row => row.SortOrder).ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase).First())
            .Select(row => new CollectionItemDto
            {
                Id = DeterministicCollectionItemId(collectionId, row.WorkId),
                WorkId = row.WorkId,
                Title = row.Title,
                Creator = ToNullableText(row.Creator),
                MediaType = row.MediaType,
                CoverUrl = row.CoverUrl,
                SortOrder = row.SortOrder,
            }).ToList();
    }

    private static Guid DeterministicCollectionItemId(Guid collectionId, Guid workId)
    {
        var bytes = collectionId.ToByteArray().Concat(workId.ToByteArray()).ToArray();
        return new Guid(System.Security.Cryptography.MD5.HashData(bytes));
    }

    private async Task<IReadOnlyList<CollectionArtworkItemDto>> GetCollectionArtworkItemsAsync(
        IReadOnlyList<Guid> sourceWorkIds,
        int limit,
        CancellationToken ct)
    {
        var workIds = (await GetCollectionCatalogDisplayWorkIdsAsync(sourceWorkIds, ct).ConfigureAwait(false))
            .Take(Math.Clamp(limit, 1, 8))
            .ToList();
        if (workIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<CollectionArtworkItemRow>(new CommandDefinition(
            $"""
            WITH RECURSIVE work_tree(RootWorkId, WorkId) AS (
                SELECT w.id AS RootWorkId,
                       w.id AS WorkId
                FROM works w
                WHERE w.id IN @WorkIds
                UNION ALL
                SELECT work_tree.RootWorkId,
                       child.id AS WorkId
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.WorkId
            ),
            representative_assets AS (
                SELECT work_tree.RootWorkId AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM work_tree
                INNER JOIN editions e ON e.work_id = work_tree.WorkId
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE {visibleAssetPredicate}
                GROUP BY work_tree.RootWorkId
            )
            SELECT w.id AS WorkId,
                   COALESCE(NULLIF(title_work.value, ''), NULLIF(episode_title.value, ''), NULLIF(show_name.value, ''), NULLIF(series_item.item_label, ''), 'Untitled') AS Title,
                   w.media_type AS MediaType,
                   preferred_cover.id AS CoverAssetId,
                   COALESCE(NULLIF(cover_asset.value, ''), NULLIF(cover_work.value, ''), CASE WHEN ra.AssetId IS NOT NULL THEN '/stream/' || ra.AssetId || '/cover' END) AS CoverUrl,
                   COALESCE(
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'short_description' LIMIT 1),
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'description' LIMIT 1)) AS Description,
                   COALESCE(
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'release_year' LIMIT 1),
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'year' LIMIT 1)) AS Year,
                   (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'author' LIMIT 1) AS Author,
                   (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'artist' LIMIT 1) AS Artist,
                   COALESCE(
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'content_rating' LIMIT 1),
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'certification' LIMIT 1)) AS ContentRating,
                   (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'runtime' LIMIT 1) AS Runtime,
                   (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'duration' LIMIT 1) AS Duration,
                   (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'page_count' LIMIT 1) AS PageCount,
                   COALESCE(
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'rating' LIMIT 1),
                       (SELECT NULLIF(cv.value, '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'star_rating' LIMIT 1)) AS Rating,
                   COALESCE(NULLIF(primary_work.value, ''), NULLIF(cover_primary_work.value, ''), NULLIF(preferred_cover.primary_hex, '')) AS PrimaryColor,
                   COALESCE(NULLIF(secondary_work.value, ''), NULLIF(cover_secondary_work.value, ''), NULLIF(preferred_cover.secondary_hex, '')) AS SecondaryColor,
                   COALESCE(NULLIF(accent_work.value, ''), NULLIF(dominant_work.value, ''), NULLIF(cover_accent_work.value, ''), NULLIF(preferred_cover.accent_hex, '')) AS AccentColor,
                   COALESCE(NULLIF(preferred_cover.local_image_path_s, ''), NULLIF(preferred_cover.local_image_path_m, ''), NULLIF(preferred_cover.local_image_path, '')) AS LocalImagePath
            FROM works w
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN entity_assets preferred_cover ON preferred_cover.id = (
                SELECT ea.id
                FROM entity_assets ea
                WHERE ea.entity_id = w.id
                  AND ea.entity_type = 'Work'
                  AND ea.asset_type IN ('CoverArt', 'SquareArt', 'Background', 'Banner')
                ORDER BY ea.is_preferred DESC, ea.created_at DESC, ea.id
                LIMIT 1
            )
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = w.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values show_name ON show_name.entity_id = w.id AND show_name.key = 'show_name'
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ra.AssetId AND cover_asset.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values primary_work ON primary_work.entity_id = w.id AND primary_work.key = 'artwork_primary_hex'
            LEFT JOIN canonical_values secondary_work ON secondary_work.entity_id = w.id AND secondary_work.key = 'artwork_secondary_hex'
            LEFT JOIN canonical_values accent_work ON accent_work.entity_id = w.id AND accent_work.key = 'artwork_accent_hex'
            LEFT JOIN canonical_values dominant_work ON dominant_work.entity_id = w.id AND dominant_work.key = 'dominant_color'
            LEFT JOIN canonical_values cover_primary_work ON cover_primary_work.entity_id = w.id AND cover_primary_work.key = 'cover_primary_hex'
            LEFT JOIN canonical_values cover_secondary_work ON cover_secondary_work.entity_id = w.id AND cover_secondary_work.key = 'cover_secondary_hex'
            LEFT JOIN canonical_values cover_accent_work ON cover_accent_work.entity_id = w.id AND cover_accent_work.key = 'cover_accent_hex'
            LEFT JOIN series_manifest_items series_item ON series_item.linked_work_id = w.id
            WHERE w.id IN @WorkIds
              AND ({visibleWorkPredicate} OR ra.AssetId IS NOT NULL)
            """,
            new { WorkIds = workIds.Select(GuidSql.ToBlob).ToArray() },
            cancellationToken: ct))).ToList();

        var rowById = rows
            .GroupBy(row => row.WorkId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First());

        return workIds
            .Where(rowById.ContainsKey)
            .Select(id =>
            {
                var row = rowById[id];
                return new CollectionArtworkItemDto
                {
                    WorkId = id,
                    Title = string.IsNullOrWhiteSpace(row.Title) ? "Untitled" : row.Title,
                    MediaType = row.MediaType ?? "Unknown",
                    CoverUrl = row.CoverAssetId is { } coverAssetId
                        ? $"/stream/artwork/{coverAssetId:D}"
                        : row.CoverUrl,
                    Description = row.Description,
                    Facts = DisplayFactBuilder.Build(
                        DisplayMediaRules.NormalizeDisplayKind(row.MediaType ?? string.Empty),
                        row.Title ?? string.Empty,
                        year: row.Year,
                        author: row.Author,
                        artist: row.Artist,
                        contentRating: row.ContentRating,
                        runtime: row.Runtime,
                        duration: row.Duration,
                        pageCount: row.PageCount,
                        starRating: row.Rating),
                    PrimaryColor = row.PrimaryColor,
                    SecondaryColor = row.SecondaryColor,
                    AccentColor = row.AccentColor,
                    ArtworkShape = ArtworkShapeForMediaType(row.MediaType),
                    LocalImagePath = row.LocalImagePath,
                };
            })
            .ToList();
    }

    private static bool IsWatchMediaType(string? mediaType) =>
        string.Equals(mediaType, "Movies", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "TV", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Video", StringComparison.OrdinalIgnoreCase);

    private static bool IsListenMediaType(string? mediaType) =>
        string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Audiobooks", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Audiobook", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadMediaType(string? mediaType) =>
        string.Equals(mediaType, "Books", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Book", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Comics", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Comic", StringComparison.OrdinalIgnoreCase);

    private static string ArtworkShapeForMediaType(string? mediaType)
    {
        if (IsReadMediaType(mediaType) || IsWatchMediaType(mediaType))
        {
            return "portrait";
        }

        return "square";
    }

    private static MediaType? TryParseMediaType(string? mediaType) =>
        Enum.TryParse<MediaType>(mediaType, ignoreCase: true, out var parsed)
            ? parsed
            : mediaType switch
            {
                "Movie" => MediaType.Movies,
                "Book" => MediaType.Books,
                "Audiobook" => MediaType.Audiobooks,
                "Comic" => MediaType.Comics,
                "Shows" or "Show" => MediaType.TV,
                _ => null,
            };

    private static ArtworkShape? TryParseArtworkShape(string? shape) => shape?.Trim().ToLowerInvariant() switch
    {
        "square" => ArtworkShape.Square,
        "portrait" => ArtworkShape.Portrait,
        "wide" or "landscape" => ArtworkShape.Wide,
        _ => null,
    };

    private sealed class CollectionArtworkItemRow
    {
        public Guid WorkId { get; init; }
        public string? Title { get; init; }
        public string? MediaType { get; init; }
        public Guid? CoverAssetId { get; init; }
        public string? CoverUrl { get; init; }
        public string? Description { get; init; }
        public string? Year { get; init; }
        public string? Author { get; init; }
        public string? Artist { get; init; }
        public string? ContentRating { get; init; }
        public string? Runtime { get; init; }
        public string? Duration { get; init; }
        public string? PageCount { get; init; }
        public string? Rating { get; init; }
        public string? PrimaryColor { get; init; }
        public string? SecondaryColor { get; init; }
        public string? AccentColor { get; init; }
        public string? LocalImagePath { get; init; }
    }

    private sealed class GeneratedCollectionItemRow
    {
        public Guid WorkId { get; init; }
        public string Title { get; init; } = string.Empty;
        public object? Creator { get; init; }
        public string MediaType { get; init; } = string.Empty;
        public string? CoverUrl { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed record CollectionDisplayWorkRow(Guid WorkId);

    private sealed record CollectionCatalogAggregation(string Key, string? Label);

    private sealed record CollectionManagementCatalogCandidate(
        Collection Collection,
        CollectionCatalogClassification Classification,
        CollectionCatalogAggregation? Grouping,
        IReadOnlyList<Guid> WorkIds,
        int ItemCount,
        CollectionMediaCounts MediaCounts);
}
