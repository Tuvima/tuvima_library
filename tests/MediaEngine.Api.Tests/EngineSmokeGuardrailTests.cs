namespace MediaEngine.Api.Tests;

public sealed class EngineSmokeGuardrailTests
{
    [Fact]
    public void EngineProgram_RegistersHealthChecksAndEndpointGroups()
    {
        var repoRoot = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Api", "Program.cs"));

        Assert.Contains("AddHealthChecks", program);
        Assert.Contains("SqliteHealthCheck", program);
        Assert.Contains("MapHealthChecks(\"/health\")", program);
        Assert.Contains("MapEngineEndpoints()", program);
    }

    [Fact]
    public void EngineStartup_AllowsTestConfigToBypassHeavyAiModelDownloads()
    {
        var repoRoot = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Api", "Program.cs"));

        Assert.Contains("TUVIMA_DB_PATH", program);
        Assert.Contains("TUVIMA_CONFIG_DIR", program);
        Assert.Contains("TUVIMA_LIBRARY_ROOT", program);
        Assert.DoesNotContain("DownloadModel", program, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
