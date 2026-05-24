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
    private string? _lastSnapshotSignature;
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
    public LibraryUpdateStatusViewModel LibraryUpdateStatus => BuildLibraryUpdateStatus(
        Snapshot,
        ActiveJobs,
        CurrentActivities,
        RecentActivity,
        PendingReviews,
        Metrics,
        Error,
        LastUpdated,
        DateTimeOffset.UtcNow);
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
        var shouldNotify = IsLoading;
        if (IsLoading)
            Notify();

        try
        {
            var snapshotTask = _api.GetIngestionOperationsSnapshotAsync(ct);
            var activityTask = _api.GetRecentActivityAsync(50, ct);
            var reviewTask = _api.GetPendingReviewsAsync(3, ct);
            await Task.WhenAll(snapshotTask, activityTask, reviewTask);

            Snapshot = snapshotTask.Result;
            RecentActivity = activityTask.Result
                .Where(IsUsefulActivity)
                .Take(12)
                .ToList();
            PendingReviews = reviewTask.Result;
            var nextSignature = BuildSnapshotSignature(Snapshot, RecentActivity, PendingReviews);
            var changed = !string.Equals(_lastSnapshotSignature, nextSignature, StringComparison.Ordinal);
            _lastSnapshotSignature = nextSignature;
            if (changed)
            {
                LastUpdated = DateTimeOffset.Now;
                shouldNotify = true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error = $"Ingestion status could not load: {ex.Message}";
            shouldNotify = true;
        }
        finally
        {
            IsLoading = false;
            if (shouldNotify)
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
            var delay = ActiveJobs.Count > 0 ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(40);
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
                await Task.Delay(TimeSpan.FromSeconds(10), token);
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
        var totalFiles = Total(snapshot, "detected", snapshot?.Summary.TotalItems ?? activeTotal);
        var fallbackProcessed = (snapshot?.Summary.RegisteredItems ?? 0) + (snapshot?.Summary.ItemsNeedingReview ?? 0);
        var processedFiles = Count(snapshot, "detected", fallbackProcessed);
        if (snapshot?.Summary.TotalItems > 0 && processedFiles == 0)
            processedFiles = Math.Min(snapshot.Summary.TotalItems, fallbackProcessed);

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
            .ToList() ?? [];

        if (activities.Count > 0)
        {
            return activities;
        }

        return activeJobs
            .Where(job => job.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
                || job.Status.Equals("processing", StringComparison.OrdinalIgnoreCase)
                || job.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
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
        var duplicateCount = Count(snapshot, "duplicate");
        var skippedCount = Count(snapshot, "skipped");
        var failedCount = Count(snapshot, "failed");
        var skippedOrDuplicate = duplicateCount + skippedCount;
        var terminalOther = skippedOrDuplicate + failedCount;
        var retailMatched = Count(snapshot, "matched");
        var retailReview = Count(snapshot, "retail_review");
        var retailTotal = Math.Max(totalFiles, Total(snapshot, "matched", Math.Max(0, retailMatched + retailReview + terminalOther)));
        var retailDone = retailMatched + retailReview + terminalOther;
        var canonicalized = Count(snapshot, "canonicalized");
        var wikidataReview = Count(snapshot, "wikidata_review");
        var wikidataTotal = Total(snapshot, "canonicalized", Math.Max(0, canonicalized + wikidataReview));
        var wikidataDone = canonicalized + wikidataReview;
        var enriched = Count(snapshot, "enriched");
        var enrichmentDone = enriched + wikidataReview;
        var enrichmentTotal = Total(snapshot, "enriched", Math.Max(0, enrichmentDone));
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
                otherCount: terminalOther),
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
                enrichmentDone,
                enrichmentTotal,
                totalFiles,
                activeKey),
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
            var status = isActive && percent < 100
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
        var pipelineStages = stages.Where(stage => !stage.HideCount).ToList();
        var hasPipelineWork = metrics.TotalFiles > 0 || pipelineStages.Any(stage => stage.Total > 0 || stage.Count > 0);
        var percent = hasPipelineWork && pipelineStages.Count > 0
            ? Math.Clamp(pipelineStages.Average(stage => Math.Clamp(stage.Percent, 0, 100)), 0, 100)
            : metrics.TotalFiles > 0
                ? Math.Clamp(metrics.ProcessedFiles * 100d / metrics.TotalFiles, 0, 100)
                : 0;

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

    public static LibraryUpdateStatusViewModel BuildLibraryUpdateStatus(
        IngestionOperationsSnapshotViewModel? snapshot,
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        IReadOnlyList<IngestionCurrentActivityViewModel> currentActivities,
        IReadOnlyList<ActivityEntryViewModel> recentActivity,
        IReadOnlyList<ReviewItemViewModel> pendingReviews,
        IngestionDashboardMetrics metrics,
        string? error,
        DateTimeOffset? lastUpdated,
        DateTimeOffset now)
    {
        var latestBatch = snapshot?.RecentBatches
            .OrderByDescending(batch => batch.StartedAt)
            .FirstOrDefault();
        var lastCompletedAt = ResolveLastCompletedAt(snapshot);
        var hasPriorRun = snapshot is not null
            && (snapshot.RecentBatches.Count > 0
                || lastCompletedAt.HasValue
                || snapshot.Summary.TotalItems > 0
                || metrics.TotalFiles > 0);
        var isRunning = activeJobs.Any(IsActiveJob)
            || snapshot?.Summary.ActiveJobs > 0;
        var pageState = ResolveLibraryUpdatePageState(
            snapshot,
            latestBatch,
            hasPriorRun,
            isRunning,
            lastCompletedAt,
            error,
            now);

        var totalFiles = Math.Max(0, metrics.TotalFiles);
        if (totalFiles == 0)
            totalFiles = Math.Max(0, latestBatch?.TotalFiles ?? snapshot?.Summary.TotalItems ?? 0);

        var processedFiles = Math.Max(0, metrics.ProcessedFiles);
        if (processedFiles == 0 && latestBatch is not null)
        {
            processedFiles = Math.Max(0, latestBatch.RegisteredCount + latestBatch.ReviewCount + latestBatch.FailedCount);
        }

        if (totalFiles > 0)
            processedFiles = Math.Clamp(processedFiles, 0, totalFiles);

        var matchedItems = Count(snapshot, "matched", snapshot?.Summary.RegisteredItems ?? latestBatch?.RegisteredCount ?? 0);
        if (matchedItems == 0)
            matchedItems = Math.Max(0, latestBatch?.RegisteredCount ?? 0);

        var reviewItems = Math.Max(
            Math.Max(0, snapshot?.Summary.ItemsNeedingReview ?? 0),
            Math.Max(0, latestBatch?.ReviewCount ?? 0));
        var activeItems = currentActivities.Sum(activity => Math.Max(0, activity.ActiveCount));
        if (activeItems == 0)
            activeItems = activeJobs.Count(job => IsActiveJob(job));

        var addedOrUpdatedCount = latestBatch is not null
            ? Math.Max(0, latestBatch.RegisteredCount + latestBatch.ReviewCount)
            : Math.Max(0, processedFiles);

        var activeStep = ResolveActiveLibraryUpdateStep(activeJobs, currentActivities);
        var progressPercent = ResolveLibraryUpdateProgress(pageState, activeJobs, processedFiles, totalFiles);
        var showProgress = pageState is LibraryUpdatePageState.Running or LibraryUpdatePageState.Complete or LibraryUpdatePageState.Failed;
        var isIndeterminate = pageState == LibraryUpdatePageState.Running && totalFiles == 0;
        var primaryActivity = SelectPrimaryActivity(activeJobs, currentActivities, activeStep);
        var currentItem = CleanDisplayTitle(FirstNonBlank(
            primaryActivity?.CurrentItem,
            primaryActivity?.CurrentBatch?.ActiveItems.FirstOrDefault(),
            activeJobs.Select(job => job.CurrentItem).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))));
        var currentStep = ResolveCurrentStepLabel(activeJobs, primaryActivity, activeStep);
        var currentSource = ResolveCurrentSource(primaryActivity);
        var activityLine = ResolveCurrentActivityLine(pageState, activeStep, totalFiles);
        var secondaryLine = ResolveSecondaryLine(pageState, activeItems, matchedItems, reviewItems, totalFiles, lastCompletedAt, now);
        var status = ResolveStatusPill(pageState, reviewItems);
        var recentItems = BuildRecentItems(recentActivity, currentActivities);
        var attentionReasons = BuildAttentionReasons(snapshot?.ReviewReasons ?? [], reviewItems);
        var steps = BuildLibraryUpdateSteps(pageState, activeStep, reviewItems);

        return new LibraryUpdateStatusViewModel(
            pageState,
            totalFiles,
            processedFiles,
            matchedItems,
            reviewItems,
            activeItems,
            addedOrUpdatedCount,
            progressPercent,
            showProgress,
            isIndeterminate,
            ResolveHeading(pageState),
            ResolveMainLine(pageState, processedFiles, totalFiles, lastCompletedAt, now),
            activityLine,
            secondaryLine,
            ResolveTimestampLine(pageState, lastCompletedAt, lastUpdated, now),
            status.Label,
            status.Tone,
            ResolvePanelTitle(pageState, hasPriorRun),
            ResolvePanelText(pageState, lastCompletedAt, now),
            currentItem,
            currentStep,
            currentSource,
            steps,
            attentionReasons,
            recentItems,
            hasPriorRun);
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
            return "enrichment";
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
            QueuedCount = Math.Max(0, total - processed),
            ActiveCount = 1,
            SampleItems = [item],
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

    private static LibraryUpdatePageState ResolveLibraryUpdatePageState(
        IngestionOperationsSnapshotViewModel? snapshot,
        IngestionOperationsBatchViewModel? latestBatch,
        bool hasPriorRun,
        bool isRunning,
        DateTimeOffset? lastCompletedAt,
        string? error,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return LibraryUpdatePageState.StatusUnavailable;

        if (isRunning)
            return LibraryUpdatePageState.Running;

        if (latestBatch is not null && IsFailedBatchStatus(latestBatch.Status))
            return LibraryUpdatePageState.Failed;

        if (lastCompletedAt is not null && now - lastCompletedAt.Value.ToUniversalTime() <= TimeSpan.FromSeconds(60))
            return LibraryUpdatePageState.Complete;

        return snapshot is null || !hasPriorRun
            ? LibraryUpdatePageState.NoPriorRun
            : LibraryUpdatePageState.Idle;
    }

    private static DateTimeOffset? ResolveLastCompletedAt(IngestionOperationsSnapshotViewModel? snapshot)
    {
        var lastBatchCompletion = snapshot?.RecentBatches
            .Where(batch => batch.CompletedAt.HasValue && !IsFailedBatchStatus(batch.Status))
            .OrderByDescending(batch => batch.CompletedAt)
            .Select(batch => batch.CompletedAt)
            .FirstOrDefault();

        return lastBatchCompletion ?? snapshot?.Summary.LastSuccessfulScanTime;
    }

    private static double ResolveLibraryUpdateProgress(
        LibraryUpdatePageState pageState,
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        int processedFiles,
        int totalFiles)
    {
        if (pageState == LibraryUpdatePageState.Complete)
            return 100;

        var calculatedPercent = totalFiles > 0
            ? Math.Clamp(processedFiles * 100d / totalFiles, 0, 100)
            : 0;
        var percent = calculatedPercent;

        return pageState == LibraryUpdatePageState.Running
            ? Math.Clamp(percent, 0, 100)
            : Math.Clamp(percent, 0, 100);
    }

    private static string BuildSnapshotSignature(
        IngestionOperationsSnapshotViewModel? snapshot,
        IReadOnlyList<ActivityEntryViewModel> recentActivity,
        IReadOnlyList<ReviewItemViewModel> pendingReviews)
    {
        if (snapshot is null)
            return $"null|activity:{recentActivity.Count}|reviews:{pendingReviews.Count}";

        var summary = snapshot.Summary;
        var active = string.Join(';', snapshot.ActiveJobs
            .OrderBy(job => job.JobId)
            .Select(job => string.Join(':',
                job.JobId,
                job.CurrentStage,
                job.ProcessedCount,
                job.TotalCount,
                Math.Round(job.PercentComplete, 1),
                job.Status)));
        var activities = string.Join(';', snapshot.CurrentActivities
            .OrderBy(activity => activity.StageKey)
            .Select(activity => string.Join(':',
                activity.StageKey,
                activity.CurrentItem,
                activity.ProcessedCount,
                activity.TotalCount,
                activity.ActiveCount,
                activity.QueuedCount)));

        return string.Join('|',
            summary.TotalItems,
            summary.RegisteredItems,
            summary.ItemsNeedingReview,
            summary.ActiveJobs,
            summary.FailedJobs,
            summary.LastSuccessfulScanTime?.ToUnixTimeSeconds(),
            active,
            activities,
            recentActivity.FirstOrDefault()?.Id,
            pendingReviews.Count);
    }

    private static int ResolveActiveLibraryUpdateStep(
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        IReadOnlyList<IngestionCurrentActivityViewModel> currentActivities)
    {
        var value = FirstNonBlank(
            activeJobs.Select(job => FirstNonBlank(job.CurrentStage, job.JobType)).FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)),
            currentActivities.FirstOrDefault(activity => activity.ActiveCount > 0)?.StageKey,
            currentActivities.FirstOrDefault(activity => activity.ActiveCount > 0)?.Message,
            currentActivities.FirstOrDefault(activity => activity.PercentComplete < 100)?.StageKey,
            currentActivities.FirstOrDefault(activity => activity.PercentComplete < 100)?.Message);

        if (string.IsNullOrWhiteSpace(value))
            return activeJobs.Count > 0 ? 1 : 4;

        var normalized = value.ToLowerInvariant();
        if (normalized.Contains("save") || normalized.Contains("register") || normalized.Contains("organize") || normalized.Contains("write"))
            return 4;
        if (normalized.Contains("artwork") || normalized.Contains("cover") || normalized.Contains("metadata")
            || normalized.Contains("hydrate") || normalized.Contains("enrich") || normalized.Contains("universe"))
        {
            return 3;
        }

        if (normalized.Contains("match") || normalized.Contains("identity") || normalized.Contains("identify")
            || normalized.Contains("retail") || normalized.Contains("wikidata") || normalized.Contains("qid") || normalized.Contains("bridge"))
        {
            return 2;
        }

        if (normalized.Contains("read") || normalized.Contains("parse") || normalized.Contains("hash") || normalized.Contains("process"))
            return 1;

        return normalized.Contains("scan") || normalized.Contains("detect") || normalized.Contains("queue")
            ? 0
            : 1;
    }

    private static IngestionCurrentActivityViewModel? SelectPrimaryActivity(
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        IReadOnlyList<IngestionCurrentActivityViewModel> currentActivities,
        int activeStep)
    {
        if (currentActivities.Count == 0)
            return null;

        var activeStageKey = ResolveActiveStage(activeJobs);
        var expectedStageKey = activeStep switch
        {
            0 or 1 => "scanning",
            2 => "wikidata",
            3 => "artwork",
            4 => "saving",
            _ => activeStageKey,
        };

        return currentActivities
            .Where(activity => !IsCompletedActivity(activity))
            .FirstOrDefault(activity => activity.StageKey.Equals(activeStageKey, StringComparison.OrdinalIgnoreCase))
            ?? currentActivities
                .Where(activity => !IsCompletedActivity(activity))
                .FirstOrDefault(activity => activity.StageKey.Equals(expectedStageKey, StringComparison.OrdinalIgnoreCase))
            ?? currentActivities.FirstOrDefault(activity => !IsCompletedActivity(activity))
            ?? currentActivities.FirstOrDefault();
    }

    private static bool IsCompletedActivity(IngestionCurrentActivityViewModel activity) =>
        activity.TotalCount > 0
        && activity.ProcessedCount >= activity.TotalCount
        && activity.ActiveCount <= 0
        && activity.QueuedCount <= 0;

    private static string ResolveCurrentStepLabel(
        IReadOnlyList<IngestionOperationsJobViewModel> activeJobs,
        IngestionCurrentActivityViewModel? primaryActivity,
        int activeStep)
    {
        var explicitStep = FirstNonBlank(
            primaryActivity?.Message,
            activeJobs.Select(job => job.CurrentStage).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(explicitStep))
            return ToFriendlyStepLabel(explicitStep);

        return activeStep switch
        {
            0 => "Scanning library folders",
            1 => "Reading media details",
            2 => "Matching titles and identity",
            3 => "Checking artwork and metadata",
            4 => "Saving library records",
            _ => "Checking library update",
        };
    }

    private static string ResolveCurrentSource(IngestionCurrentActivityViewModel? primaryActivity)
    {
        return LooksLikeProvider(primaryActivity?.Source)
            ? primaryActivity!.Source!
            : string.Empty;
    }

    private static bool LooksLikeProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return !trimmed.Equals("Watch folders", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals("Current file", StringComparison.OrdinalIgnoreCase)
            && !KnownMediaExtensions.Any(extension => trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            && !trimmed.Contains('\\')
            && !trimmed.Contains('/')
            && !trimmed.Contains(':');
    }

    private static string ResolveCurrentActivityLine(LibraryUpdatePageState pageState, int activeStep, int totalFiles) =>
        pageState switch
        {
            LibraryUpdatePageState.Running when totalFiles == 0 => "Looking for new media",
            LibraryUpdatePageState.Running => activeStep switch
            {
                0 => "Currently scanning library folders",
                1 => "Currently reading media details",
                2 => "Currently matching titles and identity",
                3 => "Currently matching artwork and metadata",
                4 => "Currently saving library records",
                _ => "Currently updating your library",
            },
            LibraryUpdatePageState.Complete => string.Empty,
            LibraryUpdatePageState.Idle => string.Empty,
            LibraryUpdatePageState.Failed => "Tuvima stopped before the update was complete.",
            LibraryUpdatePageState.StatusUnavailable => "Status temporarily unavailable",
            _ => "Start a scan to find new or changed media.",
        };

    private static string? ResolveSecondaryLine(
        LibraryUpdatePageState pageState,
        int activeItems,
        int matchedItems,
        int reviewItems,
        int totalFiles,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset now)
    {
        return pageState switch
        {
            LibraryUpdatePageState.Running when activeItems > 0 => $"{activeItems.ToString("N0", CultureInfo.CurrentCulture)} items are being checked now",
            LibraryUpdatePageState.Complete => $"{matchedItems.ToString("N0", CultureInfo.CurrentCulture)} matched · {reviewItems.ToString("N0", CultureInfo.CurrentCulture)} need review",
            LibraryUpdatePageState.Idle => $"{totalFiles.ToString("N0", CultureInfo.CurrentCulture)} files processed · {matchedItems.ToString("N0", CultureInfo.CurrentCulture)} matched · {reviewItems.ToString("N0", CultureInfo.CurrentCulture)} need review",
            LibraryUpdatePageState.Failed => $"{totalFiles.ToString("N0", CultureInfo.CurrentCulture)} files found · {matchedItems.ToString("N0", CultureInfo.CurrentCulture)} matched · {reviewItems.ToString("N0", CultureInfo.CurrentCulture)} need review",
            LibraryUpdatePageState.StatusUnavailable when lastCompletedAt.HasValue => $"Last successful scan completed {FormatRelativeLong(lastCompletedAt.Value, now)}",
            _ => null,
        };
    }

    private static (string Label, string Tone) ResolveStatusPill(LibraryUpdatePageState pageState, int reviewItems) =>
        pageState switch
        {
            LibraryUpdatePageState.Running => ("In progress", "info"),
            LibraryUpdatePageState.Complete => ("Complete", reviewItems > 0 ? "warning" : "success"),
            LibraryUpdatePageState.Idle => ("Ready", reviewItems > 0 ? "warning" : "success"),
            LibraryUpdatePageState.Failed => ("Could not finish", "danger"),
            LibraryUpdatePageState.StatusUnavailable => ("Unavailable", "warning"),
            _ => ("Ready", "neutral"),
        };

    private static string ResolveHeading(LibraryUpdatePageState pageState) =>
        pageState switch
        {
            LibraryUpdatePageState.Running => "Updating your library",
            LibraryUpdatePageState.Complete => "Library update complete",
            LibraryUpdatePageState.Idle => "Library is up to date",
            LibraryUpdatePageState.Failed => "Library update could not finish",
            LibraryUpdatePageState.StatusUnavailable => "Status temporarily unavailable",
            _ => "Ready to scan your library",
        };

    private static string ResolveMainLine(
        LibraryUpdatePageState pageState,
        int processedFiles,
        int totalFiles,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset now)
    {
        return pageState switch
        {
            LibraryUpdatePageState.Running when totalFiles == 0 => "Looking for new media",
            LibraryUpdatePageState.Running or LibraryUpdatePageState.Complete => $"{processedFiles.ToString("N0", CultureInfo.CurrentCulture)} of {totalFiles.ToString("N0", CultureInfo.CurrentCulture)} files processed",
            LibraryUpdatePageState.Idle when lastCompletedAt.HasValue => $"Last scan completed {FormatRelativeLong(lastCompletedAt.Value, now)}",
            LibraryUpdatePageState.Failed => "Tuvima stopped before the update was complete.",
            LibraryUpdatePageState.StatusUnavailable => "Tuvima could not refresh the latest status.",
            _ => "Start a scan to find new or changed media.",
        };
    }

    private static string ResolveTimestampLine(
        LibraryUpdatePageState pageState,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset? lastUpdated,
        DateTimeOffset now)
    {
        return pageState switch
        {
            LibraryUpdatePageState.Running => "Updated just now",
            LibraryUpdatePageState.Complete or LibraryUpdatePageState.Idle when lastCompletedAt.HasValue => $"Last scan completed {FormatRelativeLong(lastCompletedAt.Value, now)}",
            LibraryUpdatePageState.StatusUnavailable when lastUpdated.HasValue => $"Last known update: {FormatRelativeShort(lastUpdated.Value, now)}",
            _ => "No recent library update",
        };
    }

    private static string ResolvePanelTitle(LibraryUpdatePageState pageState, bool hasPriorRun) =>
        pageState switch
        {
            LibraryUpdatePageState.Running => "What's happening now",
            LibraryUpdatePageState.NoPriorRun when !hasPriorRun => "No library updates have run yet",
            _ => "Last update summary",
        };

    private static string ResolvePanelText(
        LibraryUpdatePageState pageState,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset now)
    {
        return pageState switch
        {
            LibraryUpdatePageState.Running => "Tuvima is matching new media against retail providers, then confirming identity with Wikidata.",
            LibraryUpdatePageState.Complete => "Tuvima finished scanning and matching your library.",
            LibraryUpdatePageState.Idle when lastCompletedAt.HasValue => $"Your library is ready. The most recent scan finished {FormatRelativeLong(lastCompletedAt.Value, now)}.",
            LibraryUpdatePageState.Failed => "Tuvima stopped before the update was complete.",
            LibraryUpdatePageState.StatusUnavailable => "Tuvima could not refresh the latest status. Last known counts are still shown.",
            _ => "No library updates have run yet.",
        };
    }

    private static IReadOnlyList<LibraryUpdateStepViewModel> BuildLibraryUpdateSteps(
        LibraryUpdatePageState pageState,
        int activeStep,
        int reviewItems)
    {
        var labels = new[]
        {
            ("Scanned folders", Icons.Material.Outlined.FolderOpen),
            ("Read media details", Icons.Material.Outlined.MenuBook),
            ("Matched identity", Icons.Material.Outlined.Link),
            ("Collected artwork", Icons.Material.Outlined.ImageSearch),
            ("Saved to library", Icons.Material.Outlined.Storage),
        };

        return labels
            .Select((step, index) =>
            {
                var status = ResolveStepStatus(pageState, index, activeStep, reviewItems);
                return new LibraryUpdateStepViewModel(step.Item1, step.Item2, status, StepStatusLabel(status));
            })
            .ToList();
    }

    private static LibraryUpdateStepStatus ResolveStepStatus(
        LibraryUpdatePageState pageState,
        int stepIndex,
        int activeStep,
        int reviewItems)
    {
        return pageState switch
        {
            LibraryUpdatePageState.NoPriorRun => LibraryUpdateStepStatus.Pending,
            LibraryUpdatePageState.Complete or LibraryUpdatePageState.Idle => reviewItems > 0 && stepIndex == 2
                ? LibraryUpdateStepStatus.NeedsReview
                : LibraryUpdateStepStatus.Complete,
            LibraryUpdatePageState.Failed => stepIndex < activeStep
                ? LibraryUpdateStepStatus.Complete
                : stepIndex == activeStep ? LibraryUpdateStepStatus.NeedsReview : LibraryUpdateStepStatus.Pending,
            LibraryUpdatePageState.StatusUnavailable => stepIndex < activeStep
                ? LibraryUpdateStepStatus.Complete
                : stepIndex == activeStep ? LibraryUpdateStepStatus.NeedsReview : LibraryUpdateStepStatus.Pending,
            _ => stepIndex < activeStep
                ? LibraryUpdateStepStatus.Complete
                : stepIndex == activeStep ? LibraryUpdateStepStatus.InProgress : LibraryUpdateStepStatus.Pending,
        };
    }

    private static string StepStatusLabel(LibraryUpdateStepStatus status) =>
        status switch
        {
            LibraryUpdateStepStatus.Complete => "Complete",
            LibraryUpdateStepStatus.InProgress => "In progress",
            LibraryUpdateStepStatus.NeedsReview => "Needs review",
            _ => "Pending",
        };

    private static IReadOnlyList<LibraryUpdateAttentionReasonViewModel> BuildAttentionReasons(
        IReadOnlyList<IngestionReviewReasonViewModel> reasons,
        int reviewItems)
    {
        if (reviewItems <= 0)
            return [];

        var uncertain = SumReasonCounts(reasons, "low_confidence");
        var missingIdentity = SumReasonCounts(reasons, "missing_wikidata", "unmatched");
        var duplicates = SumReasonCounts(reasons, "duplicates");
        var missingArtwork = SumReasonCounts(reasons, "missing_artwork");
        var rows = new List<LibraryUpdateAttentionReasonViewModel>();

        AddReason(rows, uncertain, "uncertain matches");
        AddReason(rows, missingIdentity, "missing identity");
        AddReason(rows, duplicates, "possible duplicates");
        AddReason(rows, missingArtwork, "missing artwork");

        if (rows.Count == 0)
            AddReason(rows, reviewItems, "items awaiting review");

        return rows;
    }

    private static int SumReasonCounts(IReadOnlyList<IngestionReviewReasonViewModel> reasons, params string[] keys) =>
        reasons
            .Where(reason => keys.Any(key => reason.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Sum(reason => Math.Max(0, reason.Count));

    private static void AddReason(List<LibraryUpdateAttentionReasonViewModel> rows, int count, string label)
    {
        if (count > 0)
            rows.Add(new LibraryUpdateAttentionReasonViewModel(count, label));
    }

    private static IReadOnlyList<LibraryUpdateRecentItemViewModel> BuildRecentItems(
        IReadOnlyList<ActivityEntryViewModel> recentActivity,
        IReadOnlyList<IngestionCurrentActivityViewModel> currentActivities)
    {
        var activityItems = recentActivity
            .Where(IsRecentLibraryUpdateActivity)
            .Select(ToRecentItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Take(5)
            .ToList();

        if (activityItems.Count > 0)
            return activityItems;

        return currentActivities
            .SelectMany(activity => activity.CurrentBatch?.CompletedPreview ?? [])
            .Concat(currentActivities.SelectMany(activity => activity.SampleItems))
            .Select(CleanDisplayTitle)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(title => new LibraryUpdateRecentItemViewModel(
                title,
                "Added or updated in this library update",
                "Updated",
                "success",
                "Just now",
                null,
                null))
            .ToList();
    }

    private static bool IsRecentLibraryUpdateActivity(ActivityEntryViewModel activity) =>
        activity.ActionType.Equals("MediaAdded", StringComparison.OrdinalIgnoreCase)
        || activity.ActionType.Equals("FileIngested", StringComparison.OrdinalIgnoreCase)
        || activity.ActionType.Equals("ReviewItemResolved", StringComparison.OrdinalIgnoreCase);

    private static LibraryUpdateRecentItemViewModel ToRecentItem(ActivityEntryViewModel activity)
    {
        var rich = activity.RichData;
        var review = activity.ReviewData;
        var title = CleanDisplayTitle(FirstNonBlank(
            rich?.Title,
            review?.Title,
            activity.CollectionName,
            ExtractTitleFromDetail(activity.Detail),
            "Library item"));
        var needsReview = rich?.NeedsReview == true || activity.ActionType.Contains("Review", StringComparison.OrdinalIgnoreCase);
        var hasArtwork = !string.IsNullOrWhiteSpace(rich?.ResolvedCoverUrl) || !string.IsNullOrWhiteSpace(review?.CoverUrl);
        var hasWikidata = !string.IsNullOrWhiteSpace(rich?.WikidataQid) || !string.IsNullOrWhiteSpace(review?.Qid);
        var statusText = needsReview
            ? "Added as provisional · Needs identity review"
            : hasWikidata
                ? hasArtwork ? "Matched · Artwork found · Wikidata confirmed" : "Matched · Wikidata confirmed"
                : hasArtwork ? "Matched · Artwork found" : "Added or updated";
        var statusLabel = needsReview ? "Provisional" : "Matched";
        var statusTone = needsReview ? "warning" : "success";

        return new LibraryUpdateRecentItemViewModel(
            title,
            statusText,
            statusLabel,
            statusTone,
            activity.RelativeTime,
            rich?.ResolvedCoverUrl ?? review?.CoverUrl,
            ResolveDetailHref(activity));
    }

    private static string? ExtractTitleFromDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;

        var trimmed = detail.Trim();
        var quoted = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
        if (quoted.Length >= 2)
            return quoted[1];

        return trimmed.Contains(':', StringComparison.Ordinal)
            ? trimmed[(trimmed.LastIndexOf(':') + 1)..].Trim()
            : trimmed;
    }

    private static string? ResolveDetailHref(ActivityEntryViewModel activity)
    {
        var id = FirstNonBlank(activity.EntityId, activity.RichData?.EntityId, activity.ReviewData?.EntityId);
        if (!Guid.TryParse(id, out var entityId))
            return null;

        var entityType = FirstNonBlank(activity.EntityType, "work").ToLowerInvariant() switch
        {
            "movie" or "film" => "movie",
            "tvshow" or "tv_show" or "show" or "series" => "tv-show",
            "tvepisode" or "tv_episode" or "episode" => "tv-episode",
            _ => "work",
        };
        return $"/details/{entityType}/{entityId:D}";
    }

    private static bool IsActiveJob(IngestionOperationsJobViewModel job) =>
        job.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
        || job.Status.Equals("processing", StringComparison.OrdinalIgnoreCase)
        || job.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
        || job.Status.Equals("queued", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedBatchStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && (status.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || status.Contains("abandon", StringComparison.OrdinalIgnoreCase));

    private static string ToFriendlyStepLabel(string value)
    {
        var normalized = value.Replace('_', ' ').Trim();
        if (normalized.Contains("artwork", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cover", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("metadata", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("hydrat", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("enrich", StringComparison.OrdinalIgnoreCase))
        {
            return "Checking artwork and metadata";
        }

        if (normalized.Contains("wikidata", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("qid", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("match", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("retail", StringComparison.OrdinalIgnoreCase))
        {
            return "Matching titles and identity";
        }

        if (normalized.Contains("scan", StringComparison.OrdinalIgnoreCase))
            return "Scanning library folders";

        if (normalized.Contains("read", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("parse", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return "Reading media details";
        }

        if (normalized.Contains("save", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("register", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("organize", StringComparison.OrdinalIgnoreCase))
        {
            return "Saving library records";
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string CleanDisplayTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        var fileName = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(fileName))
            trimmed = fileName;

        foreach (var mediaExtension in KnownMediaExtensions)
        {
            if (trimmed.EndsWith(mediaExtension, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^mediaExtension.Length];
                break;
            }
        }

        var extension = Path.GetExtension(trimmed);
        if (!string.IsNullOrWhiteSpace(extension)
            && extension.Length <= 6
            && extension.Skip(1).All(char.IsLetterOrDigit))
        {
            trimmed = Path.GetFileNameWithoutExtension(trimmed);
        }

        return trimmed.Replace('_', ' ');
    }

    private static readonly string[] KnownMediaExtensions =
    [
        ".epub",
        ".pdf",
        ".cbz",
        ".cbr",
        ".m4b",
        ".mp3",
        ".flac",
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
    ];

    private static string FormatRelativeLong(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now.ToUniversalTime() - value.ToUniversalTime();
        return elapsed.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)elapsed.TotalMinutes} minutes ago",
            < 120 => "1 hour ago",
            < 1440 => $"{(int)elapsed.TotalHours} hours ago",
            < 2880 => "1 day ago",
            _ => $"{(int)elapsed.TotalDays} days ago",
        };
    }

    private static string FormatRelativeShort(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now.ToUniversalTime() - value.ToUniversalTime();
        return elapsed.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)elapsed.TotalMinutes}m ago",
            < 1440 => $"{(int)elapsed.TotalHours}h ago",
            _ => value.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture),
        };
    }

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

public enum LibraryUpdatePageState
{
    NoPriorRun,
    Running,
    Complete,
    Idle,
    Failed,
    StatusUnavailable,
}

public enum LibraryUpdateStepStatus
{
    Complete,
    InProgress,
    Pending,
    NeedsReview,
}

public sealed record LibraryUpdateStatusViewModel(
    LibraryUpdatePageState PageState,
    int TotalFiles,
    int ProcessedFiles,
    int MatchedItems,
    int ReviewItems,
    int ActiveItems,
    int AddedOrUpdatedCount,
    double ProgressPercent,
    bool ShowProgress,
    bool IsIndeterminate,
    string Heading,
    string MainLine,
    string ActivityLine,
    string? SecondaryLine,
    string TimestampLine,
    string StatusLabel,
    string StatusTone,
    string PanelTitle,
    string PanelText,
    string? CurrentItemTitle,
    string CurrentStep,
    string? CurrentProvider,
    IReadOnlyList<LibraryUpdateStepViewModel> Steps,
    IReadOnlyList<LibraryUpdateAttentionReasonViewModel> AttentionReasons,
    IReadOnlyList<LibraryUpdateRecentItemViewModel> RecentItems,
    bool HasPriorRun);

public sealed record LibraryUpdateStepViewModel(
    string Label,
    string Icon,
    LibraryUpdateStepStatus Status,
    string StatusLabel);

public sealed record LibraryUpdateAttentionReasonViewModel(int Count, string Label);

public sealed record LibraryUpdateRecentItemViewModel(
    string Title,
    string StatusText,
    string StatusLabel,
    string StatusTone,
    string RelativeTime,
    string? ThumbnailUrl,
    string? Href);
