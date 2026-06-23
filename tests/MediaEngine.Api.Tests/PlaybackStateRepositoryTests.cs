using Dapper;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class PlaybackStateRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima-playback-state-{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _db;

    public PlaybackStateRepositoryTests()
    {
        DapperConfiguration.Configure();
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    [Fact]
    public async Task StoreInspectionAsync_UsesGuidBlobAssetForeignKey()
    {
        var assetId = await CreateAssetAsync();
        var repository = new PlaybackStateRepository(_db);

        await repository.StoreInspectionAsync(
            assetId,
            sourceHash: $"hash-{Guid.NewGuid():N}",
            fileSize: 1234,
            durationSecs: 65,
            container: "m4b",
            metadataJson: "{}");

        using var conn = _db.CreateConnection();
        var storageType = await conn.ExecuteScalarAsync<string>(
            "SELECT typeof(asset_id) FROM playback_inspection_cache WHERE asset_id = @assetId",
            new { assetId });

        Assert.Equal("blob", storageType);
    }

    private async Task<Guid> CreateAssetAsync()
    {
        using var conn = _db.CreateConnection();
        var collectionId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO collections (id, created_at) VALUES (@collectionId, datetime('now'));
            INSERT INTO works (id, collection_id, media_type) VALUES (@workId, @collectionId, 'Audiobooks');
            INSERT INTO editions (id, work_id) VALUES (@editionId, @workId);
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
            VALUES (@assetId, @editionId, @contentHash, '/library/Audiobooks/test.m4b', 'Normal');
            """,
            new
            {
                collectionId,
                workId,
                editionId,
                assetId,
                contentHash = $"asset-{assetId:N}",
            });
        return assetId;
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
