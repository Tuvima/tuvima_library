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
    [InlineData(SettingsSection.AdminOverview, "/settings/admin")]
    [InlineData(SettingsSection.Review, "/settings/review")]
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
    [InlineData("general")]
    [InlineData("library")]
    [InlineData("mediatypes")]
    [InlineData("connections")]
    [InlineData("provider-priority")]
    [InlineData("universe")]
    [InlineData("ai")]
    [InlineData("servergeneral")]
    [InlineData("apikeys")]
    [InlineData("needsreview")]
    public void ResolveRoute_LegacyAliases_AreUnknown(string alias)
    {
        var resolution = SettingsNav.ResolveRoute(alias, "Administrator");

        Assert.Equal(SettingsSection.Overview, resolution.Section);
        Assert.Equal("/settings", resolution.CanonicalRoute);
        Assert.False(resolution.IsCanonicalRoute);
        Assert.False(resolution.IsKnownRoute);
        Assert.False(resolution.RequestedSectionAllowed);
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

        var adminItems = SettingsNav.FilteredTreeItems(SettingsNav.TreeGroups.Single(group => group.Key == "admin"), "Administrator")
            .Select(item => item.Value)
            .ToArray();

        Assert.Contains(SettingsSection.AdminOverview, adminItems);
        Assert.Contains(SettingsSection.Folders, adminItems);
        Assert.Contains(SettingsSection.Metadata, adminItems);
        Assert.Contains(SettingsSection.Providers, adminItems);
        Assert.Contains(SettingsSection.Models, adminItems);
        Assert.Contains(SettingsSection.System, adminItems);
        Assert.DoesNotContain(SettingsSection.Overview, adminItems);
        var removedSectionName = "Reg" + "istry";
        Assert.DoesNotContain(adminItems, section => section.ToString().Equals(removedSectionName, StringComparison.OrdinalIgnoreCase));
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
}
