using System.Net;
using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Services.Integration;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Web.Tests;

public sealed class OriginalClientContextTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.1.25", false)]
    [InlineData("10.0.0.25", false)]
    [InlineData("203.0.113.20", false)]
    public void LocalEntry_UsesProcessedRemoteAddressOnly(string address, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.Equal(expected, DashboardAuthenticationEndpoints.IsLocalClient(context, new AuthSettings()));
    }

    [Fact]
    public void ExplicitTrustedLocalNetwork_AllowsOnlyConfiguredRange()
    {
        var policy = new AuthSettings { TrustedLocalNetworks = ["192.168.40.0/24"] };
        var trusted = new DefaultHttpContext();
        trusted.Connection.RemoteIpAddress = IPAddress.Parse("192.168.40.25");
        var otherPrivate = new DefaultHttpContext();
        otherPrivate.Connection.RemoteIpAddress = IPAddress.Parse("192.168.41.25");

        Assert.True(DashboardAuthenticationEndpoints.IsLocalClient(trusted, policy));
        Assert.False(DashboardAuthenticationEndpoints.IsLocalClient(otherPrivate, policy));
    }

    [Fact]
    public void BrowserHeaders_CannotAssertLocalEntry()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.20");
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        context.Request.Headers["X-Tuvima-Original-Client-Is-Local"] = "true";

        Assert.False(DashboardAuthenticationEndpoints.IsLocalClient(context, new AuthSettings()));
    }

    [Fact]
    public void MissingRemoteAddress_FailsClosed()
    {
        Assert.False(DashboardAuthenticationEndpoints.IsLocalClient(new DefaultHttpContext(), new AuthSettings()));
    }

    [Fact]
    public void LocalProfileSignIn_DoesNotRequireAConfiguredPin()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "MediaEngine.Web", "Services", "Integration", "DashboardAuthenticationEndpoints.cs"));

        Assert.Contains("PIN (if configured)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"pin\" required", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
