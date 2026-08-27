using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class ViewStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuvima-view-storage-{Guid.NewGuid():N}");
    private readonly string _storageRoot;
    private readonly ConfigurationDirectoryLoader _configuration;
    private readonly DatabaseConnection _database;
    private readonly ViewPersonalSpaceRepository _spaces;
    private readonly ViewStorageService _service;

    public ViewStorageServiceTests()
    {
        _storageRoot = Directory.CreateDirectory(Path.Combine(_root, "storage")).FullName;
        _configuration = new ConfigurationDirectoryLoader(Path.Combine(_root, "config"));
        _configuration.SaveLibraries(new LibrariesConfiguration
        {
            SchemaVersion = "5.0",
            StorageLocations =
            [
                new ServerStorageLocationConfig
                {
                    Id = "view",
                    Label = "View",
                    Path = _storageRoot,
                    AllowWrite = true,
                },
            ],
            ViewStorage = new ViewStorageConfig
            {
                StorageLocationId = "view",
                RelativeRoot = "View",
            },
        });
        _database = new DatabaseConnection(Path.Combine(_root, "view.db"));
        _database.InitializeSchema();
        _spaces = new ViewPersonalSpaceRepository(_database);
        _service = new ViewStorageService(_configuration, _spaces);
    }

    [Fact]
    public async Task EnsurePersonalSpace_CreatesOneStableProfileRootAndUploadSource()
    {
        var profileId = await AddProfileAsync();

        var first = await _service.EnsurePersonalSpaceAsync(profileId);
        var second = await _service.EnsurePersonalSpaceAsync(profileId);
        var source = Assert.Single(await _spaces.GetSourcesAsync(first.Id));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(ViewSourceStorageMode.Managed, source.StorageMode);
        Assert.Equal("Browser uploads", source.Name);
        Assert.Equal(
            Path.Combine(_service.GetRootPath(), "profiles", profileId.ToString("N")),
            _service.GetProfileRoot(first));
        Assert.Equal(
            Path.Combine(_service.GetProfileRoot(first), "sources", source.Id.ToString("N")),
            _service.GetSourcePath(first, source));
        Assert.True(Directory.Exists(_service.GetSourcePath(first, source)));
    }

    [Fact]
    public async Task ImportFolder_CopiesOriginalsIntoManagedSource()
    {
        var profileId = await AddProfileAsync();
        var space = await _service.EnsurePersonalSpaceAsync(profileId);
        var origin = Directory.CreateDirectory(Path.Combine(_root, "phone-export"));
        var nested = Directory.CreateDirectory(Path.Combine(origin.FullName, "Camera"));
        var original = Path.Combine(nested.FullName, "photo.jpg");
        await File.WriteAllTextAsync(original, "original bytes");

        var source = await _service.ImportFolderAsync(space, "Shy's phone", origin.FullName);
        var copy = Path.Combine(_service.GetSourcePath(space, source), "Camera", "photo.jpg");

        Assert.Equal(ViewSourceStorageMode.Managed, source.StorageMode);
        Assert.True(File.Exists(original));
        Assert.Equal("original bytes", await File.ReadAllTextAsync(copy));
    }

    [Fact]
    public async Task LinkFolder_RemainsExternalAndRejectsManagedRootAliases()
    {
        var profileId = await AddProfileAsync();
        var space = await _service.EnsurePersonalSpaceAsync(profileId);
        var external = Directory.CreateDirectory(Path.Combine(_root, "family-archive"));

        var source = await _service.AddLinkedSourceAsync(space, "Family archive", external.FullName, true);

        Assert.Equal(ViewSourceStorageMode.Linked, source.StorageMode);
        Assert.Equal(external.FullName, _service.GetSourcePath(space, source));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddLinkedSourceAsync(
            space, "Managed alias", _service.GetProfileRoot(space), true));
    }

    private async Task<Guid> AddProfileAsync()
    {
        var profileId = Guid.NewGuid();
        await new ProfileRepository(_database).InsertAsync(new Profile
        {
            Id = profileId,
            DisplayName = "View owner",
            Role = ProfileRole.RestrictedProfile,
        });
        return profileId;
    }

    public void Dispose()
    {
        _configuration.Dispose();
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
