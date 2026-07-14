using Dapper;
using MediaEngine.Api.Services.ReadServices;
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

    public void Dispose()
    {
        try { _database.Dispose(); } catch { }
        try { File.Delete(_databasePath); } catch { }
    }

    private sealed record SeededMusic(Guid AlbumWorkId, Guid TrackWorkId, Guid AssetId);
}
