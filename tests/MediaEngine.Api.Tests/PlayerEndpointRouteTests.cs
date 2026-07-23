namespace MediaEngine.Api.Tests;

public sealed class PlayerEndpointRouteTests
{
    [Fact]
    public void PlayerEndpoints_ExposeEngineBackedPlaybackSessionSurface()
    {
        var endpointSource = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs"));
        var routeBuilderSource = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/DependencyInjection/ApiEndpointRouteBuilderExtensions.cs"));
        var programSource = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Program.cs"));

        Assert.Contains("app.MapPlayerEndpoints();", routeBuilderSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<PlayerSessionRepository>();", programSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<AudiobookListenHistoryRepository>();", programSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<MusicPlayStatsRepository>();", programSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<AudiobookBookmarkRepository>();", programSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<AudiobookChapterTitleOverrideRepository>();", programSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<AudiobookChapterNamingService>();", programSource, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<PlayerService>();", programSource, StringComparison.Ordinal);
        Assert.Contains("app.MapGroup(\"/player\")", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/state\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/capabilities\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/queue/replace\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/queue/items\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapMethods(\"/queue/order\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapDelete(\"/queue/items/{queueItemId:guid}\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapDelete(\"/queue\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/command\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/heartbeat\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/session/takeover\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/audiobooks/{workId:guid}/history\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/audiobooks/{workId:guid}/bookmarks\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/audiobooks/{workId:guid}/bookmarks\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapDelete(\"/audiobooks/bookmarks/{bookmarkId:guid}\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/audiobooks/{workId:guid}/chapters/suggest-names\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/audiobooks/{workId:guid}/chapter-overrides\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/audiobooks/{workId:guid}/chapter-overrides\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapDelete(\"/audiobooks/{workId:guid}/chapter-overrides/{assetId:guid}/{chapterIndex:int}\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("RequireAnyRole()", endpointSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IDatabaseConnection", endpointSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGroup(\"/playback\")", endpointSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerStorageSchema_PersistsQueueSessionAndExactResumePosition()
    {
        var schema = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Storage/Schema/schema.sql"));
        var repositorySource = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Services/Playback/PlayerSessionRepository.cs"));
        var serviceSource = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Services/Playback/PlayerService.cs"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS player_sessions", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS player_queue_items", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS audiobook_listen_active_segments", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS audiobook_listen_history", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS audiobook_bookmarks", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS music_play_active_segments", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS music_play_stats", schema, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS audiobook_chapter_title_overrides", schema, StringComparison.Ordinal);
        Assert.Contains("position_seconds", schema, StringComparison.Ordinal);
        Assert.Contains("progress_pct", schema, StringComparison.Ordinal);
        Assert.Contains("duration_seconds", schema, StringComparison.Ordinal);
        Assert.Contains("state_version", schema, StringComparison.Ordinal);
        Assert.Contains("last_heartbeat_at", schema, StringComparison.Ordinal);
        Assert.Contains("idx_player_queue_items_profile_position", schema, StringComparison.Ordinal);
        Assert.Contains("idx_player_sessions_heartbeat", schema, StringComparison.Ordinal);
        Assert.Contains("idx_audiobook_listen_history_profile_work", schema, StringComparison.Ordinal);
        Assert.Contains("idx_audiobook_bookmarks_profile_work", schema, StringComparison.Ordinal);
        Assert.Contains("idx_audiobook_chapter_title_overrides_work", schema, StringComparison.Ordinal);
        Assert.Contains("CreateConnection()", repositorySource, StringComparison.Ordinal);
        Assert.Contains("queueItemId = item.QueueItemId", repositorySource, StringComparison.Ordinal);
        Assert.Contains("workId = item.WorkId", repositorySource, StringComparison.Ordinal);
        Assert.Contains("PlayerStateConflictException", repositorySource, StringComparison.Ordinal);
        Assert.Contains("PlayerSessionConflictException", repositorySource, StringComparison.Ordinal);
        Assert.Contains("StaleSessionWindow", serviceSource, StringComparison.Ordinal);
        Assert.Contains("position_seconds", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ProgressPct = Math.Clamp(progressPct, 0, 100)", serviceSource, StringComparison.Ordinal);
        Assert.Contains("LOWER(w.media_type) IN ('music', 'audiobooks', 'audiobook', 'audio')", serviceSource, StringComparison.Ordinal);
        Assert.Contains("PlayerQueueMutationItemDto", serviceSource, StringComparison.Ordinal);
        Assert.Contains("requested.PositionSeconds", serviceSource, StringComparison.Ordinal);
        Assert.Contains("StartIndex", serviceSource, StringComparison.Ordinal);
        Assert.Contains("PlayerExperienceModes.Audiobook", serviceSource, StringComparison.Ordinal);
        Assert.Contains("TrackHeartbeatAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackCapabilities_ProfileIncludesM4bForAudiobookDirectPlay()
    {
        var source = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Services/Playback/PlaybackCapabilitiesService.cs"));

        Assert.Contains("\"m4b\"", source, StringComparison.Ordinal);
        Assert.Contains("SupportedContainers = [\"mp4\", \"m4v\", \"webm\", \"mp3\", \"m4a\", \"m4b\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackCapabilities_DefinesAndroidAndIosMobileProfiles()
    {
        var source = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Services/Playback/PlaybackCapabilitiesService.cs"));

        Assert.Contains("Key = \"android\"", source, StringComparison.Ordinal);
        Assert.Contains("Key = \"ios\"", source, StringComparison.Ordinal);
        Assert.Contains("DisplayName = \"iOS\"", source, StringComparison.Ordinal);
        Assert.Contains("SupportedContainers = [\"mp4\", \"m4v\", \"mp3\", \"m4a\", \"m4b\", \"aac\", \"epub\", \"pdf\", \"cbz\"]", source, StringComparison.Ordinal);
        Assert.Contains("SupportsOfflineDownloads = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackCapabilities_UsesTimedProbeChaptersInsteadOfUntimedPlaceholders()
    {
        var source = File.ReadAllText(GetRepoFilePath("src/MediaEngine.Api/Services/Playback/PlaybackCapabilitiesService.cs"));

        Assert.Contains("probe?.Chapters.Count > 0", source, StringComparison.Ordinal);
        Assert.Contains("StartSeconds = chapter.StartSeconds", source, StringComparison.Ordinal);
        Assert.Contains("CompleteChapterRanges", source, StringComparison.Ordinal);
        Assert.Contains("AudiobookChapterNormalizer.Normalize", source, StringComparison.Ordinal);
        Assert.Contains("_settings.GetOrCreateDefaultsAsync", source, StringComparison.Ordinal);
        Assert.Contains("_chapterTitleOverrides.GetByAssetAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable.Range(0, probe.ChapterCount)", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
