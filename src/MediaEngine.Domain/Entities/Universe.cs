using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Domain.Entities;

/// <summary>
/// A logical grouping of related <see cref="Hub"/> instances that share a narrative
/// or thematic universe (e.g. the Marvel Cinematic Universe).
///
/// Spec invariant: "A Universe MAY contain multiple Hubs, but a Hub MUST belong
/// to a maximum of one Universe."
///
/// Note: Universe has no dedicated table in the Phase 4 storage schema.
/// Membership is recorded via <c>hubs.universe_id</c>.  This entity is a
/// first-class domain concept only.
/// </summary>
public sealed class Universe
{
    /// <summary>Stable identifier. Stored as <c>hubs.universe_id</c> on member Hubs.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable name (e.g. "Marvel Cinematic Universe").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// All Hubs that declare this Universe as their parent.
    /// Populated by the application layer; not persisted directly.
    /// </summary>
    public List<Hub> Hubs { get; set; } = [];
}
