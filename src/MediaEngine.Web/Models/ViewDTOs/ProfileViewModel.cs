using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a user profile.
/// Maps from the Engine's <c>GET /profiles</c> response.
/// </summary>
public sealed record ProfileViewModel(
    [property: JsonPropertyName("id")]                Guid Id,
    [property: JsonPropertyName("display_name")]       string DisplayName,
    [property: JsonPropertyName("avatar_color")]       string AvatarColor,
    [property: JsonPropertyName("role")]               string Role,
    [property: JsonPropertyName("created_at")]         DateTimeOffset CreatedAt,
    [property: JsonPropertyName("navigation_config")]  string? NavigationConfig = null,
    // TODO: Persist avatar images once the Engine profile schema supports upload/storage.
    [property: JsonPropertyName("avatar_image_url")]   string? AvatarImageUrl = null)
{
    /// <summary>
    /// Returns <see langword="true"/> when this is the seed "Owner" profile
    /// that cannot be deleted.
    /// </summary>
    [JsonIgnore]
    public bool IsSeed => Id == new Guid("00000000-0000-0000-0000-000000000001");
}
