using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Tests;

/// <summary>
/// Tests for <see cref="ApiKeyService"/> — cryptographic key generation,
/// hashing, and role validation.
/// </summary>
public sealed class ApiKeyServiceTests
{
    // ── Key generation ───────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_ReturnsUniqueKeys()
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        var (_, key1) = await service.GenerateAsync("App 1");
        var (_, key2) = await service.GenerateAsync("App 2");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsUrlSafeKey()
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        var (_, plaintext) = await service.GenerateAsync("Test");

        // URL-safe base64: no +, /, or = characters.
        Assert.DoesNotContain("+", plaintext);
        Assert.DoesNotContain("/", plaintext);
        Assert.DoesNotContain("=", plaintext);
        // 32 random bytes → 43-char URL-safe base64 (no padding).
        Assert.Equal(43, plaintext.Length);
    }

    [Fact]
    public async Task GenerateAsync_StoresHashedKey_NotPlaintext()
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        var (apiKey, plaintext) = await service.GenerateAsync("Test");

        // The stored hash must NOT be the plaintext.
        Assert.NotEqual(plaintext, apiKey.HashedKey);
        // The stored hash should match what HashKey produces.
        Assert.Equal(ApiKeyService.HashKey(plaintext), apiKey.HashedKey);
    }

    [Fact]
    public async Task GenerateAsync_SetsCorrectRole()
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        var (key, _) = await service.GenerateAsync("Curator App", "Curator");
        Assert.Equal("Curator", key.Role);
    }

    // ── Hash determinism ─────────────────────────────────────────────────────

    [Fact]
    public void HashKey_IsDeterministic()
    {
        var hash1 = ApiKeyService.HashKey("test-key-123");
        var hash2 = ApiKeyService.HashKey("test-key-123");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashKey_DifferentInputs_ProduceDifferentHashes()
    {
        var hash1 = ApiKeyService.HashKey("key-alpha");
        var hash2 = ApiKeyService.HashKey("key-bravo");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashKey_ReturnsLowercaseHex()
    {
        var hash = ApiKeyService.HashKey("test");

        // SHA-256 hex string: 64 lowercase hex characters.
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    // ── Role validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Curator")]
    [InlineData("Consumer")]
    public async Task GenerateAsync_AcceptsValidRoles(string role)
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        var (key, _) = await service.GenerateAsync("Test", role);
        Assert.Equal(role, key.Role);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("")]
    [InlineData("viewer")]
    public async Task GenerateAsync_RejectsInvalidRoles(string role)
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateAsync("Test", role));
    }

    [Fact]
    public async Task GenerateAsync_RejectsEmptyLabel()
    {
        var repo = new InMemoryApiKeyRepo();
        var service = new ApiKeyService(repo);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateAsync("", "Administrator"));
    }
}

// ── Minimal in-memory repository for isolated tests ──────────────────────────

file sealed class InMemoryApiKeyRepo : IApiKeyRepository
{
    private readonly List<ApiKey> _keys = [];

    public Task InsertAsync(ApiKey key, CancellationToken ct = default)
    {
        _keys.Add(key);
        return Task.CompletedTask;
    }

    public Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default)
        => Task.FromResult(_keys.FirstOrDefault(k => k.HashedKey == hashedKey));

    public Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ApiKey>>(_keys);

    public Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        _keys.RemoveAll(k => k.Id == id);
        return Task.CompletedTask;
    }
}
