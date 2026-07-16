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
        var heroStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseHero.razor.css"));
        var browseShellSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor"));
        var browseShellStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\BrowseShellStyles.razor.css"));
        var tileGridSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaTileGrid.razor"));
        var groupTileSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaGroupTile.razor"));
        var groupTileStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaGroupTile.razor.css"));

        Assert.Contains("GetCollectionCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("Where(collection => collection.Id != CurrentHeroCollection?.Id)", source, StringComparison.Ordinal);
        Assert.Contains("Presentation = MediaTilePresentation.Default", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolvePresentation", source, StringComparison.Ordinal);
        Assert.Contains("@key=\"collection.Id\"", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", browseShellSource, StringComparison.Ordinal);
        Assert.Contains(".browse-shell__grid", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("display: flex", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("browse-shell collections-browse", source, StringComparison.Ordinal);
        Assert.Contains("<MediaBrowseHero", source, StringComparison.Ordinal);
        Assert.Contains("<MediaTileGrid", source, StringComparison.Ordinal);
        Assert.Contains("Shape = MediaTileShape.Landscape", source, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind = MediaTileSurfaceKind.BannerLandscape", source, StringComparison.Ordinal);
        Assert.Contains("UseLandscapeGroupTile = true", source, StringComparison.Ordinal);
        Assert.Contains("item.UseLandscapeGroupTile", tileGridSource, StringComparison.Ordinal);
        Assert.Contains("<MediaGroupTile", tileGridSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__carousel", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 16 / 10", groupTileStylesSource, StringComparison.Ordinal);
        Assert.Contains("PreviewTotalCount = collection.ItemCount", source, StringComparison.Ordinal);
        Assert.Contains("TileTextMode = MediaTileTextMode.CoverOnly", source, StringComparison.Ordinal);
        Assert.Contains("Take(5)", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__search", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__sort", source, StringComparison.Ordinal);
        Assert.Contains("Search collections", source, StringComparison.Ordinal);
        Assert.Contains("Recently Updated", source, StringComparison.Ordinal);
        Assert.Contains("Item Count", source, StringComparison.Ordinal);
        Assert.Contains("Continue browsing", source, StringComparison.Ordinal);
        Assert.Contains("Mode = \"Collection\"", source, StringComparison.Ordinal);
        Assert.Contains("MediaKind = \"Collection\"", source, StringComparison.Ordinal);
        Assert.Contains("PaletteArtwork(collection)", source, StringComparison.Ordinal);
        Assert.Contains("ArtworkPalette = collection.ArtworkPalette", source, StringComparison.Ordinal);
        Assert.Contains("SecondaryAccentColor = secondaryAccentColor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Mode = type == \"Playlist\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaKind = \"Playlist\"", source, StringComparison.Ordinal);
        Assert.Contains("browse-hero__carousel", heroStylesSource, StringComparison.Ordinal);
        Assert.Contains("FooterContent", heroSource, StringComparison.Ordinal);
        var cardSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaTile.razor"));
        var cardStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaTile.razor.css"));
        Assert.Contains("is-collection-card", cardSource, StringComparison.Ordinal);
        Assert.Contains("--media-tile-secondary-accent", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("<TuvimaArtworkStack", cardSource, StringComparison.Ordinal);
        Assert.Contains("CollectionArtworkStackItems", cardSource, StringComparison.Ordinal);
        Assert.DoesNotContain("media-tile-collection-copy", cardSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowMediaKindBadge => !HideMediaKindBadge && !ShowCollectionBanner", cardSource, StringComparison.Ordinal);
        Assert.Contains("16 / 7.25", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("media-tile-artwork-stack--collection-tile", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("Palette=\"@Item.ArtworkPalette\"", cardSource, StringComparison.Ordinal);
        Assert.Contains("--art-bg-base", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("inset:0 10px 0 38%", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("background:transparent; overflow:visible", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("left:min(90%, calc(var(--left) + 10%))", cardStylesSource, StringComparison.Ordinal);
        Assert.Contains("ArtworkStackItems = artworkStackItems", source, StringComparison.Ordinal);
        Assert.Contains("ToArtworkShape(item.ArtworkShape, item.MediaType)", source, StringComparison.Ordinal);
        Assert.Contains("\"Video\", collection.WatchCount", source, StringComparison.Ordinal);
        Assert.Contains("\"Audio\", collection.ListenCount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Watch\", collection.WatchCount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Listen\", collection.ListenCount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Read\", collection.ReadCount", source, StringComparison.Ordinal);
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
        Assert.Contains("GetCollectionSummaryAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCollectionCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItemsAsync", source, StringComparison.Ordinal);
        Assert.Contains("LookupCollectionMediaAsync", source, StringComparison.Ordinal);
        Assert.Contains("AddCollectionItemAsync", source, StringComparison.Ordinal);
        Assert.Contains("RemoveCollectionItemAsync", source, StringComparison.Ordinal);
        Assert.Contains("collection-detail-hero", source, StringComparison.Ordinal);
        Assert.Contains("EDIT COLLECTION", source, StringComparison.Ordinal);
        Assert.Contains("OVERVIEW", source, StringComparison.Ordinal);
        Assert.Contains("HeroArtworkStackItems", source, StringComparison.Ordinal);
        Assert.Contains("<TuvimaArtworkStack", source, StringComparison.Ordinal);
        Assert.Contains("Palette=\"@_collection.ArtworkPalette\"", source, StringComparison.Ordinal);
        Assert.Contains("Variant=\"ArtworkStackVariant.Hero\"", source, StringComparison.Ordinal);
        Assert.Contains("ResolveHeroPalette", source, StringComparison.Ordinal);
        Assert.Contains("ToRgbCss", source, StringComparison.Ordinal);
        Assert.Contains("ItemGroups", source, StringComparison.Ordinal);
        Assert.Contains("tl-detail-page collection-detail-page", source, StringComparison.Ordinal);
        Assert.Contains("GENERATED COLLECTION", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOM COLLECTION", source, StringComparison.Ordinal);
        Assert.Contains("MediaCountKey", source, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Outlined.Tv", source, StringComparison.Ordinal);
        var clientSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\EngineApiClient.ManagedCollections.cs"));
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
        Assert.Contains("align-items: center", css, StringComparison.Ordinal);
        Assert.Contains("align-self: center", css, StringComparison.Ordinal);
        Assert.DoesNotContain("transform: translateY", css, StringComparison.Ordinal);
        Assert.Contains("collection-detail-artwork-stack", css, StringComparison.Ordinal);
        Assert.Contains("collection-detail-hero__cascade", css, StringComparison.Ordinal);
        Assert.Contains("collection-detail-item-grid", css, StringComparison.Ordinal);
        Assert.Contains("position: sticky", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TuvimaArtworkStack_IsGenericSeededAndShapeAware()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\TuvimaArtworkStack.razor"));
        var modelSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Models\ViewDTOs\ArtworkStackModels.cs"));

        Assert.Contains("public sealed class ArtworkStackItem", modelSource, StringComparison.Ordinal);
        Assert.Contains("public enum ArtworkShape", modelSource, StringComparison.Ordinal);
        Assert.Contains("public enum ArtworkStackVariant", modelSource, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public IReadOnlyList<ArtworkStackItem> Items", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string Seed", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public MediaEngine.Domain.Models.ArtworkPalette? Palette", source, StringComparison.Ordinal);
        Assert.Contains("Palette?.CssVariableStyle", source, StringComparison.Ordinal);
        Assert.Contains("OrderBy(item => StableHash", source, StringComparison.Ordinal);
        Assert.Contains("data-shape=\"@ShapeValue(slot.item.Shape)\"", source, StringComparison.Ordinal);
        Assert.Contains("--artwork-ratio: 1 / 1", source, StringComparison.Ordinal);
        Assert.Contains("--artwork-ratio: 2 / 3", source, StringComparison.Ordinal);
        Assert.Contains("--artwork-ratio: 16 / 9", source, StringComparison.Ordinal);
        Assert.Contains("--left", source, StringComparison.Ordinal);
        Assert.Contains("--top", source, StringComparison.Ordinal);
        Assert.Contains("translate(-50%, -50%)", source, StringComparison.Ordinal);
        Assert.Contains("calc(var(--left) + 18%)", source, StringComparison.Ordinal);
        Assert.Contains("width: calc(var(--artwork-width) * 1.72)", source, StringComparison.Ordinal);
        Assert.Contains("top: calc(var(--top) - 5%)", source, StringComparison.Ordinal);
        Assert.Contains("min-height: clamp(46rem, 84vh, 64rem)", source, StringComparison.Ordinal);
        Assert.Contains("overflow: visible", source, StringComparison.Ordinal);
        Assert.Contains(".artwork-stack--hero .artwork-stack__stage", source, StringComparison.Ordinal);
        Assert.Contains("background: transparent", source, StringComparison.Ordinal);
        Assert.Contains("artwork-stack--all-square", source, StringComparison.Ordinal);
        Assert.Contains("artwork-stack--all-portrait", source, StringComparison.Ordinal);
        Assert.Contains("artwork-stack--mixed", source, StringComparison.Ordinal);
        Assert.Contains("nth-of-type(n + 4)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FeaturedCover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryCover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BestCover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Collection", modelSource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
