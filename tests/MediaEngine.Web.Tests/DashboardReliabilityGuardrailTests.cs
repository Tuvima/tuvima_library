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
    [InlineData(@"src\MediaEngine.Web\Components\Activity\ActivityBatchExplorer.razor", "@key=\"batch.BatchId\"")]
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
    public void BrowseShell_DetailedRoutesDoNotRenderHero()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");
        var normalized = source.ReplaceLineEndings("\n");

        Assert.DoesNotContain("<MediaBrowseHero", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BrowseHeroViewModel? _hero", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetJourneyAsync(limit: 24)", source, StringComparison.Ordinal);
        Assert.Contains("if (!_hasLoadedOnce)\n            StateHasChanged();", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseShell_ReloadsWhenLayoutModeChanges()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");

        Assert.Contains("LayoutToggleClass(LibraryLayoutMode.Card)", source, StringComparison.Ordinal);
        Assert.Contains("LayoutToggleClass(LibraryLayoutMode.List)", source, StringComparison.Ordinal);
        Assert.Contains("await NavigateAndReloadAsync(reloadData: true);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await NavigateAndReloadAsync(reloadData: false);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WatchAndReadDefaultTabsKeepExplicitBrowseRoutes()
    {
        var browseShell = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");
        var watchPage = Read(@"src\MediaEngine.Web\Components\Pages\WatchPage.razor");
        var readPage = Read(@"src\MediaEngine.Web\Components\Pages\ReadPage.razor");

        Assert.Contains("&& !Preset.UseExplicitDefaultTabRoute", browseShell, StringComparison.Ordinal);
        Assert.Contains("UseExplicitDefaultTabRoute = true", watchPage, StringComparison.Ordinal);
        Assert.Contains("UseExplicitDefaultTabRoute = true", readPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseShell_UsesDisplayCardsForTvShowCardView()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");

        Assert.Contains("IsTvShowsGrouping", source, StringComparison.Ordinal);
        Assert.Contains("IsTvShowsGrouping && !UseListLayout", source, StringComparison.Ordinal);
        Assert.Contains("LoadDisplayCardsAsync(append)", source, StringComparison.Ordinal);
        Assert.Contains("Display API did not return a TV browse page.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseShell_NormalizesMediaTypeWhenFilteringContainerGroups()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");

        Assert.Contains(".Where(group => MediaTypeMatches(group.PrimaryMediaType, mediaType))", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeEditorMediaType(candidate)", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeEditorMediaType(requested)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(group => string.Equals(group.PrimaryMediaType, mediaType, StringComparison.OrdinalIgnoreCase))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseShell_BindsSortValueInsteadOfLiteralFieldName()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");

        Assert.Contains("Value=\"@_sortBy\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"_sortBy\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverySpotlightCarousel_UsesSubtleCounterInsteadOfDotStrip()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Discovery\DiscoverySpotlightCarousel.razor");
        var styles = Read(@"src\MediaEngine.Web\Components\Discovery\DiscoverySpotlightCarousel.razor.css");

        Assert.Contains("discovery-spotlight-carousel__counter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("discovery-spotlight-carousel__dots", source, StringComparison.Ordinal);
        Assert.DoesNotContain("discovery-spotlight-carousel__dot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("discovery-spotlight-carousel__dots", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("discovery-spotlight-carousel__dot", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionDashboard_ExplainsIconMeaningWithSimpleTooltips()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Settings\IngestionLiveDashboard.razor");

        Assert.Contains("MudTooltip Text=\"Reload the latest ingestion status.\"", source, StringComparison.Ordinal);
        Assert.Contains("MudTooltip Text=\"Start a new scan of the watched folders.\"", source, StringComparison.Ordinal);
        Assert.Contains("StageIconTooltip(stage)", source, StringComparison.Ordinal);
        Assert.Contains("StageProgressTooltip(stage)", source, StringComparison.Ordinal);
        Assert.Contains("StageDetailTooltip(detail)", source, StringComparison.Ordinal);
        Assert.Contains("BatchSelectionTooltip(batch)", source, StringComparison.Ordinal);
        Assert.Contains("BatchMediaTooltip(chip)", source, StringComparison.Ordinal);
        Assert.Contains("BatchArtifactTooltip(chip)", source, StringComparison.Ordinal);
        Assert.Contains("BatchActivityTooltip(batch)", source, StringComparison.Ordinal);
        Assert.Contains("Recent batches show the latest scans and whether they are active or complete.", source, StringComparison.Ordinal);
        Assert.Contains("Matches files to retail catalog data like title, cover, and description.", source, StringComparison.Ordinal);
        Assert.Contains("Links matched items to Wikidata IDs so relationships can be built.", source, StringComparison.Ordinal);
        Assert.Contains("Files or works that have a direct Wikidata ID.", source, StringComparison.Ordinal);
        Assert.Contains("Extra Wikidata IDs found for people, series, universes, or story details.", source, StringComparison.Ordinal);
        Assert.Contains("Items Tuvima could not confidently finish.", source, StringComparison.Ordinal);
        Assert.Contains("People with extra profile data such as biography or images.", source, StringComparison.Ordinal);
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
