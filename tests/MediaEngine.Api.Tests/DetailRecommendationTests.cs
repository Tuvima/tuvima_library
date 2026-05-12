using System.Reflection;
using MediaEngine.Api.Services.Details;
using MediaEngine.Contracts.Details;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class DetailRecommendationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public DetailRecommendationTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_detail_recs_{Guid.NewGuid():N}.db");
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
    public async Task MoreLikeThis_CollapsesTvEpisodesToShowsAndScoresSharedSignals()
    {
        var currentCollectionId = Guid.NewGuid();
        var relatedCollectionId = Guid.NewGuid();
        var currentShowId = Guid.NewGuid();
        var currentSeasonId = Guid.NewGuid();
        var currentEpisodeId = Guid.NewGuid();
        var relatedShowId = Guid.NewGuid();
        var relatedSeasonId = Guid.NewGuid();
        var relatedEpisodeId = Guid.NewGuid();
        var unrelatedMovieId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO collections (id, display_name, collection_type, created_at)
                    VALUES ($currentCollectionId, 'Breaking Bad', 'Series', $now);
                INSERT INTO collections (id, display_name, collection_type, created_at)
                    VALUES ($relatedCollectionId, 'Better Call Saul', 'Series', $now);

                INSERT INTO works (id, collection_id, media_type, work_kind, ownership)
                    VALUES ($currentShowId, $currentCollectionId, 'TV', 'parent', 'Owned');
                INSERT INTO works (id, collection_id, media_type, work_kind, parent_work_id, ownership)
                    VALUES ($currentSeasonId, $currentCollectionId, 'TV', 'parent', $currentShowId, 'Owned');
                INSERT INTO works (id, collection_id, media_type, work_kind, parent_work_id, ownership)
                    VALUES ($currentEpisodeId, $currentCollectionId, 'TV', 'child', $currentSeasonId, 'Owned');

                INSERT INTO works (id, collection_id, media_type, work_kind, ownership)
                    VALUES ($relatedShowId, $relatedCollectionId, 'TV', 'parent', 'Owned');
                INSERT INTO works (id, collection_id, media_type, work_kind, parent_work_id, ownership)
                    VALUES ($relatedSeasonId, $relatedCollectionId, 'TV', 'parent', $relatedShowId, 'Owned');
                INSERT INTO works (id, collection_id, media_type, work_kind, parent_work_id, ownership)
                    VALUES ($relatedEpisodeId, $relatedCollectionId, 'TV', 'child', $relatedSeasonId, 'Owned');

                INSERT INTO works (id, media_type, work_kind, ownership)
                    VALUES ($unrelatedMovieId, 'Movies', 'standalone', 'Owned');

                INSERT INTO editions (id, work_id, format_label) VALUES ($currentEditionId, $currentEpisodeId, 'MKV');
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                    VALUES ($currentAssetId, $currentEditionId, 'current-tv-asset', 'C:/tv/Breaking Bad/S01E01.mkv');

                INSERT INTO editions (id, work_id, format_label) VALUES ($relatedEditionId, $relatedEpisodeId, 'MKV');
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                    VALUES ($relatedAssetId, $relatedEditionId, 'related-tv-asset', 'C:/tv/Better Call Saul/S01E01.mkv');

                INSERT INTO editions (id, work_id, format_label) VALUES ($movieEditionId, $unrelatedMovieId, 'MP4');
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                    VALUES ($movieAssetId, $movieEditionId, 'movie-asset', 'C:/movies/Unrelated.mp4');
                """;
            Add(cmd, "$currentCollectionId", currentCollectionId);
            Add(cmd, "$relatedCollectionId", relatedCollectionId);
            Add(cmd, "$currentShowId", currentShowId);
            Add(cmd, "$currentSeasonId", currentSeasonId);
            Add(cmd, "$currentEpisodeId", currentEpisodeId);
            Add(cmd, "$relatedShowId", relatedShowId);
            Add(cmd, "$relatedSeasonId", relatedSeasonId);
            Add(cmd, "$relatedEpisodeId", relatedEpisodeId);
            Add(cmd, "$unrelatedMovieId", unrelatedMovieId);
            Add(cmd, "$currentEditionId", Guid.NewGuid());
            Add(cmd, "$currentAssetId", Guid.NewGuid());
            Add(cmd, "$relatedEditionId", Guid.NewGuid());
            Add(cmd, "$relatedAssetId", Guid.NewGuid());
            Add(cmd, "$movieEditionId", Guid.NewGuid());
            Add(cmd, "$movieAssetId", Guid.NewGuid());
            Add(cmd, "$now", now);
            cmd.ExecuteNonQuery();

            InsertCanonical(conn, currentShowId, "title", "Breaking Bad", now);
            InsertCanonical(conn, currentShowId, "year", "2008", now);
            InsertArray(conn, currentShowId, "genre", 0, "Crime");
            InsertArray(conn, currentShowId, "cast_member", 0, "Aaron Paul");
            InsertArray(conn, currentShowId, "mood", 0, "Tense");

            InsertCanonical(conn, relatedShowId, "title", "Better Call Saul", now);
            InsertCanonical(conn, relatedShowId, "year", "2015", now);
            InsertArray(conn, relatedShowId, "genre", 0, "Crime");
            InsertArray(conn, relatedShowId, "cast_member", 0, "Aaron Paul");
            InsertArray(conn, relatedShowId, "mood", 0, "Tense");

            InsertCanonical(conn, unrelatedMovieId, "title", "Unrelated Movie", now);
            InsertCanonical(conn, unrelatedMovieId, "year", "1990", now);
            InsertArray(conn, unrelatedMovieId, "genre", 0, "Comedy");
        }

        var groups = await InvokeBuildWorkMediaGroupsAsync(currentEpisodeId, DetailEntityType.TvEpisode);

        var group = Assert.Single(groups);
        Assert.Equal("more-like-this", group.Key);
        Assert.DoesNotContain(group.Items, item => item.EntityType == DetailEntityType.TvEpisode);
        Assert.DoesNotContain(group.Items, item => item.Actions.Any(action =>
            action.Route?.Contains("/episode/", StringComparison.OrdinalIgnoreCase) == true));

        var relatedShow = Assert.Single(group.Items, item => item.Id == relatedCollectionId.ToString("D"));
        Assert.Equal(DetailEntityType.TvShow, relatedShow.EntityType);
        Assert.Equal("Better Call Saul", relatedShow.Title);
        Assert.Contains("Shared Person", relatedShow.Subtitle);
        Assert.Contains($"/watch/tv/show/{relatedCollectionId:D}", relatedShow.Actions.Single().Route);
    }

    private async Task<IReadOnlyList<MediaGroupingViewModel>> InvokeBuildWorkMediaGroupsAsync(Guid workId, DetailEntityType entityType)
    {
        var composer = new DetailComposerService(_db, null!, null!, null!, null!, null!);
        var method = typeof(DetailComposerService).GetMethod("BuildWorkMediaGroupsAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DetailComposerService), "BuildWorkMediaGroupsAsync");

        var task = (Task<IReadOnlyList<MediaGroupingViewModel>>)method.Invoke(
            composer,
            [workId, entityType, CancellationToken.None])!;

        return await task;
    }

    private static void InsertCanonical(Microsoft.Data.Sqlite.SqliteConnection conn, Guid entityId, string key, string value, string now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES ($entityId, $key, $value, $now);
            """;
        Add(cmd, "$entityId", entityId);
        Add(cmd, "$key", key);
        Add(cmd, "$value", value);
        Add(cmd, "$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertArray(Microsoft.Data.Sqlite.SqliteConnection conn, Guid entityId, string key, int ordinal, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO canonical_value_arrays (entity_id, key, ordinal, value)
                VALUES ($entityId, $key, $ordinal, $value);
            """;
        Add(cmd, "$entityId", entityId);
        Add(cmd, "$key", key);
        Add(cmd, "$ordinal", ordinal);
        Add(cmd, "$value", value);
        cmd.ExecuteNonQuery();
    }

    private static void Add(Microsoft.Data.Sqlite.SqliteCommand cmd, string name, object value)
        => cmd.Parameters.AddWithValue(name, value is Guid guid ? guid.ToString("D") : value);
}
