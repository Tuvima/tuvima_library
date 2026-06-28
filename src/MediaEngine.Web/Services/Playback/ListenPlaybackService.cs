using System.Text.Json.Serialization;
using MediaEngine.Contracts.Playback;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Web.Services.Playback;

public sealed class ListenPlaybackService
{
    private static readonly TimeSpan TransportUiUpdateInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private readonly UIOrchestratorService _orchestrator;
    private readonly IEngineApiClient _apiClient;
    private readonly ILogger<ListenPlaybackService>? _logger;
    private readonly IUserPlaybackPreferencesAccessor? _preferences;
    private readonly List<ListenQueueItem> _queue = [];
    private readonly List<ListenQueueItem> _history = [];
    private readonly List<AudiobookListenHistoryItemDto> _audiobookHistory = [];
    private readonly List<AudiobookBookmarkDto> _audiobookBookmarks = [];
    private readonly List<ListenTransportCommand> _pendingTransportCommands = [];
    private readonly Guid _sessionId = Guid.NewGuid();
    private DateTimeOffset _lastHeartbeatAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTransportUiNotificationAt = DateTimeOffset.MinValue;
    private CancellationTokenSource? _sleepTimerCts;
    private bool _transportHostReady = true;
    private string? _currentAudiobookStartKind;

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
    public IReadOnlyList<AudiobookBookmarkDto> AudiobookBookmarks => _audiobookBookmarks;
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
    public long PlaybackStartVersion { get; private set; }
    public string Experience { get; private set; } = PlayerExperienceModes.Music;
    public bool NeedsUserGestureToStart { get; private set; }
    public bool IsPopupOpen { get; private set; }
    public string? CurrentError { get; private set; }
    public int SkipBackSeconds { get; private set; } = 15;
    public int SkipForwardSeconds { get; private set; } = 15;
    public int ResumeRewindSeconds { get; private set; } = 10;
    public int AudiobookNearStartGuardSeconds { get; private set; } = 30;
    public IReadOnlyList<int> SleepTimerOptionsMinutes { get; private set; } = [5, 10, 15, 30, 45, 60];
    public bool AllowEndOfChapterSleepTimer { get; private set; } = true;
    public string SleepTimerMode { get; private set; } = ListenSleepTimerModes.Off;
    public DateTimeOffset? SleepTimerEndsAtUtc { get; private set; }
    public string SleepTimerLabel => SleepTimerMode switch
    {
        ListenSleepTimerModes.EndOfChapter => "End of chapter",
        ListenSleepTimerModes.Timer when SleepTimerEndsAtUtc.HasValue => FormatSleepTimerRemaining(SleepTimerEndsAtUtc.Value),
        _ => "Off",
    };

    public bool HasQueue => _queue.Count > 0 && !IsDismissed;

    public ListenQueueItem? CurrentItem =>
        CurrentIndex >= 0 && CurrentIndex < _queue.Count
            ? _queue[CurrentIndex]
            : null;

    public string? CurrentStreamUrl => CurrentItem?.StreamUrl;
    public string? CurrentBrowserStreamUrl => ToDashboardDirectStreamUrl(CurrentStreamUrl) ?? CurrentStreamUrl;
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
        => StartAudiobookAsync(new AudiobookStartRequest(item, AudiobookStartKinds.Resume, item.InitialPositionSeconds, item.ChapterIndex, sourceLabel), ct);

    public Task StartAudiobookAsync(AudiobookStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startKind = NormalizeAudiobookStartKind(request.StartKind);
        var exact = !string.Equals(startKind, AudiobookStartKinds.Resume, StringComparison.Ordinal);
        var item = request.Item with
        {
            MediaType = "Audiobooks",
            InitialPositionSeconds = request.PositionSeconds ?? request.Item.InitialPositionSeconds,
            ChapterIndex = request.ChapterIndex ?? request.Item.ChapterIndex,
            StartAtExactPosition = exact || request.Item.StartAtExactPosition,
            AudiobookStartKind = startKind,
        };

        return PlayQueueItemCoreAsync(item, request.SourceLabel ?? item.Album ?? item.Title, ct);
    }

    public Task PlayAudiobookChapterAsync(ListenQueueItem item, PlaybackChapterDto chapter, string? sourceLabel = null, CancellationToken ct = default)
    {
        var normalized = NormalizeChapter(chapter, chapter.Index);
        var chapterItem = item with
        {
            MediaType = "Audiobooks",
            Subtitle = normalized.Title,
            InitialPositionSeconds = normalized.StartSeconds,
            ChapterIndex = normalized.Index,
            StartAtExactPosition = true,
        };

        return StartAudiobookAsync(
            new AudiobookStartRequest(chapterItem, AudiobookStartKinds.Chapter, normalized.StartSeconds, normalized.Index, sourceLabel ?? item.Album ?? item.Title),
            ct);
    }

