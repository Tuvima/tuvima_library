using System.Globalization;
using Dapper;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Playback;

public sealed class PlayerService
{
    private static readonly Guid DefaultProfileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly TimeSpan StaleSessionWindow = TimeSpan.FromSeconds(45);

    private readonly PlayerSessionRepository _sessions;
    private readonly PlaybackCapabilitiesService _playback;
    private readonly IDatabaseConnection _db;
    private readonly IMediaAssetRepository _assets;
    private readonly IUserStateStore _userStates;
    private readonly IUserPlaybackSettingsService _settings;
    private readonly AudiobookListenHistoryRepository _history;

    public PlayerService(
        PlayerSessionRepository sessions,
        PlaybackCapabilitiesService playback,
        IDatabaseConnection db,
        IMediaAssetRepository assets,
        IUserStateStore userStates,
        IUserPlaybackSettingsService settings,
        AudiobookListenHistoryRepository history)
    {
        _sessions = sessions;
        _playback = playback;
        _db = db;
        _assets = assets;
        _userStates = userStates;
        _settings = settings;
        _history = history;
    }

    public async Task<PlayerStateDto> GetStateAsync(Guid? profileId, string? deviceId, string? client, CancellationToken ct = default)
    {
        var normalizedProfile = ResolveProfileId(profileId);
        await _sessions.EnsureSessionAsync(
            normalizedProfile,
            Guid.NewGuid(),
            NormalizeDeviceId(deviceId),
            NormalizeClient(client),
            ct);

        return await EnrichStateAsync(
            await _sessions.GetStateAsync(normalizedProfile, StaleSessionWindow, ct)
                ?? EmptyState(normalizedProfile, NormalizeDeviceId(deviceId), NormalizeClient(client)),
            ct);
    }

    public async Task<PlayerStateDto> ReplaceQueueAsync(PlayerQueueMutationDto request, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        var deviceId = NormalizeDeviceId(request.DeviceId);
        var client = NormalizeClient(request.Client);
        var sessionId = Guid.NewGuid();
        var items = await ResolveQueueItemsAsync(request, ct);
        ValidateQueueReplacement(items);

        if (request.Shuffle)
        {
            items = items.OrderBy(_ => Guid.NewGuid()).ToList();
        }

        var currentQueueItemId = request.StartQueueItemId;
        if (!currentQueueItemId.HasValue && request.StartIndex.HasValue && items.Count > 0)
        {
            currentQueueItemId = items[Math.Clamp(request.StartIndex.Value, 0, items.Count - 1)].QueueItemId;
        }

        if (!currentQueueItemId.HasValue && request.StartWorkId.HasValue)
        {
            currentQueueItemId = items.FirstOrDefault(item => item.WorkId == request.StartWorkId.Value)?.QueueItemId;
        }

        await _sessions.ReplaceQueueAsync(
            profileId,
            sessionId,
            deviceId,
            client,
            items,
            currentQueueItemId,
            request.SourceLabel,
            request.Shuffle,
            request.ExpectedStateVersion,
            request.Force,
            ct);

        return await GetStateAsync(profileId, deviceId, client, ct);
    }

    public async Task<PlayerStateDto> AddQueueItemsAsync(PlayerQueueMutationDto request, bool insertNext, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        var deviceId = NormalizeDeviceId(request.DeviceId);
        var client = NormalizeClient(request.Client);
        var items = await ResolveQueueItemsAsync(request, ct);
        ValidateQueueAddition(items);

        await _sessions.AddQueueItemsAsync(
            profileId,
            Guid.NewGuid(),
            deviceId,
            client,
            items,
            insertNext,
            request.ExpectedStateVersion,
            request.Force,
            ct);

        return await GetStateAsync(profileId, deviceId, client, ct);
    }

    public async Task<PlayerStateDto> ReorderQueueAsync(PlayerQueueMutationDto request, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        await _sessions.ReorderQueueAsync(profileId, request.QueueItemIds, request.ExpectedStateVersion, request.Force, ct);
        return await GetStateAsync(profileId, request.DeviceId, request.Client, ct);
    }

