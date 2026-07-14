using System.Globalization;
using MediaEngine.Web.Models.ViewDTOs;
using MudBlazor;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class IngestionLiveDashboardState : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan LiveUniverseProgressFreshness = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LiveItemProgressFreshness = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveNotifyThrottle = TimeSpan.FromSeconds(2);

    private readonly IEngineApiClient _api;
    private readonly UIOrchestratorService _orchestrator;
    private readonly UniverseStateContainer _stateContainer;
    private readonly object _snapshotRefreshGate = new();
    private readonly object _liveNotifyGate = new();
    private readonly SemaphoreSlim _snapshotRefreshSignal = new(0, 1);
    private readonly SemaphoreSlim _liveNotifySignal = new(0, 1);
    private CancellationTokenSource? _backgroundCts;
    private Task? _pollTask;
    private Task? _snapshotRefreshTask;
    private Task? _liveNotifyTask;
    private string? _lastSnapshotSignature;
    private DateTimeOffset _lastLiveNotifyAt = DateTimeOffset.MinValue;
    private int _loadInProgress;
    private bool _loadAgainRequested;
    private bool _liveNotifyScheduled;
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
    public IReadOnlyDictionary<string, int> OperationsSummary { get; private set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> CapabilitySummary { get; private set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MediaOperationViewModel> Operations { get; private set; } = [];
    public IReadOnlyList<IngestionQueueHealthItem> QueueHealth => BuildQueueHealth(OperationsSummary, Operations);
    public IReadOnlyList<MediaOperationViewModel> FilteredOperations => FilterOperations(Operations, QueueFilter, QueueSort);
    public MediaOperationViewModel? PrimaryOperation => SelectPrimaryOperation(Operations);
    public IReadOnlyList<MediaOperationViewModel> QueuePreview => BuildQueuePreview(Operations);
    public MediaOperationDetailViewModel? ExpandedOperationDetail =>
        ExpandedOperationId is { } id && _operationDetails.TryGetValue(id, out var detail) ? detail : null;
    public IReadOnlyList<EntityCapabilityStateViewModel> ExpandedOperationCapabilities =>
        ExpandedOperationDetail?.Operation.EntityId is { } entityId
        && _capabilitiesByEntity.TryGetValue(entityId, out var capabilities)
            ? capabilities
            : [];
    public bool ShowDetails { get; private set; }
    public string QueueFilter { get; private set; } = "all";
    public string QueueSort { get; private set; } = "priority";
    public Guid? ExpandedOperationId { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsScanStarting { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? LastUpdated { get; private set; }

    private bool _detailsPreferenceSet;
    private readonly Dictionary<Guid, MediaOperationDetailViewModel> _operationDetails = new();
    private readonly Dictionary<Guid, IReadOnlyList<EntityCapabilityStateViewModel>> _capabilitiesByEntity = new();

    public EngineConnectionState ConnectionState => _orchestrator.EngineConnectionState;
    public IReadOnlyList<IngestionProviderActivityViewModel> ProviderActivity =>
        _stateContainer.ProviderActivity.Count > 0
            ? _stateContainer.ProviderActivity
            : Snapshot?.ProviderActivity ?? [];
    public IReadOnlyList<IngestionOperationsJobViewModel> ActiveJobs => BuildActiveJobs(Snapshot, _stateContainer);
    public IReadOnlyList<IngestionCurrentActivityViewModel> CurrentActivities => BuildCurrentActivities(Snapshot, ActiveJobs, Stages, _stateContainer);
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
        StartBackgroundTasks();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (System.Threading.Interlocked.Exchange(ref _loadInProgress, 1) == 1)
        {
            _loadAgainRequested = true;
            return;
        }

        try
        {
            do
            {
                _loadAgainRequested = false;
                await LoadSnapshotAsync(ct).ConfigureAwait(false);
            }
            while (_loadAgainRequested && !ct.IsCancellationRequested && !_disposed);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _loadInProgress, 0);
        }
    }

    private async Task LoadSnapshotAsync(CancellationToken ct)
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
            var reviewTask = _api.GetPendingReviewsAsync(200, ct);
            var operationSummaryTask = _api.GetMediaOperationsSummaryAsync(ct);
            var capabilitySummaryTask = _api.GetCapabilitySummaryAsync(ct);
            await Task.WhenAll(snapshotTask, activityTask, reviewTask, operationSummaryTask, capabilitySummaryTask);

            Snapshot = MergeSnapshot(Snapshot, snapshotTask.Result);
            RecentActivity = activityTask.Result
                .Where(IsUsefulActivity)
                .Take(12)
                .ToList();
            PendingReviews = reviewTask.Result;
            OperationsSummary = operationSummaryTask.Result;
            CapabilitySummary = capabilitySummaryTask.Result;

            if (!_detailsPreferenceSet)
                ShowDetails = ShouldDefaultShowDetails(OperationsSummary);

            if (ShowDetails || ShouldLoadOperationRows(OperationsSummary))
            {
                Operations = await _api.GetMediaOperationsAsync(limit: 100, ct: ct).ConfigureAwait(false);
                PruneOperationCaches();
            }
            else
            {
                Operations = [];
            }

            var nextSignature = BuildSnapshotSignature(
                Snapshot,
                RecentActivity,
                PendingReviews,
                OperationsSummary,
                Operations,
                ShowDetails,
                QueueFilter,
                QueueSort,
                ExpandedOperationId);
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

    private static IngestionOperationsSnapshotViewModel? MergeSnapshot(
        IngestionOperationsSnapshotViewModel? current,
        IngestionOperationsSnapshotViewModel? next)
    {
        if (next is null)
            return current;

        if (current is null)
        {
            NormalizeSnapshotLists(next);
            return next;
        }

        current.Summary = next.Summary;
        current.PipelineStages = next.PipelineStages;
        current.ReviewReasons = next.ReviewReasons;
        current.SourceGroups = next.SourceGroups;
        current.ProviderHealth = next.ProviderHealth;
        current.ProviderActivity = next.ProviderActivity;
        current.Organization = next.Organization;
        current.GeneratedAt = next.GeneratedAt;
        current.ActiveJobs = MergeByKey(
            current.ActiveJobs,
            next.ActiveJobs,
            job => job.JobId,
            CopyJob,
            OrderJobsStable);
        current.CurrentActivities = MergeByKey(
            current.CurrentActivities,
            next.CurrentActivities,
            activity => NormalizeActivityKey(activity.StageKey),
            CopyActivity,
            OrderActivitiesStable);
        current.StageProgress = MergeByKey(
            current.StageProgress,
            next.StageProgress,
            StageProgressKey,
            CopyStageProgress,
            stages => stages.OrderBy(stage => stage.StageNumber).ThenBy(stage => stage.StageKey, StringComparer.OrdinalIgnoreCase));
        current.RecentBatches = MergeByKey(
            current.RecentBatches,
            next.RecentBatches,
            batch => batch.BatchId,
            CopyBatch,
            OrderBatchesStable);
        return current;
    }

    private static void NormalizeSnapshotLists(IngestionOperationsSnapshotViewModel snapshot)
    {
        snapshot.ActiveJobs = OrderJobsStable(snapshot.ActiveJobs).ToList();
        snapshot.CurrentActivities = OrderActivitiesStable(snapshot.CurrentActivities).ToList();
        snapshot.StageProgress = snapshot.StageProgress
            .OrderBy(stage => stage.StageNumber)
            .ThenBy(stage => stage.StageKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        snapshot.RecentBatches = OrderBatchesStable(snapshot.RecentBatches).ToList();
    }

    private static List<TItem> MergeByKey<TItem, TKey>(
        IReadOnlyList<TItem> current,
        IReadOnlyList<TItem> next,
        Func<TItem, TKey> keySelector,
        Action<TItem, TItem> copy,
        Func<IEnumerable<TItem>, IEnumerable<TItem>> order)
        where TKey : notnull
    {
        var currentByKey = current
            .GroupBy(keySelector)
            .ToDictionary(group => group.Key, group => group.First());
        var merged = new List<TItem>(next.Count);

        foreach (var nextItem in next)
        {
            var key = keySelector(nextItem);
            if (currentByKey.TryGetValue(key, out var currentItem))
            {
                copy(currentItem, nextItem);
                merged.Add(currentItem);
            }
            else
            {
                merged.Add(nextItem);
            }
        }

        return order(merged).ToList();
    }

    private static IEnumerable<IngestionOperationsJobViewModel> OrderJobsStable(IEnumerable<IngestionOperationsJobViewModel> jobs) =>
        jobs
            .OrderBy(JobSortKey)
            .ThenBy(job => job.JobType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.MediaType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.JobId);

    private static int JobSortKey(IngestionOperationsJobViewModel job)
    {
        if (job.JobId == Guid.Empty)
            return 0;
        if (job.JobType.Contains("batch", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (job.JobType.Contains("reading", StringComparison.OrdinalIgnoreCase))
            return 2;
        return IsActiveBatchStatus(job.Status) ? 3 : 4;
    }

    private static IEnumerable<IngestionCurrentActivityViewModel> OrderActivitiesStable(IEnumerable<IngestionCurrentActivityViewModel> activities) =>
        activities
            .OrderBy(activity => ActivitySortKey(activity.StageKey))
            .ThenBy(activity => activity.StageKey, StringComparer.OrdinalIgnoreCase);

    private static int ActivitySortKey(string stageKey) => stageKey.ToLowerInvariant() switch
    {
        "scanning" or "scan" => 0,
        "retail" or "metadata" => 1,
        "wikidata" => 2,
        "people" => 3,
        "relationships" or "universes" => 4,
        "artwork" or "deep_artwork" => 5,
        _ => 99,
    };

    private static IEnumerable<IngestionOperationsBatchViewModel> OrderBatchesStable(IEnumerable<IngestionOperationsBatchViewModel> batches) =>
        batches
            .OrderByDescending(batch => IsActiveBatchStatus(batch.Status))
            .ThenByDescending(batch => batch.StartedAt)
            .ThenBy(batch => batch.BatchId);

    private static string NormalizeActivityKey(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(unknown)" : value.Trim().ToLowerInvariant();

    private static string StageProgressKey(IngestionStageProgressViewModel stage) =>
        $"{stage.StageNumber}:{NormalizeActivityKey(stage.StageKey)}";

    private static void CopyJob(IngestionOperationsJobViewModel target, IngestionOperationsJobViewModel source)
    {
        target.JobId = source.JobId;
        target.JobType = source.JobType;
        target.MediaType = source.MediaType;
        target.SourceFolder = source.SourceFolder;
        target.CurrentStage = source.CurrentStage;
        target.CurrentItem = source.CurrentItem;
        target.ProcessedCount = source.ProcessedCount;
        target.TotalCount = source.TotalCount;
        target.PercentComplete = source.PercentComplete;
        target.Status = source.Status;
        target.Elapsed = source.Elapsed;
        target.LastUpdatedTime = source.LastUpdatedTime;
        target.WarningSummary = source.WarningSummary;
    }

    private static void CopyActivity(IngestionCurrentActivityViewModel target, IngestionCurrentActivityViewModel source)
    {
        target.StageKey = source.StageKey;
        target.Message = source.Message;
        target.Detail = source.Detail;
        target.CurrentItem = source.CurrentItem;
        target.Source = source.Source;
        target.ProcessedCount = source.ProcessedCount;
        target.TotalCount = source.TotalCount;
        target.CountUnit = source.CountUnit;
        target.PercentComplete = source.PercentComplete;
        target.LastUpdatedTime = source.LastUpdatedTime;
        target.QueuedCount = source.QueuedCount;
        target.ActiveCount = source.ActiveCount;
        target.SampleItems = source.SampleItems;
        target.MetricLabel = source.MetricLabel;
        target.MetricValue = source.MetricValue;
        target.MetricTone = source.MetricTone;
        target.CurrentBatch = source.CurrentBatch;
    }

    private static void CopyStageProgress(IngestionStageProgressViewModel target, IngestionStageProgressViewModel source)
    {
        target.StageNumber = source.StageNumber;
        target.StageKey = source.StageKey;
        target.Label = source.Label;
        target.CompletedFiles = source.CompletedFiles;
        target.TotalFiles = source.TotalFiles;
        target.PercentComplete = source.PercentComplete;
        target.ActiveCount = source.ActiveCount;
        target.QueuedCount = source.QueuedCount;
        target.StatusLabel = source.StatusLabel;
        target.ActiveItemLabel = source.ActiveItemLabel;
        target.ActiveGroupLabel = source.ActiveGroupLabel;
        target.ActiveGroupCount = source.ActiveGroupCount;
        target.LabelAccuracy = source.LabelAccuracy;
        target.ArtifactLabel = source.ArtifactLabel;
        target.ArtifactCount = source.ArtifactCount;
        target.LastUpdatedTime = source.LastUpdatedTime;
        target.IsStale = source.IsStale;
        target.DetailItems = source.DetailItems;
    }

    private static void CopyBatch(IngestionOperationsBatchViewModel target, IngestionOperationsBatchViewModel source)
    {
        target.BatchId = source.BatchId;
        target.StartedAt = source.StartedAt;
        target.CompletedAt = source.CompletedAt;
        target.Source = source.Source;
        target.MediaType = source.MediaType;
        target.TotalFiles = source.TotalFiles;
        target.ProcessedFiles = source.ProcessedFiles;
        target.MoviesCount = source.MoviesCount;
        target.TvShowsCount = source.TvShowsCount;
        target.BooksCount = source.BooksCount;
        target.AudiobooksCount = source.AudiobooksCount;
        target.MusicCount = source.MusicCount;
        target.ComicsCount = source.ComicsCount;
        target.RegisteredCount = source.RegisteredCount;
        target.ReviewCount = source.ReviewCount;
        target.FailedCount = source.FailedCount;
        target.PeopleGeneratedCount = source.PeopleGeneratedCount;
        target.ArtworkDownloadedCount = source.ArtworkDownloadedCount;
        target.MetadataUpdatedCount = source.MetadataUpdatedCount;
        target.DurationSeconds = source.DurationSeconds;
        target.Status = source.Status;
        target.Summary = source.Summary;
        target.StageProgress = MergeByKey(
            target.StageProgress,
            source.StageProgress,
            StageProgressKey,
            CopyStageProgress,
            stages => stages.OrderBy(stage => stage.StageNumber).ThenBy(stage => stage.StageKey, StringComparer.OrdinalIgnoreCase));
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

    public async Task ToggleDetailsAsync(CancellationToken ct = default)
    {
        _detailsPreferenceSet = true;
        ShowDetails = !ShowDetails;
        Notify();

        if (ShowDetails && Operations.Count == 0)
        {
            Operations = await _api.GetMediaOperationsAsync(limit: 100, ct: ct).ConfigureAwait(false);
            PruneOperationCaches();
            Notify();
        }
    }

    public void SetQueueFilter(string filter)
    {
        QueueFilter = NormalizeQueueFilter(filter);
        Notify();
    }

    public void SetQueueSort(string sort)
    {
        QueueSort = NormalizeQueueSort(sort);
        Notify();
    }

    public async Task ToggleOperationExpandedAsync(Guid operationId, CancellationToken ct = default)
    {
        if (ExpandedOperationId == operationId)
        {
            ExpandedOperationId = null;
            Notify();
            return;
        }

        ExpandedOperationId = operationId;
        Notify();

        if (!_operationDetails.TryGetValue(operationId, out var detail))
        {
            detail = await _api.GetMediaOperationAsync(operationId, ct).ConfigureAwait(false);
            if (detail is not null)
                _operationDetails[operationId] = detail;
        }

        var entityId = detail?.Operation.EntityId;
        if (entityId is { } id && !_capabilitiesByEntity.ContainsKey(id))
        {
            _capabilitiesByEntity[id] = await _api.GetAssetCapabilitiesAsync(id, ct).ConfigureAwait(false);
        }

        Notify();
    }

    public async Task RetryOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        if (await _api.RetryMediaOperationAsync(operationId, ct).ConfigureAwait(false))
        {
            _operationDetails.Remove(operationId);
            await LoadAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task CancelOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        if (await _api.CancelMediaOperationAsync(operationId, ct).ConfigureAwait(false))
        {
            _operationDetails.Remove(operationId);
            await LoadAsync(ct).ConfigureAwait(false);
        }
    }

    private void StartBackgroundTasks()
    {
        if (_backgroundCts is not null || _disposed)
            return;

        _backgroundCts = new CancellationTokenSource();
        var token = _backgroundCts.Token;
        _pollTask = PollLoopAsync(token);
        _snapshotRefreshTask = SnapshotRefreshLoopAsync(token);
        _liveNotifyTask = LiveNotifyLoopAsync(token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = ActiveJobs.Count > 0 || ShouldLoadOperationRows(OperationsSummary)
                ? TimeSpan.FromSeconds(15)
                : TimeSpan.FromSeconds(40);
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
        NotifyLiveThrottled();
        if (_stateContainer.LastStateChangeRequiresSnapshotRefresh)
            DebounceSnapshotRefresh();
    }

    private void OnConnectionStateChanged(EngineConnectionState _)
    {
        Notify();
    }

    private void DebounceSnapshotRefresh()
    {
        lock (_snapshotRefreshGate)
        {
            if (_disposed || _snapshotRefreshSignal.CurrentCount > 0)
                return;

            _snapshotRefreshSignal.Release();
        }
    }

    private async Task SnapshotRefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _snapshotRefreshSignal.WaitAsync(ct).ConfigureAwait(false);

                bool receivedMoreSignals;
                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    receivedMoreSignals = false;
                    while (_snapshotRefreshSignal.Wait(0))
                        receivedMoreSignals = true;
                }
                while (receivedMoreSignals);

                await LoadAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void Notify()
    {
        if (!_disposed)
            OnChanged?.Invoke();
    }

    private void NotifyLiveThrottled()
    {
        var now = DateTimeOffset.UtcNow;
        TimeSpan delay;
        lock (_liveNotifyGate)
        {
            if (_disposed)
                return;

            if (!_liveNotifyScheduled && now - _lastLiveNotifyAt >= LiveNotifyThrottle)
            {
                _lastLiveNotifyAt = now;
                delay = TimeSpan.Zero;
            }
            else
            {
                if (_liveNotifyScheduled)
                    return;

                delay = LiveNotifyThrottle - (now - _lastLiveNotifyAt);
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;
                _liveNotifyScheduled = true;
            }
        }

        if (delay == TimeSpan.Zero)
        {
            Notify();
            return;
        }

        _liveNotifySignal.Release();
    }

    private async Task LiveNotifyLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _liveNotifySignal.WaitAsync(ct).ConfigureAwait(false);

                TimeSpan delay;
                lock (_liveNotifyGate)
                {
                    delay = LiveNotifyThrottle - (DateTimeOffset.UtcNow - _lastLiveNotifyAt);
                    if (delay < TimeSpan.Zero)
                        delay = TimeSpan.Zero;
                }

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct).ConfigureAwait(false);

                lock (_liveNotifyGate)
                {
                    _lastLiveNotifyAt = DateTimeOffset.UtcNow;
                    _liveNotifyScheduled = false;
                }

                Notify();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    public void Stop()
    {
        UnsubscribeAndCancel();
    }

    public async ValueTask StopAsync()
    {
        UnsubscribeAndCancel();

        var tasks = new[] { _pollTask, _snapshotRefreshTask, _liveNotifyTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (tasks.Length > 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);

        _backgroundCts?.Dispose();
        _backgroundCts = null;
        _pollTask = null;
        _snapshotRefreshTask = null;
        _liveNotifyTask = null;

        lock (_liveNotifyGate)
        {
            _liveNotifyScheduled = false;
        }

        while (_snapshotRefreshSignal.Wait(0)) { }
        while (_liveNotifySignal.Wait(0)) { }
    }

    private void UnsubscribeAndCancel()
    {
        if (_initialized)
        {
            _stateContainer.OnStateChanged -= OnRealtimeStateChanged;
            _orchestrator.OnEngineConnectionStateChanged -= OnConnectionStateChanged;
            _initialized = false;
        }

        _backgroundCts?.Cancel();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        lock (_snapshotRefreshGate)
        {
            _snapshotRefreshSignal.Dispose();
        }
        lock (_liveNotifyGate)
        {
            _liveNotifySignal.Dispose();
        }
        GC.SuppressFinalize(this);
    }

}
