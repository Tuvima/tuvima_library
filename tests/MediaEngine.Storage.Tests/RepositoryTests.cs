using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Storage.Tests;

/// <summary>
/// Repository tests using a real SQLite database per test class instance.
/// </summary>
public sealed class RepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public RepositoryTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_repo_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MediaAssetRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MediaAsset_InsertAndFindByHash()
    {
        var repo = new MediaAssetRepository(_db);
        var hash = $"hash_{Guid.NewGuid():N}";
        var editionId = await CreateTestEditionAsync();

        var asset = new MediaAsset
        {
            Id           = Guid.NewGuid(),
            EditionId    = editionId,
            ContentHash  = hash,
            FilePathRoot = "/library/Books/test.epub",
            Status       = AssetStatus.Normal,
        };

        await repo.InsertAsync(asset);
        var found = await repo.FindByHashAsync(hash);

        Assert.NotNull(found);
        Assert.Equal(asset.Id, found.Id);
        Assert.Equal(hash, found.ContentHash);
        Assert.Equal(AssetStatus.Normal, found.Status);
    }

    [Fact]
    public async Task MediaAsset_FindByHash_ReturnsNull_WhenNotFound()
    {
        var repo = new MediaAssetRepository(_db);
        var found = await repo.FindByHashAsync("nonexistent_hash_999");

        Assert.Null(found);
    }

    [Fact]
    public async Task MediaAsset_DuplicateHash_Ignored()
    {
        var repo = new MediaAssetRepository(_db);
        var hash = $"dup_{Guid.NewGuid():N}";
        var editionId = await CreateTestEditionAsync();

        var asset1 = new MediaAsset
        {
            Id = Guid.NewGuid(), EditionId = editionId, ContentHash = hash,
            FilePathRoot = "/first.epub", Status = AssetStatus.Normal,
        };
        var asset2 = new MediaAsset
        {
            Id = Guid.NewGuid(), EditionId = editionId, ContentHash = hash,
            FilePathRoot = "/second.epub", Status = AssetStatus.Normal,
        };

        await repo.InsertAsync(asset1);
        await repo.InsertAsync(asset2); // INSERT OR IGNORE

        var found = await repo.FindByHashAsync(hash);
        Assert.Equal(asset1.Id, found!.Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MetadataClaimRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MetadataClaim_InsertAndRetrieve()
    {
        var repo = new MetadataClaimRepository(_db);
        var entityId = Guid.NewGuid();
        var providerId = await CreateTestProviderAsync();
        var claim = MakeClaim(entityId, "title", "Dune", 0.95, providerId);

        await repo.InsertBatchAsync([claim]);
        var claims = await repo.GetByEntityAsync(entityId);

        Assert.Single(claims);
        Assert.Equal("Dune", claims[0].ClaimValue);
        Assert.Equal(0.95, claims[0].Confidence);
    }

    [Fact]
    public async Task MetadataClaim_MultipleClaims_AllReturned()
    {
        var repo = new MetadataClaimRepository(_db);
        var entityId = Guid.NewGuid();
        var providerId = await CreateTestProviderAsync();

        await repo.InsertBatchAsync([MakeClaim(entityId, "title", "Dune", 0.9, providerId)]);
        await repo.InsertBatchAsync([MakeClaim(entityId, "author", "Frank Herbert", 0.9, providerId)]);
        await repo.InsertBatchAsync([MakeClaim(entityId, "year", "1965", 0.9, providerId)]);

        var claims = await repo.GetByEntityAsync(entityId);
        Assert.Equal(3, claims.Count);
    }

    [Fact]
    public async Task MetadataClaim_UserLockedFlag_Preserved()
    {
        var repo = new MetadataClaimRepository(_db);
        var entityId = Guid.NewGuid();
        var providerId = await CreateTestProviderAsync();
        var claim = MakeClaim(entityId, "title", "My Title", 0.9, providerId);
        claim.IsUserLocked = true;

        await repo.InsertBatchAsync([claim]);
        var claims = await repo.GetByEntityAsync(entityId);

        Assert.Single(claims);
        Assert.True(claims[0].IsUserLocked);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CanonicalValueRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CanonicalValue_UpsertAndRetrieve()
    {
        var repo = new CanonicalValueRepository(_db);
        var entityId = Guid.NewGuid();

        await repo.UpsertBatchAsync([new CanonicalValue
        {
            EntityId = entityId, Key = "title", Value = "Dune",
            LastScoredAt = DateTimeOffset.UtcNow,
        }]);

        var values = await repo.GetByEntityAsync(entityId);
        Assert.Single(values);
        Assert.Equal("Dune", values[0].Value);
    }

    [Fact]
    public async Task CanonicalValue_Upsert_UpdatesExisting()
    {
        var repo = new CanonicalValueRepository(_db);
        var entityId = Guid.NewGuid();

        await repo.UpsertBatchAsync([new CanonicalValue
        {
            EntityId = entityId, Key = "title", Value = "Old",
            LastScoredAt = DateTimeOffset.UtcNow,
        }]);
        await repo.UpsertBatchAsync([new CanonicalValue
        {
            EntityId = entityId, Key = "title", Value = "New",
            LastScoredAt = DateTimeOffset.UtcNow,
        }]);

        var values = await repo.GetByEntityAsync(entityId);
        Assert.Single(values);
        Assert.Equal("New", values[0].Value);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ReviewQueueRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReviewQueue_InsertAndGetPending()
    {
        var repo = new ReviewQueueRepository(_db);
        var entry = new ReviewQueueEntry
        {
            Id = Guid.NewGuid(), EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset), Trigger = ReviewTrigger.LowConfidence,
            Status = ReviewStatus.Pending, ConfidenceScore = 0.45,
            Detail = "Low confidence file", CreatedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(entry);
        var pending = await repo.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(entry.Id, pending[0].Id);
    }

    [Fact]
    public async Task ReviewQueue_Resolve_RemovesFromPending()
    {
        var repo = new ReviewQueueRepository(_db);
        var entry = new ReviewQueueEntry
        {
            Id = Guid.NewGuid(), EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset), Trigger = ReviewTrigger.LowConfidence,
            Status = ReviewStatus.Pending, ConfidenceScore = 0.45,
            Detail = "Test", CreatedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(entry);
        await repo.UpdateStatusAsync(entry.Id, ReviewStatus.Resolved);

        var pending = await repo.GetPendingAsync();
        Assert.Empty(pending);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ApiKeyRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApiKey_InsertAndFindByHash()
    {
        var repo = new ApiKeyRepository(_db);
        var key = new ApiKey
        {
            Id = Guid.NewGuid(), Label = "Test Key",
            HashedKey = $"hash_{Guid.NewGuid():N}",
            Role = "Administrator", CreatedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(key);
        var found = await repo.FindByHashedKeyAsync(key.HashedKey);

        Assert.NotNull(found);
        Assert.Equal(key.Id, found.Id);
        Assert.Equal("Administrator", found.Role);
    }

    [Fact]
    public async Task ApiKey_Revoke_RemovesKey()
    {
        var repo = new ApiKeyRepository(_db);
        var key = new ApiKey
        {
            Id = Guid.NewGuid(), Label = "Revoke Me",
            HashedKey = $"revoke_{Guid.NewGuid():N}",
            Role = "Consumer", CreatedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(key);
        await repo.DeleteAsync(key.Id);

        var found = await repo.FindByHashedKeyAsync(key.HashedKey);
        Assert.Null(found);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SystemActivityRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SystemActivity_LogAndRetrieve()
    {
        var repo = new SystemActivityRepository(_db);

        await repo.LogAsync(new SystemActivityEntry
        {
            ActionType = SystemActionType.FileIngested,
            Detail = "Ingested test.epub", OccurredAt = DateTimeOffset.UtcNow,
        });

        var recent = await repo.GetRecentAsync(10);
        Assert.Single(recent);
        Assert.Equal(SystemActionType.FileIngested, recent[0].ActionType);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DatabaseConnection — schema + migrations
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DatabaseConnection_SchemaCreation_Succeeds()
    {
        // If constructor + InitializeSchema + RunStartupChecks succeeded, schema is valid.
        Assert.True(true);
    }

    [Fact]
    public void DatabaseConnection_SecondInit_IsIdempotent()
    {
        _db.InitializeSchema();
        _db.RunStartupChecks();
        Assert.True(true);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TransactionJournal
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TransactionJournal_LogAndPrune()
    {
        var journal = new TransactionJournal(_db);
        journal.Log("HUB_CREATED", "Collection", Guid.NewGuid().ToString());
        journal.Log("WORK_AUTO_LINKED", "Work", Guid.NewGuid().ToString());
        journal.Prune(1);
        Assert.True(true); // no exception = pass
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MetadataClaim MakeClaim(
        Guid entityId, string key, string value, double confidence = 0.9, Guid? providerId = null) => new()
    {
        Id = Guid.NewGuid(), EntityId = entityId, ProviderId = providerId ?? Guid.NewGuid(),
        ClaimKey = key, ClaimValue = value, Confidence = confidence,
        ClaimedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Creates a Collection → Work → Edition chain in the database and returns the Edition ID.
    /// Required to satisfy the FK constraint on media_assets.edition_id.
    /// </summary>
    private async Task<Guid> CreateTestEditionAsync()
    {
        using var conn = _db.CreateConnection();
        var collectionId     = Guid.NewGuid();
        var workId    = Guid.NewGuid();
        var editionId = Guid.NewGuid();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO collections (id, created_at) VALUES ('{collectionId}', datetime('now'));
            INSERT INTO works (id, collection_id, media_type) VALUES ('{workId}', '{collectionId}', 'Epub');
            INSERT INTO editions (id, work_id) VALUES ('{editionId}', '{workId}');
            """;
        await cmd.ExecuteNonQueryAsync();
        return editionId;
    }

    /// <summary>
    /// Inserts a test provider into provider_registry and returns its ID.
    /// Required to satisfy the FK constraint on metadata_claims.provider_id.
    /// </summary>
    private async Task<Guid> CreateTestProviderAsync()
    {
        using var conn = _db.CreateConnection();
        var providerId = Guid.NewGuid();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO provider_registry (id, name, version) VALUES ('{providerId}', 'test-provider-{providerId:N}', '1.0')";
        await cmd.ExecuteNonQueryAsync();
        return providerId;
    }
}

/// <summary>
/// Tests for <see cref="CollectionRuleEvaluator.ComputeRuleHash"/> — verifies that
/// the discriminator predicate technique produces distinct hashes for collections
/// with identical rules but different group_by_field values.
/// </summary>
public sealed class CollectionRuleEvaluatorHashTests
{
    [Fact]
    public void SameRules_SameHash()
    {
        var rules1 = new CollectionRulePredicate[] { new() { Field = "media_type", Op = "eq", Value = "Books" } };
        var rules2 = new CollectionRulePredicate[] { new() { Field = "media_type", Op = "eq", Value = "Books" } };

        var hash1 = CollectionRuleEvaluator.ComputeRuleHash(rules1);
        var hash2 = CollectionRuleEvaluator.ComputeRuleHash(rules2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void SameRules_DifferentGroupBy_DifferentHash()
    {
        // "All Books" (no group_by) and "Books by Series" (group_by=series)
        // should produce different hashes when a _group_by discriminator is added.
        var baseRules = new CollectionRulePredicate[] { new() { Field = "media_type", Op = "eq", Value = "Books" } };

        var hashFlat = CollectionRuleEvaluator.ComputeRuleHash(baseRules);
        var hashGrouped = CollectionRuleEvaluator.ComputeRuleHash(
            [..baseRules, new CollectionRulePredicate { Field = "_group_by", Op = "eq", Value = "series" }]);

        Assert.NotEqual(hashFlat, hashGrouped);
    }

    [Fact]
    public void DifferentGroupByFields_DifferentHashes()
    {
        // "Music by Artist" and "Music by Album" should produce different hashes.
        var baseRules = new CollectionRulePredicate[] { new() { Field = "media_type", Op = "eq", Value = "Music" } };

        var hashArtist = CollectionRuleEvaluator.ComputeRuleHash(
            [..baseRules, new CollectionRulePredicate { Field = "_group_by", Op = "eq", Value = "artist" }]);
        var hashAlbum = CollectionRuleEvaluator.ComputeRuleHash(
            [..baseRules, new CollectionRulePredicate { Field = "_group_by", Op = "eq", Value = "album" }]);

        Assert.NotEqual(hashArtist, hashAlbum);
    }

    [Fact]
    public void SameGroupBy_SameHash_Idempotent()
    {
        var rules = new CollectionRulePredicate[]
        {
            new() { Field = "media_type", Op = "eq", Value = "Books" },
            new() { Field = "_group_by", Op = "eq", Value = "series" },
        };

        var hash1 = CollectionRuleEvaluator.ComputeRuleHash(rules);
        var hash2 = CollectionRuleEvaluator.ComputeRuleHash(rules);

        Assert.Equal(hash1, hash2);
    }
}