    public async Task<PlayerStateDto> RemoveQueueItemAsync(Guid queueItemId, PlayerQueueMutationDto request, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        await _sessions.RemoveQueueItemAsync(profileId, queueItemId, request.ExpectedStateVersion, request.Force, ct);
        return await GetStateAsync(profileId, request.DeviceId, request.Client, ct);
    }

    public async Task<PlayerStateDto> ClearQueueAsync(PlayerQueueMutationDto request, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        await _sessions.ClearQueueAsync(profileId, request.ExpectedStateVersion, request.Force, ct);
        return await GetStateAsync(profileId, request.DeviceId, request.Client, ct);
    }

    public async Task<PlayerStateDto> ApplyCommandAsync(PlayerCommandRequestDto request, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        await _sessions.EnsureSessionAsync(profileId, Guid.NewGuid(), NormalizeDeviceId(request.DeviceId), NormalizeClient(request.Client), ct);
        var state = await _sessions.GetStateAsync(profileId, StaleSessionWindow, ct)
            ?? EmptyState(profileId, NormalizeDeviceId(request.DeviceId), NormalizeClient(request.Client));

        var command = NormalizeCommand(request.Command);
        switch (command)
        {
            case PlayerCommands.Play:
                await PlayAsync(profileId, state, request.QueueItemId, ct);
                break;
            case PlayerCommands.Pause:
                await _sessions.UpdateTransportAsync(profileId, playbackState: PlayerPlaybackStates.Paused, ct: ct);
                break;
            case PlayerCommands.Stop:
                await _sessions.UpdateTransportAsync(
                    profileId,
                    playbackState: PlayerPlaybackStates.Stopped,
                    positionSeconds: 0,
                    progressPct: 0,
                    ct: ct);
                break;
            case PlayerCommands.Next:
                await MoveRelativeAsync(profileId, state, 1, ct);
                break;
            case PlayerCommands.Previous:
                await MoveRelativeAsync(profileId, state, -1, ct);
                break;
            case PlayerCommands.Seek:
                await _sessions.UpdateTransportAsync(
                    profileId,
                    positionSeconds: Math.Max(0, request.PositionSeconds ?? state.PositionSeconds),
                    durationSeconds: request.DurationSeconds,
                    progressPct: CalculateProgress(request.PositionSeconds ?? state.PositionSeconds, request.DurationSeconds ?? state.DurationSeconds),
                    ct: ct);
                break;
            case PlayerCommands.RelativeSeek:
                var relativePosition = Math.Max(0, state.PositionSeconds + (request.DeltaSeconds ?? 0));
                await _sessions.UpdateTransportAsync(
                    profileId,
                    positionSeconds: relativePosition,
                    durationSeconds: request.DurationSeconds ?? state.DurationSeconds,
                    progressPct: CalculateProgress(relativePosition, request.DurationSeconds ?? state.DurationSeconds),
                    ct: ct);
                break;
            case PlayerCommands.Volume:
                await _sessions.UpdateTransportAsync(profileId, volume: ClampVolume(request.Volume), ct: ct);
                break;
            case PlayerCommands.Mute:
                await _sessions.UpdateTransportAsync(profileId, isMuted: request.IsMuted ?? !state.IsMuted, ct: ct);
                break;
            case PlayerCommands.Speed:
                await _sessions.UpdateTransportAsync(profileId, playbackRate: ClampPlaybackRate(request.PlaybackRate), ct: ct);
                break;
            case PlayerCommands.ScanStart:
                await _sessions.UpdateTransportAsync(profileId, playbackRate: ClampScanRate(request.PlaybackRate), ct: ct);
                break;
            case PlayerCommands.ScanStop:
                await _sessions.UpdateTransportAsync(profileId, playbackRate: ClampPlaybackRate(request.PlaybackRate ?? 1d), ct: ct);
                break;
            case PlayerCommands.Shuffle:
                await _sessions.UpdateTransportAsync(profileId, shuffleEnabled: request.ShuffleEnabled ?? !state.ShuffleEnabled, ct: ct);
                break;
            case PlayerCommands.Repeat:
                await _sessions.UpdateTransportAsync(profileId, repeatMode: NormalizeRepeatMode(request.RepeatMode), ct: ct);
                break;
        }

        return await GetStateAsync(profileId, request.DeviceId, request.Client, ct);
    }

