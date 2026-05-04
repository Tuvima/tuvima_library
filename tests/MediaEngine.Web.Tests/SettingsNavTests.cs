using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class SettingsNavTests
{
    [Fact]
    public void SettingsComponents_DoNotContainLegacyFoldersTab()
    {
        var root = GetRepoFilePath("");
        var legacyPath = Path.Combine(
            root,
            "src",
            "MediaEngine.Web",
            "Components",
            "Settings",
            "FoldersTab.razor");

        Assert.False(File.Exists(legacyPath));

        var settingsSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\Settings.razor"));
        Assert.Contains("<LibrariesTab />", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FoldersTab", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_UsesActiveProfileRoleInsteadOfHardcodedAdministrator()
    {
        var settingsSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\Settings.razor"));
        var orchestratorSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\UIOrchestratorService.cs"));
        var sessionSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\ActiveProfileSessionService.cs"));

        Assert.Contains("await LoadActiveProfileRoleAsync()", settingsSource, StringComparison.Ordinal);
        Assert.Contains("SettingsNav.ResolveRoute(Section, _currentRole)", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly string _currentRole = \"Administrator\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("SetActiveProfileAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.Contains("tuvima-active-profile-id", sessionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteFor_Overview_UsesBaseSettingsUrl()
    {
        Assert.Equal("/settings", SettingsNav.RouteFor(SettingsSection.Overview));
    }

    [Theory]
    [InlineData(SettingsSection.AdminOverview, "/settings/admin")]
    [InlineData(SettingsSection.Playback, "/settings/playback")]
    [InlineData(SettingsSection.Display, "/settings/display")]
    [InlineData(SettingsSection.Privacy, "/settings/privacy")]
    [InlineData(SettingsSection.Libraries, "/settings/libraries")]
    [InlineData(SettingsSection.Ingestion, "/settings/ingestion")]
    [InlineData(SettingsSection.Metadata, "/settings/metadata")]
    [InlineData(SettingsSection.Providers, "/settings/providers")]
    [InlineData(SettingsSection.LocalAi, "/settings/ai")]
    [InlineData(SettingsSection.Delivery, "/settings/delivery")]
    [InlineData(SettingsSection.Access, "/settings/access")]
    [InlineData(SettingsSection.Review, "/settings/review")]
    [InlineData(SettingsSection.Setup, "/settings/setup")]
    [InlineData(SettingsSection.ProviderTester, "/settings/provider-tester")]
    [InlineData(SettingsSection.EnrichmentTester, "/settings/enrichment-tester")]
    public void ResolveRoute_CanonicalSegments_AreStable(SettingsSection section, string expectedRoute)
    {
        var segment = expectedRoute.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var resolution = SettingsNav.ResolveRoute(segment, "Administrator");

        Assert.Equal(section, resolution.Section);
        Assert.Equal(expectedRoute, resolution.CanonicalRoute);
        Assert.True(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.RequestedSectionAllowed);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_BaseSettings_MapsToUserOverview()
    {
        var resolution = SettingsNav.ResolveRoute(null, "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings", resolution.CanonicalRoute);
        Assert.True(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_AdminSegment_MapsToAdminOverview()
    {
        var resolution = SettingsNav.ResolveRoute("admin", "Administrator");

        Assert.Equal(SettingsSection.AdminOverview, resolution.Section);
        Assert.Equal("/settings/admin", resolution.CanonicalRoute);
        Assert.True(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.RequestedSectionAllowed);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_ProfileSegment_IsUnknownAndFallsBackToUserOverview()
    {
        var resolution = SettingsNav.ResolveRoute("profile", "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.False(resolution.IsKnownRoute);
        Assert.False(resolution.RequestedSectionAllowed);
        Assert.True(resolution.ShouldRedirect);
    }

    [Theory]
    [InlineData("folders", SettingsSection.Libraries, "/settings/libraries")]
    [InlineData("activity", SettingsSection.Ingestion, "/settings/ingestion")]
    [InlineData("registry", SettingsSection.Ingestion, "/settings/ingestion")]
    [InlineData("tasks", SettingsSection.Ingestion, "/settings/ingestion")]
    [InlineData("maintenance", SettingsSection.Ingestion, "/settings/ingestion")]
    [InlineData("wikidata", SettingsSection.Metadata, "/settings/metadata")]
    [InlineData("models", SettingsSection.LocalAi, "/settings/ai")]
    [InlineData("features", SettingsSection.LocalAi, "/settings/ai")]
    [InlineData("vocabulary", SettingsSection.LocalAi, "/settings/ai")]
    [InlineData("schedule", SettingsSection.LocalAi, "/settings/ai")]
    [InlineData("encode", SettingsSection.Delivery, "/settings/delivery")]
    [InlineData("offline-downloads", SettingsSection.Delivery, "/settings/delivery")]
    [InlineData("users", SettingsSection.Access, "/settings/access")]
    [InlineData("security", SettingsSection.Access, "/settings/access")]
    [InlineData("apikeys", SettingsSection.Access, "/settings/access")]
    [InlineData("api-keys", SettingsSection.Access, "/settings/access")]
    public void ResolveRoute_LegacyAliases_RedirectToCanonicalRoutes(string alias, SettingsSection expectedSection, string expectedRoute)
    {
        var resolution = SettingsNav.ResolveRoute(alias, "Administrator");

        Assert.Equal(expectedSection, resolution.Section);
        Assert.Equal(expectedRoute, resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.RequestedSectionAllowed);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_DisallowedAdminPage_FallsBackToUserOverview()
    {
        var resolution = SettingsNav.ResolveRoute("providers", "Viewer");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.False(resolution.RequestedSectionAllowed);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void FilteredGroups_NonAdmin_OnlyShowsPublicGroups()
    {
        var groups = SettingsNav.FilteredGroups("Viewer").Select(group => group.Key).ToArray();

        Assert.Equal(["user"], groups);
    }

    [Fact]
    public void FilteredTreeGroups_Admin_RendersUserAndAdminSettingsTree()
    {
        var groups = SettingsNav.FilteredTreeGroups("Administrator").Select(group => group.Key).ToArray();

        Assert.Equal(["user", "admin"], groups);

        var userLabels = SettingsNav.FilteredTreeItems(SettingsNav.TreeGroups.Single(group => group.Key == "user"), "Administrator")
            .Select(item => item.Label)
            .ToArray();

        Assert.Equal(["User Overview", "Playback & Reading", "Display & Personalization", "Privacy & History"], userLabels);

        var adminLabels = SettingsNav.FilteredTreeItems(SettingsNav.TreeGroups.Single(group => group.Key == "admin"), "Administrator")
            .Select(item => item.Label)
            .ToArray();

        Assert.Equal([
            "Admin Overview",
            "Libraries",
            "Ingestion & Tasks",
            "Metadata & Matching",
            "Providers",
            "Local AI",
            "Playback & Delivery",
            "Users & Access",
        ], adminLabels);

        Assert.DoesNotContain("Registry", adminLabels);
        Assert.DoesNotContain("Activity", adminLabels);
        Assert.DoesNotContain("Maintenance", adminLabels);
        Assert.DoesNotContain("Provider Tester", adminLabels);
        Assert.DoesNotContain("Enrichment Tester", adminLabels);
        Assert.DoesNotContain("Setup", adminLabels);
        Assert.DoesNotContain("Wikidata", adminLabels);
        Assert.DoesNotContain("AI Models", adminLabels);
        Assert.DoesNotContain("AI Features", adminLabels);
        Assert.DoesNotContain("AI Vocabulary", adminLabels);
        Assert.DoesNotContain("AI Schedule", adminLabels);
        Assert.DoesNotContain("Encode", adminLabels);
        Assert.DoesNotContain("Offline Variants", adminLabels);
        Assert.DoesNotContain("Security", adminLabels);
        Assert.DoesNotContain("Users", adminLabels);
    }

    [Theory]
    [InlineData("review", SettingsSection.Review, "/settings/review")]
    [InlineData("setup", SettingsSection.Setup, "/settings/setup")]
    [InlineData("provider-tester", SettingsSection.ProviderTester, "/settings/provider-tester")]
    [InlineData("enrichment-tester", SettingsSection.EnrichmentTester, "/settings/enrichment-tester")]
    public void ResolveRoute_HiddenRoutes_StillResolveForAdmins(string segment, SettingsSection expectedSection, string expectedRoute)
    {
        var resolution = SettingsNav.ResolveRoute(segment, "Administrator");

        Assert.Equal(expectedSection, resolution.Section);
        Assert.Equal(expectedRoute, resolution.CanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.RequestedSectionAllowed);
    }

    [Theory]
    [InlineData("admin", SettingsSection.AdminOverview, "/settings/admin")]
    [InlineData("user", SettingsSection.Overview, "/settings")]
    public void GroupDefaults_ResolveToExpectedCanonicalRoutes(string groupKey, SettingsSection expectedSection, string expectedRoute)
    {
        var section = SettingsNav.GetDefaultSection(groupKey);

        Assert.Equal(expectedSection, section);
        Assert.Equal(expectedRoute, SettingsNav.RouteFor(section));
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
