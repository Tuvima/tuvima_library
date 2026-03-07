using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Typed configuration model for <c>config/hubs.json</c>.
/// Controls which Hub relationship types are displayed on the dashboard
/// and at what minimum Work count threshold.
/// </summary>
public sealed class HubDisplaySettings
{
    /// <summary>
    /// Map of relationship type name to its display configuration.
    /// Keys: franchise, series, fictional_universe, based_on, narrative_chain, characters, narrative_location.
    /// </summary>
    [JsonPropertyName("relationship_types")]
    public Dictionary<string, HubRelationshipTypeConfig> RelationshipTypes { get; set; } = new();
}

/// <summary>
/// Display configuration for a single Hub relationship type.
/// </summary>
public sealed class HubRelationshipTypeConfig
{
    /// <summary>Whether this type creates visible Hubs on the dashboard grid.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Minimum Works in a Hub before it appears on the dashboard.</summary>
    [JsonPropertyName("min_items")]
    public int MinItems { get; set; } = 1;

    /// <summary>Grouping priority tier (1 = highest priority, tried first).</summary>
    [JsonPropertyName("tier")]
    public int Tier { get; set; } = 1;
}
