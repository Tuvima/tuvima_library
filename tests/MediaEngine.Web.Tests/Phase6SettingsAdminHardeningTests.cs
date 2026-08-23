namespace MediaEngine.Web.Tests;

public sealed class Phase6SettingsAdminHardeningTests
{
    [Fact]
    public void SettingsShell_RendersStatusBadgesAndEngineUnavailableState()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\Settings.razor");

        Assert.Contains("Status=\"@GetHeaderStatusLabel()\"", source, StringComparison.Ordinal);
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
    public void LibrariesTab_RendersSchemaThreeMediaManagement()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\LibrariesTab.razor");
        var nav = ReadRepoFile(@"src\MediaEngine.Web\Models\ViewDTOs\SettingsNav.cs");
        var settings = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\Settings.razor");

        Assert.DoesNotContain("aria-label=\"Media Management sections\"", source, StringComparison.Ordinal);
        Assert.Contains("new(\"overview\", \"Overview\"", nav, StringComparison.Ordinal);
        Assert.Contains("new(\"incoming\", \"Incoming\"", nav, StringComparison.Ordinal);
        Assert.Contains("new(\"libraries\", \"Libraries\"", nav, StringComparison.Ordinal);
        Assert.Contains("new(\"activity\", \"Activity\"", nav, StringComparison.Ordinal);
        Assert.Contains("<SettingsSubsectionNav", settings, StringComparison.Ordinal);

