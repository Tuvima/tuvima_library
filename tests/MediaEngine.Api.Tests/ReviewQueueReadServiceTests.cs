using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class ReviewQueueReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public ReviewQueueReadServiceTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_review_read_{Guid.NewGuid():N}.db");
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
    public async Task PendingReviews_IncludeAssetRowsAndUseSameEligibilityAsCount()
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        SeedMediaAsset(workId, editionId, assetId);
        SeedCanonicalValue(assetId, "title", "Asset Level Review Movie");
        SeedCanonicalValue(assetId, "media_type", "Movies");
        SeedCanonicalValue(assetId, "tmdb_id", "603");
        SeedCanonicalValue(workId, "wikidata_qid", "Q83495");

        var repository = new ReviewQueueRepository(_db);
        var assetReviewId = Guid.NewGuid();
        var workReviewId = Guid.NewGuid();
        await repository.InsertAsync(Review(assetReviewId, assetId, "MediaAsset", "Asset needs confirmation"));
        await repository.InsertAsync(Review(workReviewId, workId, "Work", "Work needs confirmation"));
        await repository.InsertAsync(Review(Guid.NewGuid(), Guid.NewGuid(), "MediaAsset", "Stale missing asset"));

        var service = new ReviewQueueReadService(_db);

        var pending = await service.GetPendingAsync(10, CancellationToken.None);
        var count = await service.GetPendingCountAsync(CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, item => item.Id == assetReviewId && item.EntityId == assetId);
        Assert.Contains(pending, item => item.Id == workReviewId && item.EntityId == workId);

        var assetReview = Assert.Single(pending, item => item.Id == assetReviewId);
        Assert.Equal("Asset Level Review Movie", assetReview.EntityTitle);
        Assert.Equal("Movies", assetReview.MediaType);
        Assert.Equal("603", assetReview.BridgeIdentifiers["tmdb_id"]);
        Assert.Equal("Q83495", assetReview.BridgeIdentifiers["wikidata_qid"]);
    }

    private static ReviewQueueEntry Review(Guid id, Guid entityId, string entityType, string detail) => new()
    {
        Id = id,
        EntityId = entityId,
        EntityType = entityType,
        Trigger = ReviewTrigger.MetadataConflict,
        Status = ReviewStatus.Pending,
        Detail = detail,
        CreatedAt = DateTimeOffset.UtcNow,
        ReviewReadyAt = DateTimeOffset.UtcNow,
        AutomationCompletedAt = DateTimeOffset.UtcNow,
    };

    private void SeedMediaAsset(Guid workId, Guid editionId, Guid assetId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works (id, media_type, work_kind)
            VALUES ($workId, 'Movies', 'standalone');
            INSERT INTO editions (id, work_id)
            VALUES ($editionId, $workId);
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
            VALUES ($assetId, $editionId, $hash, $path);
            """;
        AddGuid(cmd, "$workId", workId);
        AddGuid(cmd, "$editionId", editionId);
        AddGuid(cmd, "$assetId", assetId);
        cmd.Parameters.AddWithValue("$hash", $"review-{assetId:N}");
        cmd.Parameters.AddWithValue("$path", $"C:/library/{assetId:N}.mkv");
        cmd.ExecuteNonQuery();
    }

    private void SeedCanonicalValue(Guid entityId, string key, string value)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
            VALUES ($entityId, $key, $value, $now);
            """;
        AddGuid(cmd, "$entityId", entityId);
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void AddGuid(SqliteCommand command, string name, Guid value) =>
        command.Parameters.Add(name, SqliteType.Blob).Value = GuidSql.ToBlob(value);
}