    public async Task<PlayerStateDto> HeartbeatAsync(PlayerHeartbeatDto heartbeat, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(heartbeat.ProfileId);
        var deviceId = NormalizeDeviceId(heartbeat.DeviceId);
        var client = NormalizeClient(heartbeat.Client);
        await _sessions.EnsureSessionAsync(profileId, heartbeat.SessionId ?? Guid.NewGuid(), deviceId, client, ct);
        var priorState = await _sessions.GetStateAsync(profileId, StaleSessionWindow, ct);

        var position = Math.Max(0, heartbeat.PositionSeconds);
        var duration = heartbeat.DurationSeconds;
        var progress = heartbeat.ProgressPct ?? CalculateProgress(position, duration);
        var state = heartbeat.IsPlaying ? PlayerPlaybackStates.Playing : PlayerPlaybackStates.Paused;

        await _sessions.UpdateTransportAsync(
            profileId,
            playbackState: state,
            currentQueueItemId: heartbeat.QueueItemId,
            setCurrentQueueItem: heartbeat.QueueItemId.HasValue,
            positionSeconds: position,
            durationSeconds: duration,
            progressPct: progress,
            volume: heartbeat.Volume.HasValue ? ClampVolume(heartbeat.Volume) : null,
            isMuted: heartbeat.IsMuted,
            playbackRate: heartbeat.PlaybackRate.HasValue ? ClampPlaybackRate(heartbeat.PlaybackRate) : null,
            heartbeat: true,
            ct: ct);

        var assetId = heartbeat.AssetId;
        if (!assetId.HasValue && heartbeat.QueueItemId.HasValue)
        {
            var current = await _sessions.GetStateAsync(profileId, StaleSessionWindow, ct);
            assetId = current?.Queue.FirstOrDefault(item => item.QueueItemId == heartbeat.QueueItemId.Value)?.AssetId;
        }

        if (assetId.HasValue)
        {
            await SaveResumeAsync(profileId, assetId.Value, position, duration, progress, state, heartbeat.SessionId, deviceId, client, ct);
        }

        var historyItem = ResolveHistoryItem(priorState, heartbeat, assetId);
        if (historyItem is not null && IsAudiobook(historyItem.MediaType))
        {
            var settings = await GetPlaybackSettingsOrDefaultAsync(profileId, ct);
            await _history.TrackHeartbeatAsync(
                profileId,
                historyItem,
                heartbeat,
                settings.Listening.AudiobookListenQualificationSeconds,
                deviceId,
                client,
                ct);
        }

        return await GetStateAsync(profileId, deviceId, client, ct);
    }

    public async Task<PlayerStateDto> TakeoverAsync(PlayerSessionTakeoverRequestDto request, CancellationToken ct = default)
    {
        var profileId = ResolveProfileId(request.ProfileId);
        var deviceId = NormalizeDeviceId(request.DeviceId);
        var client = NormalizeClient(request.Client);
        await _sessions.TakeoverAsync(profileId, Guid.NewGuid(), deviceId, client, request.Force, StaleSessionWindow, ct);
        return await GetStateAsync(profileId, deviceId, client, ct);
    }

    public PlayerCapabilitiesDto GetCapabilities() => new();

    private async Task PlayAsync(Guid profileId, PlayerStateDto state, Guid? requestedQueueItemId, CancellationToken ct)
    {
        var queueItemId = requestedQueueItemId ?? state.CurrentQueueItemId ?? state.Queue.FirstOrDefault()?.QueueItemId;
        var item = queueItemId.HasValue
            ? state.Queue.FirstOrDefault(candidate => candidate.QueueItemId == queueItemId.Value)
            : null;

        await _sessions.UpdateTransportAsync(
            profileId,
            playbackState: item is null ? PlayerPlaybackStates.Stopped : PlayerPlaybackStates.Playing,
            currentQueueItemId: item?.QueueItemId,
            setCurrentQueueItem: item is not null,
            positionSeconds: item is null ? null : Math.Max(0, item.PositionSeconds ?? 0),
            durationSeconds: item?.DurationSeconds,
            progressPct: CalculateProgress(item?.PositionSeconds, item?.DurationSeconds),
            ct: ct);
    }

