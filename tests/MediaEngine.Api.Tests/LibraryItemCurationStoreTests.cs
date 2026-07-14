using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class LibraryItemCurationStoreTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DatabaseConnection _database;
    private readonly LibraryItemCurationStore _store;

    public LibraryItemCurationStoreTests()
    {
        DapperConfiguration.Configure();
        _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_library_item_store_{Guid.NewGuid():N}.db");
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _store = new LibraryItemCurationStore(_database);
    }

    [Fact]
    public async Task TargetAndRemovalReads_AreSetBased_AndDeleteUsesOneTransactionalBoundary()
    {
        var first = await SeedWorkAsync("First", "C:/library/a.epub", "Books");
        var secondAssetId = Guid.NewGuid();
        var second = await SeedWorkAsync("Second", "C:/library/c.epub", "Movies");
        var managedPath = "C:/library/.data/assets/work/cover.jpg";

        using (var connection = _database.CreateConnection())
        {
            await connection.ExecuteAsync("""
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
                VALUES (@secondAssetId, @editionId, @hash, 'C:/library/b.epub', 'Normal');
                INSERT INTO entity_assets (id, entity_id, entity_type, asset_type, local_image_path)
                VALUES (@entityAssetId, @workId, 'Work', 'CoverArt', @managedPath);
                INSERT INTO review_queue (id, entity_id, entity_type, trigger, status)
                VALUES (@reviewId, @assetId, 'MediaAsset', 'LowConfidence', 'Pending');
                """, new
            {
                secondAssetId,
                editionId = first.EditionId,
                hash = Guid.NewGuid().ToString("N"),
                entityAssetId = Guid.NewGuid(),
                workId = first.WorkId,
                managedPath,
                reviewId = Guid.NewGuid(),
                assetId = first.AssetId,
            });
        }

        var resolved = await _store.ResolveTargetAsync(first.WorkId);
        var batch = await _store.ResolveWorkTargetsAsync([first.WorkId, second.WorkId]);
        var removals = await _store.GetRemovalTargetsAsync([first.WorkId, second.WorkId]);

        Assert.NotNull(resolved);
        Assert.Equal(first.AssetId, resolved.AssetId);
        Assert.Equal("First", resolved.Title);
        Assert.Equal(2, batch.Count);
        Assert.Equal("Movies", batch[second.WorkId].MediaType);
        var removal = removals[first.WorkId];
        Assert.Equal(2, removal.FilePaths.Count);
        Assert.Contains(managedPath, removal.ManagedAssetPaths);

        await _store.DeleteWorkRecordsAsync(removal);

        using var verify = _database.CreateConnection();
        Assert.Equal(0, await verify.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM works WHERE id = @workId", new { workId = first.WorkId }));
        Assert.Equal(0, await verify.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM review_queue WHERE entity_id = @assetId", new { assetId = first.AssetId }));
        Assert.Equal(0, await verify.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM entity_assets WHERE entity_id = @workId", new { workId = first.WorkId }));
    }

    [Fact]
    public async Task CanonicalAndReviewUpdates_AreAppliedTogetherWithCurrentGuidStorage()
    {
        var seeded = await SeedWorkAsync("Before", "C:/library/book.epub", "Books");
        var now = DateTimeOffset.UtcNow;
        var claims = new[]
        {
            new MetadataClaim
            {
                Id = Guid.NewGuid(),
                EntityId = seeded.AssetId,
                ProviderId = WellKnownProviders.UserManual,
                ClaimKey = MetadataFieldConstants.Title,
                ClaimValue = "After",
                ClaimedAt = now,
                Confidence = 1,
                IsUserLocked = true,
            },
        };

        using (var connection = _database.CreateConnection())
        {
            await connection.ExecuteAsync("""
                INSERT INTO review_queue (id, entity_id, entity_type, trigger, status)
                VALUES (@assetReviewId, @assetId, 'MediaAsset', 'LowConfidence', 'Pending');
                INSERT INTO review_queue (id, entity_id, entity_type, trigger, status)
                VALUES (@workReviewId, @workId, 'Work', 'LowConfidence', 'Pending');
                """, new
            {
                assetReviewId = Guid.NewGuid(),
                workReviewId = Guid.NewGuid(),
                assetId = seeded.AssetId,
                workId = seeded.WorkId,
            });
        }

        await _store.UpsertCanonicalValuesAsync(seeded.AssetId, claims);
        await _store.MarkWorkRegisteredAsync(seeded.WorkId);
        await _store.CompletePendingReviewsAsync(
            seeded.AssetId, seeded.WorkId, "Resolved", "user:test", now);

        using var verify = _database.CreateConnection();
        Assert.Equal("After", await verify.ExecuteScalarAsync<string>("""
            SELECT value FROM canonical_values WHERE entity_id = @assetId AND key = 'title'
            """, new { assetId = seeded.AssetId }));
        Assert.Equal("registered", await verify.ExecuteScalarAsync<string>(
            "SELECT curator_state FROM works WHERE id = @workId", new { workId = seeded.WorkId }));
        Assert.Equal(2, await verify.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM review_queue
            WHERE entity_id IN (@assetId, @workId) AND status = 'Resolved' AND resolved_by = 'user:test'
            """, new { assetId = seeded.AssetId, workId = seeded.WorkId }));
    }

    [Fact]
    public async Task BatchApprovalAndRejection_UpdateSetsWithoutPerItemReads()
    {
        var first = await SeedWorkAsync("First", "C:/library/first.epub", "Books");
        var second = await SeedWorkAsync("Second", "C:/library/second.epub", "Books");
        using (var connection = _database.CreateConnection())
        {
            await connection.ExecuteAsync("""
                INSERT INTO review_queue (id, entity_id, entity_type, trigger, status)
                VALUES (@firstReview, @firstAsset, 'MediaAsset', 'LowConfidence', 'Pending');
                INSERT INTO review_queue (id, entity_id, entity_type, trigger, status)
                VALUES (@secondReview, @secondAsset, 'MediaAsset', 'LowConfidence', 'Pending');
                """, new
            {
                firstReview = Guid.NewGuid(),
                secondReview = Guid.NewGuid(),
                firstAsset = first.AssetId,
                secondAsset = second.AssetId,
            });
        }

        var processed = await _store.ApproveWorksAsync(
            [first.WorkId, second.WorkId], DateTimeOffset.UtcNow);
        var firstTarget = Assert.IsType<LibraryItemTarget>(await _store.ResolveTargetAsync(first.WorkId));
        await _store.MarkRejectedAsync(
            firstTarget, "C:/library/.data/staging/rejected/first.epub", DateTimeOffset.UtcNow);

        using var verify = _database.CreateConnection();
        Assert.Equal(2, processed);
        Assert.Equal(2, await verify.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM works WHERE wikidata_status = 'missing'"));
        Assert.Equal(2, await verify.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM review_queue WHERE status = 'Resolved'"));
        Assert.Equal("rejected", await verify.ExecuteScalarAsync<string>(
            "SELECT curator_state FROM works WHERE id = @workId", new { workId = first.WorkId }));
        Assert.Equal("C:/library/.data/staging/rejected/first.epub", await verify.ExecuteScalarAsync<string>(
            "SELECT file_path_root FROM media_assets WHERE id = @assetId", new { assetId = first.AssetId }));
    }

    [Fact]
    public async Task RecoverProvisionalAndHistory_AreTypedAndTransactional()
    {
        var seeded = await SeedWorkAsync("History", "C:/library/history.epub", "Books");
        using (var connection = _database.CreateConnection())
        {
            await connection.ExecuteAsync("""
                UPDATE works SET curator_state = 'rejected', rejected_at = @now WHERE id = @workId;
                INSERT OR IGNORE INTO metadata_providers (id, name, version, is_enabled)
                VALUES (@providerId, 'Local Processor', '1', 1);
                INSERT INTO system_activity (occurred_at, action_type, entity_id, entity_type, detail)
                VALUES (@now, 'FileDetected', @assetId, 'MediaAsset', 'Detected for test');
                """, new
            {
                now = DateTimeOffset.UtcNow.ToString("O"),
                workId = seeded.WorkId,
                assetId = seeded.AssetId,
                providerId = WellKnownProviders.LocalProcessor,
            });
        }

        var recovered = await _store.RecoverAsync(seeded.WorkId, DateTimeOffset.UtcNow);
        var provisional = await _store.MarkProvisionalAsync(
            seeded.WorkId,
            new ProvisionalMetadataRequest { Title = "Curated", Creator = "Author" },
            DateTimeOffset.UtcNow);
        var history = await _store.GetHistoryAsync(seeded.WorkId);

        Assert.NotNull(recovered);
        Assert.Equal(seeded.AssetId, recovered.AssetId);
        Assert.NotNull(recovered.ReviewId);
        Assert.NotNull(provisional);
        Assert.Equal(2, provisional.ClaimsWritten);
        var entry = Assert.Single(history);
        Assert.Equal("File detected", entry.Label);
        Assert.Equal(seeded.AssetId, entry.EntityId);

        using var verify = _database.CreateConnection();
        Assert.Equal("provisional", await verify.ExecuteScalarAsync<string>(
            "SELECT curator_state FROM works WHERE id = @workId", new { workId = seeded.WorkId }));
        Assert.Equal("Curated", await verify.ExecuteScalarAsync<string>("""
            SELECT value FROM canonical_values WHERE entity_id = @assetId AND key = 'title'
            """, new { assetId = seeded.AssetId }));
    }

    [Fact]
    public async Task Reads_PropagateCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _store.ResolveTargetAsync(Guid.NewGuid(), cancellation.Token));
    }

    private async Task<SeededWork> SeedWorkAsync(string title, string filePath, string mediaType)
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        using var connection = _database.CreateConnection();
        await connection.ExecuteAsync("""
            INSERT INTO works (id, media_type, work_kind, ownership)
            VALUES (@workId, @mediaType, 'standalone', 'Owned');
            INSERT INTO editions (id, work_id, format_label)
            VALUES (@editionId, @workId, 'Test');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
            VALUES (@assetId, @editionId, @contentHash, @filePath, 'Normal');
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
            VALUES (@assetId, 'title', @title, @now);
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
            VALUES (@assetId, 'media_type', @mediaType, @now);
            """, new
        {
            workId,
            editionId,
            assetId,
            mediaType,
            contentHash = Guid.NewGuid().ToString("N"),
            filePath,
            title,
            now = DateTimeOffset.UtcNow.ToString("O"),
        });
        return new SeededWork(workId, editionId, assetId);
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private sealed record SeededWork(Guid WorkId, Guid EditionId, Guid AssetId);
}
