using System.Text.Json.Serialization;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Services.Playback;

public sealed class ListenPlaybackService
{
    private readonly UIOrchestratorService _orchestrator;
    private readonly IEngineApiClient _apiClient;
    private readonly List<ListenQueueItem> _queue = [];
    private readonly List<ListenQueueItem> _history = [];

    public ListenPlaybackService(UIOrchestratorService orchestrator, IEngineApiClient apiClient)
    {
        _orchestrator = orchestrator;
        _apiClient = apiClient;
    }

    public event Action? OnChanged;

    public IReadOnlyList<ListenQueueItem> Queue => _queue;
    public IReadOnlyList<ListenQueueItem> History => _history;
    public IReadOnlyList<ListenQueueItem> UpcomingQueue =>
        CurrentIndex < 0 || CurrentIndex >= _queue.Count
            ? _queue
            : _queue.Skip(CurrentIndex + 1).ToList();

    public int CurrentIndex { get; private set; } = -1;
    public string? SourceLabel { get; private set; }
    public bool IsPanelOpen { get; private set; }
    public string ActiveTab { get; private set; } = ListenPlaybackTabs.Queue;
    public bool IsDismissed { get; private set; }
    public double CurrentTimeSeconds { get; private set; }
    public double DurationSeconds { get; private set; }
    public double Volume { get; private set; } = 0.8d;
    public bool IsMuted { get; private set; }
    public bool IsPlaying { get; private set; } = true;
    public bool IsPopupOpen { get; private set; }

    public bool HasQueue => _queue.Count > 0 && !IsDismissed;

    public ListenQueueItem? CurrentItem =>
        CurrentIndex >= 0 && CurrentIndex < _queue.Count
            ? _queue[CurrentIndex]
            : null;

    public string? CurrentStreamUrl => CurrentItem?.StreamUrl;

    public async Task PlayWorkAsync(WorkViewModel work, string? sourceLabel = null, CancellationToken ct = default)
    {
        await ReplaceQueueAsync([work], 0, sourceLabel ?? work.Album ?? work.Title, false, ct);
    }

    public Task PlayQueueItemAsync(ListenQueueItem item, string? sourceLabel = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        RememberCurrentItem();
        _queue.Clear();
        _queue.Add(item);
        CurrentIndex = 0;
        SourceLabel = sourceLabel ?? item.Album ?? item.Title;
        IsDismissed = false;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        IsPlaying = true;
        NotifyChanged();
        return Task.CompletedTask;
    }

