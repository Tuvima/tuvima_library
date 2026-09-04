namespace MediaEngine.Domain.Entities;

public enum AccountCredentialKind
{
    Password = 0,
}

/// <summary>A versioned first-party credential belonging to one sign-in account.</summary>
public sealed class AccountCredential
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public AccountCredentialKind Kind { get; set; }
    public string SecretHash { get; set; } = string.Empty;
    public string HashScheme { get; set; } = "aspnet-pbkdf2-v3";
    public int HashVersion { get; set; } = 1;
    public string SecurityStamp { get; set; } = string.Empty;
    public int FailedAttemptCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
