using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Tests;

/// <summary>
/// Tests for <see cref="ApiKeyLookupCache"/> — verifies the cache avoids repeated
/// repository calls for both hits and misses, and that <see cref="IApiKeyLookupCache.InvalidateAll"/>
/// forces the next lookup to go back to the repository.
/// </summary>
public sealed class ApiKeyLookupCacheTests
{
    private static ApiKey MakeKey(string hashedKey) => new()
    {
        Id        = Guid.NewGuid(),
        Label     = "Test Key",
        HashedKey = hashedKey,
        Role      = "Administrator",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task FindByHashedKeyAsync_SecondLookupForSameHash_DoesNotHitRepository()
    {
        var key = MakeKey("hash-a");
        var repo = new SpyApiKeyRepo([key]);
        using var cache = new ApiKeyLookupCache(repo);

        var first = await cache.FindByHashedKeyAsync("hash-a");
        var second = await cache.FindByHashedKeyAsync("hash-a");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(key.Id, first!.Id);
        Assert.Equal(key.Id, second!.Id);
        Assert.Equal(1, repo.FindByHashedKeyCallCount);
    }

    [Fact]
    public async Task FindByHashedKeyAsync_NullResult_IsCached()
    {
        var repo = new SpyApiKeyRepo([]);
        using var cache = new ApiKeyLookupCache(repo);

        var first = await cache.FindByHashedKeyAsync("unknown-hash");
        var second = await cache.FindByHashedKeyAsync("unknown-hash");

        Assert.Null(first);
        Assert.Null(second);
        // Only one repository round trip even though both lookups miss — this is
        // exactly the behaviour that stops invalid-key floods from hitting the DB.
        Assert.Equal(1, repo.FindByHashedKeyCallCount);
    }

    [Fact]
    public async Task InvalidateAll_CausesNextLookup_ToHitRepositoryAgain()
    {
        var key = MakeKey("hash-b");
        var repo = new SpyApiKeyRepo([key]);
        using var cache = new ApiKeyLookupCache(repo);

        await cache.FindByHashedKeyAsync("hash-b");
        Assert.Equal(1, repo.FindByHashedKeyCallCount);

        cache.InvalidateAll();

        await cache.FindByHashedKeyAsync("hash-b");
        Assert.Equal(2, repo.FindByHashedKeyCallCount);
    }

    [Fact]
    public async Task FindByHashedKeyAsync_DifferentHashes_EachHitRepositoryOnce()
    {
        var keyA = MakeKey("hash-c1");
        var keyB = MakeKey("hash-c2");
        var repo = new SpyApiKeyRepo([keyA, keyB]);
        using var cache = new ApiKeyLookupCache(repo);

        await cache.FindByHashedKeyAsync("hash-c1");
        await cache.FindByHashedKeyAsync("hash-c2");
        await cache.FindByHashedKeyAsync("hash-c1");
        await cache.FindByHashedKeyAsync("hash-c2");

        Assert.Equal(2, repo.FindByHashedKeyCallCount);
    }

    // NOTE: TTL expiry (30s absolute) is intentionally NOT covered here. The repo
    // has no TimeProvider/fake-clock convention for cache expiry in this project
    // (ApiKeyLookupCache uses MemoryCacheEntryOptions.AbsoluteExpirationRelativeToNow
    // directly against the system clock), and a real 30-second delay is not
    // appropriate for a unit test. Behaviour above the TTL boundary is covered by
    // InvalidateAll instead, which exercises the same underlying eviction path
    // (a cancelled generation token) without waiting out the clock.
}

// ── Spy repository — hand-written, no Moq/NSubstitute (repo convention) ────────

file sealed class SpyApiKeyRepo : IApiKeyRepository
{
    private readonly List<ApiKey> _keys;

    public SpyApiKeyRepo(IReadOnlyList<ApiKey> keys)
    {
        _keys = [.. keys];
    }

    public int FindByHashedKeyCallCount { get; private set; }

    public Task InsertAsync(ApiKey key, CancellationToken ct = default)
    {
        _keys.Add(key);
        return Task.CompletedTask;
    }

    public Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default)
    {
        FindByHashedKeyCallCount++;
        return Task.FromResult(_keys.FirstOrDefault(k => k.HashedKey == hashedKey));
    }

    public Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ApiKey>>(_keys);

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var removed = _keys.RemoveAll(k => k.Id == id);
        return Task.FromResult(removed > 0);
    }

    public Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        var count = _keys.Count;
        _keys.Clear();
        return Task.FromResult(count);
    }
}
