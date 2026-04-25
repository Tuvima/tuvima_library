namespace MediaEngine.Domain.Entities;

/// <summary>
/// External sign-in account bound to a local Tuvima profile.
/// </summary>
public sealed class ProfileExternalLogin
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}