    private async Task MoveRelativeAsync(Guid profileId, PlayerStateDto state, int delta, CancellationToken ct)
    {
        if (state.Queue.Count == 0)
        {
            await _sessions.UpdateTransportAsync(profileId, playbackState: PlayerPlaybackStates.Stopped, ct: ct);
            return;
        }

        var currentIndex = state.CurrentQueueItemId.HasValue
            ? state.Queue.ToList().FindIndex(item => item.QueueItemId == state.CurrentQueueItemId.Value)
            : -1;
        var nextIndex = currentIndex + delta;

        if (nextIndex < 0)
        {
            nextIndex = string.Equals(state.RepeatMode, PlayerRepeatModes.All, StringComparison.OrdinalIgnoreCase)
                ? state.Queue.Count - 1
                : 0;
        }
        else if (nextIndex >= state.Queue.Count)
        {
            if (!string.Equals(state.RepeatMode, PlayerRepeatModes.All, StringComparison.OrdinalIgnoreCase))
            {
                await _sessions.UpdateTransportAsync(profileId, playbackState: PlayerPlaybackStates.Paused, ct: ct);
                return;
            }

            nextIndex = 0;
        }

        var next = state.Queue[nextIndex];
        await _sessions.UpdateTransportAsync(
            profileId,
            playbackState: PlayerPlaybackStates.Playing,
            currentQueueItemId: next.QueueItemId,
            setCurrentQueueItem: true,
            positionSeconds: 0,
            durationSeconds: next.DurationSeconds,
            progressPct: 0,
            ct: ct);
    }

    private async Task<IReadOnlyList<PlayerQueueItemDto>> ResolveQueueItemsAsync(PlayerQueueMutationDto request, CancellationToken ct)
    {
        var items = new List<PlayerQueueItemDto>();
        var explicitWorkIds = new HashSet<Guid>();

        foreach (var requested in request.Items.Where(item => item.WorkId != Guid.Empty))
        {
            var resolved = await ResolvePlayableWorkAsync(requested.WorkId, ct);
            if (resolved is null)
            {
                continue;
            }

            explicitWorkIds.Add(requested.WorkId);
            items.Add(MergeQueueItem(resolved, requested, request.SourceLabel));
        }

        foreach (var workId in request.WorkIds.Where(id => id != Guid.Empty).Distinct().Where(id => !explicitWorkIds.Contains(id)))
        {
            var resolved = await ResolvePlayableWorkAsync(workId, ct);
            if (resolved is null)
            {
                continue;
            }

            items.Add(resolved with
            {
                QueueItemId = Guid.NewGuid(),
                AddedAt = DateTimeOffset.UtcNow,
                Subtitle = FirstNonBlank(resolved.Subtitle, resolved.Artist, resolved.Author, resolved.Narrator, request.SourceLabel),
            });
        }

        return items;
    }

