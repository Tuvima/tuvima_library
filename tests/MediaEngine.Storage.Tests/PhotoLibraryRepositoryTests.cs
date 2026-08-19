using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class PhotoLibraryRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tuvima-photos-{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _database;
    private readonly PhotoLibraryRepository _repository;

    public PhotoLibraryRepositoryTests()
    {
        _database = new DatabaseConnection(_path);
        _database.InitializeSchema();
        _repository = new PhotoLibraryRepository(_database);
    }

    [Fact]
    public async Task Upsert_DeduplicatesContentWhileRetainingEverySource()
    {
        var libraryId = Guid.NewGuid();
        var captured = new DateTimeOffset(2025, 7, 4, 14, 30, 0, TimeSpan.Zero);
        var first = await _repository.UpsertAsync(
            libraryId, @"C:\photos\one.jpg", "abc123", "one.jpg", captured,
            4000, 3000, "image/jpeg", 1200, captured,
            latitude: 41.8781, longitude: -87.6298,
            cameraMake: "Fujifilm", cameraModel: "X-T5");
        var duplicate = await _repository.UpsertAsync(
            libraryId, @"D:\backup\one-copy.jpg", "abc123", "one-copy.jpg", captured,
            4000, 3000, "image/jpeg", 1200, captured);

        Assert.True(first.PhotoAdded);
        Assert.True(first.SourceAdded);
        Assert.False(duplicate.PhotoAdded);
        Assert.True(duplicate.SourceAdded);

        var page = _repository.Query(0, 20, null, favorites: false, includeHidden: false, albumId: null);
        var photo = Assert.Single(page.Items);
        Assert.Equal(2, photo.DuplicateCount);
        Assert.Equal(1, page.Total);
        Assert.Equal(41.8781, photo.Latitude);
        Assert.Equal(-87.6298, photo.Longitude);
        Assert.Equal("Fujifilm", photo.CameraMake);
        Assert.Equal("X-T5", photo.CameraModel);
    }

    [Fact]
    public async Task FavoritesHiddenAndAlbums_AreIndependentLocalCuration()
    {
        var libraryId = Guid.NewGuid();
        var captured = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(
            libraryId, @"C:\photos\favorite.jpg", "favorite-hash", "favorite.jpg", captured,
            1200, 1800, "image/jpeg", 500, captured);
        var photo = Assert.Single(_repository.Query(0, 20, null, false, false, null).Items);

        Assert.True(await _repository.SetFlagAsync(photo.Id, "favorite", true));
        Assert.Single(_repository.Query(0, 20, null, favorites: true, includeHidden: false, albumId: null).Items);

        var album = await _repository.CreateAlbumAsync("Family", "Private family photos");
        Assert.Equal(1, await _repository.AddToAlbumAsync(album.Id, [photo.Id]));
        Assert.Single(_repository.Query(0, 20, null, false, false, album.Id).Items);
        Assert.Equal(1, Assert.Single(_repository.GetAlbums()).ItemCount);

        Assert.True(await _repository.SetFlagAsync(photo.Id, "hidden", true));
        Assert.Empty(_repository.Query(0, 20, null, false, includeHidden: false, albumId: null).Items);
        Assert.Single(_repository.Query(0, 20, null, false, includeHidden: true, albumId: null).Items);
        Assert.Single(_repository.Query(
            0, 20, null, false, includeHidden: true, albumId: null, hiddenOnly: true).Items);
    }

    public void Dispose()
    {
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
