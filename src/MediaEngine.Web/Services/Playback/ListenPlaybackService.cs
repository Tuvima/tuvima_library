using System.Text.Json.Serialization;
using MediaEngine.Contracts.Playback;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Web.Services.Playback;

public sealed class ListenPlaybackService
{
    private readonly UIOrchestratorService _orchestrator;
    private readonly IEngineApiClient _apiClient;
    private readonly ILogger<ListenPlaybackService>? _logger;
    private readonly IUserPlaybackPreferencesAccessor? _preferences;
    private readonly List<ListenQueueItem> _queue = [];
    private readonly List<ListenQueueItem> _history = [];
    private readonly List<AudiobookListenHistoryItemDto> _audiobookHistory = [];
    private readonly Guid _sessionId = Guid.NewGuid();
    private DateTimeOffset _lastHeartbeatAt = DateTimeOffset.MinValue;

    public ListenPlaybackService(
        UIOrchestratorService orchestrator,
        IEngineApiClient apiClient,
        ILogger<ListenPlaybackService>? logger = null,
        IUserPlaybackPreferencesAccessor? preferences = null)
    {
        _orchestrator = orchestrator;
        _apiClient = apiClient;
        _logger = logger;
        _preferences = preferences;
    }

    public event Action? OnChanged;
    public event Func<ListenTransportCommand, Task>? OnTransportCommandRequested;

    public IReadOnlyList<ListenQueueItem> Queue => _queue;
    public IReadOnlyList<ListenQueueItem> History => _history;
    public IReadOnlyList<AudiobookListenHistoryItemDto> AudiobookHistory => _audiobookHistory;
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
    public double PlaybackRate { get; private set; } = 1d;
    public string Experience { get; private set; } = PlayerExperienceModes.Music;
    public bool NeedsUserGestureToStart { get; private set; }
    public bool IsPopupOpen { get; private set; }
    public string? CurrentError { get; private set; }

    public bool HasQueue => _queue.Count > 0 && !IsDismissed;

    public ListenQueueItem? CurrentItem =>
        CurrentIndex >= 0 && CurrentIndex < _queue.Count
            ? _queue[CurrentIndex]
            : null;

    public string? CurrentStreamUrl => CurrentItem?.StreamUrl;
    public bool IsAudiobookMode => string.Equals(Experience, PlayerExperienceModes.Audiobook, StringComparison.OrdinalIgnoreCase);
    public bool IsMusicMode => !IsAudiobookMode;

    public async Task PlayWorkAsync(WorkViewModel work, string? sourceLabel = null, CancellationToken ct = default)
    {
        await ReplaceQueueAsync([work], 0, sourceLabel ?? work.Album ?? work.Title, false, ct);
    }

    public Task PlayQueueItemAsync(ListenQueueItem item, string? sourceLabel = null, CancellationToken ct = default)
        => IsAudiobook(item.MediaType)
            ? PlayAudiobookAsync(item, sourceLabel, ct)
            : PlayQueueItemCoreAsync(item, sourceLabel, ct);

    public Task PlayAudiobookAsync(ListenQueueItem item, string? sourceLabel = null, CancellationToken ct = default)
        => PlayQueueItemCoreAsync(item with { MediaType = "Audiobooks" }, sourceLabel ?? item.Album ?? item.Title, ct);

