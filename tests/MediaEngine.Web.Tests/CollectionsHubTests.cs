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
    public void CollectionsPage_UsesCardOnlyHubSections()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsPage.razor"));

        Assert.Contains("GetCollectionManagementCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("READ COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("LISTEN COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("WATCH COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("CROSS-MEDIA COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("CONTINUE BROWSING", source, StringComparison.Ordinal);
        Assert.Contains("collections-hub__tabs", source, StringComparison.Ordinal);
        Assert.Contains("collections-hub-tab", source, StringComparison.Ordinal);
        Assert.Contains("OpenCollection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GLOBAL COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SYSTEM COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATED BY YOU", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionInlineInspector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-table-wrap", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudTable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineInspector_ExposesGlobalToggleOnlyWhenAllowed()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionInlineInspector.razor"));

        Assert.Contains("Collection.CanToggleGlobal", source, StringComparison.Ordinal);
        Assert.Contains("ToggleGlobalAsync", source, StringComparison.Ordinal);
        Assert.Contains("Value=\"Collection.IsGlobal\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionsComponents_UseDetailLanguageArtworkAndContextSections()
    {
        var sectionSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionHubSection.razor"));
        var inspectorSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionInlineInspector.razor"));

        Assert.Contains("<CollectionArtworkStack", sectionSource, StringComparison.Ordinal);
        Assert.Contains("<CollectionHubCard", sectionSource, StringComparison.Ordinal);
        Assert.Contains("collection-hub-section__grid", sectionSource, StringComparison.Ordinal);
        Assert.Contains("View All", sectionSource, StringComparison.Ordinal);
        Assert.Contains("collection-hub-row", sectionSource, StringComparison.Ordinal);
        var sectionCss = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionHubSection.razor.css"));
        Assert.Contains("collection-hub-section__grid", sectionCss, StringComparison.Ordinal);
        var cardCss = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionHubCard.razor.css"));
        Assert.Contains("text-transform: uppercase", cardCss, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp: 2", cardCss, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 16 / 9", cardCss, StringComparison.Ordinal);
        Assert.Contains("<CollectionSectionLabel", sectionSource, StringComparison.Ordinal);
        Assert.Contains("MEDIA MIX", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("INCLUDED ITEMS", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("COLLECTION DETAILS", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("VISIBILITY", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("ACTIVITY", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("ToggleEditMode", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("Manage Full Collection", inspectorSource, StringComparison.Ordinal);
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
        Assert.Contains("CURATED COLLECTION", source, StringComparison.Ordinal);
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
