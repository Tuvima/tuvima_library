namespace MediaEngine.Web.Tests;

public sealed class Phase6SettingsAdminHardeningTests
{
    [Fact]
    public void SettingsShell_RendersStatusBadgesAndEngineUnavailableState()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\Settings.razor");

        Assert.Contains("<SettingsStatusBadge Status=\"@GetCurrentStatus()\" />", source, StringComparison.Ordinal);
        Assert.Contains("Engine state could not be loaded", source, StringComparison.Ordinal);
        Assert.Contains("ShouldDeferForRoleResolution", source, StringComparison.Ordinal);
        Assert.Contains("SettingsNav.ResolveRoute(Section, _currentRole)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsNav_ClassifiesAdminSectionsWithTruthStatuses()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Models\ViewDTOs\SettingsNav.cs");

        Assert.Contains("SettingsStatusKind.Live", source, StringComparison.Ordinal);
        Assert.Contains("SettingsStatusKind.Partial", source, StringComparison.Ordinal);
        Assert.Contains("public static SettingsStatusKind GetStatus", source, StringComparison.Ordinal);
        Assert.Contains("SettingsSection.LocalAi", source, StringComparison.Ordinal);
        Assert.Contains("SettingsSection.Delivery", source, StringComparison.Ordinal);
        Assert.Contains("SettingsSection.Plugins", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibrariesTab_RendersPathValidationAndSaveTruth()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\LibrariesTab.razor");

        Assert.Contains("file-organization-page__actions", source, StringComparison.Ordinal);
        Assert.Contains("Folder health", source, StringComparison.Ordinal);
        Assert.Contains("<AppTabs ActivePanelIndex", source, StringComparison.Ordinal);
        Assert.Contains("settings-tab-strip settings-file-org-tabs", source, StringComparison.Ordinal);
        Assert.Contains("Import Folders", source, StringComparison.Ordinal);
        Assert.Contains("IsImportFoldersTab", source, StringComparison.Ordinal);
        Assert.Contains("Folders for @activeLibrary.Label", source, StringComparison.Ordinal);
        Assert.Contains("StructureModeRecommended", source, StringComparison.Ordinal);
        Assert.Contains("StructureModeCustom", source, StringComparison.Ordinal);
        Assert.Contains("StructureModeNone", source, StringComparison.Ordinal);
        Assert.Contains("Recommended", source, StringComparison.Ordinal);
        Assert.Contains("Custom", source, StringComparison.Ordinal);
        Assert.Contains("None", source, StringComparison.Ordinal);
        Assert.Contains("Naming and Folder Structure", source, StringComparison.Ordinal);
        Assert.Contains("Tuvima will index these files in place", source, StringComparison.Ordinal);
        Assert.Contains("Engine unavailable - path could not be checked.", source, StringComparison.Ordinal);
        Assert.Contains("Folder monitoring was updated", source, StringComparison.Ordinal);
        Assert.Contains("A rescan is recommended", source, StringComparison.Ordinal);
        Assert.Contains("PreviewOrganizationTemplateAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateLibrariesAsync", source, StringComparison.Ordinal);
        Assert.Contains("ValidateImportFoldersAreSeparate", source, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(_savingFolders || _engineUnavailable || !HasUnsavedChanges)\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The Library Root applies to all libraries", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Global Library Root", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Move to Library", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Import in Place", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Global Paths &amp; Watch Folders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Class=\"file-org-tabs\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Year + Title", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Title (Year)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WatchTv_UsesSharedBrowseShellAndDirectShowDetails()
    {
        var watchPage = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\WatchPage.razor");
        var browseShell = ReadRepoFile(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");
        var queryBuilder = ReadRepoFile(@"src\MediaEngine.Web\Components\Browse\BrowseQueryBuilder.cs");

        Assert.Contains("<MediaBrowseShell Tab=\"@Tab\"", watchPage, StringComparison.Ordinal);
        Assert.DoesNotContain("<TvBrowsePage", watchPage, StringComparison.Ordinal);
        Assert.Contains("IsTvShowsGrouping && !UseListLayout", browseShell, StringComparison.Ordinal);
        Assert.Contains("LoadDisplayCardsAsync(append)", browseShell, StringComparison.Ordinal);
        Assert.Contains("(\"tv\", \"shows\") => $\"/watch/tv/show/{group.CollectionId:D}\"", browseShell, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"tv\", \"shows\") => \"show_name\"", queryBuilder, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersTab_DoesNotUseHardcodedFallbackAsLiveConfig()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor");

        Assert.Contains("sample data is not presented as live configuration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Load sample chain", source, StringComparison.Ordinal);
        Assert.Contains("Provider settings saved to Engine configuration.", source, StringComparison.Ordinal);
        Assert.Contains("Last tested", source, StringComparison.Ordinal);
        Assert.Contains("SavePipelinesAsync", source, StringComparison.Ordinal);
        Assert.Contains("SaveProviderConfigAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Provider Setup", source, StringComparison.Ordinal);
        Assert.Contains("Assign Providers to Media Types", source, StringComparison.Ordinal);
        Assert.Contains("[\"Movies\", \"TV\", \"Music\", \"Books\", \"Audiobooks\", \"Comics\"]", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderLogoUrl", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderCards", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersTab_DefinesStageScopedProviderSetsAndRetailOnlyPipelineSave()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor");

        Assert.Contains("new(ProviderStageRetail, 1, \"Retail Lookup\"", source, StringComparison.Ordinal);
        Assert.Contains("new(ProviderStageCanonical, 2, \"Canonical Identity\"", source, StringComparison.Ordinal);
        Assert.Contains("new(ProviderStageEnrichment, 3, \"Enrichment & Artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"apple_api\", \"comicvine\", \"musicbrainz\", \"open_library\", \"tmdb\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"wikidata\", \"wikidata_reconciliation\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"fanart_tv\", \"lrclib\", \"opensubtitles\"]", source, StringComparison.Ordinal);
        Assert.Contains("GetStageCatalogueEntries(_activeStage", source, StringComparison.Ordinal);
        Assert.Contains("ProviderBelongsToStage", source, StringComparison.Ordinal);
        Assert.Contains("GetCanonicalSummaryCards", source, StringComparison.Ordinal);
        Assert.Contains("GetWikidataMetrics", source, StringComparison.Ordinal);
        Assert.Contains("GetEnrichmentProviderCards", source, StringComparison.Ordinal);
        Assert.Contains("GetEnrichmentMetrics", source, StringComparison.Ordinal);
        Assert.Contains("@if (IsRetailStage)", source, StringComparison.Ordinal);
        Assert.Contains("SavePipelinesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersTab_HasDownloadedIconsForVisibleProviders()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor");

        var expectedIcons = new[]
        {
            "apple_books.svg",
            "comicvine.png",
            "fanart_tv.png",
            "lrclib.png",
            "musicbrainz.svg",
            "opensubtitles.png",
            "open_library.png",
            "tmdb.svg",
            "wikidata_reconciliation.svg",
        };

        foreach (var icon in expectedIcons)
        {
            Assert.Contains($"images/providers/{icon}", source, StringComparison.Ordinal);
            Assert.True(
                File.Exists(GetRepoPath($@"src\MediaEngine.Web\wwwroot\images\providers\{icon}")),
                $"Expected provider icon asset {icon} to exist.");
        }

        var providerConfigs = new[]
        {
            "apple_api.json",
            "comicvine.json",
            "fanart_tv.json",
            "lrclib.json",
            "musicbrainz.json",
            "opensubtitles.json",
            "open_library.json",
            "tmdb.json",
            "wikidata_reconciliation.json",
        };

        foreach (var config in providerConfigs)
        {
            var configJson = ReadRepoFile($@"config\providers\{config}");
            Assert.Contains("\"icon\"", configJson, StringComparison.Ordinal);
            Assert.Contains("images/providers/", configJson, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MetadataSettings_AreHiddenFromSettingsUi()
    {
        var settings = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\Settings.razor");
        var nav = ReadRepoFile(@"src\MediaEngine.Web\Models\ViewDTOs\SettingsNav.cs");
        var metadataTabPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            @"src\MediaEngine.Web\Components\Settings\MetadataMatchingTab.razor"));

        Assert.False(File.Exists(metadataTabPath));
        Assert.DoesNotContain("MetadataMatchingTab", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Metadata & Matching", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("/settings/metadata", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessSettings_UsesRealApiKeyTabAndMarksUnpersistedControls()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\UsersAccessSettingsTab.razor");

        Assert.Contains("<ApiKeysTab />", source, StringComparison.Ordinal);
        Assert.Contains("Not connected. Local network, remote access, and rate-limit controls", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner Administrator\", \"library:read, ingest:write", source, StringComparison.Ordinal);
        Assert.Contains("Session listing and revocation are not persisted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryPluginsAndLocalAi_AreTruthLabeled()
    {
        var delivery = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\PlaybackDeliverySettingsTab.razor");
        var plugins = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\PluginSettingsTab.razor");
        var localAi = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\LocalAiSettingsTab.razor");

        Assert.Contains("SettingsStatusKind.Partial", delivery, StringComparison.Ordinal);
        Assert.Contains("remain disabled until persistence exists", delivery, StringComparison.Ordinal);
        Assert.Contains("Install and update marketplace flows are planned", plugins, StringComparison.Ordinal);
        Assert.Contains("Local AI runs on this server", localAi, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(GetRepoPath(relativePath));

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            relativePath));
}
