namespace MediaEngine.Domain.Entities;

/// <summary>
/// A directed graph edge connecting two entities in the universe graph.
///
/// Subject and object are Wikidata QIDs referencing <see cref="FictionalEntity"/>
/// or <see cref="Person"/> records. The relationship type corresponds to a
/// Wikidata property (see <see cref="Enums.RelationshipType"/>).
///
/// UNIQUE constraint on (subject_qid, relationship_type, object_qid) prevents
/// duplicate edges. Idempotent inserts via INSERT OR IGNORE.
///
/// Maps 1:1 to a row in the <c>entity_relationships</c> table.
/// </summary>
public sealed class EntityRelationship
{
    /// <summary>Stable row identifier (UUID → TEXT in SQLite).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Wikidata QID of the source entity.
    /// Example: <c>"Q937618"</c> (Paul Atreides).
    /// </summary>
    public string SubjectQid { get; set; } = string.Empty;

    /// <summary>
    /// The type of relationship. One of <see cref="Enums.RelationshipType"/> constants.
    /// Example: <c>"father"</c>, <c>"member_of"</c>, <c>"performer"</c>.
    /// </summary>
    public string RelationshipTypeValue { get; set; } = string.Empty;

    /// <summary>
    /// Wikidata QID of the target entity.
    /// Example: <c>"Q937620"</c> (Duke Leto).
    /// </summary>
    public string ObjectQid { get; set; } = string.Empty;

    /// <summary>
    /// Confidence of this relationship (0.0–1.0).
    /// Typically derived from the Wikidata claim confidence.
    /// </summary>
    public double Confidence { get; set; } = 0.9;

    /// <summary>
    /// Optional: the work QID providing context for this relationship.
    /// Used for performer relationships where the actor plays a character
    /// in a specific film (e.g. <c>"Q104686073"</c> = Dune 2021).
    /// </summary>
    public string? ContextWorkQid { get; set; }

    /// <summary>When this relationship edge was first discovered.</summary>
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
}
