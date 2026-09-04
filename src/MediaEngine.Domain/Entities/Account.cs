namespace MediaEngine.Domain.Entities;

/// <summary>A sign-in principal that may be granted access to one or more library profiles.</summary>
public sealed class Account
{
    public static readonly Guid SeedAccountId = new("00000000-0000-0000-0000-000000000002");

    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public bool IsLocalOnly { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AccountProfileGrant
{
    public Guid AccountId { get; set; }
    public Guid ProfileId { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
