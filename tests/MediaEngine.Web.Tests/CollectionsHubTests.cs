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
        var heroSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Cinematic\CinematicHeroCarousel.razor"));
        var heroStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Cinematic\CinematicHeroCarousel.razor.css"));
        var browseShellSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor"));
        var browseShellStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\BrowseShellStyles.razor.css"));
        var tileGridSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaTileGrid.razor"));
        var groupTileSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaGroupTile.razor"));
        var groupTileStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaGroupTile.razor.css"));

        Assert.Contains("GetCollectionCatalogAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionHeroes", source, StringComparison.Ordinal);
        Assert.Contains("Presentation = MediaTilePresentation.Default", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolvePresentation", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", browseShellSource, StringComparison.Ordinal);
        Assert.Contains(".browse-shell__grid", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("display: flex", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("browse-shell collections-browse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CinematicHeroCarousel", source, StringComparison.Ordinal);
        Assert.Contains("<SurfaceTabBar", source, StringComparison.Ordinal);
        Assert.Contains("<MediaTileGrid", source, StringComparison.Ordinal);
        Assert.Contains("Shape = MediaTileShape.Landscape", source, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind = MediaTileSurfaceKind.BannerLandscape", source, StringComparison.Ordinal);
        Assert.Contains("UseLandscapeGroupTile = true", source, StringComparison.Ordinal);
        Assert.Contains("item.RenderAsLandscapeGroupTile", tileGridSource, StringComparison.Ordinal);
        Assert.Contains("<MediaGroupTile", tileGridSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__artwork", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("MediaArtworkGroupPreviewLayout.Cluster", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaArtworkGroupPreviewLayout.Mosaic", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__identity", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__year", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__media-count", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("media-group-tile__kind", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("At a glance", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HighlightedItems", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("media-group-tile__highlights", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaArtworkCarousel", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("--media-group-tile-width: clamp(560px, 40vw, 740px)", groupTileStylesSource, StringComparison.Ordinal);
        Assert.Contains("--media-group-tile-height: clamp(300px, 20vw, 365px)", groupTileStylesSource, StringComparison.Ordinal);
        Assert.Contains("PreviewTotalCount = collection.ItemCount", source, StringComparison.Ordinal);
        Assert.Contains("TileTextMode = MediaTileTextMode.CoverOnly", source, StringComparison.Ordinal);
        Assert.Contains("Take(4)", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__search", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__sort", source, StringComparison.Ordinal);
        Assert.Contains("Search collections", source, StringComparison.Ordinal);
        Assert.Contains("Recently Updated", source, StringComparison.Ordinal);
        Assert.Contains("Item Count", source, StringComparison.Ordinal);
        Assert.Contains("Broader rollups", source, StringComparison.Ordinal);
        Assert.Contains("Mode = \"Collection\"", source, StringComparison.Ordinal);
        Assert.Contains("MediaKind = \"Collection\"", source, StringComparison.Ordinal);
        Assert.Contains("PaletteArtwork(collection)", source, StringComparison.Ordinal);
        Assert.Contains("ArtworkPalette = collection.ArtworkPalette", source, StringComparison.Ordinal);
        Assert.Contains("SecondaryAccentColor = secondaryAccentColor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Mode = type == \"Playlist\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaKind = \"Playlist\"", source, StringComparison.Ordinal);
        Assert.Contains("cinematic-hero-carousel", heroStylesSource, StringComparison.Ordinal);
        Assert.Contains("CinematicHeroSurface", heroSource, StringComparison.Ordinal);
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
        Assert.Contains("\"Watch\", collection.WatchCount", source, StringComparison.Ordinal);
        Assert.Contains("\"Listen\", collection.ListenCount", source, StringComparison.Ordinal);
        Assert.Contains("\"Read\", collection.ReadCount", source, StringComparison.Ordinal);
        Assert.Contains("\"Movies\", collection.MovieCount", source, StringComparison.Ordinal);
        Assert.Contains("\"TV\", collection.TvCount", source, StringComparison.Ordinal);
        Assert.Contains("EarliestYear = collection.EarliestYear", source, StringComparison.Ordinal);
        Assert.Contains("LatestYear = collection.LatestYear", source, StringComparison.Ordinal);
        Assert.Contains("var navigationUrl = collection.Person is null", source, StringComparison.Ordinal);
        Assert.Contains("$\"/details/person/{collection.Person.Id:D}\"", source, StringComparison.Ordinal);
        Assert.Contains("PrimaryNavigationUrl = navigationUrl", source, StringComparison.Ordinal);
        Assert.Contains("Person = collection.Person is null", source, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__person", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("GroupActionNoun => HasPerson ? \"Person\"", groupTileSource, StringComparison.Ordinal);
        Assert.Contains(".media-group-tile.has-person .media-group-tile__identity", groupTileStylesSource, StringComparison.Ordinal);
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
    public void CollectionDetail_UsesCanonicalSharedDetailSurface()
    {
        var pagesPath = Path.GetDirectoryName(
            GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\UnifiedDetailPage.razor"))!;
        var route = File.ReadAllText(Path.Combine(pagesPath, "UnifiedDetailPage.razor"));
        var detail = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Details\DetailPage.razor"));
        var collections = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsPage.razor"));

        Assert.False(File.Exists(Path.Combine(pagesPath, "CollectionDetail.razor")));
        Assert.False(File.Exists(Path.Combine(pagesPath, "CollectionDetail.razor.css")));
        Assert.Contains("@page \"/details/{EntityType}/{Id:guid}\"", route, StringComparison.Ordinal);
        Assert.Contains("/details/collection/", collections, StringComparison.Ordinal);
        Assert.Contains("CollectionEditorLauncher.OpenAsync(new CollectionEditorLaunchRequest", detail, StringComparison.Ordinal);
        Assert.Contains("<DetailPrimaryModule Model=\"Model\"", detail, StringComparison.Ordinal);
        Assert.Contains("<DetailTabs Tabs=\"VisibleTabs\"", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionEditor_UsesTypedTextareaForDescription()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionEditorShell.razor"));

        Assert.Contains("<AppTextarea Value=\"_description\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<AppTextField T=\"string\"\r\n                          Value=\"_description\"", source, StringComparison.Ordinal);
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
