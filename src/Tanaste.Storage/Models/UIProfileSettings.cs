using System.Text.Json.Serialization;

namespace Tanaste.Storage.Models;

/// <summary>
/// Per-user UI preferences loaded from <c>config/ui/profiles/{profile-id}.json</c>.
///
/// <para>
/// The third and most specific tier of the three-tier cascade (Global → Device → Profile).
/// All fields are nullable — <c>null</c> means "use the cascade default" from Device or Global.
/// Profile preferences cannot override device <see cref="UIDeviceConstraints"/>
/// (e.g. cannot re-enable a feature disabled by the device class).
/// </para>
/// </summary>
public sealed class UIProfileSettings
{
    /// <summary>The profile UUID this settings file belongs to.</summary>
    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Override dark mode. <c>null</c> = use cascade default.</summary>
    [JsonPropertyName("dark_mode")]
    public bool? DarkMode { get; set; }

    /// <summary>Override accent colour. <c>null</c> = use cascade default.</summary>
    [JsonPropertyName("accent_color")]
    public string? AccentColor { get; set; }

    /// <summary>Override border radius. <c>null</c> = use cascade default.</summary>
    [JsonPropertyName("border_radius")]
    public int? BorderRadius { get; set; }
}
