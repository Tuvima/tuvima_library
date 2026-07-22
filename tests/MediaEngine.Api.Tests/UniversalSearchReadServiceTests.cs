using MediaEngine.Api.Models;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class UniversalSearchReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public UniversalSearchReadServiceTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_universal_search_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task SearchAsync_ReturnsRankedPeopleAndPlaylistsFromOneQuery()
    {
        var personId = Guid.NewGuid();
        var playlistId = Guid.NewGuid();
        using (var conn = _db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO persons (id, name, biography, occupation, created_at)
                VALUES ($personId, 'Aurora Drift', 'Electronic musician and composer.', 'Musician', $createdAt);

                INSERT INTO person_roles (person_id, role)
                VALUES ($personId, 'Artist');

                INSERT INTO collections (id, display_name, collection_type, description, created_at)
                VALUES ($playlistId, 'Aurora Drift Favorites', 'Playlist', 'A saved listening queue.', $createdAt);
                """;
            cmd.Parameters.AddWithValue("$personId", GuidSql.ToBlob(personId));
            cmd.Parameters.AddWithValue("$playlistId", GuidSql.ToBlob(playlistId));
            cmd.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        var service = new UniversalSearchReadService(_db, new StubWorkSearch([]));

        var response = await service.SearchAsync("Aurora Drift", 20, CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.NotNull(response.TopResult);
        Assert.Equal("person", response.TopResult.EntityType);
        Assert.Equal($"/details/person/{personId:D}", response.TopResult.DetailRoute);
        Assert.Contains(response.Sections, section => section.Key == "people");
        var groupSection = Assert.Single(response.Sections, section => section.Key == "series-collections");
        var playlist = Assert.Single(groupSection.Results);
        Assert.Equal("playlist", playlist.EntityType);
        Assert.Equal($"/listen/music/playlists/{playlistId:D}", playlist.DetailRoute);
    }

    [Fact]
    public async Task SearchAsync_NormalizesOwnedMediaWithoutHydratingDetailModels()
    {
        var workId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var works = new[]
        {
            new SearchResultDto
            {
                WorkId = workId,
                CollectionId = collectionId,
                Title = "Midnight Run",
                Author = "Martin Brest",
                MediaType = "Movies",
                CollectionDisplayName = "Midnight Run",
                Year = "1988",
                Rating = "7.5",
                Description = "A reluctant cross-country journey.",
                CoverUrl = $"/stream/artwork/{Guid.NewGuid():D}",
            },
        };
        var service = new UniversalSearchReadService(_db, new StubWorkSearch(works));

        var response = await service.SearchAsync("Midnight", 12, CancellationToken.None);

        var result = Assert.Single(response.Sections.Single(section => section.Key == "watch").Results);
        Assert.Equal("movie", result.EntityType);
        Assert.Equal("Movie", result.MediaType);
        Assert.Equal("Watch", result.PrimaryActionLabel);
        Assert.Equal($"/watch/movie/{workId:D}?collectionId={collectionId:D}", result.DetailRoute);
        Assert.Equal("1988", result.Year);
        Assert.Contains("7.5", result.Facts);
    }

    [Fact]
    public async Task SearchAsync_DoesNotRepeatTheTitleAsCreatorOrSubtitle()
    {
        var service = new UniversalSearchReadService(_db, new StubWorkSearch(
        [
            new SearchResultDto
            {
                WorkId = Guid.NewGuid(),
                Title = "Dune",
                Author = "Dune",
                MediaType = "Audiobooks",
                CollectionDisplayName = "Dune",
            },
        ]));

        var response = await service.SearchAsync("Dune", 12, CancellationToken.None);

        Assert.NotNull(response.TopResult);
        Assert.Null(response.TopResult.Creator);
        Assert.Null(response.TopResult.Subtitle);
        Assert.DoesNotContain("Dune", response.TopResult.Facts);
    }

    [Fact]
    public async Task OwnedWorkSearch_MaterializesCanonicalFactsAndNormalizedCreatorFromGuidBlobStorage()
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        using (var conn = _db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO works (id, media_type, work_kind)
                VALUES ($workId, 'Books', 'standalone');

                INSERT INTO editions (id, work_id, format_label)
                VALUES ($editionId, $workId, 'EPUB');

                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                VALUES ($assetId, $editionId, 'dune-search-hash', 'C:/library/books/Dune.epub');

                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES
                    ($assetId, 'title', 'Dune', $now),
                    ($assetId, 'original_publication_year', '1965', $now),
                    ($assetId, 'description', 'A desert world and a dangerous inheritance.', $now),
                    ($assetId, 'rating', '4.8', $now);

                INSERT INTO persons (id, name, created_at)
                VALUES ($personId, 'Frank Herbert', $now);

                INSERT INTO person_media_links (media_asset_id, person_id, role)
                VALUES ($assetId, $personId, 'Author');
                """;
            cmd.Parameters.AddWithValue("$workId", GuidSql.ToBlob(workId));
            cmd.Parameters.AddWithValue("$editionId", GuidSql.ToBlob(editionId));
            cmd.Parameters.AddWithValue("$assetId", GuidSql.ToBlob(assetId));
            cmd.Parameters.AddWithValue("$personId", GuidSql.ToBlob(personId));
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        var results = await new CollectionSearchReadService(_db).SearchAsync("Frank Herbert", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(workId, result.WorkId);
        Assert.Equal("Dune", result.Title);
        Assert.Equal("Frank Herbert", result.Author);
        Assert.Equal("1965", result.Year);
        Assert.Equal("4.8", result.Rating);
        Assert.StartsWith("A desert world", result.Description, StringComparison.Ordinal);
    }

    private sealed class StubWorkSearch(IReadOnlyList<SearchResultDto> results) : ICollectionSearchReadService
    {
        public Task<List<SearchResultDto>> SearchAsync(string? query, CancellationToken ct) =>
            Task.FromResult(results.ToList());
    }
}
