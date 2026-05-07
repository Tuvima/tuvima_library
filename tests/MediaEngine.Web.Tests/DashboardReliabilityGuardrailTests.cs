namespace MediaEngine.Web.Tests;

public sealed class DashboardReliabilityGuardrailTests
{
    [Fact]
    public void Routes_WrapsRoutedPagesInErrorBoundary()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Routes.razor");

        Assert.Contains("<ErrorBoundary>", source, StringComparison.Ordinal);
        Assert.Contains("<AppErrorState", source, StringComparison.Ordinal);
        Assert.Contains("<FocusOnNavigate", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"src\MediaEngine.Web\Components\Library\LibraryMediaGrid.razor", "@key=\"item.EntityId\"")]
    [InlineData(@"src\MediaEngine.Web\Components\LibraryItems\LibraryItemGrid.razor", "@key=\"item.EntityId\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Universe\AlphabeticalGrid.razor", "@key=\"@GetItemKey(item)\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Universe\PosterSwimlane.razor", "@key=\"item.Id\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor", "@key=\"card.Id\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Library\LibraryConfigurableTable.razor", "@key=\"item.EntityId\"")]
    [InlineData(@"src\MediaEngine.Web\Shared\MainLayout.razor", "@key=\"link.Path\"")]
    public void HighRiskListComponents_UseStableKeys(string relativePath, string expectedKey)
    {
        var source = Read(relativePath);

        Assert.Contains(expectedKey, source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppErrorState_ProvidesTitleMessageAndRetryAffordance()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Shared\AppErrorState.razor");

        Assert.Contains("[Parameter] public string? Title", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? Message", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback Retry", source, StringComparison.Ordinal);
        Assert.Contains("app-error-state__retry", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath)));
}
