namespace MediaEngine.Domain.Entities;

/// <summary>A revocable, device-scoped first-party authentication session.</summary>
public sealed class AuthSession
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ActiveProfileId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string AuthenticationMethod { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
