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

    [Fact]
    public void IdentityStartupRecovery_RunsBeforeIdentityWorkersCanLeaseJobs()
    {
        var repoRoot = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Api", "Program.cs"));
        var recoverySource = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Api", "Services", "HydrationStartupSweepService.cs"));

        var recoveryRegistration = program.IndexOf("AddHostedService<HydrationStartupSweepService>", StringComparison.Ordinal);
        var retailRegistration = program.IndexOf("AddHostedService<MediaEngine.Api.Services.RetailMatchHostedService>", StringComparison.Ordinal);
        var bridgeRegistration = program.IndexOf("AddHostedService<MediaEngine.Api.Services.WikidataBridgeHostedService>", StringComparison.Ordinal);
        var hydrationRegistration = program.IndexOf("AddHostedService<MediaEngine.Api.Services.QuickHydrationHostedService>", StringComparison.Ordinal);

        Assert.True(recoveryRegistration >= 0);
        Assert.True(recoveryRegistration < retailRegistration);
        Assert.True(recoveryRegistration < bridgeRegistration);
        Assert.True(recoveryRegistration < hydrationRegistration);
        Assert.Contains("public override async Task StartAsync", recoverySource, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedJobsAsync(cancellationToken)", recoverySource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", recoverySource, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
