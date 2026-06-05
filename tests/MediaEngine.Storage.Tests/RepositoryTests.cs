using Dapper;
using Microsoft.Data.Sqlite;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Services;

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

    [Fact]
    public async Task MediaAsset_UpdateContentHash_RefreshesKnownPathAsset()
    {
        var repo = new MediaAssetRepository(_db);
        var editionId = await CreateTestEditionAsync();
        var oldHash = $"old_{Guid.NewGuid():N}";
        var newHash = $"new_{Guid.NewGuid():N}";
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            EditionId = editionId,
            ContentHash = oldHash,
            FilePathRoot = "/library/Books/changed-after-writeback.epub",
            Status = AssetStatus.Normal,
        };

        await repo.InsertAsync(asset);

        Assert.True(await repo.UpdateContentHashAsync(asset.Id, newHash));

        Assert.Null(await repo.FindByHashAsync(oldHash));
        var found = await repo.FindByHashAsync(newHash);
        Assert.NotNull(found);
        Assert.Equal(asset.Id, found.Id);
        Assert.Equal(asset.FilePathRoot, found.FilePathRoot);
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

    [Fact]
    public async Task MetadataClaim_UserManualProvider_IsSelfHealingWhenSeedMissing()
    {
        using (var conn = _db.CreateConnection())
        {
            await conn.ExecuteAsync(
                "DELETE FROM metadata_providers WHERE id = @id;",
                new { id = WellKnownProviders.UserManual });
        }

        var repo = new MetadataClaimRepository(_db);
        var entityId = Guid.NewGuid();
        var claim = MakeClaim(entityId, "wikidata_qid", "Q155653", 1.0, WellKnownProviders.UserManual);
        claim.IsUserLocked = true;

        await repo.InsertBatchAsync([claim]);

        var claims = await repo.GetByEntityAsync(entityId);
        Assert.Single(claims);
        Assert.Equal(WellKnownProviders.UserManual, claims[0].ProviderId);

        using var verifyConn = _db.CreateConnection();
        var providerCount = await verifyConn.ExecuteScalarAsync<long>(
            "SELECT COUNT(1) FROM metadata_providers WHERE id = @id;",
            new { id = WellKnownProviders.UserManual });
        Assert.Equal(1, providerCount);
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
    public async Task CanonicalValue_BatchLookup_UsesGuidBlobEntityIds()
    {
        var repo = new CanonicalValueRepository(_db);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await repo.UpsertBatchAsync([
            new CanonicalValue
            {
                EntityId = first,
                Key = "title",
                Value = "Le Petit Prince",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new CanonicalValue
            {
                EntityId = second,
                Key = "title",
                Value = "La Vie en rose",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        ]);

        var values = await repo.GetByEntitiesAsync([first, second]);

        using var conn = _db.CreateConnection();
        var storageTypes = conn.Query<string>("""
            SELECT typeof(entity_id) AS EntityIdType
            FROM canonical_values
            WHERE entity_id IN (@first, @second)
            ORDER BY value;
            """, new { first, second }).ToList();

        Assert.True(values.ContainsKey(first));
        Assert.True(values.ContainsKey(second));
        Assert.Equal("Le Petit Prince", Assert.Single(values[first]).Value);
        Assert.Equal("La Vie en rose", Assert.Single(values[second]).Value);
        Assert.Equal(2, storageTypes.Count);
        Assert.All(storageTypes, storageType => Assert.Equal("blob", storageType));
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
            Key = "title",
            Value = "Dune",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new CanonicalValue
            {
                EntityId = second,
            Key = "title",
            Value = "Foundation",
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

        var values = await repo.FindByKeyAndPrefixAsync("title", "");

        Assert.Equal(2, values.Count);
        Assert.Contains(values, item => item.EntityId == first && item.Value == "Dune");
        Assert.Contains(values, item => item.EntityId == second && item.Value == "Foundation");
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

        var discArtId = Guid.NewGuid();
        var clearArtId = Guid.NewGuid();

        await repo.UpsertAsync(new EntityAsset
        {
            Id = discArtId,
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
            Id = clearArtId,
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

        using var conn = _db.CreateConnection();
        var storageTypes = conn.Query<string>(
            """
            SELECT typeof(entity_id)
            FROM entity_assets
            WHERE id = @discArtId OR id = @clearArtId
            ORDER BY asset_type;
            """,
            new { discArtId, clearArtId }).ToList();
        Assert.Equal(2, storageTypes.Count);
        Assert.All(storageTypes, type => Assert.Equal("blob", type));
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
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(entry);
        var pending = await repo.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(entry.Id, pending[0].Id);
    }

    [Fact]
    public async Task ReviewQueue_Insert_IsIdempotentForPendingEntityAndTrigger()
    {
        var repo = new ReviewQueueRepository(_db);
        var entityId = Guid.NewGuid();
        var first = new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            Trigger = ReviewTrigger.RetailMatchAmbiguous,
            Status = ReviewStatus.Pending,
            ConfidenceScore = 0.42,
            Detail = "First ambiguous match",
            CreatedAt = DateTimeOffset.UtcNow,
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = DateTimeOffset.UtcNow,
        };
        var second = new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            Trigger = ReviewTrigger.RetailMatchAmbiguous,
            Status = ReviewStatus.Pending,
            ConfidenceScore = 0.61,
            Detail = "Refreshed ambiguous match",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            ReviewReadyAt = DateTimeOffset.UtcNow.AddMinutes(1),
            AutomationCompletedAt = DateTimeOffset.UtcNow.AddMinutes(1),
        };

        var firstId = await repo.InsertAsync(first);
        var secondId = await repo.InsertAsync(second);

        Assert.Equal(firstId, secondId);
        var pending = await repo.GetPendingByEntityAsync(entityId);
        var row = Assert.Single(pending);
        Assert.Equal(first.Id, row.Id);
        Assert.Equal("Refreshed ambiguous match", row.Detail);
        Assert.Equal(0.61, row.ConfidenceScore);
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
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = DateTimeOffset.UtcNow,
        };

        await repo.InsertAsync(entry);
        await repo.UpdateStatusAsync(entry.Id, ReviewStatus.Resolved);

        var pending = await repo.GetPendingAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task ReviewQueue_GetPendingAndCount_ExcludeRowsNotReadyForReview()
    {
        var repo = new ReviewQueueRepository(_db);
        var assetRepo = new MediaAssetRepository(_db);
        var editionId = await CreateTestEditionAsync();
        var readyAssetId = Guid.NewGuid();
        var queuedAssetId = Guid.NewGuid();

        await assetRepo.InsertAsync(new MediaAsset
        {
            Id = readyAssetId,
            EditionId = editionId,
            ContentHash = $"ready_{Guid.NewGuid():N}",
            FilePathRoot = "/library/ready.epub",
            Status = AssetStatus.Normal,
        });
        await assetRepo.InsertAsync(new MediaAsset
        {
            Id = queuedAssetId,
            EditionId = editionId,
            ContentHash = $"queued_{Guid.NewGuid():N}",
            FilePathRoot = "/library/queued.epub",
            Status = AssetStatus.Normal,
        });

        await repo.InsertAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = readyAssetId,
            EntityType = nameof(EntityType.MediaAsset),
            Trigger = ReviewTrigger.LowConfidence,
            Status = ReviewStatus.Pending,
            Detail = "Automation complete",
            CreatedAt = DateTimeOffset.UtcNow,
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = DateTimeOffset.UtcNow,
        });
        await repo.InsertAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = queuedAssetId,
            EntityType = nameof(EntityType.MediaAsset),
            Trigger = ReviewTrigger.LowConfidence,
            Status = ReviewStatus.Pending,
            Detail = "Still queued",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var pending = await repo.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(readyAssetId, pending[0].EntityId);
        Assert.Single(await repo.GetPendingByEntityAsync(queuedAssetId));
        Assert.Equal(1, await repo.GetPendingCountAsync());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ApiKeyRepository
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReviewQueue_MarkPendingReadyByEntity_PromotesHiddenPendingRows()
    {
        var repo = new ReviewQueueRepository(_db);
        var assetRepo = new MediaAssetRepository(_db);
        var editionId = await CreateTestEditionAsync();
        var assetId = Guid.NewGuid();

        await assetRepo.InsertAsync(new MediaAsset
        {
            Id = assetId,
            EditionId = editionId,
            ContentHash = $"mark_ready_{Guid.NewGuid():N}",
            FilePathRoot = "/library/mark-ready.epub",
            Status = AssetStatus.Normal,
        });

        await repo.InsertAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = assetId,
            EntityType = nameof(EntityType.MediaAsset),
            Trigger = ReviewTrigger.LowConfidence,
            Status = ReviewStatus.Pending,
            Detail = "Still in automation",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Assert.Empty(await repo.GetPendingAsync());

        var promoted = await repo.MarkPendingReadyByEntityAsync(assetId);

        Assert.Equal(1, promoted);
        var pendingAfterPromotion = await repo.GetPendingAsync();
        Assert.Single(pendingAfterPromotion);
        Assert.Equal(assetId, pendingAfterPromotion[0].EntityId);
        Assert.NotNull(pendingAfterPromotion[0].ReviewReadyAt);
        Assert.NotNull(pendingAfterPromotion[0].AutomationCompletedAt);
        Assert.Equal(0, await repo.MarkPendingReadyByEntityAsync(assetId));
    }

    [Fact]
    public async Task WorkIdentityReconciliation_MergesDuplicateReadWorksByQid()
    {
        var service = new WorkIdentityReconciliationService(_db);
        var qid = "Q43361";
        var bookWorkId = Guid.NewGuid();
        var audiobookWorkId = Guid.NewGuid();
        var bookEditionId = Guid.NewGuid();
        var audiobookEditionId = Guid.NewGuid();
        var bookAssetId = Guid.NewGuid();
        var audiobookAssetId = Guid.NewGuid();

        using (var conn = _db.CreateConnection())
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO works (id, media_type, work_kind, wikidata_qid)
                VALUES (@bookWorkId, 'Books', 'standalone', @qid),
                       (@audiobookWorkId, 'Audiobooks', 'standalone', @qid);

                INSERT INTO editions (id, work_id)
                VALUES (@bookEditionId, @bookWorkId),
                       (@audiobookEditionId, @audiobookWorkId);

                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
                VALUES (@bookAssetId, @bookEditionId, @bookHash, '/library/Books/Harry Potter.epub', 'Normal'),
                       (@audiobookAssetId, @audiobookEditionId, @audioHash, '/library/Audiobooks/Harry Potter.m4b', 'Normal');
                """,
                new
                {
                    qid,
                    bookWorkId,
                    audiobookWorkId,
                    bookEditionId,
                    audiobookEditionId,
                    bookAssetId,
                    audiobookAssetId,
                    bookHash = $"book_{Guid.NewGuid():N}",
                    audioHash = $"audio_{Guid.NewGuid():N}",
                });
        }

        var merged = await service.MergeDuplicateReadWorksByQidAsync();

        Assert.Equal(1, merged);
        using var verify = _db.CreateConnection();
        var workCount = await verify.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM works WHERE wikidata_qid = @qid;",
            new { qid });
        var remainingWorkId = await verify.ExecuteScalarAsync<Guid>(
            "SELECT id FROM works WHERE wikidata_qid = @qid;",
            new { qid });
        var editionWorkIds = (await verify.QueryAsync<Guid>(
            "SELECT DISTINCT work_id FROM editions WHERE id IN (@bookEditionId, @audiobookEditionId);",
            new { bookEditionId, audiobookEditionId })).ToList();

        Assert.Equal(1, workCount);
        Assert.Equal(bookWorkId, remainingWorkId);
        Assert.Single(editionWorkIds);
        Assert.Equal(bookWorkId, editionWorkIds[0]);
    }

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
    public async Task IdentityJob_CreateAsync_AllowsRetryAfterTerminalReviewState()
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
        Assert.Single(queued, j => j.EntityId == entityId && j.Pass == "Quick");
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
    public async Task IdentityJob_ReclaimStuckJobsAsync_FailsRetryExhaustedIntermediateJobs()
    {
        var repo = new IdentityJobRepository(_db);
        var job = new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Books),
            Pass = "Quick",
            State = IdentityJobState.UniverseEnriching.ToString(),
        };
        await repo.CreateAsync(job);

        using (var conn = _db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE identity_jobs
                SET attempt_count = 5,
                    updated_at = @updatedAt
                WHERE id = @id;
            """;
            cmd.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"));
            cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(job.Id);
            cmd.ExecuteNonQuery();
        }

        var reclaimed = await repo.ReclaimStuckJobsAsync(TimeSpan.FromMinutes(5));
        var failed = await repo.GetByIdAsync(job.Id);

        Assert.Equal(1, reclaimed);
        Assert.NotNull(failed);
        Assert.Equal(IdentityJobState.Failed.ToString(), failed!.State);
        Assert.Equal("Stuck intermediate state exceeded retry limit", failed.LastError);
    }

    [Fact]
    public async Task IdentityJob_ReclaimStuckJobsAsync_RecoversFailedStage3EnhancerCandidates()
    {
        var repo = new IdentityJobRepository(_db);
        var canonicalRepo = new CanonicalValueRepository(_db);
        var entityId = Guid.NewGuid();
        var job = new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Movies),
            Pass = "Quick",
            State = IdentityJobState.Failed.ToString(),
            LastError = "Stuck intermediate state exceeded retry limit",
        };
        await repo.CreateAsync(job);
        await canonicalRepo.UpsertBatchAsync(
        [
            new CanonicalValue { EntityId = entityId, Key = "wikidata_qid", Value = "Q123", LastScoredAt = DateTimeOffset.UtcNow },
            new CanonicalValue { EntityId = entityId, Key = "tmdb_movie_id", Value = "123", LastScoredAt = DateTimeOffset.UtcNow },
        ]);

        var reclaimed = await repo.ReclaimStuckJobsAsync(TimeSpan.FromMinutes(5));
        var recovered = await repo.GetByIdAsync(job.Id);

        Assert.Equal(1, reclaimed);
        Assert.NotNull(recovered);
        Assert.Equal(IdentityJobState.QidResolved.ToString(), recovered!.State);
        Assert.Equal(0, recovered.AttemptCount);
        Assert.Equal("Recovered for Stage 3 artwork/enhancer retry", recovered.LastError);
    }

    [Fact]
    public async Task IdentityJob_RecoverInterruptedJobsAsync_ReleasesStartupLeasesImmediately()
    {
        var repo = new IdentityJobRepository(_db);
        var job = new IdentityJob
        {
            Id = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = nameof(EntityType.MediaAsset),
            MediaType = nameof(MediaType.Movies),
            Pass = "Quick",
            State = IdentityJobState.RetailMatched.ToString(),
        };
        await repo.CreateAsync(job);

        var leased = await repo.LeaseNextAsync(
            "previous-engine",
            [IdentityJobState.RetailMatched],
            1,
            TimeSpan.FromMinutes(10));
        Assert.Single(leased);

        await repo.UpdateStateAsync(job.Id, IdentityJobState.BridgeSearching);

        var recoveredCount = await repo.RecoverInterruptedJobsAsync();
        var recovered = await repo.GetByIdAsync(job.Id);

        Assert.Equal(1, recoveredCount);
        Assert.NotNull(recovered);
        Assert.Equal(IdentityJobState.RetailMatched.ToString(), recovered!.State);
        Assert.Null(recovered.LeaseOwner);
        Assert.Null(recovered.LeaseExpiresAt);
        Assert.Equal("Recovered after engine restart", recovered.LastError);
    }

    [Fact]
    public async Task ReviewQueue_PendingCountAndPurge_UseGuidBlobEntityIds()
    {
        var assetRepo = new MediaAssetRepository(_db);
        var repo = new ReviewQueueRepository(_db);
        var editionId = await CreateTestEditionAsync();
        var assetId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        await assetRepo.InsertAsync(new MediaAsset
        {
            Id = assetId,
            EditionId = editionId,
            ContentHash = $"review_{Guid.NewGuid():N}",
            FilePathRoot = "/library/review.epub",
            Status = AssetStatus.Normal,
        });

        await repo.InsertAsync(new ReviewQueueEntry
        {
            Id = Guid.NewGuid(),
            EntityId = assetId,
            EntityType = nameof(EntityType.MediaAsset),
            Trigger = ReviewTrigger.LowConfidence,
            Status = ReviewStatus.Pending,
            SourceOperationId = operationId,
            ReviewReadyAt = DateTimeOffset.UtcNow,
            AutomationCompletedAt = DateTimeOffset.UtcNow,
        });

        using var conn = _db.CreateConnection();
        var storageTypes = conn.QuerySingle<(string IdType, string EntityIdType, string SourceOperationIdType)>("""
            SELECT typeof(id) AS IdType,
                   typeof(entity_id) AS EntityIdType,
                   typeof(source_operation_id) AS SourceOperationIdType
            FROM review_queue
            WHERE entity_id = @assetId;
            """, new { assetId });

        Assert.Equal("blob", storageTypes.IdType);
        Assert.Equal("blob", storageTypes.EntityIdType);
        Assert.Equal("blob", storageTypes.SourceOperationIdType);
        Assert.Equal(1, await repo.GetPendingCountAsync());
        Assert.Equal(0, await repo.PurgeOrphanedAsync());
        Assert.Single(await repo.GetPendingByEntityAsync(assetId));
    }

    [Fact]
    public async Task IngestionBatchLogAndActivity_UseGuidBlobRunIds()
    {
        var batchRepo = new IngestionBatchRepository(_db);
        var logRepo = new IngestionLogRepository(_db);
        var activityRepo = new SystemActivityRepository(_db);
        var batchId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await batchRepo.CreateAsync(new IngestionBatch
        {
            Id = batchId,
            Status = "running",
            SourcePath = "/library/import",
            FilesTotal = 1,
        });

        await logRepo.InsertAsync(new IngestionLogEntry
        {
            Id = Guid.NewGuid(),
            FilePath = "/library/import/book.epub",
            MediaAssetId = assetId,
            Status = "queued_identity",
            IngestionRunId = batchId,
        });

        await activityRepo.LogAsync(new SystemActivityEntry
        {
            ActionType = SystemActionType.FileIngested,
            EntityId = assetId,
            ProfileId = Profile.SeedProfileId,
            IngestionRunId = batchId,
        });

        using var conn = _db.CreateConnection();
        var batchType = conn.ExecuteScalar<string>("SELECT typeof(id) FROM ingestion_batches WHERE id = @batchId;", new { batchId });
        var logTypes = conn.QuerySingle<(string IdType, string AssetType, string RunType)>("""
            SELECT typeof(id) AS IdType,
                   typeof(media_asset_id) AS AssetType,
                   typeof(ingestion_run_id) AS RunType
            FROM ingestion_log
            WHERE ingestion_run_id = @batchId;
            """, new { batchId });
        var activityTypes = conn.QuerySingle<(string EntityType, string ProfileType, string RunType)>("""
            SELECT typeof(entity_id) AS EntityType,
                   typeof(profile_id) AS ProfileType,
                   typeof(ingestion_run_id) AS RunType
            FROM system_activity
            WHERE ingestion_run_id = @batchId;
            """, new { batchId });

        Assert.Equal("blob", batchType);
        Assert.Equal("blob", logTypes.IdType);
        Assert.Equal("blob", logTypes.AssetType);
        Assert.Equal("blob", logTypes.RunType);
        Assert.Equal("blob", activityTypes.EntityType);
        Assert.Equal("blob", activityTypes.ProfileType);
        Assert.Equal("blob", activityTypes.RunType);
        Assert.NotNull(await batchRepo.GetByIdAsync(batchId));
        Assert.Single(await logRepo.GetByRunIdAsync(batchId));
        Assert.Single(await activityRepo.GetByRunIdAsync(batchId));
    }

    [Fact]
    public async Task IngestionBatch_GetRecent_IncludesAbandonedTerminalBatches()
    {
        var batchRepo = new IngestionBatchRepository(_db);
        var batchId = Guid.NewGuid();

        await batchRepo.CreateAsync(new IngestionBatch
        {
            Id = batchId,
            Status = "abandoned",
            SourcePath = "/library/interrupted",
            FilesTotal = 5,
            FilesProcessed = 5,
            FilesFailed = 5,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var recent = await batchRepo.GetRecentAsync(10);

        var batch = Assert.Single(recent);
        Assert.Equal(batchId, batch.Id);
        Assert.Equal("abandoned", batch.Status);
        Assert.Equal(5, batch.FilesFailed);
    }

    [Fact]
    public async Task BridgeId_UpsertAndBatchLookup_UseGuidBlobEntityIds()
    {
        var repo = new BridgeIdRepository(_db);
        var entityId = Guid.NewGuid();

        await repo.UpsertAsync(new BridgeIdEntry
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            IdType = BridgeIdKeys.WikidataQid,
            IdValue = "Q123",
            ProviderId = "wikidata",
        });

        var byEntity = await repo.GetByEntityAsync(entityId);
        var byEntities = await repo.GetByEntitiesAsync([entityId]);

        using var conn = _db.CreateConnection();
        var storageTypes = conn.QuerySingle<(string IdType, string EntityIdType, string ProviderType)>("""
            SELECT typeof(id) AS IdType,
                   typeof(entity_id) AS EntityIdType,
                   typeof(provider_id) AS ProviderType
            FROM bridge_ids
            WHERE entity_id = @entityId;
            """, new { entityId });

        Assert.Single(byEntity);
        Assert.True(byEntities.ContainsKey(entityId));
        Assert.Equal("blob", storageTypes.IdType);
        Assert.Equal("blob", storageTypes.EntityIdType);
        Assert.Equal("text", storageTypes.ProviderType);
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

        await conn.ExecuteAsync(
            """
            INSERT INTO collections (id, created_at) VALUES (@collectionId, datetime('now'));
            INSERT INTO works (id, collection_id, media_type) VALUES (@workId, @collectionId, 'Epub');
            INSERT INTO editions (id, work_id) VALUES (@editionId, @workId);
            """,
            new { collectionId, workId, editionId });
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
        await conn.ExecuteAsync(
            "INSERT INTO metadata_providers (id, name, version) VALUES (@id, @name, '1.0')",
            new { id = providerId, name = $"test-provider-{providerId:N}" });
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
