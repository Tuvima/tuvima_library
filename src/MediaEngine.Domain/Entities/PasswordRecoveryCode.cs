namespace MediaEngine.Domain.Entities;

public sealed class PasswordRecoveryCode
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
