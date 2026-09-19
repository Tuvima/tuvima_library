using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class MediaEditorNavigationReadServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DatabaseConnection _database;

    public MediaEditorNavigationReadServiceTests()
    {
        DapperConfiguration.Configure();
        _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_media_editor_navigation_{Guid.NewGuid():N}.db");
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
    }

    [Fact]
    public async Task GetNavigatorAsync_TvHierarchy_MakesOwnedSeasonAndEpisodeSelectable()
    {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        using (var connection = _database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO works (id, media_type, work_kind, ownership)
                VALUES ($seriesId, 'TV', 'parent', 'Owned');

                INSERT INTO works (id, media_type, work_kind, parent_work_id, ordinal, ownership)
                VALUES ($seasonId, 'TV', 'parent', $seriesId, 1, 'Owned');

                INSERT INTO works (id, media_type, work_kind, parent_work_id, ordinal, ownership)
                VALUES ($episodeId, 'TV', 'child', $seasonId, 1, 'Owned');

                INSERT INTO editions (id, work_id, format_label)
                VALUES ($editionId, $episodeId, 'MP4');

                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                VALUES ($assetId, $editionId, 'hash', 'show-s01e01.mp4');

                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES
                    ($seriesId, 'title', 'Example Show', datetime('now')),
                    ($seasonId, 'season_number', '1', datetime('now')),
                    ($assetId, 'episode_number', '1', datetime('now')),
                    ($assetId, 'episode_title', 'Pilot', datetime('now'));
                """;
            command.Parameters.AddWithValue("$seriesId", GuidSql.ToBlob(seriesId));
            command.Parameters.AddWithValue("$seasonId", GuidSql.ToBlob(seasonId));
            command.Parameters.AddWithValue("$episodeId", GuidSql.ToBlob(episodeId));
            command.Parameters.AddWithValue("$editionId", GuidSql.ToBlob(editionId));
            command.Parameters.AddWithValue("$assetId", GuidSql.ToBlob(assetId));
            command.ExecuteNonQuery();
        }

        var service = new MediaEditorNavigationReadService(_database, null!, null!);

        var navigator = await service.GetNavigatorAsync(seriesId, CancellationToken.None);

        Assert.NotNull(navigator);
        Assert.True(navigator.Enabled);

        var season = Assert.Single(navigator.Nodes, node => node.NodeKind == "season");
        Assert.True(season.IsOwned);
        Assert.True(season.CanSelectAsEditorTarget);
        Assert.Equal("1 episode", season.Subtitle);

        var episode = Assert.Single(navigator.Nodes, node => node.NodeKind == "episode");
        Assert.True(episode.IsOwned);
        Assert.True(episode.IsClickable);
        Assert.True(episode.CanSelectAsEditorTarget);
        Assert.Equal(assetId, episode.PrimaryAssetId);
        Assert.Equal("Pilot", episode.Title);
    }

    [Fact]
    public async Task GetNavigatorAsync_MovieSeries_UsesFilmSeriesAndMovieNodes()
    {
        var seriesId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        using (var connection = _database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO works (id, media_type, work_kind, ownership)
                VALUES ($seriesId, 'Movies', 'parent', 'Owned');

                INSERT INTO works (id, media_type, work_kind, parent_work_id, ordinal, ownership)
                VALUES ($movieId, 'Movies', 'child', $seriesId, 1, 'Owned');

                INSERT INTO editions (id, work_id, format_label)
                VALUES ($editionId, $movieId, 'MKV');

                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                VALUES ($assetId, $editionId, 'movie-hash', 'movie.mkv');

                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES
                    ($seriesId, 'title', 'The Trilogy', datetime('now')),
                    ($assetId, 'title', 'The First Film', datetime('now'));
                """;
            command.Parameters.AddWithValue("$seriesId", GuidSql.ToBlob(seriesId));
            command.Parameters.AddWithValue("$movieId", GuidSql.ToBlob(movieId));
            command.Parameters.AddWithValue("$editionId", GuidSql.ToBlob(editionId));
            command.Parameters.AddWithValue("$assetId", GuidSql.ToBlob(assetId));
            command.ExecuteNonQuery();
        }

        var service = new MediaEditorNavigationReadService(_database, null!, null!);
        var navigator = await service.GetNavigatorAsync(seriesId, CancellationToken.None);

        Assert.NotNull(navigator);
        Assert.True(navigator.Enabled);
        var filmSeries = Assert.Single(navigator.Nodes, node => node.NodeKind == "film_series");
        var movie = Assert.Single(navigator.Nodes, node => node.NodeKind == "movie");
        Assert.Equal("Film Series", filmSeries.Label);
        Assert.Equal("The Trilogy", filmSeries.Title);
        Assert.Equal("Movie", movie.Label);
        Assert.Equal("The First Film", movie.Title);
        Assert.Equal("work", movie.ScopeId);
        Assert.True(movie.CanSelectAsEditorTarget);
    }

    [Fact]
    public async Task GetNavigatorAsync_StandaloneMovie_UsesMovieNode()
    {
        var movieId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        using (var connection = _database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO works (id, media_type, work_kind, ownership)
                VALUES ($movieId, 'Movies', 'standalone', 'Owned');

                INSERT INTO editions (id, work_id, format_label)
                VALUES ($editionId, $movieId, 'MKV');

                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                VALUES ($assetId, $editionId, 'standalone-movie-hash', 'standalone.mkv');

                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES ($assetId, 'title', 'Standalone Film', datetime('now'));
                """;
            command.Parameters.AddWithValue("$movieId", GuidSql.ToBlob(movieId));
            command.Parameters.AddWithValue("$editionId", GuidSql.ToBlob(editionId));
            command.Parameters.AddWithValue("$assetId", GuidSql.ToBlob(assetId));
            command.ExecuteNonQuery();
        }

        var service = new MediaEditorNavigationReadService(_database, null!, null!);
        var navigator = await service.GetNavigatorAsync(movieId, CancellationToken.None);

        Assert.NotNull(navigator);
        Assert.True(navigator.Enabled);
        var movie = Assert.Single(navigator.Nodes);
        Assert.Equal("movie", movie.NodeKind);
        Assert.Equal("Movie", movie.Label);
        Assert.Equal("Standalone Film", movie.Title);
        Assert.True(movie.CanSelectAsEditorTarget);
    }

    public void Dispose()
    {
        try { _database.Dispose(); } catch { }
        try { File.Delete(_databasePath); } catch { }
    }
}
