using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Intelligence.Tests;

public sealed class ParentCollectionResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;
    private readonly CollectionRepository _repo;
    private readonly ParentCollectionResolver _resolver;

    public ParentCollectionResolverTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_parent_collection_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
        _repo = new CollectionRepository(_db);
        _resolver = new ParentCollectionResolver(_repo, NullLogger<ParentCollectionResolver>.Instance);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_CreatesParentForBroaderFranchiseQid()
    {
        var child = CreateCollection("Dune", "ContentGroup", "Q6095696");
        await _repo.UpsertAsync(child);
        await _repo.InsertRelationshipsAsync([CreateRelationship(child.Id, "franchise", "Q18011049", "Dune")]);

        await _resolver.ResolveParentCollectionAsync(child.Id);

        var refreshedChild = await _repo.GetByIdAsync(child.Id);
        Assert.NotNull(refreshedChild);
        Assert.NotNull(refreshedChild!.ParentCollectionId);

        var parent = await _repo.GetByIdAsync(refreshedChild.ParentCollectionId!.Value);
        Assert.NotNull(parent);
        Assert.Equal("Universe", parent!.CollectionType);
        Assert.Equal("Dune", parent.DisplayName);

        var children = await _repo.GetChildCollectionsAsync(parent.Id);
        Assert.Contains(children, candidate => candidate.Id == child.Id);
    }

    [Fact]
    public async Task ResolveParentCollectionAsync_DoesNotCreateParentWhenRelationshipMatchesCollectionQid()
    {
        var child = CreateCollection("The Expanse", "ContentGroup", "Q19610143");
        await _repo.UpsertAsync(child);
        await _repo.InsertRelationshipsAsync([CreateRelationship(child.Id, "franchise", "Q19610143", "The Expanse")]);

        await _resolver.ResolveParentCollectionAsync(child.Id);

        var refreshedChild = await _repo.GetByIdAsync(child.Id);
        Assert.NotNull(refreshedChild);
        Assert.Null(refreshedChild!.ParentCollectionId);

        var parent = await _repo.FindParentCollectionByRelationshipAsync("Q19610143");
        Assert.Null(parent);
    }

    private static Collection CreateCollection(string name, string type, string qid) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        CollectionType = type,
        WikidataQid = qid,
        Resolution = "materialized",
        UniverseStatus = "Unknown",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static CollectionRelationship CreateRelationship(Guid collectionId, string relType, string qid, string label) => new()
    {
        Id = Guid.NewGuid(),
        CollectionId = collectionId,
        RelType = relType,
        RelQid = qid,
        RelLabel = label,
        Confidence = 1.0,
        DiscoveredAt = DateTimeOffset.UtcNow,
    };
}
