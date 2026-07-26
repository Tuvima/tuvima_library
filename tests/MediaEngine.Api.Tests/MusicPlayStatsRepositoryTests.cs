using Dapper;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Playback;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class MusicPlayStatsRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima-music-plays-{Guid.NewGuid():N}.db");
    private readonly ManualTimeProvider _timeProvider = new(
        new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly DatabaseConnection _db;

    public MusicPlayStatsRepositoryTests()
    {
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    [Fact]
    public async Task TrackHeartbeatAsync_CountsLongTrackAfterThirtyListenedSecondsOnlyOncePerSegment()
    {
        var ids = await CreateProfileAssetAsync();
        var repository = CreateRepository();
        var item = CreateQueueItem(ids.WorkId, ids.AssetId, durationSeconds: 240);

        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 0, 240, true));
        await SetActiveProgressAsync(ids.ProfileId, listenedSeconds: 29, positionSeconds: 29);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 31, 240, true));
        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 42, 240, true));

        var stat = Assert.Single(await repository.GetStatsAsync(ids.ProfileId, [ids.WorkId])).Value;
        Assert.Equal(1, stat.PlayCount);
        Assert.Equal(_timeProvider.GetUtcNow(), stat.LastPlayedAt);

        using var conn = _db.CreateConnection();
        var keyTypes = await conn.QuerySingleAsync<KeyTypeRow>(
            """
            SELECT typeof(profile_id) AS ProfileType,
                   typeof(work_id) AS WorkType
            FROM music_play_stats
            WHERE profile_id = @profileId;
            """,
            new { profileId = ids.ProfileId });

        Assert.Equal("blob", keyTypes.ProfileType);
        Assert.Equal("blob", keyTypes.WorkType);
    }

    [Fact]
    public async Task TrackHeartbeatAsync_CountsShortTrackAtHalfItsDuration()
    {
        var ids = await CreateProfileAssetAsync();
        var repository = CreateRepository();
        var item = CreateQueueItem(ids.WorkId, ids.AssetId, durationSeconds: 20);

        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 0, 20, true));
        await SetActiveProgressAsync(ids.ProfileId, listenedSeconds: 9, positionSeconds: 9);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 10.2, 20, true));

        var stat = Assert.Single(await repository.GetStatsAsync(ids.ProfileId, [ids.WorkId])).Value;
        Assert.Equal(1, stat.PlayCount);
        Assert.Equal(_timeProvider.GetUtcNow(), stat.LastPlayedAt);
    }

    [Fact]
    public async Task TrackHeartbeatAsync_DoesNotTreatAForwardSeekAsListeningTime()
    {
        var ids = await CreateProfileAssetAsync();
        var repository = CreateRepository();
        var item = CreateQueueItem(ids.WorkId, ids.AssetId, durationSeconds: 240);

        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 0, 240, true));
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 120, 240, true));

        Assert.Empty(await repository.GetStatsAsync(ids.ProfileId, [ids.WorkId]));
    }

    [Fact]
    public async Task TrackHeartbeatAsync_RestartsAfterInactiveGapWithoutCountingElapsedPosition()
    {
        var ids = await CreateProfileAssetAsync();
        var repository = CreateRepository();
        var item = CreateQueueItem(ids.WorkId, ids.AssetId, durationSeconds: 240);

        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 0, 240, true));
        _timeProvider.Advance(TimeSpan.FromSeconds(46));
        await repository.TrackHeartbeatAsync(ids.ProfileId, item, Heartbeat(item, 120, 240, true));

        Assert.Empty(await repository.GetStatsAsync(ids.ProfileId, [ids.WorkId]));

        using var conn = _db.CreateConnection();
        var active = await conn.QuerySingleAsync<ActiveProgressRow>(
            """
            SELECT last_position_seconds AS PositionSeconds,
                   listened_seconds AS ListenedSeconds,
                   last_heartbeat_at AS LastHeartbeatAt
            FROM music_play_active_segments
            WHERE profile_id = @profileId;
            """,
            new { profileId = ids.ProfileId });

        Assert.Equal(120, active.PositionSeconds);
        Assert.Equal(0, active.ListenedSeconds);
        Assert.Equal(_timeProvider.GetUtcNow(), active.LastHeartbeatAt);
    }

    private MusicPlayStatsRepository CreateRepository() => new(_db, _timeProvider);

    private async Task SetActiveProgressAsync(Guid profileId, double listenedSeconds, double positionSeconds)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            UPDATE music_play_active_segments
            SET listened_seconds = @listenedSeconds,
                last_position_seconds = @positionSeconds,
                last_heartbeat_at = @lastHeartbeatAt
            WHERE profile_id = @profileId;
            """,
            new
            {
                profileId,
                listenedSeconds,
                positionSeconds,
                lastHeartbeatAt = _timeProvider.GetUtcNow(),
            });
    }

    private static PlayerQueueItemDto CreateQueueItem(Guid workId, Guid assetId, double durationSeconds) => new()
    {
        QueueItemId = Guid.NewGuid(),
        WorkId = workId,
        AssetId = assetId,
        MediaType = "Music",
        Title = "Test Song",
        DurationSeconds = durationSeconds,
    };

    private static PlayerHeartbeatDto Heartbeat(
        PlayerQueueItemDto item,
        double positionSeconds,
        double durationSeconds,
        bool isPlaying) => new()
    {
        QueueItemId = item.QueueItemId,
        AssetId = item.AssetId,
        IsPlaying = isPlaying,
        PositionSeconds = positionSeconds,
        DurationSeconds = durationSeconds,
        PlaybackRate = 1,
    };

    private async Task<(Guid ProfileId, Guid WorkId, Guid AssetId)> CreateProfileAssetAsync()
    {
        using var conn = _db.CreateConnection();
        var profileId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO profiles (id, display_name, avatar_color, role, created_at)
            VALUES (@profileId, 'Owner', '#7C4DFF', 'Administrator', @now);
            INSERT INTO collections (id, created_at) VALUES (@collectionId, @now);
            INSERT INTO works (id, collection_id, media_type) VALUES (@workId, @collectionId, 'Music');
            INSERT INTO editions (id, work_id) VALUES (@editionId, @workId);
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
            VALUES (@assetId, @editionId, @contentHash, '/library/Music/test.flac', 'Normal');
            """,
            new
            {
                profileId,
                collectionId,
                workId,
                editionId,
                assetId,
                contentHash = $"asset-{assetId:N}",
                now = DateTimeOffset.UtcNow,
            });
        return (profileId, workId, assetId);
    }

    public void Dispose()
    {
        _db.Dispose();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        SqliteConnection.ClearPool(connection);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed record KeyTypeRow
    {
        public string ProfileType { get; init; } = string.Empty;
        public string WorkType { get; init; } = string.Empty;
    }

    private sealed record ActiveProgressRow
    {
        public double PositionSeconds { get; init; }
        public double ListenedSeconds { get; init; }
        public DateTimeOffset LastHeartbeatAt { get; init; }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            _utcNow += duration;
        }
    }
}
