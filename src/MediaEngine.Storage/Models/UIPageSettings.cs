using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Per-page layout settings controlling how each Dashboard page renders on a given device.
/// </summary>
public sealed class UIPageSettings
{
    [JsonPropertyName("home")]
    public UIHomePageSettings Home { get; set; } = new();

    [JsonPropertyName("preferences")]
    public UIPreferencesPageSettings Preferences { get; set; } = new();

    [JsonPropertyName("server_settings")]
    public UIServerSettingsPageSettings ServerSettings { get; set; } = new();
}

/// <summary>
/// Layout settings for the Home page (library overview).
/// </summary>
public sealed class UIHomePageSettings
{
    /// <summary>Whether the hero card for the most-accessed Collection is shown.</summary>
    [JsonPropertyName("collection_hero_enabled")]
    public bool CollectionHeroEnabled { get; set; } = true;

    /// <summary>
    /// Hero card layout: <c>two-column</c> (artwork + info side by side),
    /// <c>stacked</c> (artwork above info), <c>two-column-oversized</c> (TV),
    /// <c>hidden</c> (automotive).
    /// </summary>
    [JsonPropertyName("collection_hero_layout")]
    public string CollectionHeroLayout { get; set; } = "two-column";

    /// <summary>
    /// Progress indicator card layout: <c>row</c> (horizontal),
    /// <c>stacked</c> (vertical), <c>row-oversized</c> (TV),
    /// <c>single</c> (automotive — audio only).
    /// </summary>
    [JsonPropertyName("progress_cards_layout")]
    public string ProgressCardsLayout { get; set; } = "row";

    /// <summary>Number of Bento grid columns. 3 (desktop), 1 (mobile/automotive), 2 (TV).</summary>
    [JsonPropertyName("bento_columns")]
    public int BentoColumns { get; set; } = 3;

    /// <summary>
    /// Tile style: <c>normal</c>, <c>large</c> (TV — larger min-height + font),
    /// <c>audio-only</c> (automotive — filters to audio Collections).
    /// </summary>
    [JsonPropertyName("bento_tile_style")]
    public string BentoTileStyle { get; set; } = "normal";

    /// <summary>
    /// Pending files display: <c>expandable</c> (full table), <c>badge</c> (count only),
    /// <c>hidden</c> (TV/automotive).
    /// </summary>
    [JsonPropertyName("pending_files_display")]
    public string PendingFilesDisplay { get; set; } = "expandable";
}

/// <summary>
/// Layout settings for the Preferences page (user settings).
/// </summary>
public sealed class UIPreferencesPageSettings
{
    /// <summary>Whether the Preferences page is accessible on this device.</summary>
    [JsonPropertyName("page_enabled")]
    public bool PageEnabled { get; set; } = true;

    /// <summary>
    /// Tab bar layout: <c>horizontal</c> (icon strip), <c>vertical</c> (stacked list),
    /// <c>focus-nav</c> (TV — D-pad navigable), <c>single</c> (automotive — no tab bar).
    /// </summary>
    [JsonPropertyName("tab_bar_layout")]
    public string TabBarLayout { get; set; } = "horizontal";

    /// <summary>
    /// General tab layout: <c>full</c> (profile + appearance), <c>stacked</c> (mobile),
    /// <c>theme-only</c> (TV/automotive — no profile, no colours).
    /// </summary>
    [JsonPropertyName("general_tab_layout")]
    public string GeneralTabLayout { get; set; } = "full";

    /// <summary>Number of accent colour swatches. 8 (desktop), 4 (mobile), 0 (hidden).</summary>
    [JsonPropertyName("color_swatch_count")]
    public int ColorSwatchCount { get; set; } = 8;

    /// <summary>Whether the Playback tab is available.</summary>
    [JsonPropertyName("playback_tab_enabled")]
    public bool PlaybackTabEnabled { get; set; } = true;
}

/// <summary>
/// Layout settings for the Server Settings page (admin configuration).
/// </summary>
public sealed class UIServerSettingsPageSettings
{
    /// <summary>Whether the Server Settings page is accessible on this device.</summary>
    [JsonPropertyName("page_enabled")]
    public bool PageEnabled { get; set; } = true;

    /// <summary>
    /// Tab bar layout: <c>horizontal</c> (icon strip), <c>vertical-accordion</c> (mobile).
    /// </summary>
    [JsonPropertyName("tab_bar_layout")]
    public string TabBarLayout { get; set; } = "horizontal";

    /// <summary>
    /// Content layout: <c>full</c> (wide forms), <c>stacked-card</c> (mobile — card-per-section).
    /// </summary>
    [JsonPropertyName("tab_content_layout")]
    public string TabContentLayout { get; set; } = "full";
}