    public async Task ReplaceQueueAsync(
        IEnumerable<WorkViewModel> works,
        int startIndex,
        string? sourceLabel,
        bool shuffle,
        CancellationToken ct = default)
    {
        var items = works.Select(CreateQueueItem).ToList();
        if (items.Count == 0)
        {
            ClosePlayer();
            return;
        }

        RememberCurrentItem();

        if (shuffle)
        {
            var random = new Random();
            items = items.OrderBy(_ => random.Next()).ToList();
            startIndex = 0;
        }

        _queue.Clear();
        _queue.AddRange(items);
        CurrentIndex = Math.Clamp(startIndex, 0, _queue.Count - 1);
        SourceLabel = sourceLabel;
        IsDismissed = false;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        IsPlaying = true;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public async Task InsertNextAsync(WorkViewModel work, CancellationToken ct = default)
    {
        var item = CreateQueueItem(work);
        if (_queue.Count == 0)
        {
            _queue.Add(item);
            CurrentIndex = 0;
            SourceLabel = work.Album ?? work.Title;
            IsDismissed = false;
            IsPlaying = true;
            await EnsurePlayableAsync(CurrentIndex, ct);
            NotifyChanged();
            return;
        }

        var insertIndex = Math.Clamp(CurrentIndex + 1, 0, _queue.Count);
        _queue.Insert(insertIndex, item);
        NotifyChanged();
    }

    public async Task AddToQueueAsync(WorkViewModel work, CancellationToken ct = default)
    {
        var item = CreateQueueItem(work);
        if (_queue.Count == 0)
        {
            _queue.Add(item);
            CurrentIndex = 0;
            SourceLabel = work.Album ?? work.Title;
            IsDismissed = false;
            IsPlaying = true;
            await EnsurePlayableAsync(CurrentIndex, ct);
            NotifyChanged();
            return;
        }

        _queue.Add(item);
        NotifyChanged();
    }

    public async Task PlayIndexAsync(int index, CancellationToken ct = default)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        if (index != CurrentIndex)
        {
            RememberCurrentItem();
        }

        CurrentIndex = index;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        IsDismissed = false;
        IsPlaying = true;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public async Task<bool> SkipNextAsync(CancellationToken ct = default)
    {
        if (CurrentIndex + 1 >= _queue.Count)
        {
            CurrentTimeSeconds = DurationSeconds;
            IsPlaying = false;
            NotifyChanged();
            return false;
        }

        RememberCurrentItem();
        CurrentIndex++;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        IsPlaying = true;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
        return true;
    }

    public async Task SkipPreviousAsync(CancellationToken ct = default)
    {
        if (CurrentIndex <= 0)
        {
            CurrentTimeSeconds = 0;
            NotifyChanged();
            return;
        }

        CurrentIndex--;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        IsPlaying = true;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public async Task CompleteCurrentAsync(CancellationToken ct = default)
    {
        if (CurrentItem is not null)
        {
            AddHistoryItem(CurrentItem with { PlayedAt = DateTimeOffset.UtcNow });
        }

        if (CurrentIndex + 1 >= _queue.Count)
        {
            IsPlaying = false;
            CurrentTimeSeconds = DurationSeconds;
            NotifyChanged();
            return;
        }

        CurrentIndex++;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        IsPlaying = true;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public void RemoveUpcomingAt(int absoluteIndex)
    {
        if (absoluteIndex < 0 || absoluteIndex >= _queue.Count || absoluteIndex <= CurrentIndex)
        {
            return;
        }

        _queue.RemoveAt(absoluteIndex);
        NotifyChanged();
    }

    public void ClearUpcoming()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        if (CurrentIndex < 0)
        {
            _queue.Clear();
            CurrentIndex = -1;
        }
        else if (CurrentIndex + 1 < _queue.Count)
        {
            _queue.RemoveRange(CurrentIndex + 1, _queue.Count - (CurrentIndex + 1));
        }

        NotifyChanged();
    }

    public void TogglePanel()
    {
        IsPanelOpen = !IsPanelOpen;
        NotifyChanged();
    }

    public void ClosePanel()
    {
        if (!IsPanelOpen)
        {
            return;
        }

        IsPanelOpen = false;
        NotifyChanged();
    }

    public void SetActiveTab(string tab)
    {
        ActiveTab = string.Equals(tab, ListenPlaybackTabs.History, StringComparison.OrdinalIgnoreCase)
            ? ListenPlaybackTabs.History
            : ListenPlaybackTabs.Queue;
        NotifyChanged();
    }

    public void UpdateTransportState(
        double? currentTimeSeconds = null,
        double? durationSeconds = null,
        bool? isPlaying = null,
        double? volume = null,
        bool? isMuted = null)
    {
        var changed = false;

        if (currentTimeSeconds.HasValue)
        {
            var next = Math.Max(0, currentTimeSeconds.Value);
            if (Math.Abs(CurrentTimeSeconds - next) >= 1)
            {
                CurrentTimeSeconds = next;
                changed = true;
            }
        }

        if (durationSeconds.HasValue)
        {
            var next = Math.Max(0, durationSeconds.Value);
            if (Math.Abs(DurationSeconds - next) >= 1)
            {
                DurationSeconds = next;
                changed = true;
            }
        }

        if (isPlaying.HasValue)
        {
            if (IsPlaying != isPlaying.Value)
            {
                IsPlaying = isPlaying.Value;
                changed = true;
            }
        }

        if (volume.HasValue)
        {
            var next = Math.Clamp(volume.Value, 0d, 1d);
            if (Math.Abs(Volume - next) >= 0.01d)
            {
                Volume = next;
                changed = true;
            }
        }

        if (isMuted.HasValue)
        {
            if (IsMuted != isMuted.Value)
            {
                IsMuted = isMuted.Value;
                changed = true;
            }
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    public void SetPopupOpen(bool isOpen)
    {
        if (IsPopupOpen == isOpen)
        {
            return;
        }

        IsPopupOpen = isOpen;
        NotifyChanged();
    }

    public void ClosePlayer()
    {
        _queue.Clear();
        _history.Clear();
        CurrentIndex = -1;
        SourceLabel = null;
        IsPanelOpen = false;
        ActiveTab = ListenPlaybackTabs.Queue;
        IsDismissed = true;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        Volume = 0.8d;
        IsMuted = false;
        IsPlaying = false;
        IsPopupOpen = false;
        NotifyChanged();
    }

    public void RestoreState(ListenPlaybackSnapshot snapshot)
    {
        _queue.Clear();
        _queue.AddRange(snapshot.Queue ?? []);
        _history.Clear();
        _history.AddRange(snapshot.History ?? []);
        CurrentIndex = _queue.Count == 0
            ? -1
            : Math.Clamp(snapshot.CurrentIndex, 0, _queue.Count - 1);
        SourceLabel = snapshot.SourceLabel;
        IsPanelOpen = snapshot.IsPanelOpen;
        ActiveTab = string.Equals(snapshot.ActiveTab, ListenPlaybackTabs.History, StringComparison.OrdinalIgnoreCase)
            ? ListenPlaybackTabs.History
            : ListenPlaybackTabs.Queue;
        IsDismissed = snapshot.IsDismissed || _queue.Count == 0;
        CurrentTimeSeconds = Math.Max(0, snapshot.CurrentTimeSeconds);
        DurationSeconds = Math.Max(0, snapshot.DurationSeconds);
        Volume = snapshot.Volume is > 0 and <= 1 ? snapshot.Volume : 0.8d;
        IsMuted = snapshot.IsMuted;
        IsPlaying = snapshot.IsPlaying && _queue.Count > 0;
        IsPopupOpen = snapshot.IsPopupOpen;
        NotifyChanged();
    }

    public ListenPlaybackSnapshot CreateSnapshot() => new()
    {
        Queue = _queue.ToList(),
        History = _history.ToList(),
        CurrentIndex = CurrentIndex,
        SourceLabel = SourceLabel,
        IsPanelOpen = IsPanelOpen,
        ActiveTab = ActiveTab,
        IsDismissed = IsDismissed,
        CurrentTimeSeconds = CurrentTimeSeconds,
        DurationSeconds = DurationSeconds,
        Volume = Volume,
        IsMuted = IsMuted,
        IsPlaying = IsPlaying,
        IsPopupOpen = IsPopupOpen,
    };

    private void RememberCurrentItem()
    {
        if (CurrentItem is null)
        {
            return;
        }

        AddHistoryItem(CurrentItem with { PlayedAt = DateTimeOffset.UtcNow });
    }

    private void AddHistoryItem(ListenQueueItem item)
    {
        _history.Insert(0, item);
        if (_history.Count > 100)
        {
            _history.RemoveRange(100, _history.Count - 100);
        }
    }

    private async Task EnsurePlayableAsync(int index, CancellationToken ct)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        var item = _queue[index];
        if (!string.IsNullOrWhiteSpace(item.StreamUrl))
        {
            return;
        }

        var assetId = item.AssetId;
        if (!assetId.HasValue)
        {
            assetId = await _orchestrator.ResolveWorkToAssetAsync(item.WorkId, ct);
        }

        if (!assetId.HasValue)
        {
            return;
        }

        _queue[index] = item with
        {
            AssetId = assetId,
            StreamUrl = _apiClient.ToAbsoluteEngineUrl($"/stream/{assetId.Value}"),
        };
    }

    private static ListenQueueItem CreateQueueItem(WorkViewModel work)
        => new()
        {
            WorkId = work.Id,
            CollectionId = work.CollectionId,
            MediaType = work.MediaType,
            Title = work.Title,
            Subtitle = work.Artist ?? work.Author,
            Album = work.Album ?? work.Series,
            CoverUrl = work.CoverUrl,
            Duration = GetDuration(work),
            AssetId = work.AssetId,
            StreamUrl = null,
        };

    private static string? GetDuration(WorkViewModel work)
        => work.CanonicalValues.FirstOrDefault(cv =>
               string.Equals(cv.Key, "duration", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cv.Key, "runtime", StringComparison.OrdinalIgnoreCase))?.Value;

    private void NotifyChanged() => OnChanged?.Invoke();
}

public static class ListenPlaybackTabs
{
    public const string Queue = "queue";
    public const string History = "history";
}

public sealed record ListenQueueItem
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("collection_id")]
    public Guid? CollectionId { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("album")]
    public string? Album { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid? AssetId { get; init; }

    [JsonPropertyName("stream_url")]
    public string? StreamUrl { get; init; }

    [JsonPropertyName("played_at")]
    public DateTimeOffset? PlayedAt { get; init; }
}

public sealed record ListenPlaybackSnapshot
{
    [JsonPropertyName("queue")]
    public List<ListenQueueItem> Queue { get; init; } = [];

    [JsonPropertyName("history")]
    public List<ListenQueueItem> History { get; init; } = [];

    [JsonPropertyName("current_index")]
    public int CurrentIndex { get; init; }

    [JsonPropertyName("source_label")]
    public string? SourceLabel { get; init; }

    [JsonPropertyName("is_panel_open")]
    public bool IsPanelOpen { get; init; }

    [JsonPropertyName("active_tab")]
    public string ActiveTab { get; init; } = ListenPlaybackTabs.Queue;

    [JsonPropertyName("is_dismissed")]
    public bool IsDismissed { get; init; }

    [JsonPropertyName("current_time_seconds")]
    public double CurrentTimeSeconds { get; init; }

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("volume")]
    public double Volume { get; init; } = 0.8d;

    [JsonPropertyName("is_muted")]
    public bool IsMuted { get; init; }

    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; init; }

    [JsonPropertyName("is_popup_open")]
    public bool IsPopupOpen { get; init; }
}
