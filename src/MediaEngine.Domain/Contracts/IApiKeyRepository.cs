using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for inbound <see cref="ApiKey"/> records.
/// Only the SHA-256 hash of each key is ever stored; plaintext is never persisted.
/// </summary>
public interface IApiKeyRepository
{
    /// <summary>Inserts a new API key record.</summary>
    Task InsertAsync(ApiKey key, CancellationToken ct = default);

    /// <summary>
    /// Looks up a key by its SHA-256 hex hash.
    /// Used by <c>ApiKeyMiddleware</c> on every authenticated request.
    /// Returns <see langword="null"/> if no matching key exists.
    /// </summary>
    Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default);

    /// <summary>
    /// Returns all issued keys (id, label, created_at).
    /// <c>HashedKey</c> is intentionally set to <see cref="string.Empty"/> in results
    /// to prevent accidental logging or serialisation of the hash.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Revokes (deletes) the key with the given <paramref name="id"/>.
    /// Returns <see langword="true"/> if a row was removed.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Revokes (deletes) all issued API keys.
    /// Returns the number of keys that were removed.
    /// </summary>
    Task<int> DeleteAllAsync(CancellationToken ct = default);
}
