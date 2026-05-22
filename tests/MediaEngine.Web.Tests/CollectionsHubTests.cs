namespace MediaEngine.Web.Tests;

public sealed class CollectionsHubTests
{
    [Fact]
    public void MainLayout_OrdersPrimaryNavigationByMediaThenCollections()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Shared\MainLayout.razor"));

        var readIndex = source.IndexOf("new(\"/read\", \"Nav_Read\")", StringComparison.Ordinal);
        var watchIndex = source.IndexOf("new(\"/watch\", \"Nav_Watch\")", StringComparison.Ordinal);
        var listenIndex = source.IndexOf("new(\"/listen\", \"Nav_Listen\")", StringComparison.Ordinal);
        var collectionsIndex = source.IndexOf("new(\"/collections\", \"Nav_Collections\")", StringComparison.Ordinal);

        Assert.True(readIndex >= 0);
        Assert.True(watchIndex > readIndex);
        Assert.True(listenIndex > watchIndex);
        Assert.True(collectionsIndex > listenIndex);
    }

    [Fact]
    public void CollectionsPage_UsesCentralizedBrowseShell()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsPage.razor"));
        var heroSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseHero.razor"));
        var browseShellSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor"));
        var browseShellStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\BrowseShellStyles.razor"));

        Assert.Contains("GetCollectionManagementCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", browseShellSource, StringComparison.Ordinal);
        Assert.Contains(".browse-shell__grid", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("display: flex", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("browse-shell collections-browse", source, StringComparison.Ordinal);
        Assert.Contains("<MediaBrowseHero", source, StringComparison.Ordinal);
        Assert.Contains("<DiscoveryCard", source, StringComparison.Ordinal);
        Assert.Contains("Shape = DiscoveryCardShape.Landscape", source, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind = DiscoverySurfaceKind.BannerLandscape", source, StringComparison.Ordinal);
        Assert.Contains("TileTextMode = DiscoveryTileTextMode.CoverOnly", source, StringComparison.Ordinal);
        Assert.Contains("Take(5)", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__search", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__sort", source, StringComparison.Ordinal);
        Assert.Contains("Search collections", source, StringComparison.Ordinal);
        Assert.Contains("Recently Updated", source, StringComparison.Ordinal);
        Assert.Contains("Item Count", source, StringComparison.Ordinal);
        Assert.Contains("Continue browsing", source, StringComparison.Ordinal);
        Assert.Contains("Mode = \"Collection\"", source, StringComparison.Ordinal);
        Assert.Contains("MediaKind = \"Collection\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Mode = type == \"Playlist\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaKind = \"Playlist\"", source, StringComparison.Ordinal);
        Assert.Contains("browse-hero__carousel", heroSource, StringComparison.Ordinal);
        Assert.Contains("FooterContent", heroSource, StringComparison.Ordinal);
        var cardSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Discovery\DiscoveryCard.razor"));
        Assert.Contains("is-collection-card", cardSource, StringComparison.Ordinal);
        Assert.Contains("discovery-card-collection-art-row", cardSource, StringComparison.Ordinal);
        Assert.Contains("discovery-card-collection-copy", cardSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryNavigationUrl = $\"/collection/{collection.Id:D}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("READ COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LISTEN COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WATCH COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CROSS-MEDIA COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-hub__tabs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-hub-tab", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GLOBAL COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SYSTEM COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATED BY YOU", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionInlineInspector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionHubSection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-table-wrap", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudTable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionsHub_RemovesOldLaneComponentsAndCss()
    {
        var collectionsPath = GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections");
        var removedFiles = new[]
        {
            "CollectionsPage.razor.css",
            "CollectionHubSection.razor",
            "CollectionHubSection.razor.css",
            "CollectionHubCard.razor",
            "CollectionHubCard.razor.css",
            "CollectionHubStats.razor",
            "CollectionHubStats.razor.css",
            "CollectionArtworkStack.razor",
            "CollectionArtworkStack.razor.css",
            "CollectionInlineInspector.razor",
            "CollectionInlineInspector.razor.css",
            "CollectionSectionLabel.razor",
            "CollectionSectionLabel.razor.css",
        };

        foreach (var file in removedFiles)
        {
            Assert.False(File.Exists(Path.Combine(collectionsPath, file)), $"{file} should stay removed.");
        }
    }

    [Fact]
    public void CollectionDetail_UsesDedicatedCollectionDetailSurface()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\CollectionDetail.razor"));
        var css = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\CollectionDetail.razor.css"));

        Assert.Contains("@page \"/collection/{Id:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionManagementCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItemsAsync", source, StringComparison.Ordinal);
        Assert.Contains("LookupCollectionMediaAsync", source, StringComparison.Ordinal);
        Assert.Contains("AddCollectionItemAsync", source, StringComparison.Ordinal);
        Assert.Contains("RemoveCollectionItemAsync", source, StringComparison.Ordinal);
        Assert.Contains("collection-detail-hero", source, StringComparison.Ordinal);
        Assert.Contains("EDIT COLLECTION", source, StringComparison.Ordinal);
        Assert.Contains("OVERVIEW", source, StringComparison.Ordinal);
        Assert.Contains("HeroArtwork", source, StringComparison.Ordinal);
        Assert.Contains("ItemGroups", source, StringComparison.Ordinal);
        Assert.Contains("tl-detail-page collection-detail-page", source, StringComparison.Ordinal);
        Assert.Contains("GENERATED COLLECTION", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOM COLLECTION", source, StringComparison.Ordinal);
        Assert.Contains("MediaCountKey", source, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Outlined.Tv", source, StringComparison.Ordinal);
        var clientSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\EngineApiClient.cs"));
        Assert.Contains("foreach (var artworkItem in collection.ArtworkItems)", clientSource, StringComparison.Ordinal);
        Assert.Contains("artworkItem.CoverUrl = AbsoluteUrl(artworkItem.CoverUrl)", clientSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GLOBAL COLLECTION", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WATCH COLLECTION\",", source, StringComparison.Ordinal);
        Assert.DoesNotContain("READ COLLECTION\",", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Back to Collections", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VisibilityLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<DetailPage Model=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Play All", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Shuffle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-detail-back", css, StringComparison.Ordinal);
        Assert.Contains("font-family: Georgia", css, StringComparison.Ordinal);
        Assert.Contains("min-height: clamp(680px, 86vh, 920px)", css, StringComparison.Ordinal);
        Assert.Contains("object-fit: contain", css, StringComparison.Ordinal);
        Assert.Contains("collection-detail-hero__cascade", css, StringComparison.Ordinal);
        Assert.Contains("collection-detail-item-grid", css, StringComparison.Ordinal);
        Assert.Contains("position: sticky", css, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
