namespace MediaEngine.Domain.Entities;

/// <summary>A high-entropy machine credential. Only its SHA-256 hash is persisted.</summary>
public sealed class ServiceCredential
{
    public Guid Id { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
