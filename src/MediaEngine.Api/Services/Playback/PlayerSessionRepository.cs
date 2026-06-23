using Dapper;
using MediaEngine.Contracts.Playback;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Playback;

public sealed class PlayerSessionRepository
{
    private readonly IDatabaseConnection _db;

    public PlayerSessionRepository(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task<PlayerStateDto?> GetStateAsync(Guid profileId, TimeSpan staleAfter, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();

        var session = conn.QueryFirstOrDefault<PlayerSessionRow>("""
            SELECT profile_id AS ProfileId,
                   session_id AS SessionId,
                   device_id AS DeviceId,
                   client AS Client,
                   playback_state AS PlaybackState,
                   current_queue_item_id AS CurrentQueueItemId,
                   position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   progress_pct AS ProgressPct,
                   volume AS Volume,
                   is_muted AS IsMuted,
                   playback_rate AS PlaybackRate,
                   shuffle_enabled AS ShuffleEnabled,
                   repeat_mode AS RepeatMode,
                   source_label AS SourceLabel,
                   state_version AS StateVersion,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt,
                   last_heartbeat_at AS LastHeartbeatAt
            FROM player_sessions
            WHERE profile_id = @profileId
            LIMIT 1;
            """, new { profileId });

        if (session is null)
        {
            return Task.FromResult<PlayerStateDto?>(null);
        }

        var queue = conn.Query<PlayerQueueItemRow>("""
            SELECT id AS QueueItemId,
                   work_id AS WorkId,
                   asset_id AS AssetId,
                   collection_id AS CollectionId,
                   media_type AS MediaType,
                   title AS Title,
                   subtitle AS Subtitle,
                   album AS Album,
                   author AS Author,
                   artist AS Artist,
                   narrator AS Narrator,
                   series AS Series,
                   cover_url AS CoverUrl,
                   duration_seconds AS DurationSeconds,
                   stream_url AS StreamUrl,
                   download_url AS DownloadUrl,
                   added_at AS AddedAt
            FROM player_queue_items
            WHERE profile_id = @profileId
            ORDER BY position, added_at, title;
            """, new { profileId }).Select(ToDto).ToList();

        return Task.FromResult<PlayerStateDto?>(ToDto(session, queue, staleAfter));
    }

    public Task EnsureSessionAsync(Guid profileId, Guid sessionId, string deviceId, string client, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var now = DateTimeOffset.UtcNow;
        conn.Execute("""
            INSERT INTO player_sessions
                (profile_id, session_id, device_id, client, playback_state, created_at, updated_at)
            VALUES
                (@profileId, @sessionId, @deviceId, @client, 'stopped', @now, @now)
            ON CONFLICT(profile_id) DO NOTHING;
            """, new { profileId, sessionId, deviceId, client, now });

        return Task.CompletedTask;
    }

    public Task ReplaceQueueAsync(
        Guid profileId,
        Guid sessionId,
        string deviceId,
        string client,
        IReadOnlyList<PlayerQueueItemDto> items,
        Guid? currentQueueItemId,
        string? sourceLabel,
        bool shuffle,
        long? expectedStateVersion,
        bool force,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        EnsureExpectedVersion(conn, tx, profileId, expectedStateVersion, force);
        var now = DateTimeOffset.UtcNow;
        conn.Execute("""
            INSERT INTO player_sessions
                (profile_id, session_id, device_id, client, playback_state, created_at, updated_at)
            VALUES
                (@profileId, @sessionId, @deviceId, @client, 'stopped', @now, @now)
            ON CONFLICT(profile_id) DO NOTHING;
            """, new { profileId, sessionId, deviceId, client, now }, tx);

        conn.Execute("DELETE FROM player_queue_items WHERE profile_id = @profileId;", new { profileId }, tx);

        InsertItems(conn, tx, profileId, items, 0, sourceLabel);
        var current = currentQueueItemId ?? items.FirstOrDefault()?.QueueItemId;
        var currentItem = current.HasValue ? items.FirstOrDefault(item => item.QueueItemId == current.Value) : null;
        conn.Execute("""
            UPDATE player_sessions
            SET session_id = @sessionId,
                device_id = @deviceId,
                client = @client,
                playback_state = CASE WHEN @currentQueueItemId IS NULL THEN 'stopped' ELSE 'playing' END,
                current_queue_item_id = @currentQueueItemId,
                position_seconds = @positionSeconds,
                duration_seconds = @durationSeconds,
                progress_pct = @progressPct,
                shuffle_enabled = @shuffleEnabled,
                source_label = @sourceLabel,
                state_version = state_version + 1,
                updated_at = @now
            WHERE profile_id = @profileId;
            """, new
            {
                profileId,
                sessionId,
                deviceId,
                client,
                currentQueueItemId = current,
                positionSeconds = currentItem?.PositionSeconds ?? 0,
                durationSeconds = currentItem?.DurationSeconds,
                progressPct = CalculateProgress(currentItem?.PositionSeconds, currentItem?.DurationSeconds),
                shuffleEnabled = shuffle ? 1 : 0,
                sourceLabel,
                now,
            }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task AddQueueItemsAsync(
        Guid profileId,
        Guid sessionId,
        string deviceId,
        string client,
        IReadOnlyList<PlayerQueueItemDto> items,
        bool insertNext,
        long? expectedStateVersion,
        bool force,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        EnsureExpectedVersion(conn, tx, profileId, expectedStateVersion, force);

        var now = DateTimeOffset.UtcNow;
        conn.Execute("""
            INSERT INTO player_sessions
                (profile_id, session_id, device_id, client, playback_state, created_at, updated_at)
            VALUES
                (@profileId, @sessionId, @deviceId, @client, 'stopped', @now, @now)
            ON CONFLICT(profile_id) DO NOTHING;
            """, new { profileId, sessionId, deviceId, client, now }, tx);

        var insertPosition = conn.ExecuteScalar<int?>("""
            SELECT CASE
                     WHEN @insertNext = 1 AND current_queue_item_id IS NOT NULL THEN
                       COALESCE((SELECT position + 1 FROM player_queue_items WHERE id = current_queue_item_id), 0)
                     ELSE
                       COALESCE((SELECT MAX(position) + 1 FROM player_queue_items WHERE profile_id = @profileId), 0)
                   END
            FROM player_sessions
            WHERE profile_id = @profileId;
            """, new { profileId, insertNext = insertNext ? 1 : 0 }, tx) ?? 0;

        conn.Execute("""
            UPDATE player_queue_items
            SET position = position + @count
            WHERE profile_id = @profileId
              AND position >= @insertPosition;
            """, new { profileId, count = items.Count, insertPosition }, tx);

        InsertItems(conn, tx, profileId, items, insertPosition, null);

        var hasCurrent = conn.ExecuteScalar<int>("""
            SELECT COUNT(1)
            FROM player_sessions
            WHERE profile_id = @profileId
              AND current_queue_item_id IS NOT NULL;
            """, new { profileId }, tx) > 0;

        conn.Execute("""
            UPDATE player_sessions
            SET session_id = @sessionId,
                device_id = @deviceId,
                client = @client,
                current_queue_item_id = CASE WHEN @hasCurrent = 1 THEN current_queue_item_id ELSE @firstQueueItemId END,
                playback_state = CASE WHEN @hasCurrent = 1 THEN playback_state ELSE 'playing' END,
                position_seconds = CASE WHEN @hasCurrent = 1 THEN position_seconds ELSE @positionSeconds END,
                duration_seconds = CASE WHEN @hasCurrent = 1 THEN duration_seconds ELSE @durationSeconds END,
                progress_pct = CASE WHEN @hasCurrent = 1 THEN progress_pct ELSE @progressPct END,
                state_version = state_version + 1,
                updated_at = @now
            WHERE profile_id = @profileId;
            """, new
            {
                profileId,
                sessionId,
                deviceId,
                client,
                hasCurrent = hasCurrent ? 1 : 0,
                firstQueueItemId = items[0].QueueItemId,
                positionSeconds = items[0].PositionSeconds ?? 0,
                durationSeconds = items[0].DurationSeconds,
                progressPct = CalculateProgress(items[0].PositionSeconds, items[0].DurationSeconds),
                now,
            }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task ReorderQueueAsync(
        Guid profileId,
        IReadOnlyList<Guid> queueItemIds,
        long? expectedStateVersion,
        bool force,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        EnsureExpectedVersion(conn, tx, profileId, expectedStateVersion, force);

        for (var i = 0; i < queueItemIds.Count; i++)
        {
            conn.Execute("""
                UPDATE player_queue_items
                SET position = @position
                WHERE profile_id = @profileId
                  AND id = @queueItemId;
                """, new { profileId, queueItemId = queueItemIds[i], position = i }, tx);
        }

        conn.Execute("""
            UPDATE player_sessions
            SET state_version = state_version + 1,
                updated_at = @now
            WHERE profile_id = @profileId;
            """, new { profileId, now = DateTimeOffset.UtcNow }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task RemoveQueueItemAsync(Guid profileId, Guid queueItemId, long? expectedStateVersion, bool force, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        EnsureExpectedVersion(conn, tx, profileId, expectedStateVersion, force);

        var removedPosition = conn.ExecuteScalar<int?>("""
            SELECT position
            FROM player_queue_items
            WHERE profile_id = @profileId
              AND id = @queueItemId;
            """, new { profileId, queueItemId }, tx);

        var wasCurrent = conn.ExecuteScalar<int>("""
            SELECT COUNT(1)
            FROM player_sessions
            WHERE profile_id = @profileId
              AND current_queue_item_id = @queueItemId;
            """, new { profileId, queueItemId }, tx) > 0;

        conn.Execute("""
            DELETE FROM player_queue_items
            WHERE profile_id = @profileId
              AND id = @queueItemId;
            """, new { profileId, queueItemId }, tx);

        if (removedPosition.HasValue)
        {
            conn.Execute("""
                UPDATE player_queue_items
                SET position = position - 1
                WHERE profile_id = @profileId
                  AND position > @removedPosition;
                """, new { profileId, removedPosition }, tx);
        }

        Guid? nextCurrent = null;
        double? nextDuration = null;
        if (wasCurrent)
        {
            var next = conn.QueryFirstOrDefault<CurrentCandidateRow>("""
                SELECT id AS QueueItemId,
                       duration_seconds AS DurationSeconds
                FROM player_queue_items
                WHERE profile_id = @profileId
                ORDER BY position
                LIMIT 1;
                """, new { profileId }, tx);
            nextCurrent = next?.QueueItemId;
            nextDuration = next?.DurationSeconds;
        }

        conn.Execute("""
            UPDATE player_sessions
            SET current_queue_item_id = CASE WHEN @wasCurrent = 1 THEN @nextCurrent ELSE current_queue_item_id END,
                playback_state = CASE WHEN @wasCurrent = 1 AND @nextCurrent IS NULL THEN 'stopped' ELSE playback_state END,
                position_seconds = CASE WHEN @wasCurrent = 1 THEN 0 ELSE position_seconds END,
                duration_seconds = CASE WHEN @wasCurrent = 1 THEN @nextDuration ELSE duration_seconds END,
                progress_pct = CASE WHEN @wasCurrent = 1 THEN 0 ELSE progress_pct END,
                state_version = state_version + 1,
                updated_at = @now
            WHERE profile_id = @profileId;
            """, new
            {
                profileId,
                wasCurrent = wasCurrent ? 1 : 0,
                nextCurrent,
                nextDuration,
                now = DateTimeOffset.UtcNow,
            }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task ClearQueueAsync(Guid profileId, long? expectedStateVersion, bool force, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        EnsureExpectedVersion(conn, tx, profileId, expectedStateVersion, force);

        conn.Execute("DELETE FROM player_queue_items WHERE profile_id = @profileId;", new { profileId }, tx);
        conn.Execute("""
            UPDATE player_sessions
            SET playback_state = 'stopped',
                current_queue_item_id = NULL,
                position_seconds = 0,
                duration_seconds = NULL,
                progress_pct = 0,
                source_label = NULL,
                state_version = state_version + 1,
                updated_at = @now
            WHERE profile_id = @profileId;
            """, new { profileId, now = DateTimeOffset.UtcNow }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task UpdateTransportAsync(
        Guid profileId,
        string? playbackState = null,
        Guid? currentQueueItemId = null,
        bool setCurrentQueueItem = false,
        double? positionSeconds = null,
        double? durationSeconds = null,
        double? progressPct = null,
        double? volume = null,
        bool? isMuted = null,
        double? playbackRate = null,
        bool? shuffleEnabled = null,
        string? repeatMode = null,
        bool heartbeat = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE player_sessions
            SET playback_state = COALESCE(@playbackState, playback_state),
                current_queue_item_id = CASE WHEN @setCurrentQueueItem = 1 THEN @currentQueueItemId ELSE current_queue_item_id END,
                position_seconds = COALESCE(@positionSeconds, position_seconds),
                duration_seconds = COALESCE(@durationSeconds, duration_seconds),
                progress_pct = COALESCE(@progressPct, progress_pct),
                volume = COALESCE(@volume, volume),
                is_muted = COALESCE(@isMuted, is_muted),
                playback_rate = COALESCE(@playbackRate, playback_rate),
                shuffle_enabled = COALESCE(@shuffleEnabled, shuffle_enabled),
                repeat_mode = COALESCE(@repeatMode, repeat_mode),
                state_version = state_version + 1,
                updated_at = @now,
                last_heartbeat_at = CASE WHEN @heartbeat = 1 THEN @now ELSE last_heartbeat_at END
            WHERE profile_id = @profileId;
            """, new
            {
                profileId,
                playbackState,
                currentQueueItemId,
                setCurrentQueueItem = setCurrentQueueItem ? 1 : 0,
                positionSeconds,
                durationSeconds,
                progressPct,
                volume,
                isMuted = isMuted.HasValue ? isMuted.Value ? 1 : 0 : (int?)null,
                playbackRate,
                shuffleEnabled = shuffleEnabled.HasValue ? shuffleEnabled.Value ? 1 : 0 : (int?)null,
                repeatMode,
                heartbeat = heartbeat ? 1 : 0,
                now = DateTimeOffset.UtcNow,
            });

        return Task.CompletedTask;
    }

    public Task TakeoverAsync(Guid profileId, Guid sessionId, string deviceId, string client, bool force, TimeSpan staleAfter, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        var state = conn.QueryFirstOrDefault<PlayerSessionRow>("""
            SELECT profile_id AS ProfileId,
                   session_id AS SessionId,
                   device_id AS DeviceId,
                   client AS Client,
                   playback_state AS PlaybackState,
                   current_queue_item_id AS CurrentQueueItemId,
                   position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   progress_pct AS ProgressPct,
                   volume AS Volume,
                   is_muted AS IsMuted,
                   playback_rate AS PlaybackRate,
                   shuffle_enabled AS ShuffleEnabled,
                   repeat_mode AS RepeatMode,
                   source_label AS SourceLabel,
                   state_version AS StateVersion,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt,
                   last_heartbeat_at AS LastHeartbeatAt
            FROM player_sessions
            WHERE profile_id = @profileId
            LIMIT 1;
            """, new { profileId }, tx);

        if (state is not null && !force && !IsStale(state.LastHeartbeatAt, staleAfter))
        {
            throw new PlayerSessionConflictException("An active player session is still sending heartbeats. Retry takeover with force=true.");
        }

        var now = DateTimeOffset.UtcNow;
        conn.Execute("""
            INSERT INTO player_sessions
                (profile_id, session_id, device_id, client, playback_state, created_at, updated_at, last_heartbeat_at)
            VALUES
                (@profileId, @sessionId, @deviceId, @client, 'paused', @now, @now, @now)
            ON CONFLICT(profile_id) DO UPDATE SET
                session_id = excluded.session_id,
                device_id = excluded.device_id,
                client = excluded.client,
                playback_state = CASE WHEN player_sessions.current_queue_item_id IS NULL THEN 'stopped' ELSE 'paused' END,
                state_version = player_sessions.state_version + 1,
                updated_at = excluded.updated_at,
                last_heartbeat_at = excluded.last_heartbeat_at;
            """, new { profileId, sessionId, deviceId, client, now }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    private static void InsertItems(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid profileId,
        IReadOnlyList<PlayerQueueItemDto> items,
        int startPosition,
        string? sourceLabel)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            conn.Execute("""
                INSERT INTO player_queue_items
                    (id, profile_id, position, work_id, asset_id, collection_id, media_type, title,
                     subtitle, album, author, artist, narrator, series, cover_url, duration_seconds,
                     stream_url, download_url, added_at, source_label)
                VALUES
                    (@queueItemId, @profileId, @position, @workId, @assetId, @collectionId, @mediaType, @title,
                     @subtitle, @album, @author, @artist, @narrator, @series, @coverUrl, @durationSeconds,
                     @streamUrl, @downloadUrl, @addedAt, @sourceLabel);
                """, new
                {
                    queueItemId = item.QueueItemId,
                    profileId,
                    position = startPosition + i,
                    workId = item.WorkId,
                    assetId = item.AssetId,
                    collectionId = item.CollectionId,
                    mediaType = item.MediaType,
                    title = item.Title,
                    subtitle = item.Subtitle,
                    album = item.Album,
                    author = item.Author,
                    artist = item.Artist,
                    narrator = item.Narrator,
                    series = item.Series,
                    coverUrl = item.CoverUrl,
                    durationSeconds = item.DurationSeconds,
                    streamUrl = item.StreamUrl,
                    downloadUrl = item.DownloadUrl,
                    addedAt = item.AddedAt,
                    sourceLabel = sourceLabel ?? item.Album,
                }, tx);
        }
    }

    private static void EnsureExpectedVersion(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid profileId,
        long? expectedStateVersion,
        bool force)
    {
        if (!expectedStateVersion.HasValue || force)
        {
            return;
        }

        var current = conn.ExecuteScalar<long?>(
            "SELECT state_version FROM player_sessions WHERE profile_id = @profileId LIMIT 1;",
            new { profileId },
            tx);

        if (current.HasValue && current.Value != expectedStateVersion.Value)
        {
            throw new PlayerStateConflictException(current.Value, expectedStateVersion.Value);
        }
    }

    private static PlayerStateDto ToDto(PlayerSessionRow row, IReadOnlyList<PlayerQueueItemDto> queue, TimeSpan staleAfter)
    {
        var current = row.CurrentQueueItemId.HasValue
            ? queue.FirstOrDefault(item => item.QueueItemId == row.CurrentQueueItemId.Value)
            : null;

        return new PlayerStateDto
        {
            ProfileId = row.ProfileId,
            SessionId = row.SessionId,
            DeviceId = row.DeviceId,
            Client = row.Client,
            PlaybackState = row.PlaybackState,
            CurrentQueueItemId = row.CurrentQueueItemId,
            CurrentItem = current,
            Queue = queue,
            PositionSeconds = row.PositionSeconds,
            DurationSeconds = row.DurationSeconds,
            ProgressPct = row.ProgressPct,
            Volume = row.Volume,
            IsMuted = row.IsMuted != 0,
            PlaybackRate = row.PlaybackRate,
            ShuffleEnabled = row.ShuffleEnabled != 0,
            RepeatMode = row.RepeatMode,
            SourceLabel = row.SourceLabel,
            StateVersion = row.StateVersion,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            LastHeartbeatAt = row.LastHeartbeatAt,
            IsStale = IsStale(row.LastHeartbeatAt, staleAfter),
        };
    }

    private static PlayerQueueItemDto ToDto(PlayerQueueItemRow row) => new()
    {
        QueueItemId = row.QueueItemId,
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
        CoverUrl = row.CoverUrl,
        DurationSeconds = row.DurationSeconds,
        StreamUrl = row.StreamUrl,
        DownloadUrl = row.DownloadUrl,
        AddedAt = row.AddedAt,
    };

    private static bool IsStale(DateTimeOffset? lastHeartbeatAt, TimeSpan staleAfter) =>
        !lastHeartbeatAt.HasValue || DateTimeOffset.UtcNow - lastHeartbeatAt.Value > staleAfter;

    private static double CalculateProgress(double? positionSeconds, double? durationSeconds)
    {
        if (!positionSeconds.HasValue || !durationSeconds.HasValue || durationSeconds.Value <= 0)
        {
            return 0;
        }

        return Math.Clamp(positionSeconds.Value / durationSeconds.Value * 100d, 0d, 100d);
    }

    private sealed record PlayerSessionRow
    {
        public Guid ProfileId { get; init; }
        public Guid SessionId { get; init; }
        public string DeviceId { get; init; } = "web";
        public string Client { get; init; } = "web";
        public string PlaybackState { get; init; } = PlayerPlaybackStates.Stopped;
        public Guid? CurrentQueueItemId { get; init; }
        public double PositionSeconds { get; init; }
        public double? DurationSeconds { get; init; }
        public double ProgressPct { get; init; }
        public double Volume { get; init; } = 0.8d;
        public int IsMuted { get; init; }
        public double PlaybackRate { get; init; } = 1d;
        public int ShuffleEnabled { get; init; }
        public string RepeatMode { get; init; } = PlayerRepeatModes.Off;
        public string? SourceLabel { get; init; }
        public long StateVersion { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? LastHeartbeatAt { get; init; }
    }

    private sealed record PlayerQueueItemRow
    {
        public Guid QueueItemId { get; init; }
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
        public string? CoverUrl { get; init; }
        public double? DurationSeconds { get; init; }
        public string? StreamUrl { get; init; }
        public string? DownloadUrl { get; init; }
        public DateTimeOffset AddedAt { get; init; }
    }

    private sealed record CurrentCandidateRow(Guid QueueItemId, double? DurationSeconds);
}

public sealed class PlayerStateConflictException : Exception
{
    public PlayerStateConflictException(long currentVersion, long expectedVersion)
        : base($"Player state version conflict. Current version is {currentVersion}; request expected {expectedVersion}.")
    {
        CurrentVersion = currentVersion;
        ExpectedVersion = expectedVersion;
    }

    public long CurrentVersion { get; }
    public long ExpectedVersion { get; }
}

public sealed class PlayerSessionConflictException : Exception
{
    public PlayerSessionConflictException(string message)
        : base(message)
    {
    }
}
