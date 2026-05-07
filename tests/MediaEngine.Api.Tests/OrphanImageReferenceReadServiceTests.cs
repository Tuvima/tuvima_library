using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class OrphanImageReferenceReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public OrphanImageReferenceReadServiceTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_orphan_images_{Guid.NewGuid():N}.db");
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
    public async Task GetKnownReferencesAsync_ReturnsWorkPersonUniverseAndPendingWorkReferences()
    {
        var knownWorkId = Guid.NewGuid();
        var pendingWorkId = Guid.NewGuid();
        var pendingEditionId = Guid.NewGuid();
        var pendingAssetId = Guid.NewGuid();
        var parentCollectionId = Guid.NewGuid();
        var childCollectionId = Guid.NewGuid();
        SeedRows(knownWorkId, pendingWorkId, pendingEditionId, pendingAssetId, parentCollectionId, childCollectionId);
        var service = new OrphanImageReferenceReadService(_db);

        var references = await service.GetKnownReferencesAsync(CancellationToken.None);

        Assert.Contains("Q-WORK", references.KnownWorkQids);
        Assert.Contains(pendingAssetId.ToString("N")[..12], references.KnownWorkId12);
        Assert.Contains("Q-PERSON", references.KnownPersonQids);
        Assert.Contains("Q-UNIVERSE-PARENT", references.KnownUniverseQids);
        Assert.Contains("Q-UNIVERSE-CHILD", references.KnownUniverseQids);
    }

    private void SeedRows(
        Guid knownWorkId,
        Guid pendingWorkId,
        Guid pendingEditionId,
        Guid pendingAssetId,
        Guid parentCollectionId,
        Guid childCollectionId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works (id, media_type, wikidata_qid) VALUES ($knownWorkId, 'Books', 'Q-WORK');
            INSERT INTO works (id, media_type, wikidata_qid) VALUES ($pendingWorkId, 'Books', NULL);
            INSERT INTO editions (id, work_id, format_label) VALUES ($pendingEditionId, $pendingWorkId, 'EPUB');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
            VALUES ($pendingAssetId, $pendingEditionId, $hash, 'C:/library/book.epub');
            INSERT INTO persons (id, name, wikidata_qid, created_at)
            VALUES ($personId, 'Ada Example', 'Q-PERSON', $now);
            INSERT INTO collections (id, display_name, wikidata_qid)
            VALUES ($parentCollectionId, 'Parent', 'Q-UNIVERSE-PARENT');
            INSERT INTO collections (id, parent_collection_id, display_name, wikidata_qid)
            VALUES ($childCollectionId, $parentCollectionId, 'Child', 'Q-UNIVERSE-CHILD');
            """;
        cmd.Parameters.AddWithValue("$knownWorkId", knownWorkId.ToString());
        cmd.Parameters.AddWithValue("$pendingWorkId", pendingWorkId.ToString());
        cmd.Parameters.AddWithValue("$pendingEditionId", pendingEditionId.ToString());
        cmd.Parameters.AddWithValue("$pendingAssetId", pendingAssetId.ToString());
        cmd.Parameters.AddWithValue("$hash", new string('a', 64));
        cmd.Parameters.AddWithValue("$personId", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$parentCollectionId", parentCollectionId.ToString());
        cmd.Parameters.AddWithValue("$childCollectionId", childCollectionId.ToString());
        cmd.ExecuteNonQuery();
    }
}