    private static PlayerQueueItemDto MergeQueueItem(
        PlayerQueueItemDto resolved,
        PlayerQueueMutationItemDto requested,
        string? sourceLabel)
    {
        var assetId = requested.AssetId ?? resolved.AssetId;
        return resolved with
        {
            QueueItemId = Guid.NewGuid(),
            AddedAt = DateTimeOffset.UtcNow,
            AssetId = assetId,
            CollectionId = requested.CollectionId ?? resolved.CollectionId,
            MediaType = FirstNonBlank(requested.MediaType, resolved.MediaType) ?? resolved.MediaType,
            Title = FirstNonBlank(requested.Title, resolved.Title) ?? resolved.Title,
            Subtitle = FirstNonBlank(requested.Subtitle, resolved.Subtitle, requested.Artist, resolved.Artist, requested.Author, resolved.Author, requested.Narrator, resolved.Narrator, sourceLabel),
            Album = FirstNonBlank(requested.Album, resolved.Album),
            Author = FirstNonBlank(requested.Author, resolved.Author),
            Artist = FirstNonBlank(requested.Artist, resolved.Artist),
            Narrator = FirstNonBlank(requested.Narrator, resolved.Narrator),
            Series = FirstNonBlank(requested.Series, resolved.Series),
            CoverUrl = FirstNonBlank(requested.CoverUrl, resolved.CoverUrl, assetId.HasValue ? $"/stream/{assetId.Value}/cover" : null),
            DurationSeconds = requested.DurationSeconds ?? resolved.DurationSeconds,
            PositionSeconds = requested.PositionSeconds.HasValue ? Math.Max(0, requested.PositionSeconds.Value) : resolved.PositionSeconds,
            StreamUrl = FirstNonBlank(requested.StreamUrl, resolved.StreamUrl, assetId.HasValue ? $"/stream/{assetId.Value}" : null),
            DownloadUrl = FirstNonBlank(requested.DownloadUrl, resolved.DownloadUrl, assetId.HasValue ? $"/stream/{assetId.Value}" : null),
        };
    }

    private async Task<PlayerQueueItemDto?> ResolvePlayableWorkAsync(Guid workId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<PlayableWorkRow>("""
            SELECT w.id AS WorkId,
                   ma.id AS AssetId,
                   w.collection_id AS CollectionId,
                   w.media_type AS MediaType,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'title' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'title' THEN acv.value END),
                       'Untitled audio'
                   ) AS Title,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'subtitle' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'subtitle' THEN acv.value END)
                   ) AS Subtitle,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'album' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'album' THEN acv.value END),
                       MAX(CASE WHEN wcv.key = 'series' THEN wcv.value END)
                   ) AS Album,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'author' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'author' THEN acv.value END)
                   ) AS Author,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'artist' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'artist' THEN acv.value END)
                   ) AS Artist,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'narrator' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'narrator' THEN acv.value END)
                   ) AS Narrator,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'series' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'series' THEN acv.value END)
                   ) AS Series,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'duration' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'duration' THEN acv.value END),
                       MAX(CASE WHEN wcv.key = 'runtime' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'runtime' THEN acv.value END)
                   ) AS Duration
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values wcv ON wcv.entity_id = w.id
            LEFT JOIN canonical_values acv ON acv.entity_id = ma.id
            WHERE w.id = @workId
              AND LOWER(w.media_type) IN ('music', 'audiobooks', 'audiobook', 'audio')
            GROUP BY w.id, ma.id
            ORDER BY ma.presented_at IS NULL, ma.presented_at DESC, ma.file_path_root
            LIMIT 1;
            """, new { workId });

        if (row is null)
        {
            return null;
        }

        return new PlayerQueueItemDto
        {
            WorkId = row.WorkId,
            AssetId = row.AssetId,
            CollectionId = row.CollectionId,
            MediaType = row.MediaType,
            Title = row.Title,
            Subtitle = row.Subtitle,
            Album = row.Album,
            Author = row.Author,
            Artist = row.Artist,
            Narrator = row.Narrator,
            Series = row.Series,
            CoverUrl = row.AssetId.HasValue ? $"/stream/{row.AssetId.Value}/cover" : null,
            DurationSeconds = TryParseDurationSeconds(row.Duration),
            StreamUrl = row.AssetId.HasValue ? $"/stream/{row.AssetId.Value}" : null,
            DownloadUrl = row.AssetId.HasValue ? $"/stream/{row.AssetId.Value}" : null,
        };
    }

