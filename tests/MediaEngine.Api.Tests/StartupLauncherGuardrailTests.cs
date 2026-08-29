namespace MediaEngine.Api.Tests;

public sealed class StartupLauncherGuardrailTests
{
    [Fact]
    public void CombinedLauncher_UsesPublicLivenessWithoutBypassingReadinessSecurity()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "tools",
            "Start-TuvimaApp.ps1"));

        Assert.Contains(
            "Invoke-RestMethod -Uri \"http://localhost:61495/health/live\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("if ([string]$status -eq \"Healthy\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invoke-RestMethod -Uri \"http://localhost:61495/health/ready\"",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invoke-RestMethod -Uri \"http://localhost:61495/system/status\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CombinedLauncher_StartsAndVerifiesDashboardBeforeOpeningIt()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "tools",
            "Start-TuvimaApp.ps1"));

        Assert.Contains("Start-Process -FilePath \"dotnet\"", source, StringComparison.Ordinal);
        Assert.Contains("Wait-ForDashboard -Process $dashboardProcess", source, StringComparison.Ordinal);
        Assert.Contains(
            "Invoke-RestMethod -Uri \"http://localhost:5016/health/ready\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Start-Process \"http://localhost:5016\"", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
