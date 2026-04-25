using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record ProfileExternalLoginViewModel(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("linked_at")] DateTimeOffset LinkedAt,
    [property: JsonPropertyName("last_login_at")] DateTimeOffset? LastLoginAt);
