namespace MediaEngine.Api.Tests;

public sealed class IngestionOperationsContractTests
{
    [Fact]
    public void IngestionEndpoints_ExposeLibraryOperationsSnapshot()
    {
        var repoRoot = FindRepoRoot();
        var endpointSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "MediaEngine.Api",
            "Endpoints",
            "IngestionEndpoints.cs"));

        Assert.Contains("/operations", endpointSource, StringComparison.Ordinal);
        Assert.Contains("IIngestionOperationsStatusService", endpointSource, StringComparison.Ordinal);
        Assert.Contains("GetIngestionOperationsSnapshot", endpointSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsService_UsesRealRepositoriesAndConfiguration()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "MediaEngine.Api",
            "Services",
            "IngestionOperationsStatusService.cs"));

        Assert.Contains("IDatabaseConnection", source, StringComparison.Ordinal);
        Assert.Contains("IProviderHealthRepository", source, StringComparison.Ordinal);
        Assert.Contains("IIngestionBatchRepository", source, StringComparison.Ordinal);
        Assert.Contains("ILibraryItemRepository", source, StringComparison.Ordinal);
        Assert.Contains("LoadLibraries", source, StringComparison.Ordinal);
        Assert.Contains("LoadAllProviders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dune Messiah", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsService_ReportsPipelineStagesAsFileCounts()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "MediaEngine.Api",
            "Services",
            "IngestionOperationsStatusService.cs"));

        Assert.Contains("CurrentActivities", source, StringComparison.Ordinal);
        Assert.Contains("EnrichmentCompleteStates", source, StringComparison.Ordinal);
        Assert.DoesNotContain("projection.EnrichedStage3", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnrichedStates", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
