using Bunit;
using MediaEngine.Domain.Enums;
using MediaEngine.Web.Components.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using System.Runtime.CompilerServices;

namespace MediaEngine.Web.Tests;

public sealed class IngestionOperationsPageGuardrailTests
{
    [Fact]
    public void IngestionTab_UsesCentralLiveDashboardStateAndComponents()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\IngestionTasksTab.razor"));
        var dashboardSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\IngestionLiveDashboard.razor"));
        var stateSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\IngestionLiveDashboardState.cs"));
        var orchestratorSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\UIOrchestratorService.cs"));

        Assert.Contains("IngestionLiveDashboardState", source, StringComparison.Ordinal);
        Assert.Contains("<IngestionLiveDashboard", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<IngestionActivityList", source, StringComparison.Ordinal);
        Assert.Contains("Status=\"Dashboard.LibraryUpdateStatus\"", source, StringComparison.Ordinal);
        Assert.Contains("ShouldRender", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("BuildRenderSignature", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("StageRows", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("library-update-stage-cards", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("library-update-stage-detail-panel", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("SelectedBatch is { StageProgress.Count: > 0 }", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("DetailCountFirst(details, \"Relationship links\"", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("IsInterruptedBatchStatus", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("BuildSnapshotSignature", stateSource, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", stateSource, StringComparison.Ordinal);
        Assert.Contains("LibraryUpdatePageState.Interrupted", stateSource, StringComparison.Ordinal);
        Assert.Contains("SignalREvents.IngestionItemProgress", orchestratorSource, StringComparison.Ordinal);
        Assert.Contains("PushIngestionItemProgress", orchestratorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<IngestionDiagnosticsPanels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIngestionOperationsSnapshotAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StateContainer.BatchProgress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly CurrentRun _currentRun", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboardState_KeepsCompleteVisibleForSixtySecondsThenShowsIdle()
    {
        var completedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                LastSuccessfulScanTime = completedAt,
                TotalItems = 20,
                RegisteredItems = 18,
                ItemsNeedingReview = 2,
            },
            RecentBatches =
            [
                new()
                {
                    StartedAt = completedAt.AddMinutes(-3),
                    CompletedAt = completedAt,
                    TotalFiles = 20,
                    RegisteredCount = 18,
                    ReviewCount = 2,
                    Status = "completed",
                },
            ],
        };

        var metrics = new IngestionDashboardMetrics(20, 20, 0, 2);

        var recent = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            metrics,
            null,
            completedAt,
            completedAt.AddSeconds(30));
        var idle = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            metrics,
            null,
            completedAt,
            completedAt.AddSeconds(61));

        Assert.Equal(LibraryUpdatePageState.Complete, recent.PageState);
        Assert.Equal("Library update complete", recent.Heading);
        Assert.True(recent.ShowProgress);
        Assert.Equal(100, recent.ProgressPercent);
        Assert.Equal(LibraryUpdatePageState.Idle, idle.PageState);
        Assert.False(idle.ShowProgress);
        Assert.Equal("Library is up to date", idle.Heading);
    }

    [Fact]
    public void LiveDashboardState_IgnoresStaleActiveJobCounterWhenDetailedWorkIsComplete()
    {
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 117,
                RegisteredItems = 117,
                ActiveJobs = 1,
                LastSuccessfulScanTime = completedAt,
            },
            CurrentActivities =
            [
                new()
                {
                    StageKey = "artwork",
                    Message = "Fetching artwork",
                    ProcessedCount = 117,
                    TotalCount = 117,
                    PercentComplete = 100,
                    ActiveCount = 0,
                    QueuedCount = 0,
                },
            ],
            RecentBatches =
            [
                new()
                {
                    StartedAt = completedAt.AddMinutes(-10),
                    CompletedAt = completedAt,
                    TotalFiles = 117,
                    ProcessedFiles = 117,
                    RegisteredCount = 117,
                    Status = "completed",
                },
            ],
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            snapshot.CurrentActivities,
            [],
            [],
            new IngestionDashboardMetrics(117, 117, 1, 0),
            null,
            completedAt,
            completedAt.AddMinutes(5));

        Assert.Equal(LibraryUpdatePageState.Idle, status.PageState);
        Assert.Equal("Library is up to date", status.Heading);
        Assert.False(status.ShowProgress);
    }

    [Fact]
    public void LiveDashboardState_TreatsQueuedIncompleteStageWorkAsRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 131,
                RegisteredItems = 121,
                ItemsNeedingReview = 10,
                ActiveJobs = 0,
                LastSuccessfulScanTime = now.AddMinutes(-1),
            },
            CurrentActivities =
            [
                new()
                {
                    StageKey = "wikidata",
                    Message = "Linking Wikidata QIDs",
                    ProcessedCount = 64,
                    TotalCount = 124,
                    ActiveCount = 0,
                    QueuedCount = 60,
                },
            ],
            RecentBatches =
            [
                new()
                {
                    StartedAt = now.AddMinutes(-10),
                    CompletedAt = now.AddMinutes(-1),
                    TotalFiles = 131,
                    ProcessedFiles = 131,
                    RegisteredCount = 121,
                    ReviewCount = 10,
                    Status = "completed",
                },
            ],
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            snapshot.CurrentActivities,
            [],
            [],
            new IngestionDashboardMetrics(131, 121, 0, 10),
            null,
            now.AddMinutes(-1),
            now);

        Assert.Equal(LibraryUpdatePageState.Running, status.PageState);
        Assert.Equal("Updating your library", status.Heading);
        Assert.True(status.ShowProgress);
        Assert.Equal(131, status.ProcessedFiles);
        Assert.Equal(60, status.QueuedItems);
    }

    [Fact]
    public void LiveDashboardState_TreatsAbandonedBatchAsInterruptedNotFailed()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 40,
                RegisteredItems = 20,
                ItemsNeedingReview = 2,
            },
            RecentBatches =
            [
                new()
                {
                    StartedAt = now.AddMinutes(-40),
                    CompletedAt = now.AddMinutes(-5),
                    TotalFiles = 40,
                    ProcessedFiles = 22,
                    RegisteredCount = 20,
                    ReviewCount = 2,
                    Status = "abandoned",
                },
            ],
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(40, 22, 0, 2),
            null,
            now.AddMinutes(-5),
            now);

