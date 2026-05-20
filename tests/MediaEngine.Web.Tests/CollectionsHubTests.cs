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
        Assert.Contains("<CollectionHubSection Title=\"Watch\"", source, StringComparison.Ordinal);
        Assert.Contains("<CollectionHubSection Title=\"Listen\"", source, StringComparison.Ordinal);
        Assert.Contains("<CollectionHubSection Title=\"Read\"", source, StringComparison.Ordinal);
        Assert.Contains("<CollectionHubSection Title=\"Cross-Media\"", source, StringComparison.Ordinal);
        Assert.Contains("<CollectionInlineInspector", source, StringComparison.Ordinal);
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

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
