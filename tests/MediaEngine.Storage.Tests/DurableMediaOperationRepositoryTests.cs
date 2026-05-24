using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Tests;

public sealed class DurableMediaOperationRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public DurableMediaOperationRepositoryTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_operations_{Guid.NewGuid():N}.db");
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
    public async Task EnsureAsync_IsIdempotentByIdempotencyKey()
    {
        var repo = new MediaOperationRepository(_db);
        var key = $"ingestion:file:C:/watch/movie.mkv:{Guid.NewGuid():N}";

        var first = await repo.EnsureAsync(NewOperation(key, sourcePath: "C:/watch/movie.mkv"));
        var second = await repo.EnsureAsync(NewOperation(key, sourcePath: "C:/watch/movie-renamed.mkv"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("C:/watch/movie.mkv", second.SourcePath);
        Assert.Single(await repo.GetQueueAsync("ingestion", 10));
    }

    [Fact]
    public async Task LeaseNextAsync_LeasesQueuedRowsAndIgnoresActiveLease()
    {
        var repo = new MediaOperationRepository(_db);
        await repo.EnsureAsync(NewOperation($"ingestion:file:{Guid.NewGuid():N}"));

        var leased = await repo.LeaseNextAsync(
            "worker-1",
            [MediaOperationType.IngestionFile],
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(5));

        var secondLease = await repo.LeaseNextAsync(
            "worker-2",
            [MediaOperationType.IngestionFile],
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(5));

        Assert.Single(leased);
        Assert.Equal(MediaOperationStatus.Leased, leased[0].Status);
        Assert.Equal("worker-1", leased[0].LeaseOwner);
        Assert.Empty(secondLease);
    }

    [Fact]
    public async Task TerminalMarkers_SetExpectedStatusAndCompletion()
    {
        var repo = new MediaOperationRepository(_db);
        var operation = await repo.EnsureAsync(NewOperation($"identity:{Guid.NewGuid():N}"));

        await repo.MarkSucceededAsync(operation.Id, "completed");
        var updated = await repo.GetByIdAsync(operation.Id);

        Assert.NotNull(updated);
        Assert.Equal(MediaOperationStatus.Succeeded, updated.Status);
        Assert.Equal(100, updated.ProgressPercent);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task ReclaimStuckAsync_MarksExpiredRunningWorkInterrupted()
    {
        var repo = new MediaOperationRepository(_db);
        var operation = await repo.EnsureAsync(NewOperation(
            $"plugin:{Guid.NewGuid():N}",
            status: MediaOperationStatus.Running,
            heartbeatAt: DateTimeOffset.UtcNow.AddHours(-1)));

        var reclaimed = await repo.ReclaimStuckAsync(TimeSpan.FromMinutes(10));
        var updated = await repo.GetByIdAsync(operation.Id);

        Assert.Equal(1, reclaimed);
        Assert.NotNull(updated);
        Assert.Equal(MediaOperationStatus.Interrupted, updated.Status);
        Assert.Null(updated.LeaseOwner);
    }

    [Fact]
    public async Task EntityCapabilityState_UsesSingleKeyForNullSubKey()
    {
        var repo = new EntityCapabilityStateRepository(_db);
        var entityId = Guid.NewGuid();

        var first = await repo.EnsureAsync(NewCapability(entityId, subKey: null));
        var second = await repo.EnsureAsync(NewCapability(entityId, subKey: null));

        var states = await repo.GetByEntityAsync(entityId);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(states);
    }

    [Fact]
    public async Task InvalidateForCapabilityVersionAsync_MarksOlderVersionStale()
    {
        var repo = new EntityCapabilityStateRepository(_db);
        var entityId = Guid.NewGuid();
        await repo.EnsureAsync(NewCapability(entityId, version: "1.0"));

        await repo.InvalidateForCapabilityVersionAsync(CapabilityId.IdentityWikidataBridge, "2.0");
        var updated = await repo.GetAsync(entityId, CapabilityId.IdentityWikidataBridge);

        Assert.NotNull(updated);
        Assert.Equal(EntityCapabilityStatus.Stale, updated.Status);
        Assert.True(updated.Stale);
        Assert.True(updated.NeedsRerun);
    }

    private static MediaOperation NewOperation(
        string idempotencyKey,
        string? sourcePath = null,
        string status = MediaOperationStatus.Queued,
        DateTimeOffset? heartbeatAt = null) => new()
    {
        OperationType = MediaOperationType.IngestionFile,
        OperationKind = MediaOperationKind.Ingestion,
        SourcePath = sourcePath,
        Status = status,
        Stage = MediaOperationStage.Queued,
        QueueName = "ingestion",
        Priority = 100,
        PositionKey = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        HeartbeatAt = heartbeatAt,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        IdempotencyKey = idempotencyKey
    };

    private static EntityCapabilityState NewCapability(
        Guid entityId,
        string? subKey = null,
        string version = "1.0") => new()
    {
        EntityId = entityId,
        EntityKind = "asset",
        CapabilityId = CapabilityId.IdentityWikidataBridge,
        CapabilityKind = MediaOperationKind.Identity,
        CapabilityVersion = version,
        SubKey = subKey,
        Status = EntityCapabilityStatus.Succeeded,
        Requiredness = CapabilityRequiredness.Optional,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
