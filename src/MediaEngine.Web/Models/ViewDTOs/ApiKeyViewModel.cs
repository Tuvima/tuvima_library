namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A Guest API Key as shown in the Settings page table.
/// The key's plaintext is never included here — it was shown once at creation time.
/// </summary>
public sealed class ApiKeyViewModel
{
    public Guid           Id        { get; init; }
    public string         Label     { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Returned immediately after generating a new Guest API Key.
/// The <see cref="Key"/> field contains the plaintext — it MUST be shown to the user
/// immediately and cannot be retrieved again after this response.
/// </summary>
public sealed class NewApiKeyViewModel
{
    public Guid           Id        { get; init; }
    public string         Label     { get; init; } = string.Empty;
    public string         Key       { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
