using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class LargeLibraryReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public LargeLibraryReadServiceTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_large_library_{Guid.NewGuid():N}.db");
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
    public async Task IngestionBatchReadService_ReturnsOnlyRequestedPage()
    {
        var batchId = Guid.NewGuid();
        SeedIngestionRows(batchId, 25);
        var service = new IngestionBatchReadService(_db);

        var page = await service.GetItemsAsync(batchId, offset: 10, limit: 5, CancellationToken.None);

        Assert.Equal(5, page.Count);
        Assert.Equal("file-010.epub", page[0].FileName);
        Assert.Equal("file-014.epub", page[^1].FileName);
    }

    [Fact]
    public void LargeLibraryIndexes_ExistAfterStartupMigrations()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";
        using var reader = cmd.ExecuteReader();
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("idx_canonical_values_key_value_entity", indexes);
        Assert.Contains("idx_person_media_links_person", indexes);
        Assert.Contains("idx_ingestion_log_run_created", indexes);
        Assert.Contains("idx_identity_jobs_run_entity_updated", indexes);
    }

    private void SeedIngestionRows(Guid batchId, int count)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ingestion_batches (id, status, files_total, files_processed, created_at, started_at)
            VALUES ($batchId, 'running', $count, 0, $now, $now);
            """;
        cmd.Parameters.AddWithValue("$batchId", batchId.ToString());
        cmd.Parameters.AddWithValue("$count", count);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        for (var i = 0; i < count; i++)
        {
            using var row = conn.CreateCommand();
            row.CommandText = """
                INSERT INTO ingestion_log (
                    id, ingestion_run_id, file_path, status, created_at, updated_at
                )
                VALUES ($id, $batchId, $filePath, 'detected', $createdAt, $createdAt);
                """;
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(i).ToString("O");
            row.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            row.Parameters.AddWithValue("$batchId", batchId.ToString());
            row.Parameters.AddWithValue("$filePath", $"C:/watch/file-{i:000}.epub");
            row.Parameters.AddWithValue("$createdAt", createdAt);
            row.ExecuteNonQuery();
        }
    }
}