        Assert.Contains("Structured libraries", source, StringComparison.Ordinal);
        Assert.Contains("Personal libraries (View)", source, StringComparison.Ordinal);
        Assert.Contains("Incoming folders", source, StringComparison.Ordinal);
        Assert.Contains("Folders &amp; sources", source, StringComparison.Ordinal);
        Assert.Contains("Existing folders stay unchanged", source, StringComparison.Ordinal);
        Assert.Contains("Primary destination", source, StringComparison.Ordinal);
        Assert.Contains("Participates in organization", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate handling", source, StringComparison.Ordinal);
        Assert.Contains("Owner profile ID", source, StringComparison.Ordinal);
        Assert.Contains("Visibility", source, StringComparison.Ordinal);
        Assert.Contains("Personal library permissions", source, StringComparison.Ordinal);
        Assert.Contains("AllowUserCreation", source, StringComparison.Ordinal);
        Assert.Contains("AllowManagedStorage", source, StringComparison.Ordinal);
        Assert.Contains("AllowExistingFolderAttachment", source, StringComparison.Ordinal);
        Assert.Contains("AllowConnectedDeviceImport", source, StringComparison.Ordinal);
        Assert.Contains("DefaultVisibility", source, StringComparison.Ordinal);
        Assert.Contains("GetLibrariesAsync", source, StringComparison.Ordinal);
        Assert.Contains("TestPathAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateLibrariesAsync", source, StringComparison.Ordinal);
        Assert.Contains("HasUnsavedChanges", source, StringComparison.Ordinal);
        Assert.Contains("Save Changes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetFolderSettingsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateFolderSettingsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SourcePaths", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LibraryRoot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The Library Root applies to all libraries", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Global Library Root", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Move to Library", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Import in Place", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Global Paths &amp; Watch Folders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("file-organization-page", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WatchTv_UsesSharedBrowseShellAndDirectShowDetails()
    {
        var watchPage = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\WatchPage.razor");
        var lanePage = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\MediaLanePage.razor");
        var browseShell = ReadRepoFile(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor");
        var queryBuilder = ReadRepoFile(@"src\MediaEngine.Web\Components\Browse\BrowseQueryBuilder.cs");

        Assert.Contains("<MediaLanePage Title=\"Watch\"", watchPage, StringComparison.Ordinal);
        Assert.Contains("<MediaBrowseShell Tab=\"@Tab\"", lanePage, StringComparison.Ordinal);
        Assert.DoesNotContain("<TvBrowsePage", watchPage, StringComparison.Ordinal);
        Assert.Contains("IsTvShowsGrouping && !UseListLayout", browseShell, StringComparison.Ordinal);
        Assert.Contains("LoadDisplayCardsAsync(append)", browseShell, StringComparison.Ordinal);
        Assert.Contains("(\"tv\", \"shows\") => $\"/details/tvshow/{GetTvShowGroupRouteId(group):D}?context=watch\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("group.RootWorkId ?? group.CollectionId", browseShell, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"tv\", \"shows\") => \"show_name\"", queryBuilder, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersTab_DoesNotUseHardcodedFallbackAsLiveConfig()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor.cs")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPrioritySurface.razor")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPrioritySurface.razor.cs")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderEnrichmentSurface.razor")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderEnrichmentSurface.razor.cs");

        Assert.Contains("No sample providers are shown as live configuration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Load sample chain", source, StringComparison.Ordinal);
        Assert.Contains("Provider health uses recorded Engine checks", source, StringComparison.Ordinal);
        Assert.Contains("Last tested", source, StringComparison.Ordinal);
        Assert.Contains("SavePipelinesAsync", source, StringComparison.Ordinal);
        Assert.Contains("SaveProviderConfigAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateHydrationSettingsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Provider Setup", source, StringComparison.Ordinal);
        Assert.Contains("Source Priority", source, StringComparison.Ordinal);
        Assert.Contains("[\"Movies\", \"TV\", \"Music\", \"Books\", \"Audiobooks\", \"Comics\"]", source, StringComparison.Ordinal);
        Assert.Contains("ResolveLogo", source, StringComparison.Ordinal);
        Assert.Contains("FilteredProviders", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersTab_PreservesRetailPipelineAndLocksCanonicalResponsibilities()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPrioritySurface.razor")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPrioritySurface.razor.cs");

        Assert.Contains("provider.HydrationStages.Contains(1)", source, StringComparison.Ordinal);
        Assert.Contains("canonical_source", source, StringComparison.Ordinal);
        Assert.Contains("System or stage-defined support", source, StringComparison.Ordinal);
        Assert.Contains("item.RequiredSystemProvider", source, StringComparison.Ordinal);
        Assert.Contains("DragIndicator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyboardArrowUp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyboardArrowDown", source, StringComparison.Ordinal);
        Assert.Contains("CopyProviderEntry", source, StringComparison.Ordinal);
        Assert.Contains("AcceptedTransition = source?.AcceptedTransition", source, StringComparison.Ordinal);
        Assert.Contains("GetDefaultPipelinesAsync", source, StringComparison.Ordinal);
        Assert.Contains("SavePipelinesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersTab_HasDownloadedIconsForVisibleProviders()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor")
                     + ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ProviderPriorityTab.razor.cs");

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
        Assert.Contains("Authentication", source, StringComparison.Ordinal);
        Assert.Contains("Linked Accounts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner Administrator\", \"library:read, ingest:write", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Access Rules", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sessions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryPluginsAndLocalAi_AreTruthLabeled()
    {
        var delivery = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\PlaybackDeliverySettingsTab.razor");
        var plugins = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\PluginSettingsTab.razor");
        var localAi = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\LocalAiSettingsTab.razor");

        Assert.Contains("Variant Storage", delivery, StringComparison.Ordinal);
        Assert.Contains("Diagnostics", delivery, StringComparison.Ordinal);
        Assert.Contains("Install and update marketplace flows are planned", plugins, StringComparison.Ordinal);
        Assert.Contains("plugin-provided settings", plugins, StringComparison.Ordinal);
        Assert.Contains("Advanced settings", plugins, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"JSON\"", plugins, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Manifest\"", plugins, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings JSON", plugins, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugin manifest JSON", plugins, StringComparison.Ordinal);
        Assert.DoesNotContain("_settingsJson", plugins, StringComparison.Ordinal);
        Assert.DoesNotContain("_manifestJson", plugins, StringComparison.Ordinal);
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
