namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single key-value configuration entry for a registered metadata provider.
/// Maps to a row in the <c>provider_config</c> table (composite PK: provider_id + key).
///
/// When <see cref="IsSecret"/> is <see langword="true"/>:
/// • The <see cref="Value"/> stored in the database is ENCRYPTED via <see cref="Contracts.ISecretStore"/>.
/// • The <see cref="Value"/> returned by the repository for UI consumption is ALWAYS "********".
/// • The <see cref="Value"/> returned for internal use (API calls) is the DECRYPTED plaintext.
/// </summary>
public sealed class ProviderConfiguration
{
    /// <summary>FK to <c>metadata_providers.id</c>.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Configuration key, e.g. "ApiKey", "BaseUrl", "UserAgent".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Configuration value — plaintext for non-secrets, "********" or decrypted
    /// depending on which repository method was called (see class remarks).
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/> the value is a credential that must be
    /// encrypted at rest and masked when sent to any external consumer.
    /// </summary>
    public bool IsSecret { get; set; }
}
