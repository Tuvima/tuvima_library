using System.Text.Json.Serialization;

namespace Tanaste.Providers.Models;

/// <summary>
/// The complete knowledge model for a universe provider (currently: Wikidata).
///
/// <para>
/// Loaded from <c>config/universe/{provider}.json</c>. Contains the full property
/// map, bridge lookup priority, scope exclusions, and the Commons URL template.
/// </para>
///
/// <para>
/// The schema is intentionally generic — a different linked-data universe (not Wikidata)
/// could use the same model shape with different property codes and transforms.
/// </para>
/// </summary>
public sealed class UniverseConfiguration
{
    /// <summary>
    /// The universe provider name, e.g. <c>"wikidata"</c>.
    /// Must match the corresponding provider configuration name.
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = "wikidata";

    /// <summary>
    /// The full property map. Key = P-code (e.g. <c>"P179"</c>),
    /// value = the property configuration describing how to interpret it.
    /// </summary>
    [JsonPropertyName("property_map")]
    public Dictionary<string, WikidataPropertyConfig> PropertyMap { get; set; } = [];

    /// <summary>
    /// Ordered list of bridge identifiers to try when resolving a QID from external IDs.
    /// The adapter tries each entry in order; first match wins.
    /// </summary>
    [JsonPropertyName("bridge_lookup_priority")]
    public List<BridgeLookupEntry> BridgeLookupPriority { get; set; } = [];

    /// <summary>
    /// P-codes to exclude from SPARQL queries for a given entity scope.
    /// Key = scope name (e.g. <c>"Work"</c>), value = list of P-codes to skip.
    /// Example: <c>{ "Work": ["P18"] }</c> — P18 (Image) is excluded from Work queries
    /// because media cover art comes from Apple Books/Audnexus/TMDB, not Wikidata.
    /// </summary>
    [JsonPropertyName("scope_exclusions")]
    public Dictionary<string, List<string>> ScopeExclusions { get; set; } = [];

    /// <summary>
    /// URL template for converting Wikimedia Commons filenames to thumbnail URLs.
    /// <c>{0}</c> is replaced with the URL-encoded filename.
    /// </summary>
    [JsonPropertyName("commons_url_template")]
    public string CommonsUrlTemplate { get; set; } =
        "https://commons.wikimedia.org/wiki/Special:FilePath/{0}?width=300";
}
