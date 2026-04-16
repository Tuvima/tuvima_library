using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Tests;

public sealed class CollectionRepositoryRelationshipTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public CollectionRepositoryRelationshipTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_collection_rel_{Guid.NewGuid():N}.db");
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
    public async Task InsertRelationshipsAsync_RoundTripsRows()
    {
        var repo = new CollectionRepository(_db);
        var collection = CreateCollection("Dune");

        await repo.UpsertAsync(collection);

        var relationship = new CollectionRelationship
        {
            Id = Guid.NewGuid(),
            CollectionId = collection.Id,
            RelType = "franchise",
            RelQid = "Q6095696",
            RelLabel = "Dune",
            Confidence = 1.0,
            DiscoveredAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertRelationshipsAsync([relationship]);

        var saved = await repo.GetRelationshipsAsync(collection.Id);
        var row = Assert.Single(saved);
        Assert.Equal(relationship.Id, row.Id);
        Assert.Equal(relationship.CollectionId, row.CollectionId);
        Assert.Equal(relationship.RelType, row.RelType);
        Assert.Equal(relationship.RelQid, row.RelQid);
        Assert.Equal(relationship.RelLabel, row.RelLabel);
    }

    [Fact]
    public async Task FindParentCollectionByRelationshipAsync_IgnoresContentGroups()
    {
        var repo = new CollectionRepository(_db);
        var contentGroup = CreateCollection("The Expanse", "ContentGroup");

        await repo.UpsertAsync(contentGroup);
        await repo.InsertRelationshipsAsync([CreateRelationship(contentGroup.Id, "franchise", "Q19610143", "The Expanse")]);

        var parent = await repo.FindParentCollectionByRelationshipAsync("Q19610143");
        Assert.Null(parent);
    }

    [Fact]
    public async Task FindParentCollectionByRelationshipAsync_ReturnsUniverseParent()
    {
        var repo = new CollectionRepository(_db);
        var contentGroup = CreateCollection("The Expanse", "ContentGroup");
        var parentUniverse = CreateCollection("The Expanse Universe", "Universe");

        await repo.UpsertAsync(contentGroup);
        await repo.UpsertAsync(parentUniverse);
        await repo.InsertRelationshipsAsync(
        [
            CreateRelationship(contentGroup.Id, "franchise", "Q19610143", "The Expanse"),
            CreateRelationship(parentUniverse.Id, "franchise", "Q19610143", "The Expanse"),
        ]);

        var parent = await repo.FindParentCollectionByRelationshipAsync("Q19610143");
        Assert.NotNull(parent);
        Assert.Equal(parentUniverse.Id, parent!.Id);
    }

    private static Collection CreateCollection(string name, string type = "Universe") => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        CreatedAt = DateTimeOffset.UtcNow,
        UniverseStatus = "Unknown",
        CollectionType = type,
        Resolution = "materialized",
    };

    private static CollectionRelationship CreateRelationship(
        Guid collectionId,
        string relType,
        string qid,
        string label) => new()
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
