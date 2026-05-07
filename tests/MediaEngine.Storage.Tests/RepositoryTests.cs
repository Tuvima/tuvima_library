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
    public async Task ProfileExternalLogin_InsertFindTouchAndDelete()
    {
        var repo = new ProfileExternalLoginRepository(_db);
        var login = new ProfileExternalLogin
        {
            Id = Guid.NewGuid(),
            ProfileId = Profile.SeedProfileId,
            Provider = "oidc",
            Subject = $"subject-{Guid.NewGuid():N}",
            Email = "owner@example.com",
            DisplayName = "Owner",
            LinkedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(login);

        var byProfile = await repo.GetByProfileAsync(Profile.SeedProfileId);
        var bySubject = await repo.GetByProviderSubjectAsync(login.Provider, login.Subject);

        Assert.Contains(byProfile, item => item.Id == login.Id);
        Assert.NotNull(bySubject);
        Assert.Equal(login.Email, bySubject.Email);

        var lastLoginAt = DateTimeOffset.UtcNow.AddMinutes(1);
        Assert.True(await repo.TouchLastLoginAsync(login.Id, lastLoginAt));

        var touched = await repo.GetByProviderSubjectAsync(login.Provider, login.Subject);
        Assert.NotNull(touched?.LastLoginAt);

        Assert.True(await repo.DeleteAsync(login.Id));
        Assert.Null(await repo.GetByProviderSubjectAsync(login.Provider, login.Subject));
    }

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

    [Fact]
    public async Task CanonicalValue_FindByKeyAndPrefix_EmptyPrefixReturnsAllValuesForKey()
    {
        var repo = new CanonicalValueRepository(_db);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var otherKey = Guid.NewGuid();

        await repo.UpsertBatchAsync([
            new CanonicalValue
            {
                EntityId = first,
                Key = "genre",
                Value = "Science Fiction",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new CanonicalValue
            {
                EntityId = second,
                Key = "genre",
                Value = "Fantasy",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new CanonicalValue
            {
                EntityId = otherKey,
                Key = "media_type",
                Value = "Book",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        ]);

        var values = await repo.FindByKeyAndPrefixAsync("genre", "");

        Assert.Equal(2, values.Count);
        Assert.Contains(values, item => item.EntityId == first && item.Value == "Science Fiction");
        Assert.Contains(values, item => item.EntityId == second && item.Value == "Fantasy");
        Assert.DoesNotContain(values, item => item.EntityId == otherKey);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ReviewQueueRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EntityAsset_Upsert_AllowsDiscArtAndClearArtTypes()
    {
        var repo = new EntityAssetRepository(_db);
        var entityId = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;

        await repo.UpsertAsync(new EntityAsset
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = "Work",
            AssetTypeValue = "DiscArt",
            LocalImagePath = "/library/Movies/Arrival/discart.png",
            SourceProvider = "fanart_tv",
            AssetClassValue = "Artwork",
            StorageLocationValue = "Central",
            OwnerScope = "Work",
            IsPreferred = true,
            CreatedAt = createdAt,
        });

        await repo.UpsertAsync(new EntityAsset
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = "Work",
            AssetTypeValue = "ClearArt",
            LocalImagePath = "/library/Movies/Arrival/clearart.png",
            SourceProvider = "fanart_tv",
            AssetClassValue = "Artwork",
            StorageLocationValue = "Central",
            OwnerScope = "Work",
            IsPreferred = true,
            CreatedAt = createdAt,
        });

        var assets = await repo.GetByEntityAsync(entityId);

        Assert.Contains(assets, asset => asset.AssetTypeValue == "DiscArt");
        Assert.Contains(assets, asset => asset.AssetTypeValue == "ClearArt");
    }

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
    public async Task IdentityJob_CreateAsync_IgnoresDuplicateActiveJobForSameEntityAndPass()
    {
        var repo = new IdentityJobRepository(_db);
        var entityId = Guid.NewGuid();

        await repo.CreateAsync(new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Books),
            Pass = "Quick",
            State = IdentityJobState.Queued.ToString(),
        });

        await repo.CreateAsync(new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Books),
            Pass = "Quick",
            State = IdentityJobState.Queued.ToString(),
        });

        var queued = await repo.GetByStateAsync(IdentityJobState.Queued, 10);
        Assert.Single(queued, j => j.EntityId == entityId && j.Pass == "Quick");
    }

    [Fact]
    public async Task IdentityJob_CreateAsync_IgnoresDuplicateRetryTerminalJobForSameEntityAndPass()
    {
        var repo = new IdentityJobRepository(_db);
        var entityId = Guid.NewGuid();

        await repo.CreateAsync(new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Movies),
            Pass = "Quick",
            State = IdentityJobState.RetailNoMatch.ToString(),
        });

        await repo.CreateAsync(new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Movies),
            Pass = "Quick",
            State = IdentityJobState.Queued.ToString(),
        });

        var noMatch = await repo.GetByStateAsync(IdentityJobState.RetailNoMatch, 10);
        var queued = await repo.GetByStateAsync(IdentityJobState.Queued, 10);

        Assert.Single(noMatch, j => j.EntityId == entityId && j.Pass == "Quick");
        Assert.DoesNotContain(queued, j => j.EntityId == entityId && j.Pass == "Quick");
    }

    [Fact]
    public async Task IdentityJob_UpdateStateAsync_PreservesLeaseOnlyForProcessingStates()
    {
        var repo = new IdentityJobRepository(_db);
        var job = new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Books),
            Pass = "Quick",
            State = IdentityJobState.Queued.ToString(),
        };

        await repo.CreateAsync(job);

        var leased = await repo.LeaseNextAsync(
            "test-worker",
            [IdentityJobState.Queued],
            1,
            TimeSpan.FromMinutes(10));

        var leasedJob = Assert.Single(leased);
        Assert.Equal("test-worker", leasedJob.LeaseOwner);

        await repo.UpdateStateAsync(job.Id, IdentityJobState.RetailSearching);
        var searching = await repo.GetByIdAsync(job.Id);

        Assert.NotNull(searching);
        Assert.Equal("test-worker", searching!.LeaseOwner);
        Assert.NotNull(searching.LeaseExpiresAt);
        Assert.Equal(1, searching.AttemptCount);

        await repo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched);
        var matched = await repo.GetByIdAsync(job.Id);

        Assert.NotNull(matched);
        Assert.Null(matched!.LeaseOwner);
        Assert.Null(matched.LeaseExpiresAt);
        Assert.Equal(1, matched.AttemptCount);
    }

    [Fact]
    public async Task IdentityJob_ScheduleRetryAsync_DelaysLeaseAndIncrementsAttempts()
    {
        var repo = new IdentityJobRepository(_db);
        var job = new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Books),
            Pass = "Quick",
            State = IdentityJobState.Queued.ToString(),
        };
        await repo.CreateAsync(job);

        var leased = await repo.LeaseNextAsync("test-worker", [IdentityJobState.Queued], 1, TimeSpan.FromMinutes(10));
        Assert.Single(leased);

        await repo.ScheduleRetryAsync(job.Id, IdentityJobState.Queued, DateTimeOffset.UtcNow.AddMinutes(10), "locked", CancellationToken.None);

        var retrying = await repo.GetByIdAsync(job.Id);
        Assert.NotNull(retrying);
        Assert.Equal(1, retrying!.AttemptCount);
        Assert.Equal("locked", retrying.LastError);
        Assert.NotNull(retrying.NextRetryAt);
        Assert.Null(retrying.LeaseOwner);

        var notEligible = await repo.LeaseNextAsync("test-worker-2", [IdentityJobState.Queued], 1, TimeSpan.FromMinutes(10));
        Assert.Empty(notEligible);
    }

    [Fact]
    public async Task IdentityJob_MarkDeadLetteredAsync_UsesTerminalFailedState()
    {
        var repo = new IdentityJobRepository(_db);
        var job = new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Books),
            Pass = "Quick",
            State = IdentityJobState.Queued.ToString(),
        };
        await repo.CreateAsync(job);

        await repo.MarkDeadLetteredAsync(job.Id, "poison data", CancellationToken.None);

        var failed = await repo.GetByIdAsync(job.Id);
        Assert.NotNull(failed);
        Assert.Equal(IdentityJobState.Failed.ToString(), failed!.State);
        Assert.Equal("poison data", failed.LastError);

        var leased = await repo.LeaseNextAsync("test-worker", [IdentityJobState.Queued], 1, TimeSpan.FromMinutes(10));
        Assert.Empty(leased);
    }

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
    /// Inserts a test provider into metadata_providers and returns its ID.
    /// Required to satisfy the FK constraint on metadata_claims.provider_id.
    /// </summary>
    private async Task<Guid> CreateTestProviderAsync()
    {
        using var conn = _db.CreateConnection();
        var providerId = Guid.NewGuid();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO metadata_providers (id, name, version) VALUES ('{providerId}', 'test-provider-{providerId:N}', '1.0')";
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
