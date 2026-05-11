using System.Globalization;
using MediaEngine.Web.Models.ViewDTOs;
using MudBlazor;

namespace MediaEngine.Web.Services.Integration;

public sealed class IngestionLiveDashboardState : IDisposable
{
    private readonly IEngineApiClient _api;
    private readonly UIOrchestratorService _orchestrator;
    private readonly UniverseStateContainer _stateContainer;
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _refreshDebounceCts;
    private bool _disposed;
    private bool _initialized;

    public IngestionLiveDashboardState(
        IEngineApiClient api,
        UIOrchestratorService orchestrator,
        UniverseStateContainer stateContainer)
    {
        _api = api;
        _orchestrator = orchestrator;
        _stateContainer = stateContainer;
    }

    public event Action? OnChanged;

    public IngestionOperationsSnapshotViewModel? Snapshot { get; private set; }
    public IReadOnlyList<ActivityEntryViewModel> RecentActivity { get; private set; } = [];
    public IReadOnlyList<ReviewItemViewModel> PendingReviews { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public bool IsScanStarting { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? LastUpdated { get; private set; }

    public EngineConnectionState ConnectionState => _orchestrator.EngineConnectionState;
    public IReadOnlyList<IngestionOperationsJobViewModel> ActiveJobs => BuildActiveJobs(Snapshot, _stateContainer);
    public IReadOnlyList<IngestionCurrentActivityViewModel> CurrentActivities => BuildCurrentActivities(Snapshot, ActiveJobs, Stages);
    public IngestionDashboardMetrics Metrics => BuildMetrics(Snapshot, ActiveJobs);
    public IReadOnlyList<IngestionDashboardStage> Stages => BuildStages(Snapshot, ActiveJobs, Metrics.TotalFiles);
    public IngestionOverallProgress OverallProgress => BuildOverallProgress(Metrics, Stages, _stateContainer.BatchProgress);
    public IngestionLiveMode LiveMode => ResolveLiveMode(_orchestrator.EngineConnectionState);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!_initialized)
        {
            _stateContainer.OnStateChanged += OnRealtimeStateChanged;
            _orchestrator.OnEngineConnectionStateChanged += OnConnectionStateChanged;
            _initialized = true;
        }

        await _orchestrator.StartSignalRAsync(ct);
        await LoadAsync(ct);
        StartPolling();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = Snapshot is null;
        Error = null;
        Notify();

        try
        {
            var snapshotTask = _api.GetIngestionOperationsSnapshotAsync(ct);
            var activityTask = _api.GetRecentActivityAsync(12, ct);
            var reviewTask = _api.GetPendingReviewsAsync(3, ct);
            await Task.WhenAll(snapshotTask, activityTask, reviewTask);

            Snapshot = snapshotTask.Result;
            RecentActivity = activityTask.Result
                .Where(IsUsefulActivity)
                .Take(12)
                .ToList();
            PendingReviews = reviewTask.Result;
            LastUpdated = DateTimeOffset.Now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error = $"Ingestion status could not load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    public async Task ScanNowAsync(CancellationToken ct = default)
    {
        IsScanStarting = true;
        Notify();
        try
        {
            await _orchestrator.TriggerRescanAsync(ct);
            await LoadAsync(ct);
        }
        finally
        {
            IsScanStarting = false;
            Notify();
        }
    }

    private void StartPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = ActiveJobs.Count > 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(40);
            try
            {
                await Task.Delay(delay, ct);
                await LoadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnRealtimeStateChanged()
    {
        LastUpdated = DateTimeOffset.Now;
        Notify();
        DebounceSnapshotRefresh();
    }

    private void OnConnectionStateChanged(EngineConnectionState _)
    {
        Notify();
    }

    private void DebounceSnapshotRefresh()
    {
        _refreshDebounceCts?.Cancel();
        _refreshDebounceCts?.Dispose();
        _refreshDebounceCts = new CancellationTokenSource();
        var token = _refreshDebounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                await LoadAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void Notify()
    {
        if (!_disposed)
            OnChanged?.Invoke();
    }

    public void Stop()
    {
        if (_initialized)
        {
            _stateContainer.OnStateChanged -= OnRealtimeStateChanged;
            _orchestrator.OnEngineConnectionStateChanged -= OnConnectionStateChanged;
            _initialized = false;
        }

        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _refreshDebounceCts?.Cancel();
        _refreshDebounceCts?.Dispose();
        _refreshDebounceCts = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    public static IReadOnlyList<IngestionOperationsJobViewModel> BuildActiveJobs(
        IngestionOperationsSnapshotViewModel? snapshot,
        UniverseStateContainer stateContainer)
    {
        var jobs = snapshot?.ActiveJobs.ToList() ?? [];
        var batch = stateContainer.BatchProgress;
        if (batch is { IsComplete: false })
        {
            var existing = jobs.FirstOrDefault(job => job.JobId == batch.BatchId);
            var liveJob = new IngestionOperationsJobViewModel
            {
                JobId = batch.BatchId,
                JobType = "Ingestion batch",
                MediaType = "Mixed",
                SourceFolder = "Watch folders",
                CurrentStage = FirstNonBlank(batch.CurrentStage, batch.LifecycleStage, "Processing"),
                CurrentItem = FirstNonBlank(batch.CurrentFileTitle, batch.RecentTitles?.FirstOrDefault()),
                ProcessedCount = batch.FilesProcessed,
                TotalCount = batch.FilesTotal,
                PercentComplete = batch.ProgressPercent,
                Status = "running",
                Elapsed = existing?.Elapsed,
                LastUpdatedTime = DateTimeOffset.UtcNow,
                WarningSummary = batch.FilesFailed > 0
                    ? $"{batch.FilesFailed.ToString("N0", CultureInfo.CurrentCulture)} failed"
                    : null,
            };

            if (existing is null)
                jobs.Insert(0, liveJob);
            else
                jobs[jobs.IndexOf(existing)] = liveJob;
        }

        var progress = stateContainer.IngestionProgress;
        if (progress is not null && !progress.Stage.Equals("Complete", StringComparison.OrdinalIgnoreCase))
        {
            jobs.Insert(0, new IngestionOperationsJobViewModel
            {
                JobId = Guid.Empty,
                JobType = "Folder scan",
                MediaType = "Watched folders",
                SourceFolder = "Watch folders",
                CurrentStage = progress.Stage,
                CurrentItem = progress.CurrentFile,
                ProcessedCount = progress.ProcessedCount,
                TotalCount = progress.TotalCount,
                PercentComplete = progress.TotalCount > 0 ? progress.ProcessedCount * 100d / progress.TotalCount : 0,
                Status = "running",
                LastUpdatedTime = DateTimeOffset.UtcNow,
            });
        }

        return jobs
            .GroupBy(job => job.JobId)
            .Select(group => group.First())
            .ToList();
    }

    public static IngestionDashboardMetrics BuildMetrics(
        IngestionOperationsSnapshotViewModel? snapshot,
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs)
    {
        var activeTotal = activeJobs.Sum(job => Math.Max(0, job.TotalCount));
        var activeProcessed = activeJobs.Sum(job => Math.Max(0, job.ProcessedCount));
        var totalFiles = activeTotal > 0
            ? activeTotal
            : Total(snapshot, "detected", snapshot?.Summary.TotalItems ?? 0);
        var fallbackProcessed = (snapshot?.Summary.RegisteredItems ?? 0) + (snapshot?.Summary.ItemsNeedingReview ?? 0);
        var processedFiles = activeTotal > 0
            ? activeProcessed
            : Count(snapshot, "detected", fallbackProcessed);

        return new IngestionDashboardMetrics(
            totalFiles,
            processedFiles,
            activeJobs.Count > 0 ? activeJobs.Count : snapshot?.CurrentActivities.Count ?? 0,
            snapshot?.Summary.ItemsNeedingReview ?? 0);
    }

    public static IReadOnlyList<IngestionCurrentActivityViewModel> BuildCurrentActivities(
        IngestionOperationsSnapshotViewModel? snapshot,
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        IReadOnlyList<IngestionDashboardStage> stages)
    {
        var activities = snapshot?.CurrentActivities
            .Where(activity => !string.IsNullOrWhiteSpace(activity.Message))
            .Take(3)
            .ToList() ?? [];

        if (activities.Count > 0)
        {
            return activities;
        }

        return activeJobs
            .Where(job => job.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
                || job.Status.Equals("processing", StringComparison.OrdinalIgnoreCase)
                || job.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(job => ToCurrentActivity(job, stages))
            .Where(activity => !string.IsNullOrWhiteSpace(activity.Message))
            .ToList();
    }

    public static IReadOnlyList<IngestionDashboardStage> BuildStages(
        IngestionOperationsSnapshotViewModel? snapshot,
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        int totalFiles)
    {
        var activeKey = ResolveActiveStage(activeJobs);
        var reviewCount = snapshot?.Summary.ItemsNeedingReview ?? 0;
        var registeredCount = snapshot?.Summary.RegisteredItems ?? 0;
        var duplicateCount = Count(snapshot, "duplicate");
        var skippedCount = Count(snapshot, "skipped");
        var failedCount = Count(snapshot, "failed");
        var skippedOrDuplicate = duplicateCount + skippedCount;
        var retailMatched = Count(snapshot, "matched");
        var retailReview = Count(snapshot, "retail_review");
        var retailTotal = Math.Max(totalFiles, Total(snapshot, "matched", Math.Max(0, retailMatched + retailReview + skippedOrDuplicate)));
        var retailDone = retailMatched + retailReview + skippedOrDuplicate;
        var canonicalized = Count(snapshot, "canonicalized");
        var wikidataReview = Count(snapshot, "wikidata_review");
        var wikidataTotal = Total(snapshot, "canonicalized", Math.Max(0, canonicalized + wikidataReview));
        var wikidataDone = canonicalized + wikidataReview;
        var enriched = Count(snapshot, "enriched");
        var enrichmentTotal = Total(snapshot, "enriched", Math.Max(0, enriched));
        var scanningJobs = activeJobs
            .Where(job => ResolveActiveStage([job]) == "scanning")
            .ToList();
        var scanningTotal = scanningJobs.Sum(job => Math.Max(0, job.TotalCount));
        var scanningDone = scanningJobs.Sum(job => Math.Max(0, job.ProcessedCount));
        if (scanningTotal <= 0)
        {
            scanningTotal = Total(snapshot, "detected", totalFiles);
            scanningDone = Count(snapshot, "detected", 0);
        }

        var summaryTerminal = registeredCount
            + reviewCount
            + duplicateCount
            + skippedCount
            + failedCount;
        var summaryTotal = Math.Max(totalFiles, summaryTerminal);

        var stages = new List<IngestionDashboardStage>
        {
            CreateStage(
                "scanning",
                "Ingestion_StageScanning",
                "Ingestion_StageScanningDetail",
                Icons.Material.Outlined.Radar,
                scanningDone,
                scanningTotal,
                totalFiles,
                activeKey,
                hideCountWhenIdle: true),
            CreateStage(
                "retail",
                "Ingestion_StageRetailIdentification",
                "Ingestion_StageRetailIdentificationDetail",
                Icons.Material.Outlined.Search,
                retailDone,
                retailTotal,
                totalFiles,
                activeKey,
                matchedCount: retailMatched,
                reviewCount: retailReview,
                otherCount: skippedOrDuplicate),
            CreateStage(
                "wikidata",
                "Ingestion_StageWikidataMatch",
                "Ingestion_StageWikidataMatchDetail",
                Icons.Material.Outlined.TravelExplore,
                wikidataDone,
                wikidataTotal,
                totalFiles,
                activeKey),
            CreateStage(
                "enrichment",
                "Ingestion_StageEnrichment",
                "Ingestion_StageEnrichmentDetail",
                Icons.Material.Outlined.DataObject,
                enriched,
                enrichmentTotal,
                totalFiles,
                activeKey),
            CreateStage(
                "summary",
                "Ingestion_StageSummary",
                "Ingestion_StageSummaryDetail",
                Icons.Material.Outlined.AssignmentTurnedIn,
                summaryTerminal,
                summaryTotal,
                totalFiles,
                activeKey,
                matchedCount: registeredCount,
                reviewCount: reviewCount,
                otherCount: skippedOrDuplicate,
                isSummary: true),
        };

        return stages;

        static IngestionDashboardStage CreateStage(
            string key,
            string labelKey,
            string detailKey,
            string icon,
            int count,
            int total,
            int globalTotal,
            string activeKey,
            bool hideCountWhenIdle = false,
            int matchedCount = 0,
            int reviewCount = 0,
            int otherCount = 0,
            bool isSummary = false)
        {
            var percent = total > 0
                ? Math.Clamp(count * 100d / total, 0, 100)
                : globalTotal <= 0 && isSummary
                    ? 100
                    : 0;
            var isActive = activeKey == key;
            var hideCount = hideCountWhenIdle && !isActive && total <= 0;
            var status = isActive
                ? "Ingestion_StatusActive"
                : hideCount
                    ? "Ingestion_StatusIdle"
                    : percent >= 100
                        ? "Ingestion_StatusComplete"
                        : "Ingestion_StatusPending";
            return new IngestionDashboardStage(
                key,
                labelKey,
                detailKey,
                icon,
                count,
                total,
                percent,
                status,
                percent,
                hideCount,
                matchedCount,
                reviewCount,
                otherCount,
                isSummary);
        }
    }

    public static IngestionOverallProgress BuildOverallProgress(
        IngestionDashboardMetrics metrics,
        IReadOnlyList<IngestionDashboardStage> stages,
        BatchProgressEvent? batch)
    {
        var pipelineStages = stages.Take(5).ToList();
        var hasPipelineWork = metrics.TotalFiles > 0 || pipelineStages.Any(stage => stage.Total > 0 || stage.Count > 0);
        var percent = hasPipelineWork && pipelineStages.Count > 0
            ? Math.Clamp(pipelineStages.Average(stage => Math.Clamp(stage.Percent, 0, 100)), 0, 100)
            : metrics.TotalFiles > 0
                ? Math.Clamp(metrics.ProcessedFiles * 100d / metrics.TotalFiles, 0, 100)
                : 0;
        if (metrics.ActiveFiles > 0 && percent >= 100)
            percent = 99;

        var activeStage = stages.FirstOrDefault(stage => stage.StatusKey == "Ingestion_StatusActive")
            ?? stages.FirstOrDefault(stage => !stage.HideCount && stage.Percent < 100)
            ?? stages.LastOrDefault(stage => !stage.HideCount)
            ?? stages.LastOrDefault();

        return new IngestionOverallProgress(
            metrics.ProcessedFiles,
            metrics.TotalFiles,
            percent,
            activeStage?.LabelKey ?? "Ingestion_StageScanning",
            activeStage?.DetailKey ?? "Ingestion_StageScanningDetail",
            activeStage?.Count ?? 0,
            activeStage?.Total ?? 0,
            activeStage?.Percent ?? 0,
            batch?.EstimatedSecondsRemaining);
    }

    public static string ResolveActiveStage(IReadOnlyList<IngestionOperationsJobViewModel> activeJobs)
    {
        if (activeJobs.Count == 0)
            return string.Empty;

        var stage = activeJobs
            .Select(job => FirstNonBlank(job.CurrentStage, job.JobType))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.ToLowerInvariant() ?? string.Empty;

        if (stage.Contains("scan") || stage.Contains("queue") || stage.Contains("detect") || stage.Contains("parse"))
            return "scanning";
        if (stage.Contains("bridge") || stage.Contains("qid") || stage.Contains("wikidata") || stage.Contains("canonical"))
            return "wikidata";
        if (stage.Contains("identify") || stage.Contains("hash") || stage.Contains("fingerprint") || stage.Contains("match") || stage.Contains("retail"))
            return "retail";
        if (stage.Contains("enrich") || stage.Contains("hydrate") || stage.Contains("metadata") || stage.Contains("universe"))
            return "enrichment";
        if (stage.Contains("register") || stage.Contains("organize") || stage.Contains("review") || stage.Contains("complete"))
            return "summary";
        return "retail";
    }

    private static IngestionCurrentActivityViewModel ToCurrentActivity(
        IngestionOperationsJobViewModel job,
        IReadOnlyList<IngestionDashboardStage> stages)
    {
        var stageKey = ResolveActiveStage([job]);
        var stage = stages.FirstOrDefault(value => value.Key.Equals(stageKey, StringComparison.OrdinalIgnoreCase));
        var total = Math.Max(0, stage?.Total ?? job.TotalCount);
        var processed = Math.Clamp(Math.Max(0, stage?.Count ?? job.ProcessedCount), 0, Math.Max(0, total));
        var message = stageKey switch
        {
            "retail" => "Matching metadata",
            "wikidata" => "Checking Wikidata identity",
            "enrichment" => "Enriching relationships",
            "summary" => "Finishing library updates",
            _ => "Scanning files",
        };
        var item = FirstNonBlank(job.CurrentItem, job.CurrentStage, "Ingestion is running");

        return new IngestionCurrentActivityViewModel
        {
            StageKey = stageKey,
            Message = message,
            Detail = item,
            CurrentItem = item,
            Source = job.SourceFolder,
            ProcessedCount = processed,
            TotalCount = total,
            PercentComplete = total > 0 ? Math.Clamp(processed * 100d / total, 0, 100) : Math.Clamp(job.PercentComplete, 0, 100),
            LastUpdatedTime = job.LastUpdatedTime,
        };
    }

    public static IngestionLiveMode ResolveLiveMode(EngineConnectionState state) => state switch
    {
        EngineConnectionState.Online => IngestionLiveMode.Live,
        EngineConnectionState.Checking or EngineConnectionState.LiveUpdatesDisconnected => IngestionLiveMode.Reconnecting,
        _ => IngestionLiveMode.Polling,
    };

    private static int Count(IngestionOperationsSnapshotViewModel? snapshot, string key) =>
        snapshot?.PipelineStages.FirstOrDefault(stage => stage.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

    private static int Count(IngestionOperationsSnapshotViewModel? snapshot, string key, int fallback) =>
        snapshot?.PipelineStages.FirstOrDefault(stage => stage.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Count ?? fallback;

    private static int Total(IngestionOperationsSnapshotViewModel? snapshot, string key, int fallback)
    {
        var total = snapshot?.PipelineStages
            .FirstOrDefault(stage => stage.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.TotalCount ?? 0;
        return total > 0 ? total : fallback;
    }

    private static bool IsUsefulActivity(ActivityEntryViewModel activity) =>
        activity.ActionType.Contains("Ingest", StringComparison.OrdinalIgnoreCase)
        || activity.ActionType.Contains("MediaAdded", StringComparison.OrdinalIgnoreCase)
        || activity.ActionType.Contains("Batch", StringComparison.OrdinalIgnoreCase)
        || activity.ActionType.Contains("Review", StringComparison.OrdinalIgnoreCase)
        || activity.ActionType.Contains("Metadata", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public enum IngestionLiveMode
{
    Live,
    Reconnecting,
    Polling,
}

public sealed record IngestionDashboardMetrics(
    int TotalFiles,
    int ProcessedFiles,
    int ActiveFiles,
    int UnresolvedItems);

public sealed record IngestionOverallProgress(
    int ProcessedFiles,
    int TotalFiles,
    double Percent,
    string ActiveStageLabelKey,
    string ActiveStageDetailKey,
    int ActiveStageCount,
    int ActiveStageTotal,
    double ActiveStagePercent,
    int? EstimatedSecondsRemaining);

public sealed record IngestionDashboardStage(
    string Key,
    string LabelKey,
    string DetailKey,
    string Icon,
    int Count,
    int Total,
    double Percent,
    string StatusKey,
    double RingPercent,
    bool HideCount,
    int MatchedCount,
    int ReviewCount,
    int OtherCount,
    bool IsSummary);
