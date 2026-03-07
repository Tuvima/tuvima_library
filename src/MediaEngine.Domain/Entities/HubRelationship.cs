namespace MediaEngine.Domain.Entities;

/// <summary>
/// A multi-dimensional grouping signal that binds a Hub to a Wikidata concept.
/// Each relationship represents a Wikidata property linking the Hub's Works
/// to a shared franchise, series, fictional universe, or narrative chain.
///
/// Stored in the <c>hub_relationships</c> table.
/// </summary>
public sealed class HubRelationship
{
    public Guid Id { get; set; }
    public Guid HubId { get; set; }

    /// <summary>
    /// Relationship type: "franchise", "series", "fictional_universe",
    /// "based_on", "narrative_chain".
    /// </summary>
    public string RelType { get; set; } = string.Empty;

    /// <summary>Wikidata QID of the relationship target (e.g. Q7584 = Middle-earth).</summary>
    public string RelQid { get; set; } = string.Empty;

    /// <summary>Human-readable label (e.g. "Middle-earth", "The Expanse").</summary>
    public string? RelLabel { get; set; }

    public double Confidence { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
}
