using MediaEngine.Domain.Authorization;

namespace MediaEngine.Domain.Entities;

public static class BuiltInApplicationIds
{
    public static readonly Guid NativeClient = new("00000000-0000-0000-0000-000000000004");
}

public sealed class Application
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ApplicationType ApplicationType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsAdministrator { get; set; }
    public long AuthorizationVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed class ApplicationCredential
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CredentialHash { get; set; } = string.Empty;
    public string HashScheme { get; set; } = "sha256";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}

public sealed record ApplicationCredentialIdentity(
    Application Application,
    ApplicationCredential Credential,
    IReadOnlySet<ApplicationPermissionId> Permissions);
