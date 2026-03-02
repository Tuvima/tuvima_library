using System.Text.Json.Serialization;

namespace Tanaste.Storage.Models;

/// <summary>
/// Core application settings loaded from <c>config/tanaste.json</c>.
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
    public string DatabasePath { get; set; } = "tanaste.db";

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
}
