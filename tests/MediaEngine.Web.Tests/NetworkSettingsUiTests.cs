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
    public void IncompleteNetworkWizardDoesNotGateStartupUntilTheFeatureIsEnabled()
    {
        var layout = Read(@"src\MediaEngine.Web\Shared\MainLayout.razor");
        var appSettings = Read(@"src\MediaEngine.Web\appsettings.json");

        Assert.Contains("Features:NetworkSetupWizardEnabled", layout, StringComparison.Ordinal);
        Assert.Contains("networkSetupWizardEnabled", layout, StringComparison.Ordinal);
        Assert.Contains("\"NetworkSetupWizardEnabled\": false", appSettings, StringComparison.Ordinal);
        Assert.Contains("networkSettings is { SetupCompleted: false }", layout, StringComparison.Ordinal);
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
    public void ConnectionTestDialogOwnsItsPortalRenderedLayoutAndCannotOverflowHorizontally()
    {
        var dialog = Read(@"src\MediaEngine.Web\Components\Settings\NetworkTestDialog.razor");
        var dialogStyles = Read(@"src\MediaEngine.Web\Components\Settings\NetworkTestDialog.razor.css");
        var dialogHost = Read(@"src\MediaEngine.Web\Components\Shared\AppDialog.razor");
        var shellStyles = Read(@"src\MediaEngine.Web\Components\Shared\AppDialogShell.razor.css");
        var appStyles = Read(@"src\MediaEngine.Web\wwwroot\app.css");
        var settingsStyles = Read(@"src\MediaEngine.Web\Components\Settings\NetworkRemoteAccessSettings.razor.css");

        Assert.Contains("network-test-dialog__content", dialog, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: auto minmax(0, 1fr)", dialogStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", dialogStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("network-test-dialog__check", settingsStyles, StringComparison.Ordinal);
        Assert.Contains("width: min(100%, 760px)", shellStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", shellStyles, StringComparison.Ordinal);
        Assert.Contains("app-dialog-host", dialogHost, StringComparison.Ordinal);
        Assert.Contains(".mud-dialog.app-dialog-host > .mud-dialog-content", appStyles, StringComparison.Ordinal);
        Assert.Contains("background: transparent !important", appStyles, StringComparison.Ordinal);
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