    private async Task<PlayerStateDto> EnrichStateAsync(PlayerStateDto state, CancellationToken ct)
    {
        var current = state.CurrentItem;
        if (current?.AssetId is null)
        {
            return state with
            {
                Capabilities = GetCapabilities(),
                Experience = ExperienceFor(state.Queue),
            };
        }

        var manifest = await _playback.BuildManifestAsync(current.AssetId.Value, state.Client, ct);
        if (manifest is null)
        {
            return state with { Capabilities = GetCapabilities() };
        }

        var enrichedCurrent = current with
        {
            StreamUrl = manifest.DirectStreamUrl ?? current.StreamUrl,
            DurationSeconds = current.DurationSeconds,
            PositionSeconds = state.PositionSeconds > 0 ? state.PositionSeconds : current.PositionSeconds ?? manifest.Resume?.PositionSeconds,
            ProgressPct = state.ProgressPct > 0 ? state.ProgressPct : current.ProgressPct ?? manifest.Resume?.ProgressPct,
            Chapters = manifest.Chapters,
            Manifest = manifest,
        };

        var queue = state.Queue
            .Select(item => item.QueueItemId == enrichedCurrent.QueueItemId ? enrichedCurrent : item)
            .ToList();

        return state with
        {
            CurrentItem = enrichedCurrent,
            Queue = queue,
            Experience = ExperienceFor(queue),
            Capabilities = GetCapabilities(),
            AudiobookHistory = IsAudiobook(enrichedCurrent.MediaType)
                ? await _history.GetRecentAsync(state.ProfileId, enrichedCurrent.WorkId, 10, ct)
                : [],
            Warnings = manifest.Warnings,
        };
    }

