using System.Globalization;
using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Core application settings loaded from <c>config/core.json</c>.
///
/// Contains path configuration, schema version, and organization template.
/// These are the "where is everything" settings that apply globally.
/// </summary>
public sealed class CoreConfiguration
{
    /// <summary>
    /// Configuration format version. Increment when the shape of any config file changes.
    /// </summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "2.0";

    /// <summary>
    /// SQLite database file path. Relative paths are resolved from the config directory.
    /// </summary>
    [JsonPropertyName("database_path")]
    public string DatabasePath { get; set; } = "library.db";

    /// <summary>
    /// Root directory for all media file storage (no BLOBs in DB).
    /// </summary>
    [JsonPropertyName("data_root")]
    public string DataRoot { get; set; } = "./media";

    /// <summary>
    /// Directory monitored for new files (the inbox).
    /// Overrides <c>appsettings.json:Ingestion:WatchDirectory</c> at startup.
    /// </summary>
    [JsonPropertyName("watch_directory")]
    public string WatchDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Organised media library root.
    /// Overrides <c>appsettings.json:Ingestion:LibraryRoot</c> at startup.
    /// </summary>
    [JsonPropertyName("library_root")]
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Holding area for files that cannot be auto-organized (low confidence,
    /// unknown media type, or "Other" category).  Keeps the Watch Folder clean.
    /// When empty, unresolved files remain in the Watch Folder.
    /// </summary>
    [JsonPropertyName("staging_directory")]
    public string StagingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Tokenised path template for file organisation (e.g.
    /// <c>{Category}/{HubName} ({Year})/{Format} - {Edition}/</c>).
    /// Overrides the default template when set.
    /// </summary>
    [JsonPropertyName("organization_template")]
    public string OrganizationTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of provider names defining the priority order for metadata harvesting.
    /// Providers are called in this order; first provider to return data wins for each field.
    /// When empty or null, the default registration order is used.
    /// </summary>
    [JsonPropertyName("provider_priority")]
    public List<string> ProviderPriority { get; set; } = [];

    // ── Server identity & regional settings ───────────────────────────────────

    /// <summary>
    /// Human-readable name for this server instance, used for network discovery
    /// (mDNS/Bonjour broadcasting). Defaults to the machine name.
    /// </summary>
    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = Environment.MachineName;

    /// <summary>
    /// BCP-47 two-letter language code (e.g. "en", "fr") for metadata downloads
    /// and UI localisation. Drives provider search language and Wikidata label language.
    /// Defaults to the host OS UI culture.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g. "US", "GB") for regional metadata
    /// storefronts (e.g. Apple Books country parameter). Defaults to the host OS region.
    /// </summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = GetDefaultCountry();

    /// <summary>
    /// Date display format for the Dashboard.
    /// Values: "system" (locale default), "short", "medium", "long", "iso8601".
    /// </summary>
    [JsonPropertyName("date_format")]
    public string DateFormat { get; set; } = "system";

    /// <summary>
    /// Time display format for the Dashboard clock and timestamps.
    /// Values: "system" (locale default), "12h", "24h".
    /// </summary>
    [JsonPropertyName("time_format")]
    public string TimeFormat { get; set; } = "system";

    private static string GetDefaultCountry()
    {
        try { return RegionInfo.CurrentRegion.TwoLetterISORegionName; }
        catch { return "US"; }
    }
}
