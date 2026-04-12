using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Domain.Entities;

/// <summary>
/// A logical grouping of related <see cref="Collection"/> instances that share a narrative
/// or thematic universe (e.g. the Marvel Cinematic Universe).
///
/// Spec invariant: "A Universe MAY contain multiple Collections, but a Collection MUST belong
/// to a maximum of one Universe."
///
/// Note: Universe has no dedicated table in the Phase 4 storage schema.
/// Membership is recorded via <c>collections.universe_id</c>.  This entity is a
/// first-class domain concept only.
/// </summary>
public sealed class Universe
{
    /// <summary>Stable identifier. Stored as <c>collections.universe_id</c> on member Collections.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable name (e.g. "Marvel Cinematic Universe").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// All Collections that declare this Universe as their parent.
    /// Populated by the application layer; not persisted directly.
    /// </summary>
    public List<Collection> Collections { get; set; } = [];
}