        Assert.Equal(LibraryUpdatePageState.Interrupted, status.PageState);
        Assert.Equal("Library update interrupted", status.Heading);
        Assert.Equal("Interrupted", status.StatusLabel);
        Assert.Equal("warning", status.StatusTone);
        var run = Assert.Single(status.RecentRuns);
        Assert.Equal("Interrupted", run.StatusLabel);
        Assert.Equal("warning", run.StatusTone);
    }

    [Fact]
    public void LiveDashboardState_SeparatesFileProcessingFromWikidataProgress()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 131,
                RegisteredItems = 121,
                ItemsNeedingReview = 10,
                LastSuccessfulScanTime = now.AddMinutes(-1),
            },
            PipelineStages =
            [
                new() { Key = "detected", Count = 131, TotalCount = 131 },
                new() { Key = "parsed", Count = 131, TotalCount = 131 },
                new() { Key = "matched", Count = 124, TotalCount = 131 },
                new() { Key = "canonicalized", Count = 68, TotalCount = 124 },
                new() { Key = "needs_review", Count = 10, TotalCount = 131 },
            ],
            CurrentActivities =
            [
                new()
                {
                    StageKey = "wikidata",
                    Message = "Linking Wikidata QIDs",
                    ProcessedCount = 68,
                    TotalCount = 124,
                    ActiveCount = 50,
                    QueuedCount = 6,
                    CurrentItem = "Saga #3",
                },
            ],
            RecentBatches =
            [
                new()
                {
                    StartedAt = now.AddMinutes(-10),
                    CompletedAt = now.AddMinutes(-1),
                    TotalFiles = 131,
                    ProcessedFiles = 131,
                    RegisteredCount = 121,
                    ReviewCount = 10,
                    Status = "completed",
                },
            ],
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            snapshot.CurrentActivities,
            [],
            [],
            IngestionLiveDashboardState.BuildMetrics(snapshot, []),
            null,
            now.AddMinutes(-1),
            now);

        Assert.Equal(LibraryUpdatePageState.Running, status.PageState);
        Assert.Equal(131, status.TotalFiles);
        Assert.Equal(131, status.ProcessedFiles);
        Assert.Equal(50, status.ActiveItems);
        Assert.Equal(6, status.QueuedItems);
        Assert.Equal(100, status.ProgressPercent);
        Assert.Equal("131 of 131 files finished", status.MainLine);
        Assert.Contains("6 still in pipeline", status.SecondaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboardState_MapsNoPriorRunToReadyState()
    {
        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            new IngestionOperationsSnapshotViewModel(),
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(0, 0, 0, 0),
            null,
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal(LibraryUpdatePageState.NoPriorRun, status.PageState);
        Assert.Equal("Ready to scan your library", status.Heading);
        Assert.Equal("No recent library update", status.TimestampLine);
        Assert.False(status.ShowProgress);
    }

    [Fact]
    public void LiveDashboardState_UsesActiveStageActivityForLibraryUpdate()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 117,
                RegisteredItems = 52,
                ItemsNeedingReview = 64,
                ActiveJobs = 1,
            },
            PipelineStages =
            [
                new() { Key = "detected", Count = 117, TotalCount = 117 },
                new() { Key = "matched", Count = 52, TotalCount = 108 },
            ],
            RecentBatches =
            [
                new()
                {
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                    TotalFiles = 117,
                    RegisteredCount = 52,
                    ReviewCount = 64,
                    Status = "running",
                },
            ],
        };
        var jobs = new[]
        {
            new IngestionOperationsJobViewModel
            {
                CurrentStage = "Wikidata matching",
                ProcessedCount = 60,
                TotalCount = 108,
                PercentComplete = 55,
                Status = "running",
            },
        };
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "artwork",
                Message = "Fetching artwork",
                CurrentItem = "Moonage Daydream",
                ProcessedCount = 117,
                TotalCount = 117,
                PercentComplete = 100,
            },
            new IngestionCurrentActivityViewModel
            {
                StageKey = "wikidata",
                Message = "Linking Wikidata QIDs",
                CurrentItem = "The Empire Strikes Back",
                ProcessedCount = 60,
                TotalCount = 108,
                PercentComplete = 55,
                ActiveCount = 48,
            },
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            jobs,
            activities,
            [],
            [],
            IngestionLiveDashboardState.BuildMetrics(snapshot, jobs),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(117, status.TotalFiles);
        Assert.Equal(117, status.ProcessedFiles);
        Assert.Equal(0, status.QueuedItems);
        Assert.Equal(100, Math.Round(status.ProgressPercent, 1));
        Assert.Equal("The Empire Strikes Back", status.CurrentItemTitle);
        Assert.Equal("Matching titles and identity", status.CurrentStep);
        Assert.Contains(status.Steps, step => step.Label == "Matched identity" && step.Status == LibraryUpdateStepStatus.InProgress);
        Assert.DoesNotContain(status.Steps, step => step.Label == "Collected artwork" && step.Status == LibraryUpdateStepStatus.InProgress);
    }

    [Fact]
    public void LiveDashboardState_RecentlyAddedFallsBackToCompletedBatchPreview()
    {
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "wikidata",
                Message = "Linking Wikidata QIDs",
                CurrentBatch = new IngestionActivityBatchViewModel
                {
                    CompletedPreview = ["Dune Messiah", "Project Hail Mary"],
                },
            },
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            new IngestionOperationsSnapshotViewModel
            {
                Summary = new IngestionOperationsSummaryViewModel { TotalItems = 2, ActiveJobs = 1 },
            },
            [],
            activities,
            [],
            [],
            new IngestionDashboardMetrics(2, 2, 1, 0),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Contains(status.RecentItems, item => item.Title == "Dune Messiah");
        Assert.Contains(status.RecentItems, item => item.Title == "Project Hail Mary");
    }

    [Fact]
    public void LiveDashboardState_MapsConnectionStateToLiveBadgeModes()
    {
        Assert.Equal(IngestionLiveMode.Live, IngestionLiveDashboardState.ResolveLiveMode(EngineConnectionState.Online));
        Assert.Equal(IngestionLiveMode.Reconnecting, IngestionLiveDashboardState.ResolveLiveMode(EngineConnectionState.LiveUpdatesDisconnected));
        Assert.Equal(IngestionLiveMode.Polling, IngestionLiveDashboardState.ResolveLiveMode(EngineConnectionState.Offline));
    }

    [Fact]
    public void LiveDashboardState_MapsDurableOperationSummaryToQueueHealth()
    {
        var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["queued"] = 12,
            ["running"] = 3,
            ["retry_waiting"] = 2,
            ["blocked"] = 1,
            ["interrupted"] = 4,
            ["failed_terminal"] = 5,
            ["succeeded"] = 99,
        };
        var operations = new[]
        {
            new MediaOperationViewModel
            {
                Status = "queued",
                Stage = "waiting_for_lock",
            },
        };

        var health = IngestionLiveDashboardState.BuildQueueHealth(summary, operations);

        Assert.Contains(health, item => item.Key == "queued" && item.Count == 12);
        Assert.Contains(health, item => item.Key == "running" && item.Count == 3);
        Assert.Contains(health, item => item.Key == "waiting_for_lock" && item.Count == 1);
        Assert.Contains(health, item => item.Key == "retrying" && item.Count == 2);
        Assert.Contains(health, item => item.Key == "blocked" && item.Count == 1);
        Assert.Contains(health, item => item.Key == "interrupted" && item.Count == 4);
        Assert.Contains(health, item => item.Key == "failed" && item.Count == 5);
        Assert.Contains(health, item => item.Key == "completed" && item.Count == 99);
    }

    [Fact]
    public void LiveDashboardState_DefaultsDetailsVisibleOnlyForProblemWork()
    {
        Assert.False(IngestionLiveDashboardState.ShouldDefaultShowDetails(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["queued"] = 10,
                ["running"] = 2,
                ["succeeded"] = 20,
            }));

        Assert.True(IngestionLiveDashboardState.ShouldDefaultShowDetails(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["blocked"] = 1,
            }));
        Assert.True(IngestionLiveDashboardState.ShouldDefaultShowDetails(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["retry_waiting"] = 3,
            }));
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
    public void LiveDashboardState_MergesBatchProgressIntoCurrentActivities()
    {
        var state = new UniverseStateContainer();
        var batchId = Guid.NewGuid();
        state.PushBatchProgress(new BatchProgressEvent(
            batchId,
            FilesTotal: 12,
            FilesProcessed: 8,
            FilesIdentified: 8,
            FilesReview: 0,
            FilesNoMatch: 0,
            FilesFailed: 0,
            ProgressPercent: 67,
            EstimatedSecondsRemaining: 30,
            IsComplete: false,
            RecentTitles: ["Something"],
            CurrentStage: "Hydrating",
            FilesQueued: 3,
            FilesActive: 1,
            CurrentFileTitle: "Moonage Daydream",
            LifecycleStage: "Hydrating"));
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            CurrentActivities =
            [
                new IngestionCurrentActivityViewModel
                {
                    StageKey = "relationships",
                    Message = "Relationships",
                    CurrentItem = "Old activity",
                    ProcessedCount = 2,
                    TotalCount = 3,
                    PercentComplete = 67,
                },
            ],
        };
        var activeJobs = IngestionLiveDashboardState.BuildActiveJobs(snapshot, state);
        var stages = IngestionLiveDashboardState.BuildStages(snapshot, activeJobs, 12);

        var activities = IngestionLiveDashboardState.BuildCurrentActivities(snapshot, activeJobs, stages, state);

        var activity = Assert.Single(activities, item => item.StageKey == "retail");
        Assert.Equal("Retail Match", activity.Message);
        Assert.Equal("Moonage Daydream", activity.CurrentItem);
        Assert.Equal("Adding retail metadata and primary artwork", activity.Detail);
        Assert.Equal(1, activity.ActiveCount);
        Assert.Equal(3, activity.QueuedCount);
    }

    [Fact]
    public void LiveDashboardState_MergesItemProgressIntoActiveJobs()
    {
        var state = new UniverseStateContainer();
        var batchId = Guid.NewGuid();
        state.PushBatchProgress(new BatchProgressEvent(
            batchId,
            FilesTotal: 20,
            FilesProcessed: 0,
            FilesIdentified: 0,
            FilesReview: 0,
            FilesNoMatch: 0,
            FilesFailed: 0,
            ProgressPercent: 0,
            EstimatedSecondsRemaining: null,
            IsComplete: false,
            CurrentStage: "Queued",
            FilesQueued: 20));
        state.PushIngestionItemProgress(new IngestionItemProgressEvent(
            batchId,
            Guid.NewGuid(),
            MediaAssetId: null,
            FilePath: @"C:\drop\Dune.epub",
            FileName: "Dune.epub",
            Stage: "hashing",
            StageOrder: 1,
            ProgressPercent: 10,
            IsTerminal: false,
            Title: "Dune",
            MediaType: "Book"));

        var jobs = IngestionLiveDashboardState.BuildActiveJobs(new IngestionOperationsSnapshotViewModel(), state);

        var job = Assert.Single(jobs, job => job.JobId == batchId);
        Assert.Equal("Reading files", job.JobType);
        Assert.Equal("Hashing", job.CurrentStage);
        Assert.Equal("Dune", job.CurrentItem);
        Assert.True(job.PercentComplete > 0);
        Assert.False(state.LastStateChangeRequiresSnapshotRefresh);
    }

    [Fact]
    public void LiveDashboardState_MergesItemProgressIntoCurrentActivities()
    {
        var state = new UniverseStateContainer();
        var batchId = Guid.NewGuid();
        state.PushBatchProgress(new BatchProgressEvent(
            batchId,
            FilesTotal: 8,
            FilesProcessed: 0,
            FilesIdentified: 0,
            FilesReview: 0,
            FilesNoMatch: 0,
            FilesFailed: 0,
            ProgressPercent: 0,
            EstimatedSecondsRemaining: null,
            IsComplete: false,
            CurrentStage: "Queued",
            FilesQueued: 8));
        state.PushIngestionItemProgress(new IngestionItemProgressEvent(
            batchId,
            Guid.NewGuid(),
            MediaAssetId: null,
            FilePath: @"C:\drop\Foundation.epub",
            FileName: "Foundation.epub",
            Stage: "processed",
            StageOrder: 2,
            ProgressPercent: 35,
            IsTerminal: false,
            Title: "Foundation",
            MediaType: "Book"));
        var snapshot = new IngestionOperationsSnapshotViewModel();
        var jobs = IngestionLiveDashboardState.BuildActiveJobs(snapshot, state);
        var stages = IngestionLiveDashboardState.BuildStages(snapshot, jobs, 8);

        var activities = IngestionLiveDashboardState.BuildCurrentActivities(snapshot, jobs, stages, state);

        var activity = Assert.Single(activities, activity => activity.StageKey == "scanning");
        Assert.Equal("Reading media files", activity.Message);
        Assert.Equal("Read details", activity.Detail);
        Assert.Equal("Foundation", activity.CurrentItem);
        Assert.Contains("Foundation", activity.CurrentBatch!.ActiveItems);
        Assert.True(activity.PercentComplete > 0);
    }

    [Fact]
    public void UniverseStateContainer_ItemProgressDoesNotRequestSnapshotRefresh()
    {
        var state = new UniverseStateContainer();
        var batchId = Guid.NewGuid();

        state.PushIngestionItemProgress(new IngestionItemProgressEvent(
            batchId,
            Guid.NewGuid(),
            MediaAssetId: null,
            FilePath: @"C:\drop\Neuromancer.epub",
            FileName: "Neuromancer.epub",
            Stage: "hashing",
            StageOrder: 1,
            ProgressPercent: 10,
            IsTerminal: false));

        Assert.False(state.LastStateChangeRequiresSnapshotRefresh);

        state.PushBatchProgress(new BatchProgressEvent(
            batchId,
            FilesTotal: 1,
            FilesProcessed: 1,
            FilesIdentified: 1,
            FilesReview: 0,
            FilesNoMatch: 0,
            FilesFailed: 0,
            ProgressPercent: 100,
            EstimatedSecondsRemaining: null,
            IsComplete: true));

        Assert.True(state.LastStateChangeRequiresSnapshotRefresh);
    }

    [Fact]
    public void LiveDashboardState_MergesLiveUniverseProgressIntoCurrentActivities()
    {
        var state = new UniverseStateContainer();
        state.PushUniverseEnrichmentProgress(new UniverseEnrichmentProgressEvent(
            "Q17738",
            "Star Wars: Episode IV - A New Hope",
            ProcessedCount: 71,
            TotalCount: 77,
            CurrentStep: "Enhancers"));

        var activities = IngestionLiveDashboardState.BuildCurrentActivities(
            new IngestionOperationsSnapshotViewModel(),
            [],
            [],
            state);

        var activity = Assert.Single(activities);
        Assert.Equal("relationships", activity.StageKey);
        Assert.Equal("Artwork, people, and relationship enhancers", activity.Message);
        Assert.Equal("Star Wars: Episode IV - A New Hope", activity.CurrentItem);
        Assert.Equal(70, activity.ProcessedCount);
        Assert.Equal(77, activity.TotalCount);
        Assert.Equal(1, activity.ActiveCount);
        Assert.Equal(6, activity.QueuedCount);
        Assert.Equal("Enriching artwork, people, and relationships", activity.MetricValue);
    }

    [Fact]
    public void LiveDashboardState_TreatsRelationshipActivityAsEnrichmentWork()
    {
        var activity = new IngestionCurrentActivityViewModel
        {
            StageKey = "relationships",
            Message = "Relationships",
            CurrentItem = "Star Wars: Episode IV - A New Hope",
            ProcessedCount = 70,
            TotalCount = 77,
            PercentComplete = 91,
            ActiveCount = 1,
            QueuedCount = 6,
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            new IngestionOperationsSnapshotViewModel
            {
                Summary = new IngestionOperationsSummaryViewModel
                {
                    TotalItems = 77,
                    RegisteredItems = 70,
                    ActiveJobs = 1,
                },
            },
            [],
            [activity],
            [],
            [],
            new IngestionDashboardMetrics(77, 70, 1, 0),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(LibraryUpdatePageState.Running, status.PageState);
        Assert.Equal("Star Wars: Episode IV - A New Hope", status.CurrentItemTitle);
        Assert.Equal("Building series and relationships", status.CurrentStep);
        Assert.Contains(status.Steps, step => step.Label == "Enriched metadata" && step.Status == LibraryUpdateStepStatus.InProgress);
    }

    [Fact]
    public void LiveDashboardState_DoesNotTreatPartialIdleWorkerAsRunning()
    {
        var activity = new IngestionCurrentActivityViewModel
        {
            StageKey = "relationships",
            Message = "Relationships",
            CurrentItem = "The Empire Strikes Back",
            ProcessedCount = 2,
            TotalCount = 3,
            PercentComplete = 67,
            ActiveCount = 0,
            QueuedCount = 1,
            LastUpdatedTime = DateTimeOffset.UtcNow.AddMinutes(-10),
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            new IngestionOperationsSnapshotViewModel
            {
                Summary = new IngestionOperationsSummaryViewModel
                {
                    TotalItems = 3,
                    RegisteredItems = 2,
                },
            },
            [],
            [activity],
            [],
            [],
            new IngestionDashboardMetrics(3, 2, 0, 0),
            null,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow);

        Assert.Equal(LibraryUpdatePageState.Idle, status.PageState);
        Assert.False(status.ShowProgress);
    }

    [Fact]
    public void IngestionRealtimeProgress_ReflectsQuickHydrationTransitions()
    {
        var workerSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Workers\QuickHydrationWorker.cs"));
        var progressSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Services\BatchProgressService.cs"));
        var normalizedProgressSource = progressSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        var stateSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\IngestionLiveDashboardState.cs"));
        var operationsSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\IngestionOperationsStatusService.cs"));

        Assert.Contains("EmitBatchProgressAsync(job.IngestionRunId", workerSource, StringComparison.Ordinal);
        Assert.Contains("\"UniverseEnriching\"", progressSource, StringComparison.Ordinal);
        Assert.Contains("\"Hydrating\" => \"Retail Match\"", progressSource, StringComparison.Ordinal);
        Assert.Contains("BuildLiveBatchActivity", stateSource, StringComparison.Ordinal);
        Assert.Contains("nameof(IdentityJobState.Hydrating)", operationsSource, StringComparison.Ordinal);
        Assert.Contains("ActiveBatchFreshness", operationsSource, StringComparison.Ordinal);
        Assert.Contains("js.lease_expires_at > @now", operationsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("+ snapshot.RetailMatched\n                + snapshot.RetailMatchedNeedsReview", normalizedProgressSource, StringComparison.Ordinal);
        Assert.DoesNotContain("+ snapshot.QidResolved\n                + snapshot.Hydrating", normalizedProgressSource, StringComparison.Ordinal);
        Assert.Contains("var terminal = identified + review + noMatch + failed;", normalizedProgressSource, StringComparison.Ordinal);
        Assert.Contains("var progressed = terminal;", normalizedProgressSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AverageProgressPercent", normalizedProgressSource, StringComparison.Ordinal);
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
        Assert.Equal(3, metrics.ProcessedFiles);
        Assert.Equal(1, metrics.ActiveFiles);
        Assert.Contains(stages, stage => stage.Key == "enrichment" && stage.StatusKey == "Ingestion_StatusActive");
        Assert.Contains(stages, stage => stage.Key == "retail" && stage.LabelKey == "Ingestion_StageRetailIdentification");
        Assert.Contains(stages, stage => stage.Key == "wikidata" && stage.LabelKey == "Ingestion_StageWikidataMatch");
        Assert.DoesNotContain(stages, stage => stage.LabelKey == "Matching");
        Assert.DoesNotContain(stages, stage => stage.Key == "summary");
        Assert.DoesNotContain(stages, stage => stage.LabelKey == "Ingestion_StageSummary");
    }

    [Fact]
    public void LiveDashboardState_UsesNumberedStageProgressAsSourceOfTruth()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 97,
                RegisteredItems = 35,
                ItemsNeedingReview = 27,
            },
            PipelineStages =
            [
                new() { Key = "enriched", Count = 20, TotalCount = 97 },
            ],
            StageProgress =
            [
                new()
                {
                    StageNumber = 3,
                    StageKey = "retail",
                    Label = "Retail Match",
                    CompletedFiles = 97,
                    TotalFiles = 97,
                    PercentComplete = 100,
                    StatusLabel = "Complete",
                    ArtifactLabel = "matches",
                    ArtifactCount = 224,
                    DetailItems =
                    [
                        new() { Label = "Provider matches", Value = "97", Tone = "success" },
                        new() { Label = "Primary covers stored", Value = "87", Tone = "success" },
                    ],
                },
                new()
                {
                    StageNumber = 4,
                    StageKey = "wikidata",
                    Label = "Wikidata",
                    CompletedFiles = 35,
                    TotalFiles = 97,
                    PercentComplete = 36.1,
                    ActiveCount = 12,
                    QueuedCount = 4,
                    StatusLabel = "Active",
                    ActiveGroupLabel = "Resolving Wikidata batch: 16 files",
                    ActiveGroupCount = 16,
                    LabelAccuracy = "GroupedLookup",
                    ArtifactLabel = "QIDs resolved",
                    ArtifactCount = 35,
                    LastUpdatedTime = now,
                },
            ],
        };

        var stages = IngestionLiveDashboardState.BuildStages(snapshot, [], 97);

        Assert.Equal(2, stages.Count);
        Assert.DoesNotContain(stages, stage => stage.Key == "enrichment");

        var retail = Assert.Single(stages, stage => stage.Key == "retail");
        Assert.Equal(3, retail.StageNumber);
        Assert.Equal("Retail Match", retail.LabelKey);
        Assert.Equal("matches", retail.ArtifactLabel);
        Assert.Equal(224, retail.ArtifactCount);
        Assert.Contains(retail.DetailItems ?? Array.Empty<IngestionStageDetailItemViewModel>(), item => item.Label == "Provider matches" && item.Value == "97");
        Assert.Contains(retail.DetailItems ?? Array.Empty<IngestionStageDetailItemViewModel>(), item => item.Label == "Primary covers stored" && item.Value == "87");

        var wikidata = Assert.Single(stages, stage => stage.Key == "wikidata");
        Assert.Equal("Ingestion_StatusActive", wikidata.StatusKey);
        Assert.Equal("Resolving Wikidata batch: 16 files", wikidata.ActiveGroupLabel);
        Assert.Equal("GroupedLookup", wikidata.LabelAccuracy);
    }

    [Fact]
    public void LiveDashboardState_OverallProgressExcludesReviewExceptionStage()
    {
        var stages = new[]
        {
            new IngestionDashboardStage(
                "scan",
                "Scan",
                "Complete",
                Icons.Material.Outlined.Radar,
                27,
                27,
                100,
                "Ingestion_StatusComplete",
                100,
                false,
                0,
                0,
                0,
                false,
                StageNumber: 1),
            new IngestionDashboardStage(
                "read",
                "Read Details",
                "Complete",
                Icons.Material.Outlined.Description,
                27,
                27,
                100,
                "Ingestion_StatusComplete",
                100,
                false,
                0,
                0,
                0,
                false,
                StageNumber: 2),
            new IngestionDashboardStage(
                "retail",
                "Retail Match",
                "Complete",
                Icons.Material.Outlined.Search,
                27,
                27,
                100,
                "Ingestion_StatusComplete",
                100,
                false,
                0,
                0,
                0,
                false,
                StageNumber: 3),
            new IngestionDashboardStage(
                "wikidata",
                "Wikidata",
                "Complete",
                Icons.Material.Outlined.TravelExplore,
                27,
                27,
                100,
                "Ingestion_StatusComplete",
                100,
                false,
                0,
                0,
                0,
                false,
                StageNumber: 4),
            new IngestionDashboardStage(
                "review",
                "Review / attention",
                "Needs review",
                Icons.Material.Outlined.WarningAmber,
                12,
                27,
                44.4,
                "Ingestion_StatusPending",
                44.4,
                false,
                0,
                15,
                0,
                false,
                StageNumber: 9),
        };

        var progress = IngestionLiveDashboardState.BuildOverallProgress(
            new IngestionDashboardMetrics(27, 27, 0, 15),
            stages,
            null);

        Assert.Equal(100, progress.Percent);
        Assert.Equal(27, progress.ProcessedFiles);
        Assert.Equal(27, progress.TotalFiles);
    }

    [Fact]
    public void LiveDashboardState_EnrichmentCompletionUsesTerminalPipelineOutcomes()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 97,
                RegisteredItems = 20,
                ItemsNeedingReview = 27,
            },
            PipelineStages =
            [
                new() { Key = "detected", Count = 97, TotalCount = 97 },
                new() { Key = "parsed", Count = 97, TotalCount = 97 },
                new() { Key = "matched", Count = 70, TotalCount = 97 },
                new() { Key = "retail_review", Count = 27, TotalCount = 97 },
                new() { Key = "canonicalized", Count = 20, TotalCount = 97 },
                new() { Key = "wikidata_review", Count = 50, TotalCount = 97 },
                new() { Key = "enriched", Count = 20, TotalCount = 97 },
                new() { Key = "registered", Count = 20, TotalCount = 97 },
                new() { Key = "needs_review", Count = 27, TotalCount = 97 },
            ],
        };
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "artwork",
                Message = "Fetching artwork",
                ProcessedCount = 62,
                TotalCount = 97,
                ActiveCount = 0,
                QueuedCount = 0,
            },
        };

        var before = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            activities,
            [],
            [],
            new IngestionDashboardMetrics(97, 62, 1, 27),
            null,
            now,
            now);
        activities[0].ProcessedCount = 40;
        var after = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            activities,
            [],
            [],
            new IngestionDashboardMetrics(97, 40, 1, 27),
            null,
            now,
            now.AddSeconds(8));

        Assert.Equal(97, before.EnrichmentCompleted);
        Assert.Equal(97, after.EnrichmentCompleted);
        Assert.Equal(97, before.EnrichmentTotal);
        Assert.Equal(97, after.EnrichmentTotal);
    }

    [Fact]
    public void LiveDashboardState_SeparatesUnmatchedFromMissingIdentityReasons()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 131,
                RegisteredItems = 52,
                ItemsNeedingReview = 32,
            },
            ReviewReasons =
            [
                new() { Key = "unmatched", Count = 20 },
                new() { Key = "missing_wikidata", Count = 7 },
                new() { Key = "low_confidence", Count = 2 },
                new() { Key = "provider_failures", Count = 3 },
            ],
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(131, 52, 0, 32),
            null,
            now,
            now);

        Assert.Contains(status.AttentionReasons, reason => reason.Label == "unmatched items" && reason.Count == 20);
        Assert.Contains(status.AttentionReasons, reason => reason.Label == "missing identity" && reason.Count == 7);
        Assert.Contains(status.AttentionReasons, reason => reason.Label == "provider failures" && reason.Count == 3);
        Assert.DoesNotContain(status.AttentionReasons, reason => reason.Label == "missing identity" && reason.Count == 27);
    }

    [Fact]
    public void LiveDashboardState_ClampsOverlappingWorkerQueuesToRunSize()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 97,
                ActiveJobs = 1,
            },
            PipelineStages =
            [
                new() { Key = "detected", Count = 97, TotalCount = 97 },
                new() { Key = "parsed", Count = 97, TotalCount = 97 },
                new() { Key = "matched", Count = 49, TotalCount = 97 },
                new() { Key = "enriched", Count = 0, TotalCount = 97 },
            ],
            RecentBatches =
            [
                new()
                {
                    StartedAt = now.AddMinutes(-1),
                    TotalFiles = 97,
                    ProcessedFiles = 97,
                    Status = "running",
                },
            ],
        };
        var activities = new List<IngestionCurrentActivityViewModel>
        {
            new()
            {
                StageKey = "artwork",
                Message = "Fetching artwork",
                ProcessedCount = 0,
                TotalCount = 97,
                QueuedCount = 97,
            },
            new()
            {
                StageKey = "wikidata",
                Message = "Linking Wikidata QIDs",
                ProcessedCount = 11,
                TotalCount = 61,
                ActiveCount = 49,
                QueuedCount = 1,
            },
        };
        var metrics = new IngestionDashboardMetrics(97, 0, 1, 0);

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            activities,
            [],
            [],
            metrics,
            null,
            now,
            now);

        Assert.Equal(49, status.ActiveItems);
        Assert.Equal(48, status.QueuedItems);
        Assert.Contains("49 active", status.SecondaryLine, StringComparison.Ordinal);
        Assert.Contains("48 still in pipeline", status.SecondaryLine, StringComparison.Ordinal);
        Assert.DoesNotContain("98 still in pipeline", status.SecondaryLine, StringComparison.Ordinal);
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

        Assert.Equal(15, metrics.ProcessedFiles);
        Assert.Equal(43, metrics.TotalFiles);
        var retail = Assert.Single(stages, stage => stage.Key == "retail");
        Assert.Equal(43, retail.Count);
        Assert.Equal(43, retail.Total);
        Assert.Equal(1, retail.OtherCount);
        Assert.True(progress.Percent < 100);
        Assert.Equal(65.3, Math.Round(progress.Percent, 1));
        Assert.Equal("Ingestion_StageWikidataMatch", progress.ActiveStageLabelKey);
        Assert.Equal(19, progress.ActiveStageCount);
        Assert.Equal(31, progress.ActiveStageTotal);
        Assert.Equal(61.3, Math.Round(progress.ActiveStagePercent, 1));
    }

    [Fact]
    public void LiveDashboardState_OverallProgressCanReachCompleteWhileJobIsActive()
    {
        var stages = new[]
        {
            new IngestionDashboardStage("scanning", "Ingestion_StageScanning", "Ingestion_StageScanningDetail", Icons.Material.Outlined.Radar, 43, 43, 100, "Ingestion_StatusComplete", 100, false, 0, 0, 0, false),
            new IngestionDashboardStage("retail", "Ingestion_StageRetailIdentification", "Ingestion_StageRetailIdentificationDetail", Icons.Material.Outlined.Search, 43, 43, 100, "Ingestion_StatusComplete", 100, false, 31, 11, 1, false),
            new IngestionDashboardStage("wikidata", "Ingestion_StageWikidataMatch", "Ingestion_StageWikidataMatchDetail", Icons.Material.Outlined.TravelExplore, 31, 31, 100, "Ingestion_StatusComplete", 100, false, 0, 0, 0, false),
            new IngestionDashboardStage("enrichment", "Ingestion_StageEnrichment", "Ingestion_StageEnrichmentDetail", Icons.Material.Outlined.DataObject, 31, 31, 100, "Ingestion_StatusActive", 100, false, 0, 0, 0, false),
        };

        var progress = IngestionLiveDashboardState.BuildOverallProgress(new IngestionDashboardMetrics(43, 43, 1, 14), stages, null);

        Assert.Equal(100, progress.Percent);
        Assert.Equal("Ingestion_StageEnrichment", progress.ActiveStageLabelKey);
    }

    [Fact]
    public void LiveDashboardState_OverallProgressReachesCompleteWhenAllFilesAreTerminal()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 117,
                RegisteredItems = 90,
                ItemsNeedingReview = 26,
            },
            PipelineStages =
            [
                new() { Key = "detected", Count = 117, TotalCount = 117 },
                new() { Key = "matched", Count = 91, TotalCount = 117 },
                new() { Key = "retail_review", Count = 6, TotalCount = 117 },
                new() { Key = "canonicalized", Count = 90, TotalCount = 91 },
                new() { Key = "wikidata_review", Count = 1, TotalCount = 91 },
                new() { Key = "enriched", Count = 90, TotalCount = 91 },
                new() { Key = "duplicate", Count = 1, TotalCount = 117 },
                new() { Key = "failed", Count = 19, TotalCount = 117 },
            ],
        };

        var metrics = IngestionLiveDashboardState.BuildMetrics(snapshot, []);
        var stages = IngestionLiveDashboardState.BuildStages(snapshot, [], metrics.TotalFiles);
        var progress = IngestionLiveDashboardState.BuildOverallProgress(metrics, stages, null);

        Assert.Equal(117, metrics.ProcessedFiles);
        Assert.Equal(117, metrics.TotalFiles);
        Assert.Equal(100, progress.Percent);

        var retail = Assert.Single(stages, stage => stage.Key == "retail");
        Assert.Equal(117, retail.Count);
        Assert.Equal(117, retail.Total);
        Assert.Equal(20, retail.OtherCount);

        var enrichment = Assert.Single(stages, stage => stage.Key == "enrichment");
        Assert.Equal(91, enrichment.Count);
        Assert.Equal(91, enrichment.Total);
        Assert.Equal("Ingestion_StatusComplete", enrichment.StatusKey);
    }

    [Fact]
    public void LiveDashboardState_OverallProgressCountsTerminalRetailNoMatchForCompletedBatch()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 26,
                RegisteredItems = 10,
                ItemsNeedingReview = 15,
            },
            RecentBatches =
            [
                new()
                {
                    BatchId = Guid.NewGuid(),
                    StartedAt = now.AddMinutes(-5),
                    CompletedAt = now,
                    Status = "completed",
                    TotalFiles = 26,
                    ProcessedFiles = 26,
                    RegisteredCount = 10,
                    ReviewCount = 15,
                },
            ],
            PipelineStages =
            [
                new() { Key = "detected", Count = 26, TotalCount = 26 },
                new() { Key = "matched", Count = 10, TotalCount = 26 },
                new() { Key = "retail_review", Count = 16, TotalCount = 26 },
                new() { Key = "canonicalized", Count = 10, TotalCount = 26 },
                new() { Key = "wikidata_review", Count = 0, TotalCount = 26 },
                new() { Key = "enriched", Count = 10, TotalCount = 26 },
                new() { Key = "registered", Count = 10, TotalCount = 26 },
                new() { Key = "needs_review", Count = 15, TotalCount = 26 },
            ],
        };

        var metrics = IngestionLiveDashboardState.BuildMetrics(snapshot, []);
        var stages = IngestionLiveDashboardState.BuildStages(snapshot, [], metrics.TotalFiles);
        var progress = IngestionLiveDashboardState.BuildOverallProgress(metrics, stages, null);

        Assert.Equal(26, metrics.ProcessedFiles);
        Assert.Equal(26, progress.ProcessedFiles);
        Assert.Equal(26, progress.TotalFiles);
        Assert.Equal(100, progress.Percent);
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

    private static string GetRepoFilePath(string relativePath, [CallerFilePath] string sourceFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
    }
}

