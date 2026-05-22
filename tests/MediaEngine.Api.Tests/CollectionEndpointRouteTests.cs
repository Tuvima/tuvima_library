using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class CollectionEndpointRouteTests
{
    [Fact]
    public void CollectionEndpoints_GroupFeedsUseSharedVisibilityRulesAndRichMetadata()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CollectionEndpoints.cs"));

        var readServiceSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\ReadServices\CollectionMediaLookupReadService.cs"));
        Assert.Contains("ICollectionBrowseReadService browseReadService", source, StringComparison.Ordinal);
        Assert.Contains("ICollectionSearchReadService searchReadService", source, StringComparison.Ordinal);
        Assert.Contains("ICollectionMediaLookupReadService mediaLookupReadService", source, StringComparison.Ordinal);
        Assert.Contains("HomeVisibilitySql.VisibleAssetPathPredicate(\"ma.file_path_root\")", readServiceSource, StringComparison.Ordinal);
        Assert.Contains("HomeVisibilitySql.VisibleWorkPredicate(\"w.id\", \"w.curator_state\", \"w.is_catalog_only\")", readServiceSource, StringComparison.Ordinal);
        Assert.Contains("Description = row.Description", source, StringComparison.Ordinal);
        Assert.Contains("Tagline = row.Tagline", source, StringComparison.Ordinal);
        Assert.Contains("Network = row.Network", source, StringComparison.Ordinal);
        Assert.Contains("SeasonCount = row.SeasonCount", source, StringComparison.Ordinal);
        Assert.Contains("LogoUrl = row.LogoUrl", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/{id:guid}/square-artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/{id:guid}/square-artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/{id:guid}/square-artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("UpdateCollectionSquareArtworkAsync(id, targetPath, mimeType", source, StringComparison.Ordinal);
        Assert.Contains("UpdateCollectionSquareArtworkAsync(id, null, null", source, StringComparison.Ordinal);

        var accessPolicySource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Models\CollectionAccessPolicy.cs"));
        Assert.Contains("Smart", accessPolicySource, StringComparison.Ordinal);
        Assert.Contains("PlaylistFolder", accessPolicySource, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionSearch_UsesSqlBackedQueryInsteadOfLoadingAllCollections()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CollectionEndpoints.cs"));
        var serviceSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\ReadServices\CollectionSearchReadService.cs"));
        var searchStart = source.IndexOf("group.MapGet(\"/search\"", StringComparison.Ordinal);
        var nextRoute = source.IndexOf("group.MapGet(\"/parents\"", StringComparison.Ordinal);

        Assert.True(searchStart >= 0);
        Assert.True(nextRoute > searchStart);

        var searchSource = source[searchStart..nextRoute];
        Assert.Contains("searchReadService.SearchAsync(q, ct)", searchSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IDatabaseConnection db", searchSource, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<CollectionSearchRow>", serviceSource, StringComparison.Ordinal);
        Assert.Contains("HomeVisibilitySql.VisibleAssetPathPredicate(\"ma.file_path_root\")", serviceSource, StringComparison.Ordinal);
        Assert.Contains("HomeVisibilitySql.VisibleWorkPredicate(\"w.id\", \"w.curator_state\", \"w.is_catalog_only\")", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("collectionRepo.GetAllAsync", searchSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionReadRoutes_DelegateMigratedProjectionSqlToReadServices()
    {
        var endpointSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CollectionEndpoints.cs"));
        var registrations = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DependencyInjection\ApiReadServiceCollectionExtensions.cs"));

        Assert.Contains("ICollectionBrowseReadService browseReadService", endpointSource, StringComparison.Ordinal);
        Assert.Contains("ICollectionSearchReadService searchReadService", endpointSource, StringComparison.Ordinal);
        Assert.Contains("ICollectionMediaLookupReadService mediaLookupReadService", endpointSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ICollectionBrowseReadService, CollectionBrowseReadService>", registrations, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ICollectionSearchReadService, CollectionSearchReadService>", registrations, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ICollectionMediaLookupReadService, CollectionMediaLookupReadService>", registrations, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagementCatalog_ClassifiesCollectionsServerSide()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CollectionEndpoints.cs"));
        var dtoSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Models\ManagedCollectionDto.cs"));

        Assert.Contains("/management-catalog", source, StringComparison.Ordinal);
        Assert.Contains("ClassifyCollectionForCatalog", source, StringComparison.Ordinal);
        Assert.Contains("GetSystemCollectionKey", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionMediaCountsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionArtworkItemsAsync", source, StringComparison.Ordinal);
        Assert.Contains("cover_asset", source, StringComparison.Ordinal);
        Assert.Contains("artwork_primary_hex", source, StringComparison.Ordinal);
        Assert.Contains("artwork_secondary_hex", source, StringComparison.Ordinal);
        Assert.Contains("artwork_accent_hex", source, StringComparison.Ordinal);
        Assert.Contains("preferred_cover.primary_hex", source, StringComparison.Ordinal);
        Assert.Contains("Watchlist", source, StringComparison.Ordinal);
        Assert.Contains("Favorites", source, StringComparison.Ordinal);
        Assert.Contains("CollectionManagementCatalogDto", dtoSource, StringComparison.Ordinal);
        Assert.Contains("CollectionArtworkItemDto", dtoSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryColor", dtoSource, StringComparison.Ordinal);
        Assert.Contains("SecondaryColor", dtoSource, StringComparison.Ordinal);
        Assert.Contains("AccentColor", dtoSource, StringComparison.Ordinal);
        Assert.Contains("ArtworkItems", dtoSource, StringComparison.Ordinal);
        Assert.Contains("CanToggleGlobal", dtoSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryLaneOverride", dtoSource, StringComparison.Ordinal);
        Assert.Contains("SystemLaneForKey(systemKey)", source, StringComparison.Ordinal);
        Assert.Contains("\"favorites\" => \"Listen\"", source, StringComparison.Ordinal);
        Assert.Contains("\"watchlist\" => \"Watch\"", source, StringComparison.Ordinal);
        Assert.Contains("\"reading-list\" => \"Read\"", source, StringComparison.Ordinal);
        Assert.Contains("ShouldIncludeInManagementCatalog", source, StringComparison.Ordinal);
        Assert.Contains("IsPlaylistCatalogCollection(collection)", source, StringComparison.Ordinal);
        Assert.Contains("\"PlaylistFolder\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Smart\"", source, StringComparison.Ordinal);
        Assert.Contains("CollectionManagementCatalogCandidate", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionCatalogAggregation(collection)", source, StringComparison.Ordinal);
        Assert.Contains("fictional_universe", source, StringComparison.Ordinal);
        Assert.Contains("franchise", source, StringComparison.Ordinal);
        Assert.Contains("SelectMany(entry => entry.WorkIds)", source, StringComparison.Ordinal);
        Assert.Contains("ISeriesManifestRepository manifestRepo", source, StringComparison.Ordinal);
        Assert.Contains("HasKnownSeriesManifestAsync(collection, manifestRepo, ct)", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionCatalogAggregation(collection) is null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collection:{NormalizeCatalogQid(collection.WikidataQid)}", source, StringComparison.Ordinal);
        Assert.Contains("mediaCounts.TotalCount < 2 && !hasKnownSeriesManifest", source, StringComparison.Ordinal);
        Assert.Contains("manifest?.TotalCount > 1", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCollectionWorkIdsToItemsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetAggregatedCollectionWorkIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionCatalogSourceWorkIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ExpandWithChildCollections", source, StringComparison.Ordinal);
        Assert.Contains("candidate.ParentCollectionId == collection.Id", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionCatalogDisplayWorkIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(gp.id, p.id, w.id)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCollectionMembershipWorkIdAsync(body.WorkId, db, ct)", source, StringComparison.Ordinal);
        Assert.Contains("existingDisplayWorkIds.Contains(collectionWorkId)", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionCatalogDisplayWorkIdsAsync(sourceWorkIds, db, ct)", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionWorkIdsAsync(collection, collectionRepo, db, ct)", source, StringComparison.Ordinal);
        Assert.Contains("IsGeneratedTvShowContainer", source, StringComparison.Ordinal);
        Assert.Contains("mediaCounts.WatchCount == mediaCounts.TvCount", source, StringComparison.Ordinal);
        Assert.Contains("displayNameOverride", dtoSource, StringComparison.Ordinal);
        Assert.Contains("series_manifest_items series_item", source, StringComparison.Ordinal);
        Assert.Contains("return \"portrait\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return \"landscape\";", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount", dtoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Title     = title ?? $\"Work", source, StringComparison.Ordinal);

        var lookupSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\ReadServices\CollectionMediaLookupReadService.cs"));
        Assert.Contains("WHEN w.media_type = 'TV' THEN COALESCE(gp.id, p.id, w.id)", lookupSource, StringComparison.Ordinal);
        Assert.Contains("BuildLookupRoute(row)", lookupSource, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(row => row.WorkId)", lookupSource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
