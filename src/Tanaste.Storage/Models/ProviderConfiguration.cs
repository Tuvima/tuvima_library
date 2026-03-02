using System.Text.Json.Serialization;

namespace Tanaste.Storage.Models;

/// <summary>
/// Per-provider configuration loaded from <c>config/providers/{name}.json</c>.
///
/// Each metadata provider carries its own self-contained configuration including
/// scoring weight, endpoints, field weights, rate limits, and capability tags.
/// The provider configuration controls how the scoring engine and harvesting
/// pipeline treat this provider.
/// </summary>
public sealed class ProviderConfiguration
{
    /// <summary>Must match the provider's registered name in the provider registry.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Semantic version of the provider adapter.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>When <c>false</c>, the provider is skipped during harvesting.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default scoring weight for this provider across all metadata fields.
    /// Individual fields can be overridden via <see cref="FieldWeights"/>.
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// The media domain this provider specialises in.
    /// Used for Dashboard UI grouping and future domain-filtered scoring.
    /// </summary>
    [JsonPropertyName("domain")]
    public ProviderDomain Domain { get; set; } = ProviderDomain.Universal;

    /// <summary>
    /// Declarative list of metadata fields this provider is considered an expert in
    /// (e.g. <c>["cover", "narrator", "series"]</c>). Shown in the Dashboard.
    /// </summary>
    [JsonPropertyName("capability_tags")]
    public List<string> CapabilityTags { get; set; } = [];

    /// <summary>
    /// Per-field weight overrides. Key = claim key (e.g. <c>"cover"</c>),
    /// value = weight in [0.0, 1.0]. Falls back to <see cref="Weight"/> if absent.
    /// </summary>
    [JsonPropertyName("field_weights")]
    public Dictionary<string, double> FieldWeights { get; set; } = [];

    /// <summary>
    /// Named endpoint URLs for this provider. Convention:
    /// <c>"api"</c> for the primary endpoint; additional keys for secondary
    /// endpoints (e.g. <c>"sparql"</c> for Wikidata).
    /// </summary>
    [JsonPropertyName("endpoints")]
    public Dictionary<string, string> Endpoints { get; set; } = [];

    /// <summary>
    /// Minimum milliseconds between consecutive calls to this provider.
    /// <c>0</c> = no throttle. Examples: Apple Books 300, Wikidata 1100, Audnexus 0.
    /// </summary>
    [JsonPropertyName("throttle_ms")]
    public int ThrottleMs { get; set; }

    /// <summary>
    /// Maximum concurrent requests to this provider.
    /// Defaults to <c>1</c> (serial). Increase for providers that allow parallel calls.
    /// </summary>
    [JsonPropertyName("max_concurrency")]
    public int MaxConcurrency { get; set; } = 1;
}