public sealed class IngestionDashboardRenderTests : TestContext
{
    public IngestionDashboardRenderTests()
    {
        Services.AddLocalization();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void ActivityList_RendersContextualPanelInsteadOfReviewPreview()
    {
        var activities = new[]
        {
            Activity("artwork", "Fetching artwork"),
        };
        var reasons = new[]
        {
            new IngestionReviewReasonViewModel { Key = "missing_artwork", Label = "Missing Artwork", Count = 4 },
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.PendingReviews, Array.Empty<ReviewItemViewModel>())
            .Add(component => component.ReviewReasons, reasons)
            .Add(component => component.ReviewTotal, 4));

        Assert.Contains("Artwork worker", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Working now", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ingestion-activity-shell", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Workers", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Problem buckets", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent engine activity", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Review Queue", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ingestion-review-preview", cut.Markup, StringComparison.Ordinal);
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
        Assert.Contains("19 found", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("19 / 31 files", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("61%", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("100%</span>", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_RendersAllStructuredTaskRowsAndKeepsDetailsInSidePane()
    {
        var activities = new[]
        {
            Activity("artwork", "Fetching artwork"),
            Activity("wikidata", "Linking Wikidata QIDs"),
            Activity("relationships", "Relationships"),
            Activity("people", "People & cast enrichment"),
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Fetching artwork", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Linking Wikidata QIDs", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Relationships", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("People &amp; cast enrichment", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Metadata validation", cut.Markup, StringComparison.Ordinal);

        Assert.Contains("Working now", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Recently completed", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity breakdown", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Problem buckets", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ingestion-current-row__batch", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_ChangesContextualPanelWhenActivityIsSelected()
    {
        var activities = new[]
        {
            Activity("artwork", "Fetching artwork"),
            Activity("people", "People & cast enrichment"),
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Artwork worker", cut.Markup, StringComparison.Ordinal);

        cut.FindAll(".ingestion-current-row__main")[1].Click();

        Assert.Contains("People and cast worker", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_ShowsQueuedForIncompleteWorkerWithoutActiveItems()
    {
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "relationships",
                Message = "Relationships",
                Detail = "Building series graph",
                ProcessedCount = 2,
                TotalCount = 3,
                CountUnit = "items",
                PercentComplete = 67,
                ActiveCount = 0,
                QueuedCount = 1,
                LastUpdatedTime = DateTimeOffset.UtcNow.AddMinutes(-10),
                SampleItems = ["The Empire Strikes Back"],
                CurrentBatch = new IngestionActivityBatchViewModel
                {
                    CompletedPreview = ["A New Hope", "The Empire Strikes Back"],
                },
            },
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Queued", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2 found", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("2 / 3 items", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("1 queued", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("A New Hope", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("The Empire Strikes Back", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("67%</span>", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_UsesWorkerSpecificRecentActivityItems()
    {
        var activities = new[]
        {
            Activity("artwork", "Fetching artwork"),
        };
        var recent = Enumerable.Range(1, 12)
            .Select(index => new ActivityEntryViewModel
            {
                ActionType = SystemActionType.CoverArtSaved,
                Detail = $"Cover art saved for Artwork Item {index}.",
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-index).ToString("O"),
            })
            .ToArray();

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, recent));

        Assert.Contains("Artwork Item 1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Artwork Item 10", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Artwork Item 11", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Full activity", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings/activity\"", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivityList_ShowsArtworkAssetCountsFromWorkerPreview()
    {
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "artwork",
                Message = "Fetching artwork",
                Detail = "Retrieving covers and posters from providers.",
                ProcessedCount = 1,
                TotalCount = 1,
                CountUnit = "artwork assets",
                PercentComplete = 100,
                ActiveCount = 0,
                QueuedCount = 0,
                LastUpdatedTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                SampleItems = ["Shawshank Redemption - 7"],
                CurrentBatch = new IngestionActivityBatchViewModel
                {
                    CompletedPreview = ["Shawshank Redemption - 7"],
                },
            },
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Shawshank Redemption - 7", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("1 found", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("1 / 1 artwork assets", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_ShowsWikidataQidsAsFoundSoFar()
    {
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "wikidata",
                Message = "Linking Wikidata QIDs",
                Detail = "Matching works to canonical entities.",
                ProcessedCount = 7,
                TotalCount = 29,
                CountUnit = "QIDs",
                PercentComplete = 24,
                ActiveCount = 2,
                QueuedCount = 10,
                LastUpdatedTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                SampleItems = ["The Martian"],
            },
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("7 found", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("7 / 29 QIDs", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_ExtractsQuotedPeopleActivityTitles()
    {
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "people",
                Message = "People & cast enrichment",
                Detail = "Enriching cast and people.",
                ProcessedCount = 1,
                TotalCount = 1,
                PercentComplete = 100,
                ActiveCount = 0,
                QueuedCount = 0,
                LastUpdatedTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            },
        };
        var recent = new[]
        {
            new ActivityEntryViewModel
            {
                ActionType = SystemActionType.PersonHydrated,
                Detail = "Person \"Carrie Fisher\" enriched from Wikidata",
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
            },
        };

        var cut = RenderComponent<IngestionActivityList>(parameters => parameters
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Jobs, Array.Empty<IngestionOperationsJobViewModel>())
            .Add(component => component.Activities, recent));

        Assert.Contains("Carrie Fisher", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">Wikidata</span>", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_DoesNotLimitCurrentWorkToThreeRandomRows()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\IngestionActivityList.razor"));
        var stateSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\IngestionLiveDashboardState.cs"));

        Assert.DoesNotContain("CurrentRows.Take(3)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(activity => !string.IsNullOrWhiteSpace(activity.Message))\r\n            .Take(3)", stateSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionOperationsStatusService_UsesDomainSpecificWorkerPreviewSources()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\IngestionOperationsStatusService.cs"));

        Assert.Contains("ReadArtworkWorkerRowsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReadSeriesWorkerRowsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReadPeopleWorkerRowsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ArtworkAssetCount", source, StringComparison.Ordinal);
        Assert.Contains("entity_assets", source, StringComparison.Ordinal);
        Assert.Contains("person_media_links", source, StringComparison.Ordinal);
        Assert.Contains("series_manifest_hydrations", source, StringComparison.Ordinal);
        Assert.DoesNotContain("c.name", source, StringComparison.Ordinal);
        Assert.Contains("displayRows", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_ShowsExpectedAndUnexpectedReviewCountsFromHarnessManifest()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 10,
                RegisteredItems = 6,
                ItemsNeedingReview = 4,
                ExpectedOutcomes = new IngestionExpectedOutcomesViewModel
                {
                    TotalFiles = 10,
                    ExpectedResolved = 8,
                    ExpectedReview = 1,
                },
            },
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(10, 10, 0, 4),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(1, status.ExpectedReviewItems);
        Assert.Equal(3, status.UnexpectedReviewItems);

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Status, status)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 10, 0, 4))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 10))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.DoesNotContain("3 unexpected", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("3 unexpected items need review", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("1 were expected by the harness", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_ShowsUnexpectedReviewWhenManifestExpectedNone()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 10,
                RegisteredItems = 8,
                ItemsNeedingReview = 2,
                ExpectedOutcomes = new IngestionExpectedOutcomesViewModel
                {
                    TotalFiles = 10,
                    ExpectedResolved = 9,
                    ExpectedReview = 0,
                },
            },
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(10, 8, 0, 2),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(0, status.ExpectedReviewItems);
        Assert.Equal(2, status.UnexpectedReviewItems);

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Status, status)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 8, 0, 2))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 10))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.DoesNotContain("2 unexpected", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("2 unexpected items need review", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("0 were expected by the harness", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_DoesNotShowUnexpectedReviewWithoutExpectationContract()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 10,
                RegisteredItems = 8,
                ItemsNeedingReview = 2,
            },
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(10, 8, 0, 2),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(0, status.ExpectedReviewItems);
        Assert.Equal(0, status.UnexpectedReviewItems);

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Status, status)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 8, 0, 2))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 10))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.DoesNotContain("unexpected", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveDashboard_ReviewOnlySnapshotDoesNotUseNoPriorRunState()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                ItemsNeedingReview = 27,
            },
        };

        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(0, 0, 0, 27),
            null,
            now,
            now);

        Assert.Equal(LibraryUpdatePageState.Idle, status.PageState);
        Assert.True(status.HasPriorRun);
        Assert.Equal(27, status.ReviewItems);
        Assert.Contains("27 items need review", status.MainLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start a scan", status.ActivityLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start a scan", status.MainLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveDashboard_DoesNotRenderDuplicateNeedsAttentionPanel()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 10,
                RegisteredItems = 8,
                ItemsNeedingReview = 2,
            },
        };
        var status = IngestionLiveDashboardState.BuildLibraryUpdateStatus(
            snapshot,
            [],
            [],
            [],
            [],
            new IngestionDashboardMetrics(10, 8, 0, 2),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Status, status)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 8, 0, 2))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 10))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.DoesNotContain("Need Review", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Needs attention", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Review Items", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update-attention", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_RendersLibraryUpdateDefaultView()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 10,
                RegisteredItems = 4,
                ActiveJobs = 1,
            },
        };
        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 4, 1, 0))
            .Add(component => component.OverallProgress, new IngestionOverallProgress(4, 10, 40, "Ingestion_StageRetailIdentification", "Ingestion_StageRetailIdentificationDetail", 4, 10, 40, null))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(new IngestionOperationsSnapshotViewModel(), [], 10))
            .Add(component => component.Jobs, new[]
            {
                new IngestionOperationsJobViewModel
                {
                    CurrentStage = "Matching metadata",
                    ProcessedCount = 4,
                    TotalCount = 10,
                    PercentComplete = 40,
                    Status = "running",
                },
            })
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Scanned", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Retail Match", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Overall progress", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Recent batches", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Refresh", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Scan Now", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Files Found", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Processed", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Need Review", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Library Update", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update__header", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update__metrics", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update-primary-grid", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update-batch-table", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Update steps", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("What's happening now", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("File processing", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ingestion-stage-rail", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_RendersNumberedStageArtifactBars()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 97,
                RegisteredItems = 35,
                ItemsNeedingReview = 27,
            },
            StageProgress =
            [
                new()
                {
                    StageNumber = 3,
                    StageKey = "retail",
                    Label = "Retail Match",
                    CompletedFiles = 97,
                    TotalFiles = 97,
                    PercentComplete = 100,
                    StatusLabel = "Complete",
                    ArtifactLabel = "matches",
                    ArtifactCount = 224,
                },
                new()
                {
                    StageNumber = 8,
                    StageKey = "deep_artwork",
                    Label = "Artwork",
                    CompletedFiles = 35,
                    TotalFiles = 97,
                    PercentComplete = 36.1,
                    StatusLabel = "In progress",
                    ArtifactLabel = "assets",
                    ArtifactCount = 12,
                },
                new()
                {
                    StageNumber = 9,
                    StageKey = "review",
                    Label = "Review / attention",
                    CompletedFiles = 70,
                    TotalFiles = 97,
                    PercentComplete = 72.2,
                    StatusLabel = "Needs review",
                    ArtifactLabel = "items needing review",
                    ArtifactCount = 27,
                },
            ],
        };

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(97, 35, 1, 27))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 97))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Retail Match", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Artwork", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("224 matches", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("12 assets", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Update steps", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Needs attention", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Review / attention", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("27 items needing review", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">Complete<", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_RendersSimplifiedStageMetricTiles()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 97,
                RegisteredItems = 35,
            },
            StageProgress =
            [
                new()
                {
                    StageNumber = 4,
                    StageKey = "wikidata",
                    Label = "Wikidata",
                    CompletedFiles = 61,
                    TotalFiles = 97,
                    PercentComplete = 62.9,
                    ArtifactLabel = "QIDs",
                    ArtifactCount = 102,
                    DetailItems =
                    [
                        new() { Label = "Files with media/work QID", Value = "61", Tone = "success" },
                        new() { Label = "Retail retained without QID", Value = "13", Tone = "warning" },
                        new() { Label = "Related QIDs discovered", Value = "102", Tone = "info" },
                    ],
                },
                new()
                {
                    StageNumber = 5,
                    StageKey = "people",
                    Label = "People",
                    CompletedFiles = 97,
                    TotalFiles = 97,
                    PercentComplete = 100,
                    ArtifactLabel = "people",
                    ArtifactCount = 570,
                    DetailItems =
                    [
                        new() { Label = "Cast", Value = "200" },
                        new() { Label = "Directors", Value = "20" },
                        new() { Label = "Authors", Value = "120" },
                        new() { Label = "Narrators", Value = "30" },
                        new() { Label = "Music artists", Value = "160" },
                        new() { Label = "Creators / crew", Value = "40" },
                        new() { Label = "Deeply enriched people", Value = "47", Tone = "success" },
                    ],
                },
            ],
        };

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(97, 35, 0, 0))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 97))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Media QIDs", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Relevant QIDs", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Unresolved", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Retail retained without QID", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Label accuracy", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Active item", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Active group", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Worker status", cut.Markup, StringComparison.Ordinal);

        cut.FindAll(".library-update-stage-card")[1].Click();
        Assert.Contains("Enriched", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Creators / crew", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_HidesStaleActiveLabelsForCompletedNumberedStages()
    {
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 27,
                RegisteredItems = 12,
                ItemsNeedingReview = 15,
            },
            StageProgress =
            [
                new()
                {
                    StageNumber = 3,
                    StageKey = "retail",
                    Label = "Retail Match",
                    CompletedFiles = 27,
                    TotalFiles = 27,
                    PercentComplete = 100,
                    ActiveCount = 0,
                    QueuedCount = 0,
                    StatusLabel = "Complete",
                    ActiveGroupLabel = "Matching retail batch: 27 files",
                    ActiveItemLabel = "Department of Clockwork Rain",
                    ArtifactLabel = "matches",
                    ArtifactCount = 354,
                },
                new()
                {
                    StageNumber = 4,
                    StageKey = "wikidata",
                    Label = "Wikidata",
                    CompletedFiles = 27,
                    TotalFiles = 27,
                    PercentComplete = 100,
                    ActiveCount = 0,
                    QueuedCount = 0,
                    StatusLabel = "Complete",
                    ActiveGroupLabel = "Resolving Wikidata batch: 12 files",
                    ArtifactLabel = "QIDs resolved",
                    ArtifactCount = 10,
                },
            ],
        };

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(27, 27, 0, 15))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 27))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("354 matches", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("10 QIDs", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Matching retail batch: 27 files", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolving Wikidata batch: 12 files", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Department of Clockwork Rain", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_RendersCompletedCheckForTerminalStagesWithPartialRawCounts()
    {
        var batchId = Guid.Parse("85000000-0000-0000-0000-000000000001");
        var completedAt = new DateTimeOffset(2026, 6, 8, 18, 30, 0, TimeSpan.Zero);
        var terminalStages = new List<IngestionStageProgressViewModel>
        {
            new()
            {
                StageNumber = 4,
                StageKey = "wikidata",
                Label = "Wikidata",
                CompletedFiles = 129,
                TotalFiles = 131,
                PercentComplete = 98.47,
                StatusLabel = "Complete",
                ArtifactLabel = "QIDs",
                ArtifactCount = 124,
            },
            new()
            {
                StageNumber = 5,
                StageKey = "people",
                Label = "People",
                CompletedFiles = 129,
                TotalFiles = 131,
                PercentComplete = 98.47,
                StatusLabel = "Complete",
                ArtifactLabel = "people",
                ArtifactCount = 412,
            },
            new()
            {
                StageNumber = 6,
                StageKey = "universes",
                Label = "Universes",
                CompletedFiles = 129,
                TotalFiles = 131,
                PercentComplete = 98.47,
                StatusLabel = "Complete",
                ArtifactLabel = "links",
                ArtifactCount = 38,
            },
            new()
            {
                StageNumber = 7,
                StageKey = "artwork",
                Label = "Artwork",
                CompletedFiles = 129,
                TotalFiles = 131,
                PercentComplete = 98.47,
                StatusLabel = "Complete",
                ArtifactLabel = "assets",
                ArtifactCount = 294,
            },
        };
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 131,
                RegisteredItems = 124,
                ItemsNeedingReview = 5,
            },
            StageProgress = terminalStages,
            RecentBatches =
            [
                new()
                {
                    BatchId = batchId,
                    StartedAt = completedAt.AddMinutes(-12),
                    CompletedAt = completedAt,
                    TotalFiles = 131,
                    ProcessedFiles = 131,
                    RegisteredCount = 124,
                    ReviewCount = 5,
                    Status = "completed",
                    StageProgress = terminalStages,
                },
            ],
        };

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(131, 131, 0, 5))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 131))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        foreach (var label in new[] { "Wikidata", "People", "Universes", "Artwork" })
        {
            Assert.Contains(cut.FindAll(".library-update-stage-card.is-complete"),
                card => card.TextContent.Contains(label, StringComparison.Ordinal));
            Assert.Contains($"{label} is complete for this batch.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain($"{label} has started but is not finished.", cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LiveDashboard_RendersCompactRecentBatchesPanel()
    {
        var batchId = Guid.Parse("83000000-0000-0000-0000-000000000001");
        var completedBatchId = Guid.Parse("84000000-0000-0000-0000-000000000001");
        var startedAt = new DateTimeOffset(2026, 6, 3, 20, 15, 0, TimeSpan.Zero);
        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, new IngestionOperationsSnapshotViewModel
            {
                Summary = new IngestionOperationsSummaryViewModel
                {
                    TotalItems = 10,
                    RegisteredItems = 4,
                    ItemsNeedingReview = 9,
                    ActiveJobs = 1,
                },
                RecentBatches =
                [
                    new IngestionOperationsBatchViewModel
                    {
                        BatchId = batchId,
                        StartedAt = startedAt,
                        TotalFiles = 10,
                        MoviesCount = 2,
                        TvShowsCount = 1,
                        BooksCount = 3,
                        AudiobooksCount = 1,
                        MusicCount = 1,
                        ComicsCount = 2,
                        RegisteredCount = 4,
                        ReviewCount = 2,
                        FailedCount = 0,
                        PeopleGeneratedCount = 7,
                        ArtworkDownloadedCount = 3,
                        MetadataUpdatedCount = 4,
                        Status = "running",
                    },
                    new IngestionOperationsBatchViewModel
                    {
                        BatchId = completedBatchId,
                        StartedAt = startedAt.AddMinutes(10),
                        CompletedAt = startedAt.AddMinutes(11),
                        TotalFiles = 3,
                        ProcessedFiles = 3,
                        MoviesCount = 3,
                        RegisteredCount = 99,
                        MetadataUpdatedCount = 0,
                        DurationSeconds = 60,
                        Status = "completed",
                        StageProgress =
                        [
                            new()
                            {
                                StageNumber = 2,
                                StageKey = "retail",
                                Label = "Retail Match",
                                CompletedFiles = 3,
                                TotalFiles = 3,
                                PercentComplete = 100,
                                ArtifactLabel = "matches",
                                ArtifactCount = 3,
                                DetailItems =
                                [
                                    new() { Label = "Matches", Value = "3", Tone = "success" },
                                    new() { Label = "Movies", Value = "3" },
                                    new() { Label = "Metadata fields", Value = "123", Tone = "info" },
                                    new() { Label = "Cover art assets", Value = "2", Tone = "success" },
                                    new() { Label = "Unresolved", Value = "0", Tone = "neutral" },
                                    new() { Label = "Failed", Value = "0", Tone = "neutral" },
                                ],
                            },
                        ],
                    },
                ],
            })
            .Add(component => component.Metrics, new IngestionDashboardMetrics(10, 4, 1, 0))
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(new IngestionOperationsSnapshotViewModel(), [], 10))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Overall progress", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Recent batches", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Update 830000", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Update 840000", cut.Markup, StringComparison.Ordinal);
        Assert.True(
            cut.Markup.IndexOf("Update 830000", StringComparison.Ordinal) <
            cut.Markup.IndexOf("Update 840000", StringComparison.Ordinal));
        Assert.Contains("Active - started", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Complete -", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Open the full activity log for Update 830000", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings/activity?runId=83000000-0000-0000-0000-000000000001\"", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"/settings/activity?batchId=83000000-0000-0000-0000-000000000001\"", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("through pipeline", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("+2 this batch", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Movies", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("TV", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Audiobooks", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Comics", cut.Markup, StringComparison.Ordinal);
        AssertBatchChip(cut, "4", "matched");
        AssertBatchChip(cut, "2", "review");
        Assert.DoesNotContain(">people<", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">artwork<", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">metadata<", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recent library updates", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Last batch runs", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update-batch-grid__row", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Queue &amp; Details", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Source", cut.Markup, StringComparison.Ordinal);

        cut.FindAll(".library-update-batch__select")[1].Click();
        Assert.Contains("123", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Metadata", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("99 matches", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_RendersMetricOnlyStageDetails()
    {
        var activities = new[]
        {
            Activity("artwork", "Fetching artwork"),
        };
        var snapshot = new IngestionOperationsSnapshotViewModel
        {
            Summary = new IngestionOperationsSummaryViewModel
            {
                TotalItems = 50,
                RegisteredItems = 31,
                ActiveJobs = 1,
            },
            CurrentActivities = activities.ToList(),
            StageProgress =
            [
                new()
                {
                    StageNumber = 8,
                    StageKey = "deep_artwork",
                    Label = "Artwork",
                    CompletedFiles = 31,
                    TotalFiles = 50,
                    PercentComplete = 62,
                    ActiveCount = 3,
                    QueuedCount = 12,
                    StatusLabel = "In progress",
                    ActiveGroupLabel = "Artwork lookup: 50 files",
                    ActiveItemLabel = "Neuromancer",
                    LabelAccuracy = "ExactItem",
                    ArtifactLabel = "assets",
                    ArtifactCount = 31,
                    DetailItems =
                    [
                        new() { Label = "Covers / posters", Value = "16", Tone = "success" },
                        new() { Label = "People headshots", Value = "4" },
                        new() { Label = "Backdrops", Value = "8" },
                        new() { Label = "Season / episode art", Value = "12" },
                        new() { Label = "Album / music art", Value = "2" },
                        new() { Label = "Logos", Value = "3" },
                    ],
                },
            ],
        };
        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.Metrics, new IngestionDashboardMetrics(50, 31, 1, 0))
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(snapshot, [], 50))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.DoesNotContain("31/50", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("31 assets", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Artwork", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Covers", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Headshots", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Backdrops", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Season Art", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Album Art", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Logos", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Artwork lookup: 50 files", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Neuromancer", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Label accuracy", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Active item", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Queued", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("library-update-batch-grid__row", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDashboard_DoesNotInventFinalChecksWhenQueueIsEmpty()
    {
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var activities = new[]
        {
            new IngestionCurrentActivityViewModel
            {
                StageKey = "artwork",
                Message = "Fetching artwork",
                ProcessedCount = 117,
                TotalCount = 117,
                PercentComplete = 100,
                ActiveCount = 0,
                QueuedCount = 0,
            },
        };

        var cut = RenderComponent<IngestionLiveDashboard>(parameters => parameters
            .Add(component => component.Snapshot, new IngestionOperationsSnapshotViewModel
            {
                Summary = new IngestionOperationsSummaryViewModel
                {
                    TotalItems = 117,
                    RegisteredItems = 117,
                    ActiveJobs = 1,
                    LastSuccessfulScanTime = completedAt,
                },
                CurrentActivities = activities.ToList(),
                RecentBatches =
                [
                    new IngestionOperationsBatchViewModel
                    {
                        StartedAt = completedAt.AddMinutes(-10),
                        CompletedAt = completedAt,
                        TotalFiles = 117,
                        ProcessedFiles = 117,
                        RegisteredCount = 117,
                        Status = "completed",
                    },
                ],
            })
            .Add(component => component.Metrics, new IngestionDashboardMetrics(117, 117, 1, 0))
            .Add(component => component.CurrentActivities, activities)
            .Add(component => component.Stages, IngestionLiveDashboardState.BuildStages(new IngestionOperationsSnapshotViewModel(), [], 117))
            .Add(component => component.Activities, Array.Empty<ActivityEntryViewModel>()));

        Assert.Contains("Overall progress", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("finishing final checks", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("0 still in pipeline", cut.Markup, StringComparison.Ordinal);
    }

    private static IngestionCurrentActivityViewModel Activity(string key, string message) => new()
    {
        StageKey = key,
        Message = message,
        Detail = "Working through this batch.",
        ProcessedCount = 31,
        TotalCount = 50,
        CountUnit = key switch
        {
            "artwork" => "artwork assets",
            "relationships" => "items",
            "people" => "people",
            "wikidata" => "QIDs",
            _ => "files",
        },
        PercentComplete = 62,
        QueuedCount = 12,
        ActiveCount = 3,
        SampleItems = ["Neuromancer", "Snow Crash", "Foundation", "Dune"],
        MetricLabel = "Files checked",
        MetricValue = "31",
        MetricTone = "info",
        CurrentBatch = new IngestionActivityBatchViewModel
        {
            BatchNumber = 1,
            BatchSize = 50,
            TotalBatches = 2,
            CompletedCount = 31,
            ActiveCount = 3,
            PendingCount = 12,
            ReviewCount = 4,
            ActiveItems = ["Neuromancer", "Snow Crash"],
            PendingPreview = ["Foundation"],
            CompletedPreview = ["Dune"],
            ReviewPreview = ["Arrival"],
        },
    };

    private static void AssertBatchChip(IRenderedComponent<IngestionLiveDashboard> cut, string count, string label)
    {
        Assert.Contains(cut.FindAll(".library-update-batch-chip"), chip =>
            chip.TextContent.Contains(count, StringComparison.Ordinal)
            && chip.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRepoFilePath(string relativePath, [CallerFilePath] string sourceFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
    }
}
