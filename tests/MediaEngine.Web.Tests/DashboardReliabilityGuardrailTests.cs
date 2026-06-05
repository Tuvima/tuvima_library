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
    [InlineData(@"src\MediaEngine.Web\Components\MediaTiles\MediaTileGrid.razor", "@key=\"item.Id\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Library\LibraryConfigurableTable.razor", "@key=\"item.EntityId\"")]
    [InlineData(@"src\MediaEngine.Web\Shared\MainLayout.razor", "@key=\"link.Path\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Settings\SettingsReviewQueueTab.razor", "@key=\"item.Id\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Settings\ActivityTab.razor", "@key=\"ActivityEntryKey(entry)\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Settings\IngestionLiveDashboard.razor", "@key=\"StageDetailKey(detail)\"")]
    [InlineData(@"src\MediaEngine.Web\Components\Settings\ProviderTesterToolTab.razor", "@key=\"resultKey\"")]
    public void HighRiskListComponents_UseStableKeys(string relativePath, string expectedKey)
    {
        var source = Read(relativePath);

        Assert.Contains(expectedKey, source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewQueue_KeepsExistingRowsVisibleDuringRefresh()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Settings\SettingsReviewQueueTab.razor");
        var normalized = source.ReplaceLineEndings("\n");

        Assert.Contains("_loading && _items.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("settings-review-refresh-bar", source, StringComparison.Ordinal);
        Assert.Contains("if (_items.Count == 0)\n                _items = [];", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("else if (_items.Count == 0)\n    {\n        _items = [];", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeDiscovery_DebouncesBroadRealtimeReloads()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Pages\LibraryBrowsePage.razor");

        Assert.Contains("LastStateChangeRequiresSnapshotRefresh", source, StringComparison.Ordinal);
        Assert.Contains("DebounceStateReload", source, StringComparison.Ordinal);
        Assert.Contains("_loadInProgress", source, StringComparison.Ordinal);
        Assert.Contains("_loading && _hasLoadedOnce", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionDashboard_DoesNotClearOperationDetailsWhenRowsAreSkipped()
    {
        var source = Read(@"src\MediaEngine.Web\Services\Integration\IngestionLiveDashboardState.cs");

        Assert.Contains("LoadSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("_loadInProgress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_operationDetails.Clear();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_capabilitiesByEntity.Clear();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseShell_KeepsHeroWhileReloading()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");
        var normalized = source.ReplaceLineEndings("\n");

        Assert.DoesNotContain("_hero = null;", source, StringComparison.Ordinal);
        Assert.Contains("if (!_hasLoadedOnce)\n            StateHasChanged();", normalized, StringComparison.Ordinal);
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
