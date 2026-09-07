using Dapper;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class IngestionPresentationReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public IngestionPresentationReadServiceTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_ingestion_presentation_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task CurrentMedia_GroupsFilesByStructuralIdentityAndUsesOnlyKnownTotals()
    {
        var batchId = AddBatch("running", 45, 28);
        var completeAlbum = AddContainer("Music", "Album One", expectedKey: "track_count", expectedValue: "17");
        AddChildren(batchId, completeAlbum, "Music", "Song", 17, "track_number");

        var partialAlbum = AddContainer("Music", "Album Two", expectedKey: "track_count", expectedValue: "17");
        AddChildren(batchId, partialAlbum, "Music", "Part", 14, "track_number");

        var unknownAlbum = AddContainer("Music", "Unknown Total");
        AddChildren(batchId, unknownAlbum, "Music", "Track", 3, "track_number");

        var show = AddContainer("TV", "The Example Show");
        var season = AddContainer("TV", "Season 2", show, "episode_count", "10");
        AddChildren(batchId, season, "TV", "Episode", 10, "episode_number", seasonNumber: 2);

        var audiobook = AddContainer("Audiobooks", "A Long Listen", expectedKey: "audiobook_part_count", expectedValue: "4");
        AddChildren(batchId, audiobook, "Audiobooks", "Part", 4, partCount: 4);

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetCurrentMediaAsync(0, 50);
        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(5, snapshot.CurrentMediaTotal);
        Assert.Equal(5, snapshot.ReadyGroups);
        var one = Assert.Single(page.Items, item => item.GroupId == completeAlbum);
        Assert.Equal(17, one.ChildCompleted);
        Assert.Equal(17, one.ChildExpected);
        Assert.Equal("tracks", one.ChildUnit);
        Assert.Equal("notApplicable", one.Relationships.State);

        var partial = Assert.Single(page.Items, item => item.GroupId == partialAlbum);
        Assert.Equal(14, partial.ChildCompleted);
        Assert.Equal(17, partial.ChildExpected);

        var unknown = Assert.Single(page.Items, item => item.GroupId == unknownAlbum);
        Assert.Equal(3, unknown.ChildCompleted);
        Assert.Null(unknown.ChildExpected);

        var tv = Assert.Single(page.Items, item => item.GroupId == show);
        Assert.Equal("Season 2", tv.Subtitle);
        Assert.Equal(10, tv.ChildCompleted);
        Assert.Equal(10, tv.ChildExpected);

        var audio = Assert.Single(page.Items, item => item.GroupId == audiobook);
        Assert.Equal("Audiobooks", audio.MediaType);
        Assert.Equal(4, audio.ChildCompleted);
    }

    [Fact]
    public async Task CurrentMedia_DoesNotMergeDistinctWorksWithTheSameTitle()
    {
        var batchId = AddBatch("running", 2, 2);
        AddStandalone(batchId, "Movies", "The Same Title");
        AddStandalone(batchId, "Movies", "The Same Title");

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetCurrentMediaAsync(0, 50);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Select(item => item.GroupId).Distinct().Count());
        Assert.All(page.Items, item => Assert.Equal("The Same Title", item.Title));
    }

    [Fact]
    public async Task CurrentMedia_CountsDistinctTracksInsteadOfDuplicateAssetsOrTrackNumbers()
    {
        var batchId = AddBatch("running", 6, 6);
        var albumId = AddContainer("Music", "A Night at the Opera", expectedKey: "track_count", expectedValue: "12");
        AddChildren(batchId, albumId, "Music", "Song", 5, "track_number");

        using (var conn = _db.CreateConnection())
        {
            var child = conn.QuerySingle<ChildIdentity>("""
                SELECT w.id AS WorkId, e.id AS EditionId
                FROM works w
                JOIN editions e ON e.work_id = w.id
                WHERE w.parent_work_id = @albumId
                ORDER BY w.id
                LIMIT 1;
                """, new { albumId });
            var duplicateAssetId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow.ToString("O");
            conn.Execute("""
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, presented_at)
                VALUES (@duplicateAssetId, @editionId, @hash, @path, @now);
                """, new
            {
                duplicateAssetId,
                editionId = child.EditionId,
                hash = $"hash-{duplicateAssetId:N}",
                path = $"C:/watch/{duplicateAssetId:N}.media",
                now,
            });
            InsertLog(conn, Guid.NewGuid(), batchId, duplicateAssetId, "Music", "Duplicate encoding", now);
        }

        var service = new IngestionPresentationReadService(_db);
        var item = Assert.Single((await service.GetCurrentMediaAsync(0, 50)).Items);

        Assert.Equal(5, item.ChildCompleted);
        Assert.Equal(12, item.ChildExpected);
        Assert.Equal("tracks", item.ChildUnit);
    }

    [Fact]
    public async Task CurrentMedia_PagesGroupsBeforeProjectingDetails()
    {
        var batchId = AddBatch("running", 75, 75);
        for (var index = 1; index <= 75; index++)
            AddStandalone(batchId, "Movies", $"Current Movie {index:00}");

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetCurrentMediaAsync(50, 50);

        Assert.Equal(75, page.TotalCount);
        Assert.Equal(25, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task RecentAdditions_IncludesEarlierRunsAndPagesBeyondFiftyGroups()
    {
        var firstBatch = AddBatch("completed", 30, 30, DateTimeOffset.UtcNow.AddDays(-2));
        var secondBatch = AddBatch("completed", 55, 55, DateTimeOffset.UtcNow.AddDays(-1));
        for (var index = 1; index <= 30; index++)
            AddStandalone(firstBatch, "Books", $"Earlier Book {index:00}", presented: true);
        for (var index = 1; index <= 55; index++)
            AddStandalone(secondBatch, "Movies", $"Recent Movie {index:00}", presented: true);

        var service = new IngestionPresentationReadService(_db);
        var firstPage = await service.GetRecentAdditionsAsync(null, null, null, null, 0, 50);
        var secondPage = await service.GetRecentAdditionsAsync(null, null, null, null, 50, 50);

        Assert.Equal(85, firstPage.TotalCount);
        Assert.Equal(50, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal(35, secondPage.Items.Count);
        Assert.False(secondPage.HasMore);
        Assert.Contains(secondPage.Items, item => item.BatchId == firstBatch);
    }

    [Fact]
    public async Task BatchMedia_PagesPastTheFirstTwentyFiveItems()
    {
        var batchId = AddBatch("completed", 30, 30);
        for (var index = 1; index <= 30; index++)
            AddStandalone(batchId, "Movies", $"Movie {index:00}", presented: true);

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetBatchMediaAsync(batchId, 25, 25);
        var detail = await service.GetBatchPresentationAsync(batchId);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(5, page.Items.Count);
        Assert.NotNull(detail);
        Assert.Equal(30, detail.AddedTotal);
        Assert.Null(detail.PeopleUpdated);
        Assert.Null(detail.ArtworkAdded);
        Assert.Null(detail.TextTracksAdded);
    }

    [Fact]
    public async Task HistoricalBatch_DoesNotUseCatalogueTotalsAsRunDenominators()
    {
        var batchId = AddBatch("completed", 3, 3);
        var album = AddContainer("Music", "Partial Album", expectedKey: "track_count", expectedValue: "17");
        AddChildren(batchId, album, "Music", "Track", 3, "track_number");
        using (var conn = _db.CreateConnection())
            conn.Execute("UPDATE media_assets SET presented_at = @now;", new { now = DateTimeOffset.UtcNow.ToString("O") });

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetBatchMediaAsync(batchId, 0, 50);

        var item = Assert.Single(page.Items);
        Assert.Equal(3, item.ChildCompleted);
        Assert.Null(item.ChildExpected);
        Assert.Equal("tracks", item.ChildUnit);
    }

    [Fact]
    public async Task BatchMedia_AppliesSearchAndLaneBeforePaging()
    {
        var batchId = AddBatch("completed", 61, 61);
        for (var index = 1; index <= 60; index++)
            AddStandalone(batchId, "Movies", $"Movie {index:00}", presented: true);
        AddStandalone(batchId, "Books", "The Needle Book", presented: true);

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetBatchMediaAsync(batchId, 0, 50, "needle", "Read", "newest");

        var item = Assert.Single(page.Items);
        Assert.Equal("The Needle Book", item.Title);
        Assert.Equal("Books", item.MediaType);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task BatchMedia_HidesArtworkBearingGroupsUntilTheyHaveARealTitle()
    {
        var batchId = AddBatch("completed", 1, 1);
        var workId = AddStandalone(batchId, "Books", "Temporary detection", presented: true);
        using (var conn = _db.CreateConnection())
        {
            conn.Execute("DELETE FROM canonical_values WHERE entity_id = @workId AND key = 'title';", new { workId });
            conn.Execute("UPDATE ingestion_log SET detected_title = NULL, normalized_title = NULL WHERE ingestion_run_id = @batchId;", new { batchId });
            conn.Execute("""
                INSERT INTO entity_assets (
                    id, entity_id, entity_type, asset_type, local_image_path,
                    aspect_class, asset_class, storage_location, owner_scope,
                    is_preferred, created_at)
                VALUES (
                    @id, @workId, 'Work', 'CoverArt', 'C:/test/premature-cover.jpg',
                    'Portrait', 'Artwork', 'Central', 'Work', 1, @now);
                """, new { id = Guid.NewGuid(), workId, now = DateTimeOffset.UtcNow.ToString("O") });
        }

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetBatchMediaAsync(batchId, 0, 50);
        var presentation = await service.GetBatchPresentationAsync(batchId);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.NotNull(presentation);
        Assert.Empty(presentation.AddedPreview);
        Assert.Equal(0, presentation.AddedTotal);
    }

    [Fact]
    public async Task RecentAdditions_DoesNotTreatLaterReprocessingAsANewAddition()
    {
        var additionBatch = AddBatch("completed", 1, 1);
        var workId = AddStandalone(additionBatch, "Movies", "Added Once", presented: true);
        Guid assetId;
        using (var conn = _db.CreateConnection())
        {
            assetId = conn.ExecuteScalar<Guid>("""
                SELECT ma.id
                FROM media_assets ma
                JOIN editions e ON e.id = ma.edition_id
                WHERE e.work_id = @workId;
                """, new { workId });
        }

        var refreshTime = DateTimeOffset.UtcNow.AddMinutes(5);
        var refreshBatch = AddBatch("completed", 1, 1, refreshTime);
        using (var conn = _db.CreateConnection())
        {
            InsertLog(
                conn,
                Guid.NewGuid(),
                refreshBatch,
                assetId,
                "Movies",
                "Added Once",
                refreshTime.ToString("O"));
        }

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetRecentAdditionsAsync(null, null, null, null, 0, 50);

        var item = Assert.Single(page.Items);
        Assert.Equal(additionBatch, item.BatchId);
    }

    [Fact]
    public async Task RecentAdditions_AppliesSearchAndLaneBeforeBuildingThePage()
    {
        var batchId = AddBatch("completed", 61, 61);
        for (var index = 1; index <= 60; index++)
            AddStandalone(batchId, "Movies", $"Movie {index:00}", presented: true);
        AddStandalone(batchId, "Books", "The Needle Book", presented: true);

        var service = new IngestionPresentationReadService(_db);
        var page = await service.GetRecentAdditionsAsync("needle", "Read", null, null, 0, 50);

        var item = Assert.Single(page.Items);
        Assert.Equal("The Needle Book", item.Title);
        Assert.Equal("Books", item.MediaType);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task OperationsArtwork_UsesSmallPreviewAndMediumGridRenditions()
    {
        var batchId = AddBatch("completed", 1, 1);
        var workId = AddStandalone(batchId, "Books", "Sized Artwork", presented: true);
        var artworkId = Guid.NewGuid();
        using (var conn = _db.CreateConnection())
        {
            conn.Execute("""
                INSERT INTO entity_assets (
                    id, entity_id, entity_type, asset_type, local_image_path,
                    aspect_class, asset_class, storage_location, owner_scope,
                    is_preferred, created_at)
                VALUES (
                    @artworkId, @workId, 'Work', 'CoverArt', 'C:/test/cover.jpg',
                    'Portrait', 'Artwork', 'Central', 'Work', 1, @now);
                """, new { artworkId, workId, now = DateTimeOffset.UtcNow.ToString("O") });
        }

        var service = new IngestionPresentationReadService(_db);
        var presentation = await service.GetBatchPresentationAsync(batchId);
        var page = await service.GetBatchMediaAsync(batchId, 0, 50);

        Assert.NotNull(presentation);
        Assert.EndsWith($"/stream/artwork/{artworkId:D}?size=s", Assert.Single(presentation.AddedPreview).CoverUrl);
        Assert.EndsWith($"/stream/artwork/{artworkId:D}?size=m", Assert.Single(page.Items).CoverUrl);
    }

    [Fact]
    public async Task ActivitySummary_CountsGroupedAdditionsWithoutBuildingMediaCards()
    {
        var batchId = AddBatch("completed", 17, 17);
        var album = AddContainer("Music", "One Album", expectedKey: "track_count", expectedValue: "17");
        AddChildren(batchId, album, "Music", "Track", 17, "track_number");
        using (var conn = _db.CreateConnection())
            conn.Execute("UPDATE media_assets SET presented_at = @now;", new { now = DateTimeOffset.UtcNow.ToString("O") });

        var service = new IngestionPresentationReadService(_db);
        var summary = await service.GetActivitySummaryAsync();

        Assert.Equal(1, summary.ItemsAddedToday);
        Assert.Equal(1, summary.LastGroupsAdded);
    }

    [Fact]
    public async Task Snapshot_BoundsRecentDayCardsWhileKeepingDayTotals()
    {
        var firstDay = DateTimeOffset.UtcNow.AddDays(-2);
        var secondDay = DateTimeOffset.UtcNow.AddDays(-1);
        var firstBatch = AddBatch("completed", 4, 4, firstDay);
        var secondBatch = AddBatch("completed", 5, 5, secondDay);
        for (var index = 1; index <= 4; index++)
            AddStandalone(firstBatch, "Books", $"Earlier Book {index}", presented: true, occurredAt: firstDay);
        for (var index = 1; index <= 5; index++)
            AddStandalone(secondBatch, "Movies", $"Recent Movie {index}", presented: true, occurredAt: secondDay);

        var service = new IngestionPresentationReadService(_db);
        var snapshot = await service.GetSnapshotAsync(currentLimit: 8, recentDayLimit: 2, recentItemsPerDay: 2);

        Assert.Equal(2, snapshot.RecentDays.Count);
        Assert.All(snapshot.RecentDays, day => Assert.Equal(2, day.Items.Count));
        Assert.Equal([5, 4], snapshot.RecentDays.Select(day => day.TotalCount).ToArray());
    }

    private Guid AddBatch(string status, int total, int processed, DateTimeOffset? occurredAt = null)
    {
        var id = Guid.NewGuid();
        var time = occurredAt ?? DateTimeOffset.UtcNow;
        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO ingestion_batches (
                id, status, source_path, category, files_total, files_processed,
                files_registered, files_review, files_no_match, files_failed,
                started_at, completed_at, created_at, updated_at)
            VALUES (
                @id, @status, 'C:/watch', 'Mixed', @total, @processed,
                @processed, 0, 0, 0, @time, @completedAt, @time, @time);
            """, new { id, status, total, processed, time = time.ToString("O"), completedAt = status == "completed" ? time.ToString("O") : null });
        return id;
    }

    private Guid AddContainer(
        string mediaType,
        string title,
        Guid? parentId = null,
        string? expectedKey = null,
        string? expectedValue = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var conn = _db.CreateConnection();
        conn.Execute("INSERT INTO works (id, media_type, work_kind, parent_work_id) VALUES (@id, @mediaType, 'parent', @parentId);", new { id, mediaType, parentId });
        conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@id, 'title', @title, @now);", new { id, title, now });
        if (expectedKey is not null && expectedValue is not null)
            conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@id, @expectedKey, @expectedValue, @now);", new { id, expectedKey, expectedValue, now });
        return id;
    }

    private void AddChildren(
        Guid batchId,
        Guid parentId,
        string mediaType,
        string titlePrefix,
        int count,
        string? ordinalKey = null,
        int? seasonNumber = null,
        int? partCount = null)
    {
        for (var index = 1; index <= count; index++)
        {
            var workId = Guid.NewGuid();
            var editionId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var logId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var conn = _db.CreateConnection();
            conn.Execute("INSERT INTO works (id, media_type, work_kind, parent_work_id, ordinal) VALUES (@workId, @mediaType, 'child', @parentId, @index);", new { workId, mediaType, parentId, index });
            conn.Execute("INSERT INTO editions (id, work_id, format_label) VALUES (@editionId, @workId, 'Test');", new { editionId, workId });
            conn.Execute("INSERT INTO media_assets (id, edition_id, content_hash, file_path_root) VALUES (@assetId, @editionId, @hash, @path);", new { assetId, editionId, hash = $"hash-{assetId:N}", path = $"C:/watch/{assetId:N}.media" });
            conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@workId, 'title', @title, @now);", new { workId, title = $"{titlePrefix} {index:00}", now });
            if (ordinalKey is not null)
                conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@workId, @ordinalKey, @ordinal, @now);", new { workId, ordinalKey, ordinal = index.ToString(), now });
            if (seasonNumber.HasValue)
                conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@workId, 'season_number', @season, @now);", new { workId, season = seasonNumber.Value.ToString(), now });
            if (partCount.HasValue)
                conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@workId, 'audiobook_part_count', @parts, @now);", new { workId, parts = partCount.Value.ToString(), now });
            InsertLog(conn, logId, batchId, assetId, mediaType, $"{titlePrefix} {index:00}", now);
        }
    }

    private Guid AddStandalone(
        Guid batchId,
        string mediaType,
        string title,
        bool presented = false,
        DateTimeOffset? occurredAt = null)
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var logId = Guid.NewGuid();
        var now = (occurredAt ?? DateTimeOffset.UtcNow).ToString("O");
        using var conn = _db.CreateConnection();
        conn.Execute("INSERT INTO works (id, media_type, work_kind) VALUES (@workId, @mediaType, 'standalone');", new { workId, mediaType });
        conn.Execute("INSERT INTO editions (id, work_id, format_label) VALUES (@editionId, @workId, 'Test');", new { editionId, workId });
        conn.Execute("INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, presented_at) VALUES (@assetId, @editionId, @hash, @path, @presentedAt);", new { assetId, editionId, hash = $"hash-{assetId:N}", path = $"C:/watch/{assetId:N}.media", presentedAt = presented ? now : null });
        conn.Execute("INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES (@workId, 'title', @title, @now);", new { workId, title, now });
        InsertLog(conn, logId, batchId, assetId, mediaType, title, now);
        return workId;
    }

    private static void InsertLog(System.Data.IDbConnection conn, Guid logId, Guid batchId, Guid assetId, string mediaType, string title, string now)
    {
        conn.Execute("""
            INSERT INTO ingestion_log (
                id, file_path, media_asset_id, content_hash, status, media_type,
                detected_title, normalized_title, ingestion_run_id, created_at, updated_at)
            VALUES (
                @logId, @path, @assetId, @hash, 'registered', @mediaType,
                @title, @title, @batchId, @now, @now);
            """, new { logId, path = $"C:/watch/{assetId:N}.media", assetId, hash = $"hash-{assetId:N}", mediaType, title, batchId, now });
    }

    private sealed class ChildIdentity
    {
        public Guid WorkId { get; init; }
        public Guid EditionId { get; init; }
    }
}
