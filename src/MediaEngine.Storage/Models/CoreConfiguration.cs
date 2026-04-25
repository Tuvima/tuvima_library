using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

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
    /// Tokenised path template for file organisation (e.g.
    /// <c>{Category}/{Author}/{Title}/{Title}{Ext}</c>).
    /// Used as the "default" template when no media-type-specific template
    /// matches in <see cref="OrganizationTemplates"/>.
    /// </summary>
    [JsonPropertyName("organization_template")]
    public string OrganizationTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Per-media-type organisation templates.  Keys are media type names
    /// (e.g. "Books", "Audiobooks", "Movies", "TV", "Comics", "Music")
    /// or "default".  Values are tokenised path templates.
    /// Fallback chain: media-type-specific → "default" → <see cref="OrganizationTemplate"/>
    /// → hardcoded <c>{Category}/{Title}/{Title}{Ext}</c>.
    /// </summary>
    [JsonPropertyName("organization_templates")]
    public Dictionary<string, string> OrganizationTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
    /// Structured language preferences for UI display, metadata, and content languages.
    /// Backward compatible: deserialises from both <c>"language": "en"</c> (legacy)
    /// and the structured object format.
    /// </summary>
    [JsonPropertyName("language")]
    [JsonConverter(typeof(LanguagePreferencesConverter))]
    public LanguagePreferences Language { get; set; } = new();

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

    /// <summary>
    /// Rate limiting policy parameters for the Engine API.
    /// Controls per-IP request limits for key generation, streaming, and general endpoints.
    /// </summary>
    [JsonPropertyName("rate_limiting")]
    public RateLimitingSettings RateLimiting { get; set; } = new();

    /// <summary>
    /// User sign-in and external identity-provider settings.
    /// </summary>
    [JsonPropertyName("auth")]
    public AuthSettings Auth { get; set; } = new();

    /// <summary>
    /// Identity pipeline tuning parameters (lease batch sizes for the three workers).
    /// Centralises cross-file batching policy in one config section instead of
    /// scattered <c>const int BatchSize</c> values inside each worker.
    /// </summary>
    [JsonPropertyName("pipeline")]
    public PipelineSettings Pipeline { get; set; } = new();

    /// <summary>
    /// Storage policy for manager-owned assets such as provider artwork and derived artifacts.
    /// </summary>
    [JsonPropertyName("storage_policy")]
    public LibraryStoragePolicy StoragePolicy { get; set; } = new();

    private static string GetDefaultCountry()
    {
        try { return RegionInfo.CurrentRegion.TwoLetterISORegionName; }
        catch { return "US"; }
    }
}

/// <summary>
/// Structured language preferences supporting multi-language media libraries.
/// Backward compatible: deserialises from both <c>"language": "en"</c> (legacy)
/// and <c>"language": { "display": "en", ... }</c> (new format).
/// </summary>
public sealed class LanguagePreferences
{
    /// <summary>
    /// UI language — controls .resx resource resolution and Wikidata label display.
    /// BCP-47 two-letter code (e.g. "en", "fr").
    /// </summary>
    [JsonPropertyName("display")]
    public string Display { get; set; } = "en";

    /// <summary>
    /// Primary metadata language — provider queries default to this.
    /// BCP-47 two-letter code.
    /// </summary>
    [JsonPropertyName("metadata")]
    public string Metadata { get; set; } = "en";

    /// <summary>
    /// Additional languages the user consumes content in.
    /// Files in these languages are NOT treated as mismatches.
    /// </summary>
    [JsonPropertyName("additional")]
    public List<string> Additional { get; set; } = [];

    /// <summary>
    /// When true, no file is ever flagged LanguageMismatch regardless of its language.
    /// Default is true — files in any language ingest without friction.
    /// </summary>
    [JsonPropertyName("accept_any")]
    public bool AcceptAny { get; set; } = true;

    /// <summary>
    /// Returns true if the given BCP-47 language code is accepted by these preferences.
    /// A language is accepted if <see cref="AcceptAny"/> is true, or if it matches
    /// <see cref="Metadata"/> or any entry in <see cref="Additional"/>.
    /// </summary>
    public bool IsLanguageAccepted(string? languageCode)
    {
        if (AcceptAny || string.IsNullOrWhiteSpace(languageCode))
            return true;

        var normalized = languageCode.Split('-', '_')[0].ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(normalized))
            return true;

        var metaNorm = Metadata.Split('-', '_')[0].ToLowerInvariant().Trim();
        if (string.Equals(normalized, metaNorm, StringComparison.OrdinalIgnoreCase))
            return true;

        return Additional.Any(a =>
            string.Equals(a.Split('-', '_')[0].ToLowerInvariant().Trim(), normalized, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Deserialises the <c>"language"</c> config key from either a plain string
/// (<c>"en"</c>) or a structured object (<c>{ "display": "en", ... }</c>).
/// Always serialises as the structured object.
/// </summary>
public sealed class LanguagePreferencesConverter : JsonConverter<LanguagePreferences>
{
    public override LanguagePreferences? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var lang = reader.GetString() ?? "en";
            return new LanguagePreferences
            {
                Display = lang,
                Metadata = lang,
                Additional = [],
                AcceptAny = true
            };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Manually deserialize to avoid re-entering this converter
            var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return new LanguagePreferences
            {
                Display    = root.TryGetProperty("display", out var d)    ? d.GetString() ?? "en" : "en",
                Metadata   = root.TryGetProperty("metadata", out var m)   ? m.GetString() ?? "en" : "en",
                Additional = root.TryGetProperty("additional", out var a) ? a.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList() : [],
                AcceptAny  = root.TryGetProperty("accept_any", out var aa) ? aa.GetBoolean() : true,
            };
        }

        return new LanguagePreferences();
    }

    public override void Write(Utf8JsonWriter writer, LanguagePreferences value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("display", value.Display);
        writer.WriteString("metadata", value.Metadata);
        writer.WritePropertyName("additional");
        writer.WriteStartArray();
        foreach (var lang in value.Additional)
            writer.WriteStringValue(lang);
        writer.WriteEndArray();
        writer.WriteBoolean("accept_any", value.AcceptAny);
        writer.WriteEndObject();
    }
}
