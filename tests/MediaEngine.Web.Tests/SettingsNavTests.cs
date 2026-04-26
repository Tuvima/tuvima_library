using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class SettingsNavTests
{
    [Fact]
    public void RouteFor_Overview_UsesBaseSettingsUrl()
    {
        Assert.Equal("/settings", SettingsNav.RouteFor(SettingsSection.Overview));
    }

    [Theory]
    [InlineData(SettingsSection.Review, "/settings/review")]
    [InlineData(SettingsSection.Profile, "/settings/profile")]
    [InlineData(SettingsSection.Playback, "/settings/playback")]
    [InlineData(SettingsSection.Folders, "/settings/folders")]
    [InlineData(SettingsSection.Metadata, "/settings/metadata")]
    [InlineData(SettingsSection.Providers, "/settings/providers")]
    [InlineData(SettingsSection.Wikidata, "/settings/wikidata")]
    [InlineData(SettingsSection.Models, "/settings/models")]
    [InlineData(SettingsSection.Features, "/settings/features")]
    [InlineData(SettingsSection.Vocabulary, "/settings/vocabulary")]
    [InlineData(SettingsSection.Schedule, "/settings/schedule")]
    [InlineData(SettingsSection.System, "/settings/system")]
    [InlineData(SettingsSection.Security, "/settings/security")]
    [InlineData(SettingsSection.Users, "/settings/users")]
    [InlineData(SettingsSection.Activity, "/settings/activity")]
    [InlineData(SettingsSection.Maintenance, "/settings/maintenance")]
    [InlineData(SettingsSection.Setup, "/settings/setup")]
    [InlineData(SettingsSection.Registry, "/settings/registry")]
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
    public void ResolveRoute_BaseSettings_MapsToOverview()
    {
        var resolution = SettingsNav.ResolveRoute(null, "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings", resolution.CanonicalRoute);
        Assert.True(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void ResolveRoute_OverviewSegment_IsNotCanonical()
    {
        var resolution = SettingsNav.ResolveRoute("overview", "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.True(resolution.IsKnownRoute);
        Assert.True(resolution.ShouldRedirect);
    }

    [Theory]
    [InlineData("general", SettingsSection.Profile, "/settings/profile")]
    [InlineData("library", SettingsSection.Folders, "/settings/folders")]
    [InlineData("mediatypes", SettingsSection.Metadata, "/settings/metadata")]
    [InlineData("connections", SettingsSection.Providers, "/settings/providers")]
    [InlineData("provider-priority", SettingsSection.Providers, "/settings/providers")]
    [InlineData("universe", SettingsSection.Wikidata, "/settings/wikidata")]
    [InlineData("ai", SettingsSection.Models, "/settings/models")]
    [InlineData("servergeneral", SettingsSection.System, "/settings/system")]
    [InlineData("apikeys", SettingsSection.Security, "/settings/security")]
    [InlineData("needsreview", SettingsSection.Review, "/settings/review")]
    public void ResolveRoute_LegacyAliases_MapToCanonicalDestinations(string alias, SettingsSection expectedSection, string expectedRoute)
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
    public void ResolveRoute_DisallowedAdminPage_FallsBackToFirstVisiblePage()
    {
        var resolution = SettingsNav.ResolveRoute("providers", "Viewer");

        Assert.Equal(SettingsSection.Review, resolution.Section);
        Assert.Equal("/settings/review", resolution.CanonicalRoute);
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
    public void FilteredTreeGroups_Admin_RendersCentralizedSettingsTree()
    {
        var groups = SettingsNav.FilteredTreeGroups("Administrator").Select(group => group.Key).ToArray();

        Assert.Equal(["user", "admin", "library", "metadata", "providers", "ai", "server", "registry"], groups);
    }

    [Theory]
    [InlineData("admin", SettingsSection.Overview, "/settings")]
    [InlineData("user", SettingsSection.Profile, "/settings/profile")]
    [InlineData("library", SettingsSection.Folders, "/settings/folders")]
    [InlineData("metadata", SettingsSection.Metadata, "/settings/metadata")]
    [InlineData("providers", SettingsSection.Providers, "/settings/providers")]
    [InlineData("ai", SettingsSection.Models, "/settings/models")]
    [InlineData("server", SettingsSection.System, "/settings/system")]
    [InlineData("registry", SettingsSection.Registry, "/settings/registry")]
    public void GroupDefaults_ResolveToExpectedCanonicalRoutes(string groupKey, SettingsSection expectedSection, string expectedRoute)
    {
        var section = SettingsNav.GetDefaultSection(groupKey);

        Assert.Equal(expectedSection, section);
        Assert.Equal(expectedRoute, SettingsNav.RouteFor(section));
    }
}
