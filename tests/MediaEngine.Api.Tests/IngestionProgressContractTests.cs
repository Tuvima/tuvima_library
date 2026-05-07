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
            "MediaEngine.Api",
            "Endpoints",
            "IngestionEndpoints.cs"));

        Assert.Contains("work_units_total", source);
        Assert.Contains("work_units_completed", source);
        Assert.Contains("stage3_enhanced_at", source);
        Assert.Contains("person_media_links", source);
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
