namespace MediaEngine.Api.Tests;

public sealed class IngestionProgressContractTests
{
    [Fact]
    public void BatchItemProgress_ExposesNestedWorkUnitCounts()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "MediaEngine.Application",
            "ReadModels",
            "IngestionBatchItemResponse.cs"));

        Assert.Contains("work_units_total", source);
        Assert.Contains("work_units_completed", source);

        var readServiceSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "MediaEngine.Api",
            "Services",
            "ReadServices",
            "IngestionBatchReadService.cs"));

        Assert.Contains("stage3_enhanced_at", readServiceSource);
        Assert.Contains("person_media_links", readServiceSource);
    }

    [Fact]
    public void BatchProgressEvent_CarriesNestedWorkUnitCounts()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "MediaEngine.Domain",
            "Events",
            "IngestionEvents.cs"));

        Assert.Contains("WorkUnitsTotal", source);
        Assert.Contains("WorkUnitsCompleted", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
