namespace MediaEngine.Web.Tests;

public sealed class DashboardFirstRunExperienceTests
{
    [Fact]
    public void FirstRunSetup_RequiresTheContainerLogClaimToken()
    {
        var dashboard = Read("src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs");
        var setup = Read("src/MediaEngine.Web/Components/Pages/SetupPage.razor");
        var engine = Read("src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs");
        var claims = Read("src/MediaEngine.Api/Services/SetupClaimService.cs");

        Assert.Contains("Results.Redirect(\"/setup\")", dashboard, StringComparison.Ordinal);
        Assert.Contains("Claim this server", setup, StringComparison.Ordinal);
        Assert.Contains("container logs", setup, StringComparison.Ordinal);
        Assert.Contains("X-Tuvima-Setup-Session", claims, StringComparison.Ordinal);
        Assert.Contains("Console.Out.WriteLineAsync", claims, StringComparison.Ordinal);
        Assert.DoesNotContain("/bootstrap/administrator", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLoopbackRequest", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void AnonymousPasswordReset_RequiresRecoveryCodeOrElevatedHostCommand()
    {
        var dashboard = Read("src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs");
        var client = Read("src/MediaEngine.Web/Services/Integration/DashboardIdentityClient.cs");
        var engine = Read("src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs");
        var identity = Read("src/MediaEngine.Identity/FirstPartyIdentityService.cs");
        var publicIdentityContract = Read("src/MediaEngine.Identity/Contracts/IFirstPartyIdentityService.cs");
        var hostRecoveryContract = Read("src/MediaEngine.Identity/Contracts/IHostAdministratorRecoveryService.cs");

        Assert.DoesNotContain("reset-local-administrator", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetLocalAdministratorPassword", client, StringComparison.Ordinal);
        Assert.DoesNotContain("/auth/password/local-administrator-reset", client, StringComparison.Ordinal);
        Assert.DoesNotContain("local-administrator-reset", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetAdministratorPasswordFromHostAsync", publicIdentityContract, StringComparison.Ordinal);
        Assert.Contains("tuvima-admin auth reset-password", dashboard, StringComparison.Ordinal);
        Assert.Contains("Use one of the one-time recovery codes", dashboard, StringComparison.Ordinal);
        Assert.Contains("Reset with recovery code", dashboard, StringComparison.Ordinal);
        Assert.Contains("ResetAdministratorPasswordFromHostAsync", hostRecoveryContract, StringComparison.Ordinal);
        Assert.Contains("ProfileRole.Administrator", identity, StringComparison.Ordinal);
        Assert.Contains("host_administrator_password_reset", identity, StringComparison.Ordinal);
        Assert.Contains("RevokeProfileSessionsAsync", identity, StringComparison.Ordinal);
        Assert.Contains("ReplaceRecoveryCodesAsync", identity, StringComparison.Ordinal);
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
