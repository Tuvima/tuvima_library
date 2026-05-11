using Bunit;
using MediaEngine.Web.Components.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class IngestionOperationsPageGuardrailTests
{
    [Fact]
    public void IngestionTab_UsesCentralLiveDashboardStateAndComponents()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\IngestionTasksTab.razor"));

        Assert.Contains("IngestionLiveDashboardState", source, StringComparison.Ordinal);
        Assert.Contains("<IngestionLiveDashboard", source, StringComparison.Ordinal);
        Assert.Contains("<IngestionDiagnosticsPanels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIngestionOperationsSnapshotAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StateContainer.BatchProgress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly CurrentRun _currentRun", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboardState_MapsConnectionStateToLiveBadgeModes()
    {
        Assert.Equal(IngestionLiveMode.Live, IngestionLiveDashboardState.ResolveLiveMode(EngineConnectionState.Online));
        Assert.Equal(IngestionLiveMode.Reconnecting, IngestionLiveDashboardState.ResolveLiveMode(EngineConnectionState.LiveUpdatesDisconnected));
        Assert.Equal(IngestionLiveMode.Polling, IngestionLiveDashboardState.ResolveLiveMode(EngineConnectionState.Offline));
    }

    [Fact]
    public void LiveDashboardState_MergesBatchProgressIntoActiveJobs()
    {
        var state = new UniverseStateContainer();
        var batchId = Guid.NewGuid();
        state.PushBatchProgress(new BatchProgressEvent(
            batchId,
            FilesTotal: 20,
            FilesProcessed: 7,
            FilesIdentified: 4,
            FilesReview: 1,
            FilesNoMatch: 0,
            FilesFailed: 0,
            ProgressPercent: 35,
            EstimatedSecondsRemaining: 60,
            IsComplete: false,
            RecentTitles: ["Dune Part One"],
            CurrentStage: "Matching metadata",
            FilesActive: 2,
            CurrentFileTitle: "Dune Part One"));

        var jobs = IngestionLiveDashboardState.BuildActiveJobs(new IngestionOperationsSnapshotViewModel(), state);

        var job = Assert.Single(jobs);
        Assert.Equal(batchId, job.JobId);
        Assert.Equal("Dune Part One", job.CurrentItem);
        Assert.Equal(35, job.PercentComplete);
    }

    [Fact]
    public void LiveDashboardState_MapsStagesAndMetrics()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 100,
                RegisteredItems = 40,
                ItemsNeedingReview = 3,
            },
            PipelineStages =
            [
                new() { Key = "matched", Count = 35 },
                new() { Key = "registered", Count = 40 },
            ],
        };
        var jobs = new List<IngestionOperationsJobViewModel>
        {
            new()
            {
                CurrentStage = "Enrichment",
                TotalCount = 100,
                ProcessedCount = 42,
                PercentComplete = 42,
                Status = "running",
            },
        };

        var metrics = IngestionLiveDashboardState.BuildMetrics(snapshot, jobs);
        var stages = IngestionLiveDashboardState.BuildStages(snapshot, jobs, metrics.TotalFiles);

        Assert.Equal(100, metrics.TotalFiles);
        Assert.Equal(42, metrics.ProcessedFiles);
        Assert.Equal(1, metrics.ActiveFiles);
        Assert.Contains(stages, stage => stage.Key == "enrichment" && stage.StatusKey == "Ingestion_StatusActive");
        Assert.Contains(stages, stage => stage.Key == "retail" && stage.LabelKey == "Ingestion_StageRetailIdentification");
        Assert.Contains(stages, stage => stage.Key == "wikidata" && stage.LabelKey == "Ingestion_StageWikidataMatch");
        Assert.DoesNotContain(stages, stage => stage.LabelKey == "Matching");
        Assert.Contains(stages, stage => stage.Key == "summary" && stage.IsSummary);
    }

    [Fact]
    public void LiveDashboardState_OverallProgressUsesPipelineStagesNotScannedFiles()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 43,
                RegisteredItems = 28,
                ItemsNeedingReview = 14,
            },
            PipelineStages =
            [
                new() { Key = "detected", Count = 43, TotalCount = 43 },
                new() { Key = "matched", Count = 31, TotalCount = 42 },
                new() { Key = "retail_review", Count = 11, TotalCount = 42 },
                new() { Key = "canonicalized", Count = 19, TotalCount = 31 },
                new() { Key = "wikidata_review", Count = 0, TotalCount = 31 },
                new() { Key = "enriched", Count = 0, TotalCount = 31 },
                new() { Key = "duplicate", Count = 1, TotalCount = 43 },
            ],
        };
        var jobs = new List<IngestionOperationsJobViewModel>
        {
            new()
            {
                CurrentStage = "Wikidata matching",
                TotalCount = 43,
                ProcessedCount = 43,
                PercentComplete = 100,
                Status = "running",
            },
        };

        var metrics = IngestionLiveDashboardState.BuildMetrics(snapshot, jobs);
        var stages = IngestionLiveDashboardState.BuildStages(snapshot, jobs, metrics.TotalFiles);
        var progress = IngestionLiveDashboardState.BuildOverallProgress(metrics, stages, null);

        Assert.Equal(43, metrics.ProcessedFiles);
        Assert.Equal(43, metrics.TotalFiles);
        var retail = Assert.Single(stages, stage => stage.Key == "retail");
        Assert.Equal(43, retail.Count);
        Assert.Equal(43, retail.Total);
        Assert.Equal(1, retail.OtherCount);
        Assert.True(progress.Percent < 100);
        Assert.Equal(72.3, Math.Round(progress.Percent, 1));
        Assert.Equal("Ingestion_StageWikidataMatch", progress.ActiveStageLabelKey);
        Assert.Equal(19, progress.ActiveStageCount);
        Assert.Equal(31, progress.ActiveStageTotal);
        Assert.Equal(61.3, Math.Round(progress.ActiveStagePercent, 1));
    }

    [Fact]
    public void LiveDashboardState_OverallProgressStaysBelowCompleteWhileJobIsActive()
    {
        var stages = new[]
        {
            new IngestionDashboardStage("scanning", "Ingestion_StageScanning", "Ingestion_StageScanningDetail", Icons.Material.Outlined.Radar, 43, 43, 100, "Ingestion_StatusComplete", 100, false, 0, 0, 0, false),
            new IngestionDashboardStage("retail", "Ingestion_StageRetailIdentification", "Ingestion_StageRetailIdentificationDetail", Icons.Material.Outlined.Search, 43, 43, 100, "Ingestion_StatusComplete", 100, false, 31, 11, 1, false),
            new IngestionDashboardStage("wikidata", "Ingestion_StageWikidataMatch", "Ingestion_StageWikidataMatchDetail", Icons.Material.Outlined.TravelExplore, 31, 31, 100, "Ingestion_StatusComplete", 100, false, 0, 0, 0, false),
            new IngestionDashboardStage("enrichment", "Ingestion_StageEnrichment", "Ingestion_StageEnrichmentDetail", Icons.Material.Outlined.DataObject, 31, 31, 100, "Ingestion_StatusActive", 100, false, 0, 0, 0, false),
            new IngestionDashboardStage("summary", "Ingestion_StageSummary", "Ingestion_StageSummaryDetail", Icons.Material.Outlined.AssignmentTurnedIn, 43, 43, 100, "Ingestion_StatusComplete", 100, false, 28, 14, 1, true),
        };

        var progress = IngestionLiveDashboardState.BuildOverallProgress(new IngestionDashboardMetrics(43, 43, 1, 14), stages, null);

        Assert.Equal(99, progress.Percent);
        Assert.Equal("Ingestion_StageEnrichment", progress.ActiveStageLabelKey);
    }

    [Fact]
    public void LiveDashboardState_HidesScanningCountWhenIdle()
    {
        var stages = IngestionLiveDashboardState.BuildStages(new IngestionOperationsSnapshotViewModel(), [], 0);

        var scanning = Assert.Single(stages, stage => stage.Key == "scanning");
        Assert.True(scanning.HideCount);
        Assert.Equal("Ingestion_StatusIdle", scanning.StatusKey);
    }

    [Fact]
    public void LiveDashboardState_ShowsCompletedScanningCountFromSnapshot()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            PipelineStages =
            [
                new() { Key = "detected", Count = 43, TotalCount = 43 },
            ],
        };

        var stages = IngestionLiveDashboardState.BuildStages(snapshot, [], 43);

        var scanning = Assert.Single(stages, stage => stage.Key == "scanning");
        Assert.False(scanning.HideCount);
        Assert.Equal(43, scanning.Count);
        Assert.Equal(43, scanning.Total);
        Assert.Equal("Ingestion_StatusComplete", scanning.StatusKey);
    }

    [Fact]
    public void LiveDashboardState_ShowsScanningQueueWhenActive()
    {
        var jobs = new List<IngestionOperationsJobViewModel>
        {
            new()
            {
                CurrentStage = "Scanning",
                TotalCount = 10,
                ProcessedCount = 4,
                PercentComplete = 40,
            },
        };

        var stages = IngestionLiveDashboardState.BuildStages(new IngestionOperationsSnapshotViewModel(), jobs, 10);

        var scanning = Assert.Single(stages, stage => stage.Key == "scanning");
        Assert.False(scanning.HideCount);
        Assert.Equal(4, scanning.Count);
        Assert.Equal(10, scanning.Total);
        Assert.Equal(40, scanning.RingPercent);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}