    private async Task SaveResumeAsync(
        Guid profileId,
        Guid assetId,
        double positionSeconds,
        double? durationSeconds,
        double progressPct,
        string playbackState,
        Guid? sessionId,
        string deviceId,
        string client,
        CancellationToken ct)
    {
        var asset = await _assets.FindByIdAsync(assetId, ct);
        if (asset is null)
        {
            return;
        }

        var extended = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["position_seconds"] = positionSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            ["playback_state"] = playbackState,
            ["player_device_id"] = deviceId,
            ["player_client"] = client,
            ["updated_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        if (durationSeconds.HasValue)
        {
            extended["duration_seconds"] = durationSeconds.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (sessionId.HasValue)
        {
            extended["player_session_id"] = sessionId.Value.ToString("D");
        }

        await _userStates.SaveAsync(new UserState
        {
            UserId = profileId,
            AssetId = assetId,
            ContentHash = asset.ContentHash,
            ProgressPct = Math.Clamp(progressPct, 0, 100),
            LastAccessed = DateTimeOffset.UtcNow,
            ExtendedProperties = extended,
        }, ct);
    }

    private static PlayerStateDto EmptyState(Guid profileId, string deviceId, string client) => new()
    {
        ProfileId = profileId,
        SessionId = Guid.NewGuid(),
        DeviceId = deviceId,
        Client = client,
        PlaybackState = PlayerPlaybackStates.Stopped,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Capabilities = new(),
        IsStale = true,
    };

    public Task<IReadOnlyList<AudiobookListenHistoryItemDto>> GetAudiobookHistoryAsync(
        Guid? profileId,
        Guid workId,
        int? limit,
        CancellationToken ct = default)
    {
        var normalizedProfile = ResolveProfileId(profileId);
        return _history.GetRecentAsync(normalizedProfile, workId, limit ?? 10, ct);
    }

    private static Guid ResolveProfileId(Guid? profileId) =>
        profileId.GetValueOrDefault() == Guid.Empty ? DefaultProfileId : profileId!.Value;

    private static string NormalizeDeviceId(string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? "web-dashboard" : deviceId.Trim();

    private static string NormalizeClient(string? client) =>
        string.IsNullOrWhiteSpace(client) ? "web" : client.Trim().ToLowerInvariant();

    private static string NormalizeCommand(string? command) =>
        string.IsNullOrWhiteSpace(command) ? PlayerCommands.Pause : command.Trim().ToLowerInvariant();

    private static string NormalizeRepeatMode(string? repeatMode) =>
        repeatMode?.Trim().ToLowerInvariant() switch
        {
            PlayerRepeatModes.One => PlayerRepeatModes.One,
            PlayerRepeatModes.All => PlayerRepeatModes.All,
            _ => PlayerRepeatModes.Off,
        };

    private static double? ClampVolume(double? volume) =>
        volume.HasValue ? Math.Clamp(volume.Value, 0d, 1d) : null;

    private static double? ClampPlaybackRate(double? playbackRate) =>
        playbackRate.HasValue ? Math.Clamp(playbackRate.Value, 0.5d, 2d) : null;

    private static double? ClampScanRate(double? playbackRate) =>
        playbackRate.HasValue ? Math.Clamp(playbackRate.Value, 1d, 32d) : null;

    private static void ValidateQueueReplacement(IReadOnlyList<PlayerQueueItemDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Any(item => IsAudiobook(item.MediaType)) && items.Any(item => IsMusic(item.MediaType)))
        {
            throw new InvalidOperationException("Music and audiobooks cannot share one player queue.");
        }

        if (items.Count(item => IsAudiobook(item.MediaType)) > 1)
        {
            throw new InvalidOperationException("Audiobooks play one title at a time. Replace the current audiobook instead of queueing chapters.");
        }
    }

    private static void ValidateQueueAddition(IReadOnlyList<PlayerQueueItemDto> items)
    {
        if (items.Any(item => IsAudiobook(item.MediaType)))
        {
            throw new InvalidOperationException("Audiobooks play one title at a time. Use queue replacement to start an audiobook.");
        }
    }

    private static PlayerQueueItemDto? ResolveHistoryItem(PlayerStateDto? state, PlayerHeartbeatDto heartbeat, Guid? assetId)
    {
        if (state is null || state.Queue.Count == 0)
        {
            return null;
        }

        if (heartbeat.QueueItemId.HasValue)
        {
            return state.Queue.FirstOrDefault(item => item.QueueItemId == heartbeat.QueueItemId.Value);
        }

        if (state.CurrentItem is not null)
        {
            return state.CurrentItem;
        }

        return assetId.HasValue
            ? state.Queue.FirstOrDefault(item => item.AssetId == assetId.Value)
            : null;
    }

    private async Task<UserPlaybackSettingsDto> GetPlaybackSettingsOrDefaultAsync(Guid profileId, CancellationToken ct)
    {
        try
        {
            return await _settings.GetOrCreateDefaultsAsync(profileId, ct);
        }
        catch
        {
            return UserPlaybackSettingsDto.CreateDefaults(profileId);
        }
    }

    private static string ExperienceFor(IReadOnlyList<PlayerQueueItemDto> queue) =>
        queue.Any(item => IsAudiobook(item.MediaType))
            ? PlayerExperienceModes.Audiobook
            : PlayerExperienceModes.Music;

    private static bool IsAudiobook(string? mediaType) =>
        mediaType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true
        && mediaType.Contains("book", StringComparison.OrdinalIgnoreCase);

    private static bool IsMusic(string? mediaType) =>
        mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true;

    private static double CalculateProgress(double? positionSeconds, double? durationSeconds)
    {
        if (!positionSeconds.HasValue || !durationSeconds.HasValue || durationSeconds.Value <= 0)
        {
            return 0;
        }

        return Math.Clamp(positionSeconds.Value / durationSeconds.Value * 100d, 0d, 100d);
    }

    private static double? TryParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds;
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 2 or 3 && parts.All(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            var multiplier = 1d;
            var total = 0d;
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                total += double.Parse(parts[i], CultureInfo.InvariantCulture) * multiplier;
                multiplier *= 60d;
            }

            return total;
        }

        if (text.EndsWith("min", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
        {
            return minutes * 60d;
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record PlayableWorkRow
    {
        public Guid WorkId { get; init; }
        public Guid? AssetId { get; init; }
        public Guid? CollectionId { get; init; }
        public string MediaType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Subtitle { get; init; }
        public string? Album { get; init; }
        public string? Author { get; init; }
        public string? Artist { get; init; }
        public string? Narrator { get; init; }
        public string? Series { get; init; }
        public string? Duration { get; init; }
    }
}
