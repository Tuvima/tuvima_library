using Bunit;
using MediaEngine.Web.Components.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Contains(stages, stage => stage.Key == "enrichment" && stage.Status == "Active");
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}

public sealed class IngestionDashboardRenderTests : TestContext
{
    public IngestionDashboardRenderTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void ActivityList_RendersCoverWhenActivityHasArtwork()
    {
        var activity = new ActivityEntryViewModel
        {
            ActionType = "MediaAdded",
            OccurredAt = DateTimeOffset.UtcNow.ToString("O"),
            ChangesJson = """
            {
              "title": "Dune",
              "media_type": "Books",
              "cover": "https://example.test/dune.jpg",
              "collection_name": "Dune"
            }
            """,
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, new[] { activity }));

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
}
