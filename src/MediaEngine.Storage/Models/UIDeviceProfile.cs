using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Device-class configuration loaded from <c>config/ui/devices/{class}.json</c>.
///
/// <para>
/// The second tier of the three-tier cascade (Global → Device → Profile).
/// Contains both <see cref="Constraints"/> (hard limits that Profile cannot override)
/// and nullable override fields (soft defaults that overlay on Global).
/// </para>
///
/// <para>
/// Four device classes are defined: <c>web</c> (desktop browser, unconstrained),
/// <c>mobile</c> (phone-sized), <c>television</c> (remote-navigable),
/// <c>automotive</c> (car displays, audio-focused).
/// </para>
/// </summary>
public sealed class UIDeviceProfile
{
    /// <summary>Device class identifier (e.g. <c>"web"</c>, <c>"mobile"</c>).</summary>
    [JsonPropertyName("device_class")]
    public string DeviceClass { get; set; } = "web";

    /// <summary>Human-readable name (e.g. <c>"Desktop Web"</c>, <c>"Mobile"</c>).</summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "Desktop Web";

    // ── Constraints (hard limits — Profile cannot override) ────────────

    /// <summary>Structural constraints for this device class.</summary>
    [JsonPropertyName("constraints")]
    public UIDeviceConstraints Constraints { get; set; } = new();

    // ── Overrides (nullable — overlay on Global defaults) ──────────────

    /// <summary>Override dark mode default. <c>null</c> = use global.</summary>
    [JsonPropertyName("dark_mode")]
    public bool? DarkMode { get; set; }

    /// <summary>Override content padding. <c>null</c> = use global.</summary>
    [JsonPropertyName("content_padding")]
    public string? ContentPadding { get; set; }

    /// <summary>Override content max width. <c>null</c> = use global.</summary>
    [JsonPropertyName("content_max_width")]
    public string? ContentMaxWidth { get; set; }

    /// <summary>Override border radius. <c>null</c> = use global.</summary>
    [JsonPropertyName("border_radius")]
    public int? BorderRadius { get; set; }

    /// <summary>Feature flag overrides. <c>null</c> = use global.</summary>
    [JsonPropertyName("features")]
    public UIFeatureFlags? Features { get; set; }

    /// <summary>Shell setting overrides. <c>null</c> = use global.</summary>
    [JsonPropertyName("shell")]
    public UIShellSettings? Shell { get; set; }

    /// <summary>Page setting overrides. <c>null</c> = use global.</summary>
    [JsonPropertyName("pages")]
    public UIPageSettings? Pages { get; set; }
}

/// <summary>
/// Hard constraints for a device class. These represent physical or UX limitations
/// of the device and <b>cannot</b> be overridden by profile-level preferences.
///
/// <para>
/// A television cannot accept keyboard input regardless of user preference.
/// An automotive display forces dark mode regardless of profile settings.
/// </para>
/// </summary>
public sealed class UIDeviceConstraints
{
    /// <summary>
    /// Feature names permanently disabled on this device class.
    /// Values must match <see cref="UIFeatureFlags"/> property names (snake_case).
    /// Profile-level overrides cannot re-enable these.
    /// </summary>
    [JsonPropertyName("features_disabled")]
    public List<string> FeaturesDisabled { get; set; } = [];

    /// <summary>
    /// Page names inaccessible on this device class.
    /// Navigating to a disabled page redirects to Home.
    /// Values: <c>"server_settings"</c>, <c>"preferences"</c>.
    /// </summary>
    [JsonPropertyName("pages_disabled")]
    public List<string> PagesDisabled { get; set; } = [];

    /// <summary>
    /// Whether text input fields are allowed on this device.
    /// When <c>false</c>, components should use selection-based alternatives.
    /// </summary>
    [JsonPropertyName("allow_text_input")]
    public bool AllowTextInput { get; set; } = true;

    /// <summary>
    /// Minimum touch target size in pixels. Components should ensure all
    /// interactive elements meet this threshold. Default: 48 (WCAG).
    /// </summary>
    [JsonPropertyName("min_touch_target_px")]
    public int MinTouchTargetPx { get; set; } = 48;

    /// <summary>
    /// Forces dark mode regardless of global or profile preference.
    /// Used by automotive to ensure high-contrast display in vehicles.
    /// </summary>
    [JsonPropertyName("force_dark_mode")]
    public bool ForceDarkMode { get; set; }
}