    private async Task PlayQueueItemCoreAsync(ListenQueueItem item, string? sourceLabel = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        RememberCurrentItem();
        _queue.Clear();
        _queue.Add(item);
        CurrentIndex = 0;
        SourceLabel = sourceLabel ?? item.Album ?? item.Title;
        Experience = IsAudiobook(item.MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
        IsDismissed = false;
        CurrentTimeSeconds = await InitialPositionForAsync(item, ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(item, ct);
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        await EnsurePlayableAsync(CurrentIndex, ct);
        await RefreshAudiobookHistoryAsync(ct);
        NotifyChanged();
        await SyncReplaceQueueAsync([_queue[CurrentIndex]], 0, SourceLabel, false, ct);
    }

    public async Task ReplaceQueueAsync(
        IEnumerable<WorkViewModel> works,
        int startIndex,
        string? sourceLabel,
        bool shuffle,
        CancellationToken ct = default)
    {
        var items = works.Select(CreateQueueItem).ToList();
        await ReplaceQueueItemsAsync(items, startIndex, sourceLabel, shuffle, ct);
    }

    public async Task ReplaceQueueItemsAsync(
        IEnumerable<ListenQueueItem> queueItems,
        int startIndex,
        string? sourceLabel,
        bool shuffle,
        CancellationToken ct = default)
    {
        var items = queueItems
            .Where(item => item.WorkId != Guid.Empty)
            .ToList();
        if (items.Count == 0)
        {
            ClosePlayer();
            return;
        }

        if (items.Any(item => IsAudiobook(item.MediaType)))
        {
            items = [items[Math.Clamp(startIndex, 0, items.Count - 1)] with { MediaType = "Audiobooks" }];
            startIndex = 0;
            shuffle = false;
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
        Experience = IsAudiobook(_queue[CurrentIndex].MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
        IsDismissed = false;
        CurrentTimeSeconds = await InitialPositionForAsync(_queue[CurrentIndex], ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(_queue[CurrentIndex], ct);
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        await EnsurePlayableAsync(CurrentIndex, ct);
        await RefreshAudiobookHistoryAsync(ct);
        NotifyChanged();
        await SyncReplaceQueueAsync(items, CurrentIndex, sourceLabel, shuffle, ct);
    }

    public async Task InsertNextAsync(WorkViewModel work, CancellationToken ct = default)
    {
        var item = CreateQueueItem(work);
        if (_queue.Count == 0)
        {
            _queue.Add(item);
            CurrentIndex = 0;
            SourceLabel = work.Album ?? work.Title;
            Experience = IsAudiobook(item.MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
            IsDismissed = false;
            IsPlaying = true;
            NeedsUserGestureToStart = false;
            CurrentError = null;
            CurrentTimeSeconds = await InitialPositionForAsync(item, ct);
            PlaybackRate = await InitialPlaybackRateForAsync(item, ct);
            await EnsurePlayableAsync(CurrentIndex, ct);
            NotifyChanged();
            await SyncReplaceQueueAsync([item], 0, SourceLabel, false, ct);
            return;
        }

        if (IsAudiobook(item.MediaType))
        {
            await PlayAudiobookAsync(item, item.Album ?? item.Title, ct);
            return;
        }

        var insertIndex = Math.Clamp(CurrentIndex + 1, 0, _queue.Count);
        _queue.Insert(insertIndex, item);
        NotifyChanged();
        await SyncAddQueueItemsAsync([item], PlayerQueueMutationModes.AddNext, ct);
    }

    public async Task AddToQueueAsync(WorkViewModel work, CancellationToken ct = default)
    {
        var item = CreateQueueItem(work);
        await AddQueueItemAsync(item, next: false, ct);
    }

    public async Task AddQueueItemAsync(ListenQueueItem item, bool next = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_queue.Count == 0)
        {
            _queue.Add(item);
            CurrentIndex = 0;
            SourceLabel = item.Album ?? item.Title;
            Experience = IsAudiobook(item.MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
            IsDismissed = false;
            IsPlaying = true;
            NeedsUserGestureToStart = false;
            CurrentError = null;
            CurrentTimeSeconds = await InitialPositionForAsync(item, ct);
            PlaybackRate = await InitialPlaybackRateForAsync(item, ct);
            await EnsurePlayableAsync(CurrentIndex, ct);
            NotifyChanged();
            await SyncReplaceQueueAsync([item], 0, SourceLabel, false, ct);
            return;
        }

        if (IsAudiobook(item.MediaType))
        {
            await PlayAudiobookAsync(item, item.Album ?? item.Title, ct);
            return;
        }

        var mutationMode = PlayerQueueMutationModes.AddEnd;
        if (next)
        {
            var insertIndex = Math.Clamp(CurrentIndex + 1, 0, _queue.Count);
            _queue.Insert(insertIndex, item);
            mutationMode = PlayerQueueMutationModes.AddNext;
        }
        else
        {
            _queue.Add(item);
        }

        NotifyChanged();
        await SyncAddQueueItemsAsync([item], mutationMode, ct);
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
        Experience = IsAudiobook(_queue[CurrentIndex].MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
        CurrentTimeSeconds = await InitialPositionForAsync(_queue[CurrentIndex], ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(_queue[CurrentIndex], ct);
        IsDismissed = false;
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        await EnsurePlayableAsync(CurrentIndex, ct);
        await RefreshAudiobookHistoryAsync(ct);
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
        Experience = IsAudiobook(_queue[CurrentIndex].MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
        CurrentTimeSeconds = await InitialPositionForAsync(_queue[CurrentIndex], ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(_queue[CurrentIndex], ct);
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        await EnsurePlayableAsync(CurrentIndex, ct);
        await RefreshAudiobookHistoryAsync(ct);
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
        Experience = IsAudiobook(_queue[CurrentIndex].MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
        CurrentTimeSeconds = await InitialPositionForAsync(_queue[CurrentIndex], ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(_queue[CurrentIndex], ct);
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        await EnsurePlayableAsync(CurrentIndex, ct);
        await RefreshAudiobookHistoryAsync(ct);
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
        Experience = IsAudiobook(_queue[CurrentIndex].MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
        CurrentTimeSeconds = await InitialPositionForAsync(_queue[CurrentIndex], ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(_queue[CurrentIndex], ct);
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        await EnsurePlayableAsync(CurrentIndex, ct);
        await RefreshAudiobookHistoryAsync(ct);
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
        ActiveTab = tab.ToLowerInvariant() switch
        {
            ListenPlaybackTabs.History => ListenPlaybackTabs.History,
            ListenPlaybackTabs.Lyrics => ListenPlaybackTabs.Lyrics,
            _ => ListenPlaybackTabs.Queue,
        };
        NotifyChanged();
    }

    public void UpdateTransportState(
        double? currentTimeSeconds = null,
        double? durationSeconds = null,
        bool? isPlaying = null,
        double? volume = null,
        bool? isMuted = null,
        double? playbackRate = null,
        bool? needsUserGestureToStart = null)
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

        if (playbackRate.HasValue)
        {
            var next = Math.Clamp(playbackRate.Value, 0.5d, 32d);
            if (Math.Abs(PlaybackRate - next) >= 0.01d)
            {
                PlaybackRate = next;
                changed = true;
            }
        }

        if (needsUserGestureToStart.HasValue && NeedsUserGestureToStart != needsUserGestureToStart.Value)
        {
            NeedsUserGestureToStart = needsUserGestureToStart.Value;
            changed = true;
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    public async Task RequestTransportCommandAsync(ListenTransportCommand command)
    {
        if (OnTransportCommandRequested is not null)
        {
            await OnTransportCommandRequested.Invoke(command);
        }
    }

    public async Task SkipBackAsync(CancellationToken ct = default)
    {
        var settings = await PlaybackSettingsAsync(ct);
        await SeekRelativeAsync(-settings.Listening.SkipBackSeconds, ct);
    }

    public async Task SkipForwardAsync(CancellationToken ct = default)
    {
        var settings = await PlaybackSettingsAsync(ct);
        await SeekRelativeAsync(settings.Listening.SkipForwardSeconds, ct);
    }

    public async Task SeekRelativeAsync(double deltaSeconds, CancellationToken ct = default)
    {
        var max = DurationSeconds > 0 ? DurationSeconds : double.MaxValue;
        var next = Math.Clamp(CurrentTimeSeconds + deltaSeconds, 0, max);
        CurrentTimeSeconds = next;
        NotifyChanged();
        await RequestTransportCommandAsync(new("seek", next));
        await ReportHeartbeatAsync(force: true, ct);
    }

    public async Task CyclePlaybackRateAsync(CancellationToken ct = default)
    {
        var rates = await SupportedPlaybackRatesAsync(ct);
        var currentIndex = rates.FindIndex(rate => Math.Abs(rate - PlaybackRate) < 0.01d);
        var next = rates[(currentIndex + 1 + rates.Count) % rates.Count];
        PlaybackRate = next;
        NotifyChanged();
        await RequestTransportCommandAsync(new("set-speed", next));
    }

    public async Task PlayAudiobookHistoryAsync(AudiobookListenHistoryItemDto history, CancellationToken ct = default)
    {
        var current = CurrentItem;
        var item = (current ?? new ListenQueueItem
        {
            WorkId = history.WorkId,
            AssetId = history.AssetId,
            MediaType = "Audiobooks",
            Title = history.Title,
        }) with
        {
            WorkId = history.WorkId,
            AssetId = history.AssetId,
            MediaType = "Audiobooks",
            Title = string.IsNullOrWhiteSpace(history.Title) ? current?.Title ?? "Audiobook" : history.Title,
            Subtitle = history.ChapterTitle ?? current?.Subtitle,
            InitialPositionSeconds = history.PositionSeconds,
            Duration = history.DurationSeconds.HasValue ? FormatDuration(history.DurationSeconds.Value) : current?.Duration,
        };

        await PlayAudiobookAsync(item, item.Title, ct);
    }

    public async Task ReportHeartbeatAsync(bool force = false, CancellationToken ct = default)
    {
        if (_apiClient is null || CurrentItem?.AssetId is not Guid assetId)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastHeartbeatAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastHeartbeatAt = now;
        try
        {
            var state = await _apiClient.PostPlayerHeartbeatAsync(new PlayerHeartbeatDto
            {
                SessionId = _sessionId,
                DeviceId = "web-dashboard",
                Client = "web",
                AssetId = assetId,
                IsPlaying = IsPlaying,
                PositionSeconds = CurrentTimeSeconds,
                DurationSeconds = DurationSeconds > 0 ? DurationSeconds : null,
                ProgressPct = DurationSeconds > 0 ? Math.Clamp(CurrentTimeSeconds / DurationSeconds * 100d, 0d, 100d) : null,
                Volume = Volume,
                IsMuted = IsMuted,
                PlaybackRate = PlaybackRate,
            }, ct);
            ApplyPlayerState(state);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not sync listen player heartbeat.");
        }
    }

    public void MarkCurrentFailed(string message)
    {
        CurrentError = string.IsNullOrWhiteSpace(message)
            ? "Playback failed for this item."
            : message;
        IsPlaying = false;
        NeedsUserGestureToStart = false;
        NotifyChanged();
    }

    public void MarkNeedsUserGestureToStart()
    {
        NeedsUserGestureToStart = true;
        IsPlaying = false;
        CurrentError = "Tap play to start this audio source.";
        NotifyChanged();
    }

    public void MarkPlaybackStarted()
    {
        NeedsUserGestureToStart = false;
        CurrentError = null;
        IsPlaying = true;
        NotifyChanged();
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
        _audiobookHistory.Clear();
        CurrentIndex = -1;
        SourceLabel = null;
        IsPanelOpen = false;
        ActiveTab = ListenPlaybackTabs.Queue;
        IsDismissed = true;
        CurrentTimeSeconds = 0;
        DurationSeconds = 0;
        Volume = 0.8d;
        IsMuted = false;
        PlaybackRate = 1d;
        Experience = PlayerExperienceModes.Music;
        NeedsUserGestureToStart = false;
        IsPlaying = false;
        IsPopupOpen = false;
        CurrentError = null;
        NotifyChanged();
    }

    public void RestoreState(ListenPlaybackSnapshot snapshot)
    {
        _queue.Clear();
        _queue.AddRange(snapshot.Queue ?? []);
        _history.Clear();
        _history.AddRange(snapshot.History ?? []);
        _audiobookHistory.Clear();
        _audiobookHistory.AddRange(snapshot.AudiobookHistory ?? []);
        CurrentIndex = _queue.Count == 0
            ? -1
            : Math.Clamp(snapshot.CurrentIndex, 0, _queue.Count - 1);
        SourceLabel = snapshot.SourceLabel;
        IsPanelOpen = snapshot.IsPanelOpen;
        ActiveTab = snapshot.ActiveTab?.ToLowerInvariant() switch
        {
            ListenPlaybackTabs.History => ListenPlaybackTabs.History,
            ListenPlaybackTabs.Lyrics => ListenPlaybackTabs.Lyrics,
            _ => ListenPlaybackTabs.Queue,
        };
        IsDismissed = snapshot.IsDismissed || _queue.Count == 0;
        CurrentTimeSeconds = Math.Max(0, snapshot.CurrentTimeSeconds);
        DurationSeconds = Math.Max(0, snapshot.DurationSeconds);
        Volume = snapshot.Volume is > 0 and <= 1 ? snapshot.Volume : 0.8d;
        IsMuted = snapshot.IsMuted;
        PlaybackRate = snapshot.PlaybackRate is >= 0.5d and <= 32d ? snapshot.PlaybackRate : 1d;
        Experience = string.Equals(snapshot.Experience, PlayerExperienceModes.Audiobook, StringComparison.OrdinalIgnoreCase)
            ? PlayerExperienceModes.Audiobook
            : PlayerExperienceModes.Music;
        NeedsUserGestureToStart = snapshot.NeedsUserGestureToStart;
        IsPlaying = snapshot.IsPlaying && _queue.Count > 0;
        IsPopupOpen = snapshot.IsPopupOpen;
        CurrentError = snapshot.CurrentError;
        NotifyChanged();
    }

    public ListenPlaybackSnapshot CreateSnapshot() => new()
    {
        Queue = _queue.ToList(),
        History = _history.ToList(),
        AudiobookHistory = _audiobookHistory.ToList(),
        CurrentIndex = CurrentIndex,
        SourceLabel = SourceLabel,
        Experience = Experience,
        IsPanelOpen = IsPanelOpen,
        ActiveTab = ActiveTab,
        IsDismissed = IsDismissed,
        CurrentTimeSeconds = CurrentTimeSeconds,
        DurationSeconds = DurationSeconds,
        Volume = Volume,
        IsMuted = IsMuted,
        PlaybackRate = PlaybackRate,
        NeedsUserGestureToStart = NeedsUserGestureToStart,
        IsPlaying = IsPlaying,
        IsPopupOpen = IsPopupOpen,
        CurrentError = CurrentError,
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
            MarkCurrentFailed("No playable audio file could be resolved for this item.");
            return;
        }

        var manifest = await _apiClient.GetPlaybackManifestAsync(assetId.Value, "web", ct);
        var streamUrl = manifest?.DirectStreamUrl;
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            MarkCurrentFailed("The Engine did not return a playable audio stream for this item.");
            return;
        }

        _queue[index] = item with
        {
            AssetId = assetId,
            StreamUrl = _apiClient.ToAbsoluteEngineUrl(streamUrl),
        };
        double? manifestDuration = manifest?.Chapters
            .Where(chapter => chapter.EndSeconds.HasValue)
            .Select(chapter => chapter.EndSeconds!.Value)
            .DefaultIfEmpty()
            .Max();
        manifestDuration ??= TryParseDurationSeconds(item.Duration);
        if (manifestDuration is > 0)
        {
            DurationSeconds = manifestDuration.Value;
        }
        CurrentError = null;
    }

    private async Task SyncReplaceQueueAsync(
        IReadOnlyList<ListenQueueItem> items,
        int startIndex,
        string? sourceLabel,
        bool shuffle,
        CancellationToken ct)
    {
        if (_apiClient is null || items.Count == 0)
        {
            return;
        }

        try
        {
            var start = Math.Clamp(startIndex, 0, items.Count - 1);
            await _apiClient.ReplacePlayerQueueAsync(new PlayerQueueMutationDto
            {
                DeviceId = "web-dashboard",
                Client = "web",
                Items = items.Select(ToPlayerQueueMutationItem).ToList(),
                WorkIds = items.Select(item => item.WorkId).Where(id => id != Guid.Empty).ToList(),
                StartIndex = start,
                StartWorkId = items[start].WorkId,
                SourceLabel = sourceLabel,
                Shuffle = shuffle,
                ClearExisting = true,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not sync listen player queue replacement.");
        }
    }

    private async Task SyncAddQueueItemsAsync(
        IReadOnlyList<ListenQueueItem> items,
        string mode,
        CancellationToken ct)
    {
        if (_apiClient is null || items.Count == 0)
        {
            return;
        }

        try
        {
            await _apiClient.AddPlayerQueueItemsAsync(new PlayerQueueMutationDto
            {
                DeviceId = "web-dashboard",
                Client = "web",
                Mode = mode,
                Items = items.Select(ToPlayerQueueMutationItem).ToList(),
                WorkIds = items.Select(item => item.WorkId).Where(id => id != Guid.Empty).ToList(),
                SourceLabel = SourceLabel,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not sync listen player queue addition.");
        }
    }

    public static ListenQueueItem CreateQueueItem(WorkViewModel work)
        => new()
        {
            WorkId = work.Id,
            CollectionId = work.CollectionId,
            MediaType = work.MediaType,
            Title = GetDisplayTitle(work),
            Subtitle = work.Artist ?? work.Author,
            Album = work.Album ?? work.Series,
            CoverUrl = work.CoverUrl,
            Duration = GetDuration(work),
            AssetId = work.AssetId,
            StreamUrl = null,
        };

    private static PlayerQueueMutationItemDto ToPlayerQueueMutationItem(ListenQueueItem item)
        => new()
        {
            WorkId = item.WorkId,
            AssetId = item.AssetId,
            CollectionId = item.CollectionId,
            MediaType = item.MediaType,
            Title = item.Title,
            Subtitle = item.Subtitle,
            Album = item.Album,
            Artist = IsMusic(item.MediaType) ? item.Subtitle : null,
            Author = IsAudiobook(item.MediaType) ? item.Subtitle : null,
            CoverUrl = item.CoverUrl,
            DurationSeconds = TryParseDurationSeconds(item.Duration),
            PositionSeconds = item.InitialPositionSeconds,
            StreamUrl = item.StreamUrl,
        };

    private static double InitialPositionFor(ListenQueueItem item) =>
        Math.Max(0, item.InitialPositionSeconds ?? 0);

    private async Task<double> InitialPositionForAsync(ListenQueueItem item, CancellationToken ct)
    {
        var position = InitialPositionFor(item);
        if (position <= 0 || !IsAudiobook(item.MediaType))
        {
            return position;
        }

        var settings = await PlaybackSettingsAsync(ct);
        return Math.Max(0, position - settings.Listening.ResumeRewindSeconds);
    }

    private async Task<double> InitialPlaybackRateForAsync(ListenQueueItem item, CancellationToken ct)
    {
        if (!IsAudiobook(item.MediaType))
        {
            return 1d;
        }

        var settings = await PlaybackSettingsAsync(ct);
        return Math.Clamp((double)settings.Listening.AudiobookDefaultSpeed, 0.5d, 3d);
    }

    private async Task<UserPlaybackSettingsDto> PlaybackSettingsAsync(CancellationToken ct)
    {
        if (_preferences is null)
        {
            return UserPlaybackSettingsDto.CreateDefaults(Guid.Empty);
        }

        return await _preferences.GetAsync(ct) ?? UserPlaybackSettingsDto.CreateDefaults(Guid.Empty);
    }

    private async Task<List<double>> SupportedPlaybackRatesAsync(CancellationToken ct)
    {
        if (!IsAudiobookMode)
        {
            return [1d];
        }

        var settings = await PlaybackSettingsAsync(ct);
        return new[] { 0.75d, 1d, 1.25d, 1.5d, 1.75d, 2d, 2.5d, 3d }
            .Append((double)settings.Listening.AudiobookDefaultSpeed)
            .Distinct()
            .Order()
            .ToList();
    }

    private async Task RefreshAudiobookHistoryAsync(CancellationToken ct)
    {
        _audiobookHistory.Clear();
        if (!IsAudiobookMode || CurrentItem is null || CurrentItem.WorkId == Guid.Empty || _apiClient is null)
        {
            return;
        }

        try
        {
            var items = await _apiClient.GetAudiobookListenHistoryAsync(CurrentItem.WorkId, limit: 10, ct: ct);
            _audiobookHistory.AddRange(items);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not load audiobook listen history.");
        }
    }

    private void ApplyPlayerState(PlayerStateDto? state)
    {
        if (state is null)
        {
            return;
        }

        Experience = string.Equals(state.Experience, PlayerExperienceModes.Audiobook, StringComparison.OrdinalIgnoreCase)
            ? PlayerExperienceModes.Audiobook
            : PlayerExperienceModes.Music;
        PlaybackRate = state.PlaybackRate is >= 0.5d and <= 32d ? state.PlaybackRate : PlaybackRate;
        _audiobookHistory.Clear();
        _audiobookHistory.AddRange(state.AudiobookHistory ?? []);
    }

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsMusic(string? mediaType) =>
        mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAudiobook(string? mediaType) =>
        mediaType?.Contains("audiobook", StringComparison.OrdinalIgnoreCase) == true
        || string.Equals(mediaType, "M4B", StringComparison.OrdinalIgnoreCase);

    private static double? TryParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds >= 60000 ? seconds / 1000d : seconds;
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 2 or 3
            && parts.All(part => double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            var multiplier = 1d;
            var total = 0d;
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                total += double.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture) * multiplier;
                multiplier *= 60d;
            }

            return total;
        }

        return null;
    }

    private static string? GetDuration(WorkViewModel work)
    {
        var raw = work.CanonicalValues.FirstOrDefault(cv =>
            string.Equals(cv.Key, "duration_seconds", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cv.Key, "duration_sec", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cv.Key, "duration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cv.Key, "runtime", StringComparison.OrdinalIgnoreCase))?.Value;

        var seconds = TryParseDurationSeconds(raw);
        if (seconds is not > 0)
        {
            return raw;
        }

        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetDisplayTitle(WorkViewModel work)
        => FirstNonBlank(
               CleanUntitled(work.Title) ? null : work.Title,
               Canonical(work, "track_title"),
               Canonical(work, "track_name"),
               Canonical(work, "song_title"),
               Canonical(work, "name"),
               Canonical(work, "file_title"),
               Canonical(work, "file_name"))
           ?? work.Title;

    private static string? Canonical(WorkViewModel work, string key)
        => work.CanonicalValues.FirstOrDefault(value =>
            string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool CleanUntitled(string? value)
        => string.IsNullOrWhiteSpace(value)
           || value.Trim().StartsWith("Untitled", StringComparison.OrdinalIgnoreCase);

    private void NotifyChanged() => OnChanged?.Invoke();
}

public static class ListenPlaybackTabs
{
    public const string Queue = "queue";
    public const string History = "history";
    public const string Lyrics = "lyrics";
}

public sealed record ListenTransportCommand(string Action, double? Value = null);

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

    [JsonPropertyName("initial_position_seconds")]
    public double? InitialPositionSeconds { get; init; }

    [JsonPropertyName("played_at")]
    public DateTimeOffset? PlayedAt { get; init; }
}

public sealed record ListenPlaybackSnapshot
{
    [JsonPropertyName("queue")]
    public List<ListenQueueItem> Queue { get; init; } = [];

    [JsonPropertyName("history")]
    public List<ListenQueueItem> History { get; init; } = [];

    [JsonPropertyName("audiobook_history")]
    public List<AudiobookListenHistoryItemDto> AudiobookHistory { get; init; } = [];

    [JsonPropertyName("current_index")]
    public int CurrentIndex { get; init; }

    [JsonPropertyName("source_label")]
    public string? SourceLabel { get; init; }

    [JsonPropertyName("experience")]
    public string Experience { get; init; } = PlayerExperienceModes.Music;

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

    [JsonPropertyName("playback_rate")]
    public double PlaybackRate { get; init; } = 1d;

    [JsonPropertyName("needs_user_gesture_to_start")]
    public bool NeedsUserGestureToStart { get; init; }

    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; init; }

    [JsonPropertyName("is_popup_open")]
    public bool IsPopupOpen { get; init; }

    [JsonPropertyName("current_error")]
    public string? CurrentError { get; init; }
}
