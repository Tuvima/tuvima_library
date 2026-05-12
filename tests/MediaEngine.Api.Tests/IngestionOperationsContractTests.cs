using System.Reflection;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Entities;

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
        Assert.Contains("BuildPipelineStages(batchPipelineRows, batchIngestionRows", source, StringComparison.Ordinal);
        Assert.Contains("ToActiveJob(batch, batchPipelineRows, batchStages)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessedCount = batch.FilesProcessed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("projection.EnrichedStage3", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnrichedStates", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsSnapshot_ExposesBatchAwareCurrentActivityContract()
    {
        var dtoSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "MediaEngine.Api",
            "Models",
            "IngestionOperationsDtos.cs"));
        var serviceSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "MediaEngine.Api",
            "Services",
            "IngestionOperationsStatusService.cs"));

        Assert.Contains("current_batch", dtoSource, StringComparison.Ordinal);
        Assert.Contains("sample_items", dtoSource, StringComparison.Ordinal);
        Assert.Contains("queued_count", dtoSource, StringComparison.Ordinal);
        Assert.Contains("IngestionActivityBatchDto", dtoSource, StringComparison.Ordinal);
        Assert.Contains("ActivityBatchSize = 50", serviceSource, StringComparison.Ordinal);
        Assert.Contains("Linking Wikidata QIDs", serviceSource, StringComparison.Ordinal);
        Assert.Contains("BuildCurrentBatch", serviceSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Wikidata matching", "wikidata")]
    [InlineData("BridgeSearching", "wikidata")]
    [InlineData("Retail identification", "retail")]
    [InlineData("RetailSearching", "retail")]
    [InlineData("UniverseEnriching", "enrichment")]
    public void OperationsService_MapsActivityLabelsToCorrectProgressStage(string label, string expectedKey)
    {
        var method = typeof(IngestionOperationsStatusService).GetMethod(
            "ResolveActivityStageKey",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var actual = Assert.IsType<string>(method.Invoke(null, [label]));
        Assert.Equal(expectedKey, actual);
    }

    [Fact]
    public void OperationsService_DoesNotReportRetailCompleteAsActiveStageWhenWikidataIsPending()
    {
        var method = typeof(IngestionOperationsStatusService).GetMethod(
            "ResolveBatchStage",
            BindingFlags.Static | BindingFlags.NonPublic);

        var batch = new IngestionBatch
        {
            FilesTotal = 43,
            FilesProcessed = 43,
            FilesIdentified = 28,
            FilesReview = 14,
            Status = "running",
        };
        var pipelineRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RetailMatched"] = 31,
            ["RetailMatchedNeedsReview"] = 11,
        };
        var stages = new List<IngestionPipelineStageDto>
        {
            Stage("detected", 43, 43),
            Stage("matched", 29, 43),
            Stage("retail_review", 13, 43),
            Stage("duplicate", 1, 43),
            Stage("canonicalized", 0, 31),
            Stage("wikidata_review", 0, 31),
            Stage("enriched", 0, 31),
        };

        Assert.NotNull(method);
        var actual = Assert.IsType<string>(method.Invoke(null, [batch, pipelineRows, stages]));
        Assert.Equal("Wikidata matching", actual);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    private static IngestionPipelineStageDto Stage(string key, int count, int total) => new()
    {
        Key = key,
        Count = count,
        TotalCount = total,
    };
}