    public Task PlayAudiobookChapterAsync(int chapterIndex, CancellationToken ct = default)
    {
        var current = CurrentItem;
        if (current is null)
        {
            return Task.CompletedTask;
        }

        var chapter = current.Chapters.FirstOrDefault(item => item.Index == chapterIndex)
            ?? current.Chapters.ElementAtOrDefault(Math.Clamp(chapterIndex, 0, Math.Max(0, current.Chapters.Count - 1)));
        return chapter is null
            ? Task.CompletedTask
            : PlayAudiobookChapterAsync(current, chapter, SourceLabel, ct);
    }

    private async Task PlayQueueItemCoreAsync(ListenQueueItem item, string? sourceLabel = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        item = BootstrapDirectStream(item);
        if (IsAudiobook(item.MediaType))
        {
            var startKind = NormalizeAudiobookStartKind(item.AudiobookStartKind);
            item = item with { AudiobookStartKind = startKind };
            _currentAudiobookStartKind = startKind;
        }
        else
        {
            _currentAudiobookStartKind = null;
        }
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
        var startRequested = await TryStartCurrentAudioAsync();
        await EnsurePlayableAsync(CurrentIndex, ct);
        if (string.IsNullOrWhiteSpace(CurrentBrowserStreamUrl))
        {
            NotifyChanged();
            return;
        }

        if (!startRequested)
        {
            await TryStartCurrentAudioAsync();
        }
        else
        {
            NotifyChanged();
        }
        await RefreshAudiobookHistoryAsync(ct);
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
            items =
            [
                items[Math.Clamp(startIndex, 0, items.Count - 1)] with
                {
                    MediaType = "Audiobooks",
                    AudiobookStartKind = AudiobookStartKinds.Resume,
                },
            ];
            startIndex = 0;
            shuffle = false;
        }

        RememberCurrentItem();

        items = items.Select(BootstrapDirectStream).ToList();
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
        _currentAudiobookStartKind = IsAudiobook(_queue[CurrentIndex].MediaType)
            ? NormalizeAudiobookStartKind(_queue[CurrentIndex].AudiobookStartKind)
            : null;
        IsDismissed = false;
        CurrentTimeSeconds = await InitialPositionForAsync(_queue[CurrentIndex], ct);
        DurationSeconds = 0;
        PlaybackRate = await InitialPlaybackRateForAsync(_queue[CurrentIndex], ct);
        IsPlaying = true;
        NeedsUserGestureToStart = false;
        CurrentError = null;
        var startRequested = await TryStartCurrentAudioAsync();
        await EnsurePlayableAsync(CurrentIndex, ct);
        if (string.IsNullOrWhiteSpace(CurrentBrowserStreamUrl))
        {
            NotifyChanged();
            return;
        }

