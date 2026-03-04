using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Boolean feature flags controlling the visibility of UI elements.
///
/// <para>
/// At the global level, all features default to <c>true</c> (enabled).
/// Device profiles may disable features via <see cref="UIDeviceConstraints.FeaturesDisabled"/>
/// (hard constraint) or by setting individual flags to <c>false</c> (soft default).
/// </para>
/// </summary>
public sealed class UIFeatureFlags
{
    /// <summary>Global search overlay activated by Ctrl+K.</summary>
    [JsonPropertyName("command_palette")]
    public bool CommandPalette { get; set; } = true;

    /// <summary>Search icon button in the AppBar.</summary>
    [JsonPropertyName("search_button")]
    public bool SearchButton { get; set; } = true;

    /// <summary>Dark/light mode toggle in the AppBar.</summary>
    [JsonPropertyName("theme_toggle")]
    public bool ThemeToggle { get; set; } = true;

    /// <summary>Avatar dropdown menu in the AppBar.</summary>
    [JsonPropertyName("avatar_menu")]
    public bool AvatarMenu { get; set; } = true;

    /// <summary>Access to the Server Settings page.</summary>
    [JsonPropertyName("server_settings")]
    public bool ServerSettings { get; set; } = true;

    /// <summary>Pending files alert on the Home page.</summary>
    [JsonPropertyName("pending_files_alert")]
    public bool PendingFilesAlert { get; set; } = true;

    /// <summary>Grid/List view toggle on the Home page.</summary>
    [JsonPropertyName("view_toggle")]
    public bool ViewToggle { get; set; } = true;

    /// <summary>Profile identity section (avatar, name, role) in Preferences.</summary>
    [JsonPropertyName("profile_section")]
    public bool ProfileSection { get; set; } = true;

    /// <summary>Accent colour picker in Preferences.</summary>
    [JsonPropertyName("color_picker")]
    public bool ColorPicker { get; set; } = true;
}
