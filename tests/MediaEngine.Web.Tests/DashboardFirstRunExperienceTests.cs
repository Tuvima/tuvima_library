namespace MediaEngine.Web.Tests;

public sealed class DashboardFirstRunExperienceTests
{
    [Fact]
    public void FirstRunSetup_IsLocalOnlyAndDoesNotRequireAClaimCode()
    {
        var dashboard = Read("src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs");
        var client = Read("src/MediaEngine.Web/Services/Integration/DashboardIdentityClient.cs");
        var engine = Read("src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs");
        var program = Read("src/MediaEngine.Api/Program.cs");

        Assert.Contains("IsLoopbackRequest(context)", dashboard, StringComparison.Ordinal);
        Assert.Contains("IPAddress.IsLoopback(address)", dashboard, StringComparison.Ordinal);
        Assert.Contains("available only from localhost", dashboard, StringComparison.Ordinal);
        Assert.Contains("Create a local Tuvima user for this library", dashboard, StringComparison.Ordinal);
        Assert.Contains("value=\"Administrator\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("No external account or claim code is required", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"setupCode\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Tuvima-Bootstrap-Code", client, StringComparison.Ordinal);
        Assert.DoesNotContain("BootstrapClaimService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("BootstrapClaimService", program, StringComparison.Ordinal);
        Assert.DoesNotContain("administrator claim code", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalAdministratorPasswordReset_IsHostOnlyAndInvalidatesExistingAccess()
    {
        var dashboard = Read("src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs");
        var client = Read("src/MediaEngine.Web/Services/Integration/DashboardIdentityClient.cs");
        var engine = Read("src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs");
        var identity = Read("src/MediaEngine.Identity/FirstPartyIdentityService.cs");

        Assert.Contains("reset-local-administrator", dashboard, StringComparison.Ordinal);
        Assert.Contains("IsLoopbackRequest(context)", dashboard, StringComparison.Ordinal);
        Assert.Contains("ResetLocalAdministratorPasswordAsync", client, StringComparison.Ordinal);
        Assert.Contains("/auth/password/local-administrator-reset", client, StringComparison.Ordinal);
        Assert.Contains("ResetLocalAdministratorPassword", engine, StringComparison.Ordinal);
        Assert.Contains("ProfileRole.Administrator", identity, StringComparison.Ordinal);
        Assert.Contains("local_administrator_password_reset", identity, StringComparison.Ordinal);
        Assert.Contains("RevokeProfileSessionsAsync", identity, StringComparison.Ordinal);
        Assert.Contains("ReplaceRecoveryCodesAsync", identity, StringComparison.Ordinal);
        Assert.Contains("This signs out every device", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationShell_HasExplicitAccessibleDarkThemeColors()
    {
        var dashboard = Read("src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs");

        Assert.Contains(":root { color-scheme: dark;", dashboard, StringComparison.Ordinal);
        Assert.Contains("body { margin: 0;", dashboard, StringComparison.Ordinal);
        Assert.Contains("background: #0e0a16; color: #ffffff", dashboard, StringComparison.Ordinal);
        Assert.Contains("label { display: grid;", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>color-scheme:dark;body", dashboard, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
