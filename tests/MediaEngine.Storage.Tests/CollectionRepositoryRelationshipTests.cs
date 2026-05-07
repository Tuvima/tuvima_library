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

    [Fact]
    public async Task UpdateCollectionSquareArtworkAsync_RoundTripsMetadata()
    {
        var repo = new CollectionRepository(_db);
        var collection = CreateCollection("Road Trip Mix", "Playlist");

        await repo.UpsertAsync(collection);
        await repo.UpdateCollectionSquareArtworkAsync(collection.Id, @"C:\Tuvima\collections\road-trip.jpg", "image/jpeg");

        var saved = await repo.GetByIdAsync(collection.Id);

        Assert.NotNull(saved);
        Assert.Equal(@"C:\Tuvima\collections\road-trip.jpg", saved!.SquareArtworkPath);
        Assert.Equal("image/jpeg", saved.SquareArtworkMimeType);
    }

    [Fact]
    public async Task GetCollectionItemCountsAsync_ReturnsCountsForMultipleCollectionsInOneCall()
    {
        var repo = new CollectionRepository(_db);
        var first = CreateCollection("Road Trip Mix", "Playlist");
        var second = CreateCollection("Night Queue", "Playlist");

        await repo.UpsertAsync(first);
        await repo.UpsertAsync(second);

        var firstWorkA = Guid.NewGuid();
        var firstWorkB = Guid.NewGuid();
        var secondWork = Guid.NewGuid();
        InsertWork(firstWorkA, first.Id);
        InsertWork(firstWorkB, first.Id);
        InsertWork(secondWork, second.Id);

        await repo.AddCollectionItemAsync(CreateCollectionItem(first.Id, firstWorkA, 0));
        await repo.AddCollectionItemAsync(CreateCollectionItem(first.Id, firstWorkB, 1));
        await repo.AddCollectionItemAsync(CreateCollectionItem(second.Id, secondWork, 0));

        var counts = await repo.GetCollectionItemCountsAsync([first.Id, second.Id, Guid.NewGuid()]);

        Assert.Equal(2, counts[first.Id]);
        Assert.Equal(1, counts[second.Id]);
        Assert.Contains(counts, pair => pair.Value == 0);
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

    private static CollectionItem CreateCollectionItem(Guid collectionId, Guid workId, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        CollectionId = collectionId,
        WorkId = workId,
        SortOrder = sortOrder,
        AddedAt = DateTimeOffset.UtcNow,
    };

    private void InsertWork(Guid workId, Guid collectionId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO works (id, collection_id, media_type) VALUES ($id, $collectionId, 'Music')";
        cmd.Parameters.AddWithValue("$id", workId.ToString());
        cmd.Parameters.AddWithValue("$collectionId", collectionId.ToString());
        cmd.ExecuteNonQuery();
    }
}