public sealed class IngestionDashboardRenderTests : TestContext
{
    public IngestionDashboardRenderTests()
    {
        Services.AddLocalization();
        Services.AddMudServices();
    }

    [Fact]
    public void ActivityList_RendersReviewPreviewCoverWhenReviewHasArtwork()
    {
        var review = new ReviewItemViewModel
        {
            EntityTitle = "Dune",
            MediaType = "Books",
            Trigger = "LowConfidence",
            CoverUrl = "https://example.test/dune.jpg",
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.PendingReviews, new[] { review })
            .Add(component => component.ReviewTotal, 1));

        Assert.Contains("https://example.test/dune.jpg", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Dune", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_FallsBackToIconTileWhenNoArtworkExists()
    {
        var job = new IngestionOperationsJobViewModel
        {
            JobType = "Ingestion batch",
            MediaType = "Movies",
            CurrentStage = "Matching metadata",
            CurrentItem = "Dune Part One",
            ProcessedCount = 4,
            TotalCount = 10,
            PercentComplete = 40,
            Status = "running",
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.Jobs, new[] { job })
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Dune Part One", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivityList_UsesActiveStageProgressInsteadOfFileScanProgress()
    {
        var job = new IngestionOperationsJobViewModel
        {
            JobType = "Ingestion batch",
            MediaType = "Mixed",
            CurrentStage = "Wikidata matching",
            ProcessedCount = 43,
            TotalCount = 43,
            PercentComplete = 100,
            Status = "running",
        };
        var stages = new[]
        {
            new IngestionDashboardStage(
                "wikidata",
                "Ingestion_StageWikidataMatch",
                "Ingestion_StageWikidataMatchDetail",
                Icons.Material.Outlined.TravelExplore,
                19,
                31,
                61.29,
                "Ingestion_StatusActive",
                61.29,
                false,
                0,
                0,
                0,
                false),
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.Jobs, new[] { job })
            .Add(component => component.Stages, stages)
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Wikidata matching", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("19 / 31 files", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("61%", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("100%</span>", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_LinksToActivityLogsAndReviewQueue()
    {
        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 4, 1, 0))
            .Add(component => component.OverallProgress, new IngestionOverallProgress(4, 10, 40, "Ingestion_StageRetailIdentification", "Ingestion_StageRetailIdentificationDetail", 4, 10, 40, null))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(new IngestionOperationsSnapshotViewModel(), [], 10))
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.DoesNotContain("ingestion-live__badge", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings/activity\"", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/settings/review\"", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
