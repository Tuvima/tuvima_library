using System.Security.Cryptography;
using System.Text;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Services;

/// <summary>
/// Generates and manages inbound API keys for external application integrations.
///
/// Key lifecycle:
/// 1. <see cref="GenerateAsync"/> creates a cryptographically random key, hashes it,
///    stores the hash, and returns the plaintext exactly once to the caller.
/// 2. The plaintext is NEVER stored, logged, or returned again after this point.
/// 3. Incoming requests hash the key from the <c>X-Api-Key</c> header and compare
///    against stored hashes — the original plaintext is never reconstructed.
///
/// SECURITY: Do not log the plaintext or hash returned by <see cref="GenerateAsync"/>.
/// </summary>
public sealed class ApiKeyService(IApiKeyRepository repo)
{
    private readonly IApiKeyRepository _repo = repo;

    /// <summary>
    /// Generates a new 256-bit URL-safe random key, hashes it with SHA-256,
    /// persists the hash, and returns the key with its plaintext.
    ///
    /// The plaintext in the returned <c>PlaintextKey</c> MUST be shown to the user
    /// immediately and MUST NOT be stored anywhere else.
    /// </summary>
    /// <summary>Valid API key roles.  Checked at generation time.</summary>
    private static readonly string[] ValidRoles = ["Administrator", "Curator", "Consumer"];

    public async Task<(ApiKey Key, string PlaintextKey)> GenerateAsync(
        string label, string role = "Administrator", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (!ValidRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid role '{role}'. Must be Administrator, Curator, or Consumer.");

        // 32 random bytes → URL-safe base-64 (no padding) — 43 characters.
        var rawBytes   = RandomNumberGenerator.GetBytes(32);
        var plaintext  = Convert.ToBase64String(rawBytes)
                                .Replace('+', '-')
                                .Replace('/', '_')
                                .TrimEnd('=');

        var hashedKey  = HashKey(plaintext);

        var key = new ApiKey
        {
            Id         = Guid.NewGuid(),
            Label      = label,
            HashedKey  = hashedKey,
            Role       = role,
            CreatedAt  = DateTimeOffset.UtcNow,
        };

        await _repo.InsertAsync(key, ct).ConfigureAwait(false);

        // Return the plaintext here and nowhere else — it is lost after this call.
        return (key, plaintext);
    }

    /// <summary>
    /// Hashes an API key string with SHA-256 for safe comparison against stored hashes.
    /// Used by both this service (on generation) and <c>ApiKeyMiddleware</c> (on request).
    /// </summary>
    public static string HashKey(string plaintext)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
