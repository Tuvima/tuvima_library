using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Api.Services.View;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class ViewSharedTransferServiceTests
{
    [Fact]
    public async Task Contribution_DoesNotMoveUntilCuratorAccepts_AndIsIdempotent()
    {
        using var fixture = new Fixture();
        await fixture.Policies.SavePolicyAsync(new ViewProfilePolicy(
            fixture.ProfileId, true, true, true, true, true, null));
        var original = fixture.WriteManaged("keeper.jpg", [11, 12, 13]);
        var indexed = await fixture.Library.IndexPathAsync(fixture.Space.LibraryId, original);
        Assert.NotNull(indexed);
        var preview = await fixture.Contributions.PreviewAsync(fixture.ProfileId,
            new ViewSharedContributionPreviewRequest([indexed!.ItemId]));
        var request = new ViewSharedContributionSubmitRequest([indexed.ItemId], "timeline", null,
            "Worth keeping", preview.PreviewRevision, "stable-request");

        var pending = await fixture.Contributions.SubmitAsync(fixture.ProfileId, request);
        var repeated = await fixture.Contributions.SubmitAsync(fixture.ProfileId, request);

        Assert.Equal(pending.Id, repeated.Id);
        Assert.Equal("pending", pending.Status);
        Assert.True(File.Exists(original));

        await fixture.Contributions.DecideAsync(fixture.ProfileId, pending.Id,
            new ViewSharedContributionDecisionRequest("accepted", pending.Revision));
        await fixture.Contributions.ProcessAsync(pending.Id);
        var accepted = await fixture.Contributions.GetRequiredAsync(fixture.ProfileId, pending.Id, true);

        Assert.Equal("accepted", accepted.Status);
        Assert.Equal("completed", Assert.Single(accepted.Items).ExecutionState);
        Assert.Contains(accepted.Events, value => value.EventType == "submitted");
        Assert.Contains(accepted.Events, value => value.EventType == "accepted");
        Assert.Contains(accepted.Events, value => value.EventType == "transfer_completed");
        Assert.False(File.Exists(original));
    }

    [Fact]
    public async Task CuratorCanAddOwnedItemDirectlyWithoutSubmitGrant()
    {
        using var fixture = new Fixture();
        await fixture.Policies.SavePolicyAsync(new ViewProfilePolicy(
            fixture.ProfileId, true, true, false, true, true, null));
        var original = fixture.WriteManaged("direct.jpg", [31, 32]);
        var indexed = await fixture.Library.IndexPathAsync(fixture.Space.LibraryId, original);

        var accepted = await fixture.Contributions.AddDirectAsync(fixture.ProfileId,
            new ViewSharedDirectAddRequest([indexed!.ItemId], IdempotencyKey: "direct-request"));

        Assert.Equal("accepted", accepted.Status);
        Assert.True(File.Exists(original));
        await fixture.Contributions.ProcessAsync(accepted.Id);
        Assert.False(File.Exists(original));
    }

    [Fact]
    public async Task DeclinedContribution_LeavesOriginalUntouched()
    {
        using var fixture = new Fixture();
        await fixture.Policies.SavePolicyAsync(new ViewProfilePolicy(
            fixture.ProfileId, true, true, true, true, true, null));
        var original = fixture.WriteManaged("declined.jpg", [21, 22]);
        var indexed = await fixture.Library.IndexPathAsync(fixture.Space.LibraryId, original);
        var preview = await fixture.Contributions.PreviewAsync(fixture.ProfileId,
            new ViewSharedContributionPreviewRequest([indexed!.ItemId]));
        var pending = await fixture.Contributions.SubmitAsync(fixture.ProfileId,
            new([indexed.ItemId], "timeline", null, null, preview.PreviewRevision, "decline-request"));

        var declined = await fixture.Contributions.DecideAsync(fixture.ProfileId, pending.Id,
            new("declined", pending.Revision, "Not for the shared collection"));

        Assert.Equal("declined", declined.Status);
        Assert.Equal("waiting", Assert.Single(declined.Items).ExecutionState);
        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task ManagedItem_IsVerifiedMovedAndBrowsableUnderSharedRoot()
    {
        using var fixture = new Fixture();
        var original = fixture.WriteManaged("shared-moment.jpg", [1, 2, 3, 4, 5]);
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
        Assert.Contains(root.Sources, source => source.SourceId == ViewFolderService.SharedLibrarySourceId);
        var shared = await fixture.Folders.QueryAsync(
            fixture.ProfileId,
            new ResolvedViewScope(ViewScopeKind.Shared, null, new HashSet<Guid> { fixture.Space.LibraryId }),
            ViewFolderService.SharedLibrarySourceId, null, false, null, 0, 100);
        Assert.Equal("Shared Library", Assert.Single(shared.Breadcrumbs).Label);
        Assert.Contains(shared.Folders, folder => folder.Name == "Timeline");
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
            Root = Path.Combine(Path.GetTempPath(), $"tuvima-shared-transfer-{Guid.NewGuid():N}");
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
                DisplayName = "Shared curator",
                Role = ProfileRole.Administrator,
            }).GetAwaiter().GetResult();
            Storage = new ViewStorageService(_configuration, _spaces);
            Space = Storage.EnsurePersonalSpaceAsync(ProfileId).GetAwaiter().GetResult();
            Library = new ViewLibraryService(_assets, _configuration, new LibraryAccessEvaluator(),
                _spaces, Storage, NullLogger<ViewLibraryService>.Instance);
            Transfers = new ViewSharedTransferService(_database, _assets, Storage);
            Policies = new ViewProfileRepository(_database);
            Contributions = new ViewSharedContributionService(_database, _assets, Policies, Transfers);
            Folders = new ViewFolderService(_database, _spaces, Profiles, _assets, Storage);
        }

        public string Root { get; }
        public Guid ProfileId { get; }
        public ViewPersonalSpace Space { get; }
        public ViewPersonalSpaceRepository Sources => _spaces;
        public ProfileRepository Profiles { get; }
        public ViewStorageService Storage { get; }
        public ViewLibraryService Library { get; }
        public ViewSharedTransferService Transfers { get; }
        public ViewProfileRepository Policies { get; }
        public ViewSharedContributionService Contributions { get; }
        public ViewFolderService Folders { get; }

        public string WriteManaged(string name, byte[] bytes)
        {
            var source = _spaces.GetSourcesAsync(Space.Id).GetAwaiter().GetResult()
                .Single(value => value.SourceType == ViewSourceType.BrowserUpload);
            var path = Path.Combine(Storage.GetSourcePath(Space, source), name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
