namespace MediaEngine.Domain.Entities;

/// <summary>
/// Represents an inbound API key issued to an external application
/// (e.g. Radarr, automation scripts).
///
/// The plaintext key is shown to the user EXACTLY ONCE at creation time
/// and is NEVER stored. Only the SHA-256 hex hash is persisted in
/// <c>api_keys.hashed_key</c>.
///
/// Maps to <c>api_keys</c> in the Phase 8 schema.
/// </summary>
public sealed class ApiKey
{
    /// <summary>Stable identifier. PK in <c>api_keys</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable label, e.g. "Radarr Integration".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex hash of the plaintext key.
    /// NEVER expose or log this value — it is a one-way hash but still sensitive.
    /// </summary>
    public string HashedKey { get; set; } = string.Empty;

    /// <summary>
    /// Authorization role for this key.  Determines which endpoints
    /// the key can access.  One of: Administrator, Curator, Consumer.
    /// Defaults to Administrator so that existing keys retain full access
    /// after migration M-006.
    /// </summary>
    public string Role { get; set; } = "Administrator";

    /// <summary>When this key was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
