using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Per-field provider priority override for a single metadata field.
///
/// When the scoring engine encounters a field listed here, it picks the winner
/// by walking the <see cref="Priority"/> list (first provider with a claim wins)
/// instead of using the default Wikidata-always-wins rule.
/// </summary>
public sealed class FieldPriorityOverride
{
    /// <summary>
    /// Ordered list of provider names (e.g. "wikipedia", "apple_api", "wikidata_reconciliation").
    /// The first provider in the list that has a claim for this field wins.
    /// Up to 3 entries.
    /// </summary>
    [JsonPropertyName("priority")]
    public List<string> Priority { get; set; } = [];

    /// <summary>
    /// Optional human-readable note explaining why this override exists.
    /// Not used by the engine — purely for configuration readability.
    /// </summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>
/// Configuration for per-field provider priority overrides.
///
/// Loaded from <c>config/field_priorities.json</c>. Fields listed in
/// <see cref="FieldOverrides"/> use provider-priority ordering instead
/// of the default Wikidata-always-wins rule. Fields NOT listed here
/// continue to use Wikidata as the unconditional authority.
///
/// <para>
/// This allows Wikipedia to win for descriptions (rich multi-paragraph
/// summaries vs Wikidata one-liners), retail providers to win for cover
/// art, etc. — while Wikidata remains the authority for structured data
/// like title, author, year, series, and bridge identifiers.
/// </para>
/// </summary>
public sealed class FieldPriorityConfiguration
{
    /// <summary>
    /// Maps claim key (e.g. "description", "cover", "biography") to its
    /// provider priority override. Fields not present here use the default
    /// Wikidata-always-wins scoring rule.
    /// </summary>
    [JsonPropertyName("field_overrides")]
    public Dictionary<string, FieldPriorityOverride> FieldOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
