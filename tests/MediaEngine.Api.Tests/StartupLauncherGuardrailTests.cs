namespace MediaEngine.Api.Tests;

public sealed class StartupLauncherGuardrailTests
{
    [Fact]
    public void CombinedLauncher_WaitsForStructuredReadiness()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "tools",
            "Start-TuvimaApp.ps1"));

        Assert.Contains(
            "Invoke-RestMethod -Uri \"http://localhost:61495/health/ready\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("$status.status -in @(\"healthy\", \"degraded\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invoke-RestMethod -Uri \"http://localhost:61495/system/status\"",
            source,
            StringComparison.Ordinal);
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
