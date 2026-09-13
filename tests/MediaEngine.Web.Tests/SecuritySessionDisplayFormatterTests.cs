using MediaEngine.Contracts.Authentication;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class SecuritySessionDisplayFormatterTests
{
    [Fact]
    public void EdgeOnWindowsUserAgent_UsesFriendlyDeviceAndClientLabels()
    {
        var session = Session(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0",
            "Tuvima Dashboard",
            "Password");

        var display = SecuritySessionDisplayFormatter.Format(session);

        Assert.Equal("Windows PC", display.DeviceName);
        Assert.Equal("Edge on Windows · Password", display.ClientDescription);
    }

    [Fact]
    public void SafariOniPhone_DoesNotInventModelOrLocation()
    {
        var session = Session(
            "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1",
            "Tuvima Dashboard",
            "passkey");

        var display = SecuritySessionDisplayFormatter.Format(session);

        Assert.Equal("iPhone", display.DeviceName);
        Assert.Equal("Safari on iPhone · Passkey", display.ClientDescription);
    }

    [Fact]
    public void StoredFriendlyName_IsPreservedWhenValueIsNotAUserAgent()
    {
        var display = SecuritySessionDisplayFormatter.Format(Session("Living room", "TV app", "Password"));

        Assert.Equal("Living room", display.DeviceName);
        Assert.Equal("TV app · Password", display.ClientDescription);
    }

    [Fact]
    public void UnknownValues_FallBackToBrowser()
    {
        var display = SecuritySessionDisplayFormatter.Format(Session("Unknown device", "Dashboard", ""));

        Assert.Equal("Browser", display.DeviceName);
        Assert.Equal("Browser · Unknown method", display.ClientDescription);
    }

    private static DeviceSessionResponse Session(string deviceName, string client, string method) => new()
    {
        DeviceName = deviceName,
        Client = client,
        AuthenticationMethod = method,
    };
}
