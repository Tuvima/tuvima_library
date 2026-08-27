using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class LaunchFeatureContractTests : IDisposable
{
    public LaunchFeatureContractTests() => SettingsNav.ConfigureEnvironment(productionMode: false);

    public void Dispose() => SettingsNav.ConfigureEnvironment(productionMode: false);

    [Fact]
    public void EveryLiveSettingsSurfaceMeetsTheFivePartLaunchContract()
    {
        var incompleteLive = SettingsNav.AllItems
            .Where(item => item.Status == SettingsStatusKind.Live)
            .Where(item => !SettingsNav.MeetsLaunchContract(item.Value))
            .Select(item => item.Label)
            .ToArray();

        Assert.Empty(incompleteLive);
    }

    [Fact]
    public void ProductionHidesPartialExperimentalPlannedAndPlaceholderSurfaces()
    {
        SettingsNav.ConfigureEnvironment(productionMode: true);

        Assert.True(SettingsNav.IsVisible(SettingsSection.AdminOverview, "Administrator"));
        Assert.False(SettingsNav.IsVisible(SettingsSection.Delivery, "Administrator"));
        Assert.False(SettingsNav.IsVisible(SettingsSection.Access, "Administrator"));
        Assert.False(SettingsNav.IsVisible(SettingsSection.Plugins, "Administrator"));
        Assert.False(SettingsNav.IsVisible(SettingsSection.DevHarness, "Administrator"));
        Assert.False(SettingsNav.IsVisible(SettingsSection.ProviderTester, "Administrator"));
        Assert.False(SettingsNav.IsVisible(SettingsSection.Privacy, "Administrator"));

        var directRoute = SettingsNav.ResolveRoute("delivery", "Administrator");
        Assert.False(directRoute.RequestedSectionAllowed);
        Assert.NotEqual(SettingsSection.Delivery, directRoute.Section);
    }
}
