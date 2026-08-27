using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class SettingsNavTests
{
    [Fact]
    public void SettingsPage_UsesTheMediaLaneShellInsteadOfAParallelSidebar()
    {
        var settingsSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\Settings.razor"));
        var mediaShellSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaHub\MediaSectionShell.razor"));

        Assert.Contains("<MediaSectionShell Title=\"Settings\"", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AccordionNavigation=\"true\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("settings-mobile-navigation", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<SettingsSubsectionNav", settingsSource, StringComparison.Ordinal);
        Assert.Contains("<SidebarPageHeader", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<SidebarPageShell", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<SidebarNavGroup", settingsSource, StringComparison.Ordinal);
        Assert.Contains("media-section-shell__rail-item--child", mediaShellSource, StringComparison.Ordinal);
        Assert.False(File.Exists(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\Shell\SidebarPageShell.razor")));
        Assert.False(File.Exists(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\Shell\SidebarNavGroup.razor")));
        Assert.False(File.Exists(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\Shell\SidebarNavItem.razor")));
    }

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
        Assert.Contains("<LibrariesTab Subsection=\"@_activeSubsection\" DetailTab=\"@DetailTab\" />", settingsSource, StringComparison.Ordinal);
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
        Assert.Contains("private string _currentRole = \"Administrator\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ShouldDeferForRoleResolution", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly string _currentRole = \"Administrator\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("SetActiveProfileAsync", orchestratorSource, StringComparison.Ordinal);
        Assert.Contains("AuthenticationStateProvider", sessionSource, StringComparison.Ordinal);
        Assert.Contains("tuvima:active_profile_id", sessionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", sessionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteFor_Overview_UsesProfileSettingsUrl()
    {
        Assert.Equal("/settings/profile", SettingsNav.RouteFor(SettingsSection.Overview));
    }

    [Theory]
    [InlineData(SettingsSection.ActivityLogs, "batches", "/settings/activity/batches")]
    [InlineData(SettingsSection.LocalAi, "models", "/settings/ai/models")]
    [InlineData(SettingsSection.Providers, "providers", "/settings/metadata/providers")]
    [InlineData(SettingsSection.Providers, "ingestion-flow", "/settings/metadata/ingestion-flow")]
    public void RouteFor_Subsection_UsesNestedCanonicalUrl(
        SettingsSection section,
        string subsection,
        string expectedRoute)
    {
        Assert.Equal(expectedRoute, SettingsNav.RouteFor(section, subsection));
    }

    [Fact]
    public void LocalAi_Subsections_ReplaceTheFormerTabStrip()
    {
        var labels = SettingsNav.GetSubsections(SettingsSection.LocalAi)
            .Select(item => item.Label)
            .ToArray();

        Assert.Equal([
            "Models & Runtime",
            "Vocabulary",
            "Automation",
        ], labels);
        Assert.Equal("models", SettingsNav.GetDefaultSubsection(SettingsSection.LocalAi).Slug);
        Assert.Null(SettingsNav.ResolveSubsection(SettingsSection.LocalAi, "unknown"));
    }

    [Fact]
    public void MetadataManagement_UsesTwoClearSubsections()
    {
        var subsections = SettingsNav.GetSubsections(SettingsSection.Providers).ToArray();

        Assert.Equal(["Providers", "Ingestion Flow"], subsections.Select(item => item.Label));
        Assert.Equal(["providers", "ingestion-flow"], subsections.Select(item => item.Slug));
    }

    [Fact]
    public void SummaryFirstSections_UseRootRoutesWithoutRequiringSubsections()
    {
        Assert.Empty(SettingsNav.GetSubsections(SettingsSection.Overview));
        Assert.Empty(SettingsNav.GetSubsections(SettingsSection.Playback));
        Assert.Empty(SettingsNav.GetSubsections(SettingsSection.AdminOverview));
        Assert.Empty(SettingsNav.GetSubsections(SettingsSection.Review));
        Assert.Empty(SettingsNav.GetSubsections(SettingsSection.Server));
    }

    [Theory]
    [InlineData(SettingsSection.AdminOverview, "/settings/system")]
    [InlineData(SettingsSection.Playback, "/settings/playback")]
    [InlineData(SettingsSection.Libraries, "/settings/libraries")]
    [InlineData(SettingsSection.ImportFolders, "/settings/import-folders")]
    [InlineData(SettingsSection.Ingestion, "/settings/ingestion")]
    [InlineData(SettingsSection.DevHarness, "/settings/developer/options")]
    [InlineData(SettingsSection.Providers, "/settings/metadata/providers")]
    [InlineData(SettingsSection.LocalAi, "/settings/ai")]
    [InlineData(SettingsSection.Plugins, "/settings/plugins")]
    [InlineData(SettingsSection.Delivery, "/settings/delivery")]
    [InlineData(SettingsSection.Access, "/settings/access")]
    [InlineData(SettingsSection.Server, "/settings/backup-recovery")]
    [InlineData(SettingsSection.ActivityLogs, "/settings/activity")]
    [InlineData(SettingsSection.Review, "/settings/review")]
    [InlineData(SettingsSection.ProviderTester, "/settings/provider-tester")]
    [InlineData(SettingsSection.EnrichmentTester, "/settings/enrichment-tester")]
    public void ResolveRoute_CanonicalSegments_AreStable(SettingsSection section, string expectedRoute)
    {
        var segment = expectedRoute.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
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
        Assert.Equal("/settings/profile", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_RemovedAdminAlias_IsUnknown()
    {
        var resolution = SettingsNav.ResolveRoute("admin", "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/not-found", resolution.CanonicalRoute);
        Assert.False(resolution.IsKnownRoute);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_ProfileSegment_MapsToProfile()
    {
        var resolution = SettingsNav.ResolveRoute("profile", "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings/profile", resolution.CanonicalRoute);
        Assert.True(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.RequestedSectionAllowed);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_DisplaySegment_IsUnknownAndRoutesToNotFound()
    {
        var resolution = SettingsNav.ResolveRoute("display", "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/not-found", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.False(resolution.IsKnownRoute);
        Assert.False(resolution.RequestedSectionAllowed);
        Assert.True(resolution.ShouldRedirect);
    }

    [Theory]
    [InlineData("models")]
    [InlineData("features")]
    [InlineData("encode")]
    [InlineData("users")]
    [InlineData("security")]
    [InlineData("api-keys")]
    public void ResolveRoute_RemovedAliasesAreUnknown(string alias)
    {
        var resolution = SettingsNav.ResolveRoute(alias, "Administrator");

        Assert.Equal("/not-found", resolution.CanonicalRoute);
        Assert.False(resolution.IsKnownRoute);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_DisallowedAdminPage_FallsBackToUserOverview()
    {
        var resolution = SettingsNav.ResolveRoute("metadata", "Viewer");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings/profile", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.False(resolution.RequestedSectionAllowed);
        Assert.True(resolution.ShouldRedirect);
    }

    [Theory]
    [InlineData("providers")]
    [InlineData("wikidata")]
    public void ResolveRoute_RemovedMetadataAliases_AreUnknown(string segment)
    {
        var resolution = SettingsNav.ResolveRoute(segment, "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/not-found", resolution.CanonicalRoute);
        Assert.False(resolution.IsKnownRoute);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void FilteredGroups_NonAdmin_OnlyShowsPublicGroups()
    {
        var groups = SettingsNav.FilteredGroups("Viewer").Select(group => group.Key).ToArray();

        Assert.Equal(["personal"], groups);
    }

    [Fact]
    public void FilteredTreeGroups_Admin_RendersUserAndAdminSettingsTree()
    {
        var groups = SettingsNav.FilteredTreeGroups("Administrator").Select(group => group.Key).ToArray();

        Assert.Equal(["personal", "administration", "advanced"], groups);

        var userLabels = SettingsNav.FilteredTreeItems(SettingsNav.TreeGroups.Single(group => group.Key == "personal"), "Administrator")
            .Select(item => item.Label)
            .ToArray();

        Assert.Equal(["Profile", "Playback & Reading"], userLabels);

        var adminLabels = SettingsNav.FilteredTreeItems(SettingsNav.TreeGroups.Single(group => group.Key == "administration"), "Administrator")
            .Select(item => item.Label)
            .ToArray();

        Assert.Equal([
            "System Overview",
            "Libraries",
            "Import Folders",
            "Ingestion",
            "Metadata",
            "Needs Review",
            "Activity & Audit",
            "Network & Remote Access",
            "Playback & Delivery",
            "Users & Access",
            "Backup & Recovery",
        ], adminLabels);

        var adminGroup = SettingsNav.TreeGroups.Single(group => group.Key == "administration");
        var childGroups = SettingsNav.FilteredChildTreeGroups(adminGroup, "Administrator").Select(group => group.Key).ToArray();

        Assert.Empty(childGroups);
        Assert.DoesNotContain(SettingsNav.TreeGroups, group => string.Equals(group.Key, "library-operations", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(SettingsNav.AllGroups, group => string.Equals(group.Label, "Library Operations", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("Reg" + "istry", adminLabels);
        Assert.DoesNotContain("Maintenance", adminLabels);
        Assert.DoesNotContain("Provider Tester", adminLabels);
        Assert.DoesNotContain("Enrichment Tester", adminLabels);
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
    [InlineData("activity", SettingsSection.ActivityLogs, "/settings/activity")]
    [InlineData("provider-tester", SettingsSection.ProviderTester, "/settings/provider-tester")]
    [InlineData("enrichment-tester", SettingsSection.EnrichmentTester, "/settings/enrichment-tester")]
    public void ResolveRoute_SecondaryRoutes_StillResolveForAdmins(string segment, SettingsSection expectedSection, string expectedRoute)
    {
        var resolution = SettingsNav.ResolveRoute(segment, "Administrator");

        Assert.Equal(expectedSection, resolution.Section);
        Assert.Equal(expectedRoute, resolution.CanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.RequestedSectionAllowed);
    }

    [Theory]
    [InlineData("administration", SettingsSection.AdminOverview, "/settings/system")]
    [InlineData("personal", SettingsSection.Overview, "/settings/profile")]
    public void GroupDefaults_ResolveToExpectedCanonicalRoutes(string groupKey, SettingsSection expectedSection, string expectedRoute)
    {
        var section = SettingsNav.GetDefaultSection(groupKey);

        Assert.Equal(expectedSection, section);
        Assert.Equal(expectedRoute, SettingsNav.RouteFor(section));
    }

    [Fact]
    public void LibrariesRoute_IsCanonicalAndFoldersAliasRemainsRemoved()
    {
        var libraries = SettingsNav.ResolveRoute("libraries", "Administrator");
        Assert.True(libraries.IsKnownRoute);
        Assert.Equal(SettingsSection.Libraries, libraries.Section);
        Assert.Equal("/settings/libraries", libraries.CanonicalRoute);

        var folders = SettingsNav.ResolveRoute("folders", "Administrator");
        Assert.False(folders.IsKnownRoute);
        Assert.Equal("/not-found", folders.CanonicalRoute);
    }

    [Fact]
    public void IngestionRoute_IsCanonical()
    {
        var resolution = SettingsNav.ResolveRoute("ingestion", "Administrator");

        Assert.True(resolution.IsKnownRoute);
        Assert.Equal(SettingsSection.Ingestion, resolution.Section);
        Assert.Equal("/settings/ingestion", resolution.CanonicalRoute);
        Assert.Contains(SettingsNav.AllItems, item => item.Label == "Ingestion");
    }

    [Fact]
    public void Curator_SeesPersonalReviewAndAuditButNotServerAdministration()
    {
        var visible = SettingsNav.TreeGroups
            .SelectMany(group => SettingsNav.FilteredTreeItems(group, "StandardUser"))
            .Select(item => item.Value)
            .ToArray();

        Assert.Contains(SettingsSection.Overview, visible);
        Assert.Contains(SettingsSection.Playback, visible);
        Assert.Contains(SettingsSection.Review, visible);
        Assert.Contains(SettingsSection.ActivityLogs, visible);
        Assert.DoesNotContain(SettingsSection.Server, visible);
        Assert.DoesNotContain(SettingsSection.Providers, visible);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
