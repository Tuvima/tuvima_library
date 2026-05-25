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

        Assert.Contains("Engine unavailable - path could not be checked.", source, StringComparison.Ordinal);
        Assert.Contains("Watcher hot-swap was requested", source, StringComparison.Ordinal);
        Assert.Contains("A rescan is recommended", source, StringComparison.Ordinal);
        Assert.Contains("PreviewOrganizationTemplateAsync", source, StringComparison.Ordinal);
        Assert.Contains("Scan saved watch folder", source, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(_savingFolders || _engineUnavailable || !HasUnsavedChanges)\"", source, StringComparison.Ordinal);
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
        Assert.Contains("Metadata Providers", source, StringComparison.Ordinal);
        Assert.Contains("Assign to Media Types (Retail Lookup)", source, StringComparison.Ordinal);
        Assert.Contains("[\"Movies\", \"TV\", \"Music\", \"Books\", \"Audiobooks\", \"Comics\"]", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderLogoUrl", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderCards", source, StringComparison.Ordinal);
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
