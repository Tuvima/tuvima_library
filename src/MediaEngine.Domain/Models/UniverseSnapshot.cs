using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Models;

/// <summary>
/// A complete graph snapshot of a narrative universe for XML serialization.
/// Contains all entities, relationships, and the narrative hierarchy.
/// Written to <c>.universe/{root}/universe.xml</c> by <see cref="Contracts.IUniverseSidecarWriter"/>.
/// </summary>
public sealed class UniverseSnapshot
{
    /// <summary>The narrative root for this universe.</summary>
    public required NarrativeRoot Root { get; init; }

    /// <summary>All fictional entities in this universe.</summary>
    public required IReadOnlyList<FictionalEntitySnapshot> Entities { get; init; }

    /// <summary>All relationship edges between entities.</summary>
    public required IReadOnlyList<EntityRelationship> Relationships { get; init; }

    /// <summary>The narrative hierarchy (universe → franchise → series → works).</summary>
    public required IReadOnlyList<NarrativeHierarchyNode> Hierarchy { get; init; }

    /// <summary>When this snapshot was last built.</summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A fictional entity enriched with its work links and performer reference
/// for XML serialization.
/// </summary>
public sealed class FictionalEntitySnapshot
{
    /// <summary>The underlying entity record.</summary>
    public required FictionalEntity Entity { get; init; }

    /// <summary>All works this entity appears in.</summary>
    public required IReadOnlyList<WorkLinkSnapshot> WorkLinks { get; init; }

    /// <summary>
    /// All entity properties as key-value pairs (gender, species, etc.).
    /// Sourced from canonical values for this entity.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Properties { get; init; }

    /// <summary>
    /// The Person QID of the performer (actor) for this character, if any.
    /// Null for locations and organizations.
    /// </summary>
    public string? PerformerPersonQid { get; init; }

    /// <summary>
    /// The work QID providing context for the performer link.
    /// </summary>
    public string? PerformerWorkQid { get; init; }
}

/// <summary>A work linked to a fictional entity.</summary>
/// <param name="WorkQid">Wikidata QID of the work.</param>
/// <param name="WorkLabel">Human-readable work label.</param>
public sealed record WorkLinkSnapshot(string WorkQid, string? WorkLabel);

/// <summary>
/// A node in the narrative hierarchy tree.
/// </summary>
public sealed class NarrativeHierarchyNode
{
    /// <summary>Wikidata QID of this node.</summary>
    public required string Qid { get; init; }

    /// <summary>Human-readable label.</summary>
    public required string Label { get; init; }

    /// <summary>Level in the hierarchy (Universe, Franchise, Series, Work).</summary>
    public required string Level { get; init; }

    /// <summary>Child nodes (franchises, series, or works).</summary>
    public IReadOnlyList<NarrativeHierarchyNode> Children { get; init; } = [];
}
