namespace MediaEngine.Domain.Entities;

/// <summary>
/// External sign-in identity bound to a Tuvima account.
/// </summary>
public sealed class AccountExternalLogin
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Provider { get; set; } = string.Empty;

    /// <summary>Canonical identity issuer. Combined with subject to form the immutable external identity key.</summary>
    public string Issuer { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}
