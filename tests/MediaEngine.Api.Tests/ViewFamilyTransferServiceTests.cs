using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Api.Services.View;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class ViewFamilyTransferServiceTests
{
    [Fact]
    public async Task ManagedItem_IsVerifiedMovedAndBrowsableUnderFamilyRoot()
    {
        using var fixture = new Fixture();
        var original = fixture.WriteManaged("family-moment.jpg", [1, 2, 3, 4, 5]);
        var indexed = await fixture.Library.IndexPathAsync(fixture.Space.LibraryId, original);
        Assert.NotNull(indexed);

        var preview = fixture.Transfers.Preview(indexed!.ItemId, "timeline", null);
        var result = await fixture.Transfers.ExecuteAsync(indexed.ItemId, fixture.ProfileId, "timeline", null);
        var repeated = await fixture.Transfers.ExecuteAsync(indexed.ItemId, fixture.ProfileId, "timeline", null);

        Assert.Equal("move", preview.Operation);
        Assert.Equal("completed", result.State);
        Assert.False(File.Exists(original));
        var destination = Assert.Single(result.DestinationPaths);
        Assert.StartsWith(fixture.Storage.GetSharedRoot(), destination, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([1, 2, 3, 4, 5], await File.ReadAllBytesAsync(destination));
        Assert.Equal(result.DestinationPaths, repeated.DestinationPaths);

        var root = await fixture.Folders.QueryAsync(
            fixture.ProfileId,
            new ResolvedViewScope(ViewScopeKind.Shared, null, new HashSet<Guid> { fixture.Space.LibraryId }),
            null, null, false, null, 0, 100);
        Assert.Contains(root.Sources, source => source.SourceId == ViewFolderService.FamilyLibrarySourceId);
        var family = await fixture.Folders.QueryAsync(
            fixture.ProfileId,
            new ResolvedViewScope(ViewScopeKind.Shared, null, new HashSet<Guid> { fixture.Space.LibraryId }),
            ViewFolderService.FamilyLibrarySourceId, null, false, null, 0, 100);
        Assert.Equal("Family Library", Assert.Single(family.Breadcrumbs).Label);
        Assert.Contains(family.Folders, folder => folder.Name == "Timeline");
    }

    [Fact]
    public async Task LinkedItem_IsCopiedAndExternalOriginalRemains()
    {
        using var fixture = new Fixture();
        var linkedRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "linked"));
        await fixture.Storage.AddLinkedSourceAsync(fixture.Space, "Home Movies", linkedRoot.FullName, true);
        var original = Path.Combine(linkedRoot.FullName, "home-movie.mp4");
        await File.WriteAllBytesAsync(original, [8, 9, 10]);
        var indexed = await fixture.Library.IndexPathAsync(fixture.Space.LibraryId, original);
        Assert.NotNull(indexed);

        var preview = fixture.Transfers.Preview(indexed!.ItemId, "folder", "Home Movies");
        var result = await fixture.Transfers.ExecuteAsync(
            indexed.ItemId, fixture.ProfileId, "folder", "Home Movies");

        Assert.Equal("copy", preview.Operation);
        Assert.Equal("completed", result.State);
        Assert.True(File.Exists(original));
        Assert.Equal([8, 9, 10], await File.ReadAllBytesAsync(Assert.Single(result.DestinationPaths)));
    }

    [Fact]
    public async Task FolderPinsAndTimelinePolicies_AreScopedAndInherited()
    {
        using var fixture = new Fixture();
        var source = Assert.Single(await fixture.Sources.GetSourcesAsync(fixture.Space.Id));
        var scope = new ResolvedViewScope(ViewScopeKind.Mine, fixture.ProfileId,
            new HashSet<Guid> { fixture.Space.LibraryId });

        await fixture.Folders.SetPinAsync(fixture.ProfileId, scope, source.Id, "Timeline", true);
        await fixture.Folders.SetTimelinePolicyAsync(fixture.ProfileId, true, scope,
            source.Id, string.Empty, false);
        await fixture.Folders.SetTimelinePolicyAsync(fixture.ProfileId, true, scope,
            source.Id, "Timeline", true);

        var root = await fixture.Folders.QueryAsync(fixture.ProfileId, scope,
            null, null, false, null, 0, 100);
        var nested = await fixture.Folders.QueryAsync(fixture.ProfileId, scope,
            source.Id, Path.Combine("Timeline", "2024"), false, null, 0, 100);

        Assert.Contains(root.PinnedFolders, pin => pin.SourceId == source.Id && pin.RelativePath == "Timeline");
        Assert.True(nested.EffectiveIncludeInTimeline);
        Assert.Null(nested.IncludeInTimelineOverride);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ConfigurationDirectoryLoader _configuration;
        private readonly DatabaseConnection _database;
        private readonly ViewPersonalSpaceRepository _spaces;
        private readonly LocalAssetRepository _assets;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"tuvima-family-transfer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            _configuration = new ConfigurationDirectoryLoader(Path.Combine(Root, "config"));
            _configuration.SaveLibraries(new LibrariesConfiguration
            {
                SchemaVersion = "6.0",
                StorageLocations =
                [
                    new ServerStorageLocationConfig
                    {
                        Id = "view",
                        Label = "View",
                        Path = Path.Combine(Root, "storage"),
                        AllowWrite = true,
                    },
                ],
                ViewStorage = new ViewStorageConfig
                {
                    StorageLocationId = "view",
                    RelativeRoot = "View",
                },
            });
            _database = new DatabaseConnection(Path.Combine(Root, "view.db"));
            _database.InitializeSchema();
            _spaces = new ViewPersonalSpaceRepository(_database);
            _assets = new LocalAssetRepository(_database);
            Profiles = new ProfileRepository(_database);
            ProfileId = Guid.NewGuid();
            Profiles.InsertAsync(new Profile
            {
                Id = ProfileId,
                DisplayName = "Family owner",
                Role = ProfileRole.Administrator,
            }).GetAwaiter().GetResult();
            Storage = new ViewStorageService(_configuration, _spaces);
            Space = Storage.EnsurePersonalSpaceAsync(ProfileId).GetAwaiter().GetResult();
            Library = new ViewLibraryService(_assets, _configuration, new LibraryAccessEvaluator(),
                _spaces, Storage, NullLogger<ViewLibraryService>.Instance);
            Transfers = new ViewFamilyTransferService(_database, _assets, Storage);
            Folders = new ViewFolderService(_database, _spaces, Profiles, _assets, Storage);
        }

        public string Root { get; }
        public Guid ProfileId { get; }
        public ViewPersonalSpace Space { get; }
        public ViewPersonalSpaceRepository Sources => _spaces;
        public ProfileRepository Profiles { get; }
        public ViewStorageService Storage { get; }
        public ViewLibraryService Library { get; }
        public ViewFamilyTransferService Transfers { get; }
        public ViewFolderService Folders { get; }

        public string WriteManaged(string name, byte[] bytes)
        {
            var source = _spaces.GetSourcesAsync(Space.Id).GetAwaiter().GetResult()
                .Single(value => value.SourceType == ViewSourceType.BrowserUpload);
            var path = Path.Combine(Storage.GetSourcePath(Space, source), name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            _configuration.Dispose();
            _database.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
