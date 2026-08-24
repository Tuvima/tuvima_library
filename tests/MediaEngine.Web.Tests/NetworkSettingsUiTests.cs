using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class NetworkSettingsUiTests
{
    [Fact]
    public void NetworkNavigationUsesCanonicalFiveSectionRoutes()
    {
        var subsections = SettingsNav.GetSubsections(SettingsSection.Network).ToArray();

        Assert.Equal(["overview", "local", "remote", "streaming", "advanced"], subsections.Select(item => item.Slug));
        Assert.Equal("/settings/network/overview", SettingsNav.RouteFor(SettingsSection.Network));
        Assert.Equal("/settings/network/remote", SettingsNav.RouteFor(SettingsSection.Network, "remote"));
    }

    [Fact]
    public void SettingsAndFirstRunShareTheSameNetworkPanels()
    {
        var settings = Read(@"src\MediaEngine.Web\Components\Settings\NetworkRemoteAccessSettings.razor");
        var setup = Read(@"src\MediaEngine.Web\Components\Settings\NetworkSetupWizard.razor");

        foreach (var panel in new[] { "LocalNetworkSettingsPanel", "RemoteAccessSettingsPanel", "NetworkStreamingSettingsPanel" })
        {
            Assert.Contains(panel, settings, StringComparison.Ordinal);
            Assert.Contains(panel, setup, StringComparison.Ordinal);
        }

        Assert.Contains("SetupCompleted = true", setup, StringComparison.Ordinal);
        Assert.Contains("Remote.Enabled = false", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteAccessDoesNotPretendAnUnavailableProviderExists()
    {
        var source = Read(@"src\MediaEngine.Web\Components\Settings\RemoteAccessSettingsPanel.razor");

        Assert.Contains("No provider installed", source, StringComparison.Ordinal);
        Assert.Contains("secure tunnel or relay provider.\", true)", source, StringComparison.Ordinal);
        Assert.Contains("TestRemoteNetworkAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardHonorsForwardedHeadersOnlyFromConfiguredProxyAddresses()
    {
        var source = Read(@"src\MediaEngine.Web\Program.cs");

        Assert.Contains("networkSettings.Remote.TrustedProxies.Count > 0", source, StringComparison.Ordinal);
        Assert.Contains("options.KnownProxies.Add(proxy)", source, StringComparison.Ordinal);
        Assert.Contains("options.ForwardLimit = 1", source, StringComparison.Ordinal);
        Assert.Contains("app.UseForwardedHeaders()", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath)));
}
