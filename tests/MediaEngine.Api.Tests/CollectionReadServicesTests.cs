using Dapper;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class CollectionReadServicesTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DatabaseConnection _database;
    private readonly CollectionBrowseReadService _browse;
    private readonly CollectionMediaLookupReadService _lookup;

    public CollectionReadServicesTests()
    {
        DapperConfiguration.Configure();
        _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_collection_reads_{Guid.NewGuid():N}.db");
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _browse = new CollectionBrowseReadService(
            new CollectionRepository(_database),
            new PersonRepository(_database),
            _database,
            NullLogger<CollectionBrowseReadService>.Instance);
        _lookup = new CollectionMediaLookupReadService(_database);
    }

    [Fact]
    public async Task BrowseReads_ResolveHierarchyAssetsPaletteAndMusicDetail()
    {
        var seeded = await SeedMusicHierarchyAsync();

        var root = await _browse.GetRootWorkIdAsync(seeded.TrackWorkId, CancellationToken.None);
        var assets = await _browse.GetPrimaryAssetIdsAsync([seeded.TrackWorkId], CancellationToken.None);
        var palette = await _browse.GetAssetPaletteAsync(seeded.AlbumWorkId, CancellationToken.None);
        var artistRows = await _browse.GetArtistWorksAsync("The Artist", CancellationToken.None);
        var detailRows = await _browse.GetSystemViewDetailWorksAsync(
            "album",
            "The Album",
            "Music",
            "The Artist",
            CancellationToken.None);

        Assert.Equal(seeded.AlbumWorkId, root);
        Assert.Equal(seeded.AssetId, assets[seeded.TrackWorkId]);
        Assert.Equal("#112233", palette?.PrimaryHex);
        var artistRow = Assert.Single(artistRows);
        Assert.Equal(seeded.TrackWorkId, artistRow.WorkId);
        Assert.Equal("The Album", artistRow.Album);
        var detailRow = Assert.Single(detailRows);
        Assert.Equal(seeded.AlbumWorkId, detailRow.RootWorkId);
        Assert.Equal("Track One", detailRow.Title);
    }

    [Fact]
    public async Task MetadataLookup_IsSetBasedAndPreservesRequestedOrder()
    {
        var first = await SeedMusicHierarchyAsync("First Track", "hash-first");
        var second = await SeedMusicHierarchyAsync("Second Track", "hash-second");

        var results = await _lookup.ResolveMetadataAsync(
            [second.TrackWorkId, first.TrackWorkId],
            CancellationToken.None);

        Assert.Collection(
            results,
            item =>
            {
                Assert.Equal(second.TrackWorkId, item.EntityId);
                Assert.Equal("Second Track", item.Title);
                Assert.Equal("The Artist", item.Creator);
                Assert.Equal($"/stream/{second.AssetId:D}/cover", item.CoverUrl);
            },
            item =>
            {
                Assert.Equal(first.TrackWorkId, item.EntityId);
                Assert.Equal("First Track", item.Title);
            });
    }

    [Fact]
    public async Task CollectionItems_ResolveGuidBlobMembershipAndManagedArtwork()
    {
        var seeded = await SeedMusicHierarchyAsync();
        var collectionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var results = await _lookup.ResolveItemsAsync(
            collectionId,
            [new CollectionItem
            {
                Id = itemId,
                CollectionId = collectionId,
                WorkId = seeded.TrackWorkId,
                SortOrder = 3,
            }],
            CancellationToken.None);

        var item = Assert.Single(results);
        Assert.Equal(itemId, item.Id);
        Assert.Equal(seeded.AlbumWorkId, item.WorkId);
        Assert.Equal("The Album", item.Title);
        Assert.Equal("The Artist", item.Creator);
        Assert.Equal("Music", item.MediaType);
        Assert.Equal($"/stream/{seeded.AssetId:D}/cover", item.CoverUrl);
        Assert.Equal(3, item.SortOrder);
    }

    [Fact]
    public async Task SystemViewGroups_ReturnOrderedArtworkPreviewsAndNullableSqliteDimensions()
    {
        var first = await SeedBookSeriesMemberAsync("First Book", "1", 1649, "book-series-first");
        var second = await SeedBookSeriesMemberAsync("Second Book", "2", 1800, "book-series-second");

        var groups = await _browse.GetSystemViewGroupsAsync("Books", "series", CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal("The Test Series", group.DisplayName);
        Assert.Equal(2, group.WorkCount);
        Assert.Contains(group.CoverWidthPx, new int?[] { 1649, 1800 });
        Assert.Collection(
            group.PreviewItems,
            item =>
            {
                Assert.Equal(first.WorkId, item.WorkId);
                Assert.Equal($"/stream/{first.AssetId:D}/cover", item.ImageUrl);
                Assert.Equal("1", item.Position);
            },
            item =>
            {
                Assert.Equal(second.WorkId, item.WorkId);
                Assert.Equal("2", item.Position);
            });
    }

    [Theory]
    [InlineData("Books", "author", "Author", "Ursula K. Le Guin")]
    [InlineData("Movies", "director", "Director", "Denis Villeneuve")]
    public async Task SystemViewGroups_ResolveNormalizedPersonCredits(
        string mediaType,
        string groupField,
        string role,
        string personName)
    {
        var seeded = await SeedCreditedWorkAsync(mediaType, role, personName);

        var groups = await _browse.GetSystemViewGroupsAsync(mediaType, groupField, CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal(personName, group.DisplayName);
        Assert.Equal(personName, group.Creator);
        Assert.Equal(seeded.WorkId, Assert.Single(group.PreviewItems).WorkId);
    }

    [Fact]
    public async Task BrowseReads_ObserveCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _browse.GetFieldValuesAsync("artist", 20, cancellation.Token));
    }

    private async Task<SeededMusic> SeedMusicHierarchyAsync(
        string title = "Track One",
        string? contentHash = null)
    {
        var albumWorkId = Guid.NewGuid();
        var trackWorkId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = _database.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO works (id, media_type, work_kind) VALUES (@AlbumWorkId, 'Music', 'parent');
            INSERT INTO works (id, media_type, work_kind, parent_work_id) VALUES (@TrackWorkId, 'Music', 'child', @AlbumWorkId);
            INSERT INTO editions (id, work_id, format_label) VALUES (@EditionId, @TrackWorkId, 'Digital');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
            VALUES (@AssetId, @EditionId, @ContentHash, @FilePath);
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES
                (@AlbumWorkId, 'album', 'The Album', @Now),
                (@AlbumWorkId, 'title', 'The Album', @Now),
                (@AlbumWorkId, 'artist', 'The Artist', @Now),
                (@AssetId, 'title', @Title, @Now),
                (@AssetId, 'album', 'The Album', @Now),
                (@AssetId, 'artist', 'The Artist', @Now),
                (@AssetId, 'track_number', '1', @Now),
                (@AssetId, 'year', '2026', @Now);
            INSERT INTO entity_assets (
                id, entity_id, entity_type, asset_type, aspect_class,
                primary_hex, secondary_hex, accent_hex, created_at)
            VALUES (
                @ArtworkId, @AlbumWorkId, 'Work', 'CoverArt', 'Square',
                '#112233', '#445566', '#778899', @Now);
            """,
            new
            {
                AlbumWorkId = albumWorkId,
                TrackWorkId = trackWorkId,
                EditionId = editionId,
                AssetId = assetId,
                ArtworkId = Guid.NewGuid(),
                ContentHash = contentHash ?? $"hash-{assetId:N}",
                FilePath = $"C:/library/{assetId:N}.flac",
                Title = title,
                Now = now,
            });

        return new SeededMusic(albumWorkId, trackWorkId, assetId);
    }

    private async Task<SeededBook> SeedBookSeriesMemberAsync(
        string title,
        string position,
        int coverWidth,
        string contentHash)
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = _database.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO works (id, media_type, work_kind) VALUES (@WorkId, 'Books', 'standalone');
            INSERT INTO editions (id, work_id, format_label) VALUES (@EditionId, @WorkId, 'EPUB');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
            VALUES (@AssetId, @EditionId, @ContentHash, @FilePath);
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES
                (@AssetId, 'title', @Title, @Now),
                (@AssetId, 'series', 'The Test Series', @Now),
                (@AssetId, 'series_index', @Position, @Now),
                (@AssetId, 'cover_width_px', @CoverWidth, @Now),
                (@AssetId, 'cover_height_px', '2400', @Now);
            """,
            new
            {
                WorkId = workId,
                EditionId = editionId,
                AssetId = assetId,
                ContentHash = contentHash,
                FilePath = $"C:/library/{assetId:N}.epub",
                Title = title,
                Position = position,
                CoverWidth = coverWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Now = now,
            });

        return new SeededBook(workId, assetId);
    }

    private async Task<SeededBook> SeedCreditedWorkAsync(string mediaType, string role, string personName)
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = _database.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO works (id, media_type, work_kind) VALUES (@WorkId, @MediaType, 'standalone');
            INSERT INTO editions (id, work_id, format_label) VALUES (@EditionId, @WorkId, 'Digital');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
            VALUES (@AssetId, @EditionId, @ContentHash, @FilePath);
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at) VALUES
                (@AssetId, 'title', @Title, @Now),
                (@AssetId, 'year', '2026', @Now);
            INSERT INTO persons (id, name, created_at) VALUES (@PersonId, @PersonName, @Now);
            INSERT INTO person_media_links (media_asset_id, person_id, role)
            VALUES (@AssetId, @PersonId, @Role);
            """,
            new
            {
                WorkId = workId,
                MediaType = mediaType,
                EditionId = editionId,
                AssetId = assetId,
                ContentHash = $"credit-{assetId:N}",
                FilePath = $"C:/library/{assetId:N}.media",
                Title = $"A {role} Credit",
                PersonId = personId,
                PersonName = personName,
                Role = role,
                Now = now,
            });

        return new SeededBook(workId, assetId);
    }

    public void Dispose()
    {
        try { _database.Dispose(); } catch { }
        try { File.Delete(_databasePath); } catch { }
    }

    private sealed record SeededMusic(Guid AlbumWorkId, Guid TrackWorkId, Guid AssetId);
    private sealed record SeededBook(Guid WorkId, Guid AssetId);
}