        if (!startRequested)
        {
            await TryStartCurrentAudioAsync();
        }
        else
        {
            NotifyChanged();
        }
        await RefreshAudiobookHistoryAsync(ct);
        await SyncReplaceQueueAsync(items, CurrentIndex, sourceLabel, shuffle, ct);
    }

    public async Task InsertNextAsync(WorkViewModel work, CancellationToken ct = default)
    {
        var item = CreateQueueItem(work);
        if (_queue.Count == 0)
        {
            item = BootstrapDirectStream(item);
            _queue.Add(item);
            CurrentIndex = 0;
            SourceLabel = work.Album ?? work.Title;
            Experience = IsAudiobook(item.MediaType) ? PlayerExperienceModes.Audiobook : PlayerExperienceModes.Music;
            _currentAudiobookStartKind = IsAudiobook(item.MediaType)
                ? NormalizeAudiobookStartKind(item.AudiobookStartKind)
                : null;
            IsDismissed = false;
            IsPlaying = true;
            NeedsUserGestureToStart = false;
            CurrentError = null;
            CurrentTimeSeconds = await InitialPositionForAsync(item, ct);
            PlaybackRate = await InitialPlaybackRateForAsync(item, ct);
            var startRequested = await TryStartCurrentAudioAsync();
            await EnsurePlayableAsync(CurrentIndex, ct);
            if (string.IsNullOrWhiteSpace(CurrentBrowserStreamUrl))
            {
                NotifyChanged();
                return;
            }

            if (!startRequested)
            {
                await TryStartCurrentAudioAsync();
            }
            else
            {
                NotifyChanged();
            }
            await SyncReplaceQueueAsync([_queue[CurrentIndex]], 0, SourceLabel, false, ct);
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
            item = BootstrapDirectStream(item);
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
            var startRequested = await TryStartCurrentAudioAsync();
            await EnsurePlayableAsync(CurrentIndex, ct);
            if (!startRequested)
            {
                await TryStartCurrentAudioAsync();
            }
            else
            {
                NotifyChanged();
            }
            await SyncReplaceQueueAsync([_queue[CurrentIndex]], 0, SourceLabel, false, ct);
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
        MarkPlaybackStart();
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
        MarkPlaybackStart();
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
        MarkPlaybackStart();
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
        MarkPlaybackStart();
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
        var now = DateTimeOffset.UtcNow;
        var positionChanged = false;
        var structuralChanged = false;

        if (currentTimeSeconds.HasValue)
        {
            var next = Math.Max(0, currentTimeSeconds.Value);
            if (Math.Abs(CurrentTimeSeconds - next) >= 1)
            {
                CurrentTimeSeconds = next;
                positionChanged = true;
            }
        }

        if (durationSeconds.HasValue)
        {
            var next = Math.Max(0, durationSeconds.Value);
            if (Math.Abs(DurationSeconds - next) >= 1)
            {
                DurationSeconds = next;
                structuralChanged = true;
            }
        }

        if (isPlaying.HasValue)
        {
            if (IsPlaying != isPlaying.Value)
            {
                IsPlaying = isPlaying.Value;
                structuralChanged = true;
            }
        }

        if (volume.HasValue)
        {
            var next = Math.Clamp(volume.Value, 0d, 1d);
            if (Math.Abs(Volume - next) >= 0.01d)
            {
                Volume = next;
                structuralChanged = true;
            }
        }

        if (isMuted.HasValue)
        {
            if (IsMuted != isMuted.Value)
            {
                IsMuted = isMuted.Value;
                structuralChanged = true;
            }
        }

        if (playbackRate.HasValue)
        {
            var next = Math.Clamp(playbackRate.Value, 0.5d, 32d);
            if (Math.Abs(PlaybackRate - next) >= 0.01d)
            {
                PlaybackRate = next;
                structuralChanged = true;
            }
        }

        if (needsUserGestureToStart.HasValue && NeedsUserGestureToStart != needsUserGestureToStart.Value)
        {
            NeedsUserGestureToStart = needsUserGestureToStart.Value;
            structuralChanged = true;
        }

        if (SleepTimerMode == ListenSleepTimerModes.EndOfChapter
            && IsPlaying
            && CurrentItem is { Chapters.Count: > 0 } current
            && ResolveCurrentChapter(current, CurrentTimeSeconds) is { EndSeconds: { } endSeconds }
            && CurrentTimeSeconds >= endSeconds - 0.5d)
        {
            _ = ExpireSleepTimerAsync();
        }

        if (structuralChanged || positionChanged && now - _lastTransportUiNotificationAt >= TransportUiUpdateInterval)
        {
            _lastTransportUiNotificationAt = now;
            NotifyChanged();
        }
    }

    public async Task RequestTransportCommandAsync(ListenTransportCommand command)
    {
        if (!_transportHostReady || OnTransportCommandRequested is null)
        {
            QueuePendingTransportCommand(command);
            return;
        }

        await OnTransportCommandRequested.Invoke(command);
    }

    public async Task SetTransportHostReadyAsync()
    {
        _transportHostReady = true;
        if (OnTransportCommandRequested is null || _pendingTransportCommands.Count == 0)
        {
            return;
        }

        var commands = _pendingTransportCommands.ToList();
        _pendingTransportCommands.Clear();
        foreach (var command in commands)
        {
            await OnTransportCommandRequested.Invoke(command);
        }
    }

    public void SetTransportHostNotReady()
    {
        _transportHostReady = false;
    }

    private void QueuePendingTransportCommand(ListenTransportCommand command)
    {
        if (string.Equals(command.Action, "start", StringComparison.OrdinalIgnoreCase))
        {
            _pendingTransportCommands.RemoveAll(item => string.Equals(item.Action, "start", StringComparison.OrdinalIgnoreCase));
        }

        _pendingTransportCommands.Add(command);
    }

    private ListenTransportCommand CreateStartCommand() => new(
        "start",
        Value: CurrentTimeSeconds,
        StreamUrl: CurrentBrowserStreamUrl,
        PositionSeconds: CurrentTimeSeconds,
        PlaybackRate: PlaybackRate,
        RequestId: PlaybackStartVersion,
        AudiobookStartKind: _currentAudiobookStartKind);

    private async Task<bool> TryStartCurrentAudioAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentBrowserStreamUrl))
        {
            return false;
        }

        MarkPlaybackStart();
        await RequestTransportCommandAsync(CreateStartCommand());
        NotifyChanged();
        return true;
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

    public async Task PlayNextChapterAsync(CancellationToken ct = default)
    {
        var current = CurrentItem;
        if (current?.Chapters.Count is not > 0)
        {
            await SkipForwardAsync(ct);
            return;
        }

        var chapter = ResolveCurrentChapter(current, CurrentTimeSeconds);
        var next = current.Chapters.FirstOrDefault(item => item.StartSeconds > CurrentTimeSeconds + 0.5d)
            ?? (chapter is null ? current.Chapters.FirstOrDefault() : current.Chapters.FirstOrDefault(item => item.Index > chapter.Index));
        if (next is null)
        {
            return;
        }

        await PlayAudiobookChapterAsync(current, next, SourceLabel, ct);
    }

    public async Task PlayPreviousChapterAsync(CancellationToken ct = default)
    {
        var current = CurrentItem;
        if (current?.Chapters.Count is not > 0)
        {
            await SkipBackAsync(ct);
            return;
        }

        var chapter = ResolveCurrentChapter(current, CurrentTimeSeconds);
        var previous = current.Chapters.LastOrDefault(item => item.StartSeconds < CurrentTimeSeconds - 3d)
            ?? (chapter is null ? current.Chapters.FirstOrDefault() : current.Chapters.LastOrDefault(item => item.Index < chapter.Index))
            ?? current.Chapters.FirstOrDefault();
        if (previous is null)
        {
            return;
        }

        await PlayAudiobookChapterAsync(current, previous, SourceLabel, ct);
    }

    public async Task CyclePlaybackRateAsync(CancellationToken ct = default)
    {
        var rates = await SupportedPlaybackRatesAsync(ct);
        var currentIndex = rates.FindIndex(rate => Math.Abs(rate - PlaybackRate) < 0.01d);
        var next = rates[(currentIndex + 1 + rates.Count) % rates.Count];
        await SetPlaybackRateAsync(next, ct);
    }

    public async Task SetPlaybackRateAsync(double rate, CancellationToken ct = default)
    {
        var next = Math.Clamp(Math.Round(rate, 1), 0.5d, 3d);
        PlaybackRate = next;
        NotifyChanged();
        await RequestTransportCommandAsync(new("set-speed", next));
        await ReportHeartbeatAsync(force: true, ct);
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
            ChapterIndex = history.ChapterIndex,
            StartAtExactPosition = true,
            Duration = history.DurationSeconds.HasValue ? FormatDuration(history.DurationSeconds.Value) : current?.Duration,
        };

        await StartAudiobookAsync(new AudiobookStartRequest(item, AudiobookStartKinds.History, history.PositionSeconds, history.ChapterIndex, item.Title), ct);
    }

    public async Task PlayAudiobookBookmarkAsync(AudiobookBookmarkDto bookmark, CancellationToken ct = default)
    {
        var current = CurrentItem;
        var item = (current ?? new ListenQueueItem
        {
            WorkId = bookmark.WorkId,
            AssetId = bookmark.AssetId,
            MediaType = "Audiobooks",
            Title = bookmark.Label ?? "Audiobook",
        }) with
        {
            WorkId = bookmark.WorkId,
            AssetId = bookmark.AssetId,
            MediaType = "Audiobooks",
            Subtitle = bookmark.ChapterTitle ?? current?.Subtitle,
            InitialPositionSeconds = bookmark.PositionSeconds,
            ChapterIndex = bookmark.ChapterIndex,
            StartAtExactPosition = true,
            Duration = bookmark.DurationSeconds.HasValue ? FormatDuration(bookmark.DurationSeconds.Value) : current?.Duration,
        };

        await StartAudiobookAsync(new AudiobookStartRequest(item, AudiobookStartKinds.Bookmark, bookmark.PositionSeconds, bookmark.ChapterIndex, item.Title), ct);
    }

    public async Task<AudiobookBookmarkDto?> AddAudiobookBookmarkAsync(CancellationToken ct = default)
    {
        var current = CurrentItem;
        if (!IsAudiobookMode || current?.AssetId is not Guid assetId || current.WorkId == Guid.Empty || _apiClient is null)
        {
            return null;
        }

        var chapter = ResolveCurrentChapter(current, CurrentTimeSeconds);
        var bookmark = await _apiClient.CreateAudiobookBookmarkAsync(
            current.WorkId,
            new CreateAudiobookBookmarkRequestDto
            {
                AssetId = assetId,
                ChapterIndex = chapter?.Index ?? current.ChapterIndex,
                ChapterTitle = chapter?.Title ?? current.Subtitle,
                PositionSeconds = CurrentTimeSeconds,
                DurationSeconds = DurationSeconds > 0 ? DurationSeconds : TryParseDurationSeconds(current.Duration),
                Label = chapter is null
                    ? $"Left off at {FormatDuration(CurrentTimeSeconds)}"
                    : $"{chapter.Title} - {FormatDuration(CurrentTimeSeconds)}",
            },
            ct: ct);

        if (bookmark is not null)
        {
            _audiobookBookmarks.Insert(0, bookmark);
            NotifyChanged();
        }

        return bookmark;
    }

    public async Task DeleteAudiobookBookmarkAsync(Guid bookmarkId, CancellationToken ct = default)
    {
        if (_apiClient is null)
        {
            return;
        }

        if (await _apiClient.DeleteAudiobookBookmarkAsync(bookmarkId, ct: ct))
        {
            _audiobookBookmarks.RemoveAll(item => item.Id == bookmarkId);
            NotifyChanged();
        }
    }

    public Task SetSleepTimerAsync(TimeSpan duration, CancellationToken ct = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            return CancelSleepTimerAsync();
        }

        _sleepTimerCts?.Cancel();
        _sleepTimerCts?.Dispose();
        _sleepTimerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SleepTimerMode = ListenSleepTimerModes.Timer;
        SleepTimerEndsAtUtc = DateTimeOffset.UtcNow.Add(duration);
        NotifyChanged();
        _ = CompleteSleepTimerAfterDelayAsync(duration, _sleepTimerCts.Token);
        return Task.CompletedTask;
    }

    public Task SetSleepTimerEndOfChapterAsync()
    {
        if (!AllowEndOfChapterSleepTimer)
        {
            return CancelSleepTimerAsync();
        }

        _sleepTimerCts?.Cancel();
        _sleepTimerCts?.Dispose();
        _sleepTimerCts = null;
        SleepTimerMode = ListenSleepTimerModes.EndOfChapter;
        SleepTimerEndsAtUtc = null;
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task CancelSleepTimerAsync()
    {
        _sleepTimerCts?.Cancel();
        _sleepTimerCts?.Dispose();
        _sleepTimerCts = null;
        SleepTimerMode = ListenSleepTimerModes.Off;
        SleepTimerEndsAtUtc = null;
        NotifyChanged();
        return Task.CompletedTask;
    }

    public async Task ReportHeartbeatAsync(bool force = false, CancellationToken ct = default)
    {
        if (_apiClient is null || CurrentItem?.AssetId is not Guid assetId)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastHeartbeatAt < HeartbeatInterval)
        {
            return;
        }

        _lastHeartbeatAt = now;
        try
        {
            var current = CurrentItem;
            var chapter = current is null ? null : ResolveCurrentChapter(current, CurrentTimeSeconds);
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
                ChapterIndex = chapter?.Index ?? current?.ChapterIndex,
                ChapterTitle = chapter?.Title ?? current?.Subtitle,
                AudiobookStartKind = IsAudiobookMode
                    ? NormalizeAudiobookStartKind(_currentAudiobookStartKind ?? current?.AudiobookStartKind)
                    : null,
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
        _audiobookBookmarks.Clear();
        _sleepTimerCts?.Cancel();
        _sleepTimerCts?.Dispose();
        _sleepTimerCts = null;
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
        PlaybackStartVersion++;
        SleepTimerMode = ListenSleepTimerModes.Off;
        SleepTimerEndsAtUtc = null;
        _currentAudiobookStartKind = null;
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
        _audiobookBookmarks.Clear();
        _audiobookBookmarks.AddRange(snapshot.AudiobookBookmarks ?? []);
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
        _currentAudiobookStartKind = IsAudiobookMode
            ? NormalizeAudiobookStartKind(CurrentItem?.AudiobookStartKind)
            : null;
        NeedsUserGestureToStart = snapshot.NeedsUserGestureToStart;
        IsPlaying = snapshot.IsPlaying && _queue.Count > 0;
        IsPopupOpen = snapshot.IsPopupOpen;
        CurrentError = snapshot.CurrentError;
        SkipBackSeconds = snapshot.SkipBackSeconds > 0 ? snapshot.SkipBackSeconds : 15;
        SkipForwardSeconds = snapshot.SkipForwardSeconds > 0 ? snapshot.SkipForwardSeconds : 15;
        SleepTimerOptionsMinutes = NormalizeSleepTimerOptions(snapshot.SleepTimerOptionsMinutes);
        AllowEndOfChapterSleepTimer = snapshot.AllowEndOfChapterSleepTimer;
        PlaybackStartVersion = Math.Max(PlaybackStartVersion, snapshot.PlaybackStartVersion);
        SleepTimerMode = snapshot.SleepTimerMode is ListenSleepTimerModes.Timer or ListenSleepTimerModes.EndOfChapter
            ? snapshot.SleepTimerMode
            : ListenSleepTimerModes.Off;
        SleepTimerEndsAtUtc = snapshot.SleepTimerEndsAtUtc > DateTimeOffset.UtcNow ? snapshot.SleepTimerEndsAtUtc : null;
        if (SleepTimerMode == ListenSleepTimerModes.Timer && !SleepTimerEndsAtUtc.HasValue)
        {
            SleepTimerMode = ListenSleepTimerModes.Off;
        }
        if (SleepTimerMode == ListenSleepTimerModes.Timer && SleepTimerEndsAtUtc.HasValue)
        {
            _ = SetSleepTimerAsync(SleepTimerEndsAtUtc.Value - DateTimeOffset.UtcNow);
        }
        NotifyChanged();
    }

    public ListenPlaybackSnapshot CreateSnapshot() => new()
    {
        Queue = _queue.ToList(),
        History = _history.ToList(),
        AudiobookHistory = _audiobookHistory.ToList(),
        AudiobookBookmarks = _audiobookBookmarks.ToList(),
        CurrentIndex = CurrentIndex,
        SourceLabel = SourceLabel,
        CurrentBrowserStreamUrl = CurrentBrowserStreamUrl,
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
        SkipBackSeconds = SkipBackSeconds,
        SkipForwardSeconds = SkipForwardSeconds,
        SleepTimerOptionsMinutes = SleepTimerOptionsMinutes.ToList(),
        AllowEndOfChapterSleepTimer = AllowEndOfChapterSleepTimer,
        PlaybackStartVersion = PlaybackStartVersion,
        SleepTimerMode = SleepTimerMode,
        SleepTimerEndsAtUtc = SleepTimerEndsAtUtc,
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
            var normalizedStreamUrl = NormalizeStreamUrl(item.StreamUrl);
            if (!string.Equals(item.StreamUrl, normalizedStreamUrl, StringComparison.Ordinal))
            {
                _queue[index] = item with { StreamUrl = normalizedStreamUrl };
                item = _queue[index];
            }

            if (!IsAudiobook(item.MediaType) || item.Chapters.Count > 0)
            {
                return;
            }

            if (_apiClient is null)
            {
                return;
            }
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

        var settings = _preferences is null ? null : await _preferences.GetAsync(ct);
        var profileId = settings?.ProfileId == Guid.Empty ? null : settings?.ProfileId;
        var manifest = await _apiClient.GetPlaybackManifestAsync(assetId.Value, "web", profileId, ct);
        var streamUrl = manifest?.DirectStreamUrl;
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            MarkCurrentFailed("The Engine did not return a playable audio stream for this item.");
            return;
        }

        _queue[index] = item with
        {
            AssetId = assetId,
            StreamUrl = NormalizeStreamUrl(streamUrl),
            Chapters = NormalizeChapters(manifest?.Chapters ?? []),
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

    private static ListenQueueItem BootstrapDirectStream(ListenQueueItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.StreamUrl) || !item.AssetId.HasValue)
        {
            return item;
        }

        return item with { StreamUrl = $"/stream/{item.AssetId.Value:D}" };
    }

    private string? NormalizeStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return null;
        }

        return _apiClient is null
            ? streamUrl
            : _apiClient.ToAbsoluteEngineUrl(streamUrl);
    }

    private static string? ToDashboardDirectStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return null;
        }

        var candidate = streamUrl.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            candidate = absolute.PathAndQuery;
        }
        else if (candidate.StartsWith("stream/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "/" + candidate;
        }

        if (!candidate.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = candidate["/stream/".Length..];
        var pathEnd = remainder.IndexOfAny(['?', '#']);
        var path = (pathEnd >= 0 ? remainder[..pathEnd] : remainder).TrimEnd('/');
        return Guid.TryParse(path, out var assetId)
            ? $"/engine-stream/{assetId:D}"
            : null;
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
            Author = IsAudiobook(item.MediaType) ? null : item.Subtitle,
            CoverUrl = item.CoverUrl,
            DurationSeconds = ResolveQueueDurationSeconds(item),
            PositionSeconds = item.InitialPositionSeconds,
            StreamUrl = item.StreamUrl,
        };

    private static double? ResolveQueueDurationSeconds(ListenQueueItem item)
    {
        if (!IsAudiobook(item.MediaType) || item.Chapters.Count == 0)
        {
            return TryParseDurationSeconds(item.Duration);
        }

        return item.Chapters
            .Where(chapter => chapter.EndSeconds.HasValue)
            .Select(chapter => chapter.EndSeconds!.Value)
            .DefaultIfEmpty(TryParseDurationSeconds(item.Duration) ?? 0)
            .Max();
    }

    private static double InitialPositionFor(ListenQueueItem item) =>
        Math.Max(0, item.InitialPositionSeconds ?? 0);

    private async Task<double> InitialPositionForAsync(ListenQueueItem item, CancellationToken ct)
    {
        var position = InitialPositionFor(item);
        if (position <= 0 || !IsAudiobook(item.MediaType) || item.StartAtExactPosition)
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
            var defaults = UserPlaybackSettingsDto.CreateDefaults(Guid.Empty);
            SkipBackSeconds = defaults.Listening.SkipBackSeconds;
            SkipForwardSeconds = defaults.Listening.SkipForwardSeconds;
            ResumeRewindSeconds = defaults.Listening.ResumeRewindSeconds;
            AudiobookNearStartGuardSeconds = defaults.Listening.AudiobookNearStartGuardSeconds;
            SleepTimerOptionsMinutes = NormalizeSleepTimerOptions(defaults.Listening.SleepTimerOptionsMinutes);
            AllowEndOfChapterSleepTimer = defaults.Listening.AllowEndOfChapterSleepTimer;
            return defaults;
        }

        var settings = await _preferences.GetAsync(ct) ?? UserPlaybackSettingsDto.CreateDefaults(Guid.Empty);
        SkipBackSeconds = settings.Listening.SkipBackSeconds;
        SkipForwardSeconds = settings.Listening.SkipForwardSeconds;
        ResumeRewindSeconds = settings.Listening.ResumeRewindSeconds;
        AudiobookNearStartGuardSeconds = settings.Listening.AudiobookNearStartGuardSeconds;
        SleepTimerOptionsMinutes = NormalizeSleepTimerOptions(settings.Listening.SleepTimerOptionsMinutes);
        AllowEndOfChapterSleepTimer = settings.Listening.AllowEndOfChapterSleepTimer;
        return settings;
    }

    private async Task<List<double>> SupportedPlaybackRatesAsync(CancellationToken ct)
    {
        if (!IsAudiobookMode)
        {
            return [1d];
        }

        var settings = await PlaybackSettingsAsync(ct);
        return Enumerable.Range(5, 26).Select(value => value / 10d)
            .Append((double)settings.Listening.AudiobookDefaultSpeed)
            .Distinct()
            .Order()
            .ToList();
    }

    private async Task RefreshAudiobookHistoryAsync(CancellationToken ct)
    {
        _audiobookHistory.Clear();
        _audiobookBookmarks.Clear();
        if (!IsAudiobookMode || CurrentItem is null || CurrentItem.WorkId == Guid.Empty || _apiClient is null)
        {
            return;
        }

        try
        {
            var settings = await PlaybackSettingsAsync(ct);
            var items = await _apiClient.GetAudiobookListenHistoryAsync(CurrentItem.WorkId, limit: settings.Listening.AudiobookHistoryLimit, ct: ct);
            _audiobookHistory.AddRange(CleanAudiobookHistory(items));
            var bookmarks = await _apiClient.GetAudiobookBookmarksAsync(CurrentItem.WorkId, ct: ct);
            _audiobookBookmarks.AddRange(bookmarks);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not load audiobook listen history or bookmarks.");
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
        _audiobookHistory.AddRange(CleanAudiobookHistory(state.AudiobookHistory ?? []));
    }

    public PlaybackChapterDto? CurrentChapter =>
        CurrentItem is { Chapters.Count: > 0 } current
            ? ResolveCurrentChapter(current, CurrentTimeSeconds)
            : null;

    public static IReadOnlyList<PlaybackChapterDto> NormalizeChapters(IReadOnlyList<PlaybackChapterDto> chapters) =>
        chapters
            .OrderBy(chapter => chapter.StartSeconds)
            .Select((chapter, ordinal) => NormalizeChapter(chapter, ordinal))
            .ToList();

    public static PlaybackChapterDto NormalizeChapter(PlaybackChapterDto chapter, int ordinal) =>
        !string.IsNullOrWhiteSpace(chapter.OriginalTitle)
            || !string.Equals(chapter.TitleSource, PlaybackChapterTitleSources.Generated, StringComparison.Ordinal)
            || string.Equals(chapter.Kind, PlaybackChapterKinds.Intro, StringComparison.Ordinal)
                ? chapter
                : chapter with { Title = CleanChapterTitle(chapter.Title, ordinal) };

    public static string CleanChapterTitle(string? title, int ordinal)
    {
        var fallback = $"Chapter {Math.Max(1, ordinal + 1)}";
        if (string.IsNullOrWhiteSpace(title))
        {
            return fallback;
        }

        var text = title.Trim();
        if (text.Equals("chapter", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (text.All(char.IsDigit))
        {
            return int.TryParse(text.TrimStart('0'), out var number) && number > 0
                ? $"Chapter {number}"
                : fallback;
        }

        return text;
    }

    private static PlaybackChapterDto? ResolveCurrentChapter(ListenQueueItem item, double positionSeconds) =>
        item.Chapters.LastOrDefault(chapter =>
            chapter.StartSeconds <= positionSeconds
            && (!chapter.EndSeconds.HasValue || positionSeconds < chapter.EndSeconds.Value))
        ?? item.Chapters.FirstOrDefault();

    private static IReadOnlyList<AudiobookListenHistoryItemDto> CleanAudiobookHistory(IEnumerable<AudiobookListenHistoryItemDto> items) =>
        (items.Any(item => item.PositionSeconds > 0.5d)
            ? items.Where(item => item.PositionSeconds > 0.5d)
            : items)
            .OrderByDescending(item => item.EndedAt)
            .GroupBy(item => new
            {
                item.WorkId,
                item.AssetId,
                Chapter = item.ChapterIndex ?? -1,
                PositionBucket = (int)Math.Floor(item.PositionSeconds / 30d),
            })
            .Select(group => group.First())
            .ToList();

    private static IReadOnlyList<int> NormalizeSleepTimerOptions(IEnumerable<int>? options)
    {
        var values = (options ?? [5, 10, 15, 30, 45, 60])
            .Where(minutes => minutes is > 0 and <= 240)
            .Distinct()
            .Order()
            .ToList();
        return values.Count == 0 ? [5, 10, 15, 30, 45, 60] : values;
    }

    private static string NormalizeAudiobookStartKind(string? value) =>
        value?.Trim() switch
        {
            AudiobookStartKinds.Chapter => AudiobookStartKinds.Chapter,
            AudiobookStartKinds.History => AudiobookStartKinds.History,
            AudiobookStartKinds.Bookmark => AudiobookStartKinds.Bookmark,
            _ => AudiobookStartKinds.Resume,
        };

    private async Task CompleteSleepTimerAfterDelayAsync(TimeSpan duration, CancellationToken ct)
    {
        try
        {
            await Task.Delay(duration, ct);
            await ExpireSleepTimerAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ExpireSleepTimerAsync()
    {
        if (SleepTimerMode == ListenSleepTimerModes.Off)
        {
            return;
        }

        _sleepTimerCts?.Cancel();
        _sleepTimerCts?.Dispose();
        _sleepTimerCts = null;
        SleepTimerMode = ListenSleepTimerModes.Off;
        SleepTimerEndsAtUtc = null;
        IsPlaying = false;
        NotifyChanged();
        await RequestTransportCommandAsync(new("pause"));
        await ReportHeartbeatAsync(force: true);
    }

    private static string FormatSleepTimerRemaining(DateTimeOffset endsAtUtc)
    {
        var remaining = endsAtUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "Off";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"{minutes} min";
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

    private void MarkPlaybackStart() => PlaybackStartVersion++;

    private void NotifyChanged() => OnChanged?.Invoke();
}

public static class ListenPlaybackTabs
{
    public const string Queue = "queue";
    public const string History = "history";
    public const string Lyrics = "lyrics";
}

public static class ListenSleepTimerModes
{
    public const string Off = "off";
    public const string Timer = "timer";
    public const string EndOfChapter = "chapter";
}

public sealed record ListenTransportCommand(
    string Action,
    double? Value = null,
    string? StreamUrl = null,
    double? PositionSeconds = null,
    double? PlaybackRate = null,
    long? RequestId = null,
    string? AudiobookStartKind = null);

public sealed record AudiobookStartRequest(
    ListenQueueItem Item,
    string StartKind,
    double? PositionSeconds = null,
    int? ChapterIndex = null,
    string? SourceLabel = null);

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

    [JsonPropertyName("chapters")]
    public IReadOnlyList<PlaybackChapterDto> Chapters { get; init; } = [];

    [JsonPropertyName("chapter_index")]
    public int? ChapterIndex { get; init; }

    [JsonPropertyName("start_at_exact_position")]
    public bool StartAtExactPosition { get; init; }

    [JsonPropertyName("audiobook_start_kind")]
    public string? AudiobookStartKind { get; init; }

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

    [JsonPropertyName("audiobook_bookmarks")]
    public List<AudiobookBookmarkDto> AudiobookBookmarks { get; init; } = [];

    [JsonPropertyName("current_index")]
    public int CurrentIndex { get; init; }

    [JsonPropertyName("source_label")]
    public string? SourceLabel { get; init; }

    [JsonPropertyName("current_browser_stream_url")]
    public string? CurrentBrowserStreamUrl { get; init; }

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

    [JsonPropertyName("skip_back_seconds")]
    public int SkipBackSeconds { get; init; } = 15;

    [JsonPropertyName("skip_forward_seconds")]
    public int SkipForwardSeconds { get; init; } = 15;

    [JsonPropertyName("sleep_timer_options_minutes")]
    public List<int> SleepTimerOptionsMinutes { get; init; } = [5, 10, 15, 30, 45, 60];

    [JsonPropertyName("allow_end_of_chapter_sleep_timer")]
    public bool AllowEndOfChapterSleepTimer { get; init; } = true;

    [JsonPropertyName("playback_start_version")]
    public long PlaybackStartVersion { get; init; }

    [JsonPropertyName("sleep_timer_mode")]
    public string SleepTimerMode { get; init; } = ListenSleepTimerModes.Off;

    [JsonPropertyName("sleep_timer_ends_at_utc")]
    public DateTimeOffset? SleepTimerEndsAtUtc { get; init; }
}
