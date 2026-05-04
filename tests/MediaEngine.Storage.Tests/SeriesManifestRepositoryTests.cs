using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Tests;

public sealed class SeriesManifestRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public SeriesManifestRepositoryTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_series_manifest_{Guid.NewGuid():N}.db");
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
    public async Task UpsertManifest_PersistsNamedOwnedAndMissingItems()
    {
        var repo = new SeriesManifestRepository(_db);
        var collectionId = await CreateCollectionAsync("QSeries", "Dune");
        var ownedWorkId = await CreateWorkAsync("QOwned");
        var now = DateTimeOffset.UtcNow;

        var items = new List<SeriesManifestItemRecord>
        {
            Item(collectionId, "QSeries", "QOwned", "Dune", 1, "Owned", ownedWorkId, now),
            Item(collectionId, "QSeries", "QMissing", "Dune Messiah", 2, "Missing", null, now),
        };

        await repo.UpsertManifestAsync(new SeriesManifestHydration
        {
            SeriesQid = "QSeries",
            CollectionId = collectionId,
            SeriesLabel = "Dune",
            ManifestHash = "manifest-hash",
            KnownItemQidsHash = "qid-hash",
            WarningsJson = "[]",
            ApiMetadataJson = "{}",
            LastHydratedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        }, items);
        await repo.LinkOwnedWorksAsync(collectionId, items);

        var view = await repo.GetViewByCollectionIdAsync(collectionId);

        Assert.NotNull(view);
        Assert.Equal(2, view.TotalCount);
        Assert.Equal(1, view.OwnedCount);
        Assert.Equal(1, view.MissingCount);
        Assert.Contains(view.Items, i => i.ItemQid == "QOwned" && i.ItemLabel == "Dune" && i.LinkedWorkId == ownedWorkId);
        Assert.Contains(view.Items, i => i.ItemQid == "QMissing" && i.ItemLabel == "Dune Messiah" && i.OwnershipState == "Missing");
        Assert.Equal(1, CountRows("collection_items"));
        Assert.Equal(0, CountRows("media_assets"));
    }

    [Fact]
    public async Task FindWorkIdsByQids_ReturnsAllMatchesForAmbiguousLocalQid()
    {
        var repo = new SeriesManifestRepository(_db);
        var first = await CreateWorkAsync("QDuplicate");
        var second = await CreateWorkAsync("QDuplicate");

        var matches = await repo.FindWorkIdsByQidsAsync(["QDuplicate"]);

        Assert.True(matches.TryGetValue("QDuplicate", out var workIds));
        Assert.Contains(first, workIds);
        Assert.Contains(second, workIds);
        Assert.Equal(2, workIds.Count);
    }

    [Fact]
    public async Task UpsertManifest_UpdatesExistingItemLabelAndSortOrder()
    {
        var repo = new SeriesManifestRepository(_db);
        var collectionId = await CreateCollectionAsync("QSeries", "The Expanse");
        var now = DateTimeOffset.UtcNow;

        await repo.UpsertManifestAsync(Hydration(collectionId, "QSeries", now), [
            Item(collectionId, "QSeries", "Q1", "Old Label", 10, "Missing", null, now)
        ]);
        await repo.UpsertManifestAsync(Hydration(collectionId, "QSeries", now.AddMinutes(1)), [
            Item(collectionId, "QSeries", "Q1", "Leviathan Wakes", 1, "Missing", null, now.AddMinutes(1))
        ]);

        var view = await repo.GetViewByCollectionIdAsync(collectionId);

        Assert.NotNull(view);
        var item = Assert.Single(view.Items);
        Assert.Equal("Leviathan Wakes", item.ItemLabel);
        Assert.Equal(1, item.SortOrder);
    }

    private async Task<Guid> CreateCollectionAsync(string qid, string name)
    {
        var repo = new CollectionRepository(_db);
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            WikidataQid = qid,
            CollectionType = "ContentGroup",
            Resolution = "materialized",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return await repo.UpsertAsync(collection);
    }

    private Task<Guid> CreateWorkAsync(string qid)
    {
        var id = Guid.NewGuid();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works (id, collection_id, media_type, work_kind, wikidata_qid, external_identifiers)
            VALUES (@id, NULL, 'Books', 'standalone', @qid, @externalIdentifiers);
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@qid", qid);
        cmd.Parameters.AddWithValue("@externalIdentifiers", $$"""{"wikidata_qid":"{{qid}}"}""");
        cmd.ExecuteNonQuery();
        return Task.FromResult(id);
    }

    private int CountRows(string table)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static SeriesManifestHydration Hydration(Guid collectionId, string seriesQid, DateTimeOffset now) => new()
    {
        SeriesQid = seriesQid,
        CollectionId = collectionId,
        SeriesLabel = "Series",
        WarningsJson = "[]",
        ApiMetadataJson = "{}",
        LastHydratedAt = now,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static SeriesManifestItemRecord Item(
        Guid collectionId,
        string seriesQid,
        string qid,
        string label,
        double sortOrder,
        string ownership,
        Guid? linkedWorkId,
        DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            CollectionId = collectionId,
            SeriesQid = seriesQid,
            ItemQid = qid,
            ItemLabel = label,
            SortOrder = sortOrder,
            OrderSource = "SeriesOrdinal",
            OwnershipState = ownership,
            LinkedWorkId = linkedWorkId,
            SourcePropertiesJson = """["P179"]""",
            RelationshipsJson = "[]",
            LastHydratedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
}
