using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class LibraryOverviewReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public LibraryOverviewReadServiceTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_library_overview_{Guid.NewGuid():N}.db");
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
    public async Task GetOverviewAggregatesAsync_ReturnsRecentlyAddedWindowsAndPipelineSuccess()
    {
        SeedWorkWithClaim(DateTimeOffset.UtcNow.AddHours(-2));
        SeedWorkWithClaim(DateTimeOffset.UtcNow.AddDays(-3));
        SeedWorkWithClaim(DateTimeOffset.UtcNow.AddDays(-20));
        SeedIdentityJob("Completed");
        SeedIdentityJob("Completed");
        SeedIdentityJob("Failed");

        var service = new LibraryOverviewReadService(_db);

        var result = await service.GetOverviewAggregatesAsync(CancellationToken.None);

        Assert.Equal(1, result.Added24h);
        Assert.Equal(2, result.Added7d);
        Assert.Equal(3, result.Added30d);
        Assert.Equal(2, result.PipelineStates["Completed"]);
        Assert.Equal(1, result.PipelineStates["Failed"]);
        Assert.Equal(0.6667, result.PipelineSuccessRate);
    }

    [Fact]
    public async Task GetOverviewAggregatesAsync_ReturnsPerfectPipelineRateWhenNoTerminalJobsExist()
    {
        var service = new LibraryOverviewReadService(_db);

        var result = await service.GetOverviewAggregatesAsync(CancellationToken.None);

        Assert.Empty(result.PipelineStates);
        Assert.Equal(1.0, result.PipelineSuccessRate);
    }

    private void SeedWorkWithClaim(DateTimeOffset claimedAt)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO metadata_providers (id, name, version, is_enabled)
            VALUES ($providerId, $providerName, '1.0', 1);
            INSERT INTO works (id, media_type, work_kind)
            VALUES ($workId, 'Books', 'standalone');
            INSERT INTO editions (id, work_id)
            VALUES ($editionId, $workId);
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
            VALUES ($assetId, $editionId, $hash, $path);
            INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
            VALUES ($claimId, $assetId, $providerId, 'title', 'Seed Title', 1.0, $claimedAt);
            """;
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        cmd.Parameters.AddWithValue("$workId", workId.ToString("D"));
        cmd.Parameters.AddWithValue("$editionId", editionId.ToString("D"));
        cmd.Parameters.AddWithValue("$assetId", assetId.ToString("D"));
        cmd.Parameters.AddWithValue("$providerName", $"test-{providerId:N}");
        cmd.Parameters.AddWithValue("$hash", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$path", $"C:/library/{assetId:N}.epub");
        cmd.Parameters.AddWithValue("$claimId", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$providerId", providerId.ToString("D"));
        cmd.Parameters.AddWithValue("$claimedAt", claimedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void SeedIdentityJob(string state)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO identity_jobs (id, entity_id, entity_type, media_type, state, pass, created_at, updated_at)
            VALUES ($id, $entityId, 'MediaAsset', 'Books', $state, 'Quick', $now, $now);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$entityId", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
