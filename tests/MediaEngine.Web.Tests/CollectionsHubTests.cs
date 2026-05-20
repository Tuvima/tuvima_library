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
    public void CollectionsPage_UsesHubSectionsAndInlineInspector()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsPage.razor"));

        Assert.Contains("GetCollectionManagementCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("READ COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("LISTEN COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("WATCH COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("CROSS-MEDIA COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("GLOBAL COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("RECENTLY USED", source, StringComparison.Ordinal);
        Assert.Contains("collections-hub__tabs", source, StringComparison.Ordinal);
        Assert.Contains("collections-hub-tab", source, StringComparison.Ordinal);
        Assert.Contains("SYSTEM COLLECTIONS", source, StringComparison.Ordinal);
        Assert.Contains("CREATED BY YOU", source, StringComparison.Ordinal);
        Assert.Contains("<CollectionInlineInspector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionHubCard", source, StringComparison.Ordinal);
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
        Assert.Contains("collection-hub-row", sectionSource, StringComparison.Ordinal);
        var sectionCss = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionHubSection.razor.css"));
        Assert.Contains("text-transform: uppercase", sectionCss, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp: 2", sectionCss, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionHubCard", sectionSource, StringComparison.Ordinal);
        Assert.Contains("<CollectionSectionLabel", sectionSource, StringComparison.Ordinal);
        Assert.Contains("MEDIA MIX", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("INCLUDED ITEMS", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("COLLECTION DETAILS", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("VISIBILITY", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("ACTIVITY", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("ToggleEditMode", inspectorSource, StringComparison.Ordinal);
        Assert.Contains("Manage Full Collection", inspectorSource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
