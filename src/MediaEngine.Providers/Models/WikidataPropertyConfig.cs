using System.Text.Json.Serialization;

namespace MediaEngine.Providers.Models;

/// <summary>
/// Configuration for a single Wikidata property in the universe configuration file.
///
/// This is the JSON-serializable counterpart of <see cref="WikidataProperty"/>.
/// Each entry in <see cref="UniverseConfiguration.PropertyMap"/> is one of these.
/// </summary>
public sealed class WikidataPropertyConfig
{
    /// <summary>The claim key this property maps to, e.g. <c>"series"</c>.</summary>
    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; set; } = string.Empty;

    /// <summary>Human-readable category for Dashboard grouping, e.g. <c>"Core Identity"</c>.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Which entity type this property applies to:
    /// <c>"Work"</c>, <c>"Person"</c>, or <c>"Both"</c>.
    /// </summary>
    [JsonPropertyName("entity_scope")]
    public string EntityScope { get; set; } = "Work";

    /// <summary>
    /// Default confidence for claims produced from this property.
    /// Bridge identifiers typically carry <c>1.0</c>; descriptive fields <c>0.8</c> – <c>0.9</c>.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.9;

    /// <summary>
    /// When <c>true</c>, this property represents an external bridge identifier
    /// (e.g. TMDB ID, Apple Books ID, ASIN). Written to the sidecar's bridges section.
    /// </summary>
    [JsonPropertyName("is_bridge")]
    public bool IsBridge { get; set; }

    /// <summary>When <c>false</c>, this property is excluded from SPARQL queries.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the Wikidata value is a Q-item (entity) requiring
    /// <c>rdfs:label</c> fetching for a human-readable name.
    /// </summary>
    [JsonPropertyName("is_entity_valued")]
    public bool IsEntityValued { get; set; }

    /// <summary>
    /// Name of the value transform to apply. <c>null</c> = pass through.
    /// Valid names: <c>"year_from_iso"</c>, <c>"numeric_portion"</c>,
    /// <c>"strip_entity_uri"</c>, <c>"commons_url"</c>.
    /// </summary>
    [JsonPropertyName("value_transform")]
    public string? ValueTransform { get; set; }

    /// <summary>
    /// When <c>true</c>, the SPARQL query uses <c>GROUP_CONCAT</c> to collect
    /// all values. The adapter splits the concatenated result and emits one
    /// claim per value.
    /// </summary>
    [JsonPropertyName("is_multi_valued")]
    public bool IsMultiValued { get; set; }
}
