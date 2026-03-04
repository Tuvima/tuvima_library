using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="ProviderConfiguration"/> entries.
/// Secrets are encrypted at rest and masked in all public-facing reads.
/// </summary>
public interface IProviderConfigurationRepository
{
    /// <summary>
    /// Returns all configuration entries for the given provider.
    /// Secret values are replaced with <c>"********"</c> — safe for UI / API responses.
    /// </summary>
    Task<IReadOnlyList<ProviderConfiguration>> GetAllMaskedAsync(
        string providerId, CancellationToken ct = default);

    /// <summary>
    /// Returns the decrypted plaintext value for a specific provider config key.
    /// For internal use only (outbound API calls). MUST NOT be sent to the UI.
    /// Returns <see langword="null"/> if the entry does not exist.
    /// </summary>
    Task<string?> GetDecryptedValueAsync(
        string providerId, string key, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces a configuration entry.
    /// <paramref name="plaintextValue"/> MUST be the actual value (never "********").
    /// The repository encrypts it before storage when <paramref name="isSecret"/> is true.
    /// </summary>
    Task UpsertAsync(
        string providerId,
        string key,
        string plaintextValue,
        bool isSecret,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a configuration entry identified by its composite key.
    /// </summary>
    Task DeleteAsync(string providerId, string key, CancellationToken ct = default);
}
