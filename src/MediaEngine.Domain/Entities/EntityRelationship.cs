namespace MediaEngine.Domain.Entities;

/// <summary>
/// A directed graph edge connecting two entities in the universe graph.
///
/// Subject and object are Wikidata QIDs referencing <see cref="FictionalEntity"/>
/// or <see cref="Person"/> records. The relationship type corresponds to a
/// Wikidata property (see <see cref="Constants.RelationshipType"/>).
///
/// A relationship is a statement, not merely a subject/predicate/object triple.
/// Distinct adaptation, temporal, source, and spoiler-qualified statements are
/// preserved as separate facts while their qualifiers remain queryable.
///
/// Maps 1:1 to a row in the <c>entity_relationships</c> table.
/// </summary>
public sealed class EntityRelationship
{
    /// <summary>Stable row identifier (UUID stored as a 16-byte BLOB in SQLite).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable idempotency key for this precise statement and its qualifiers.
    /// Re-enrichment may safely upsert the same fact without collapsing a scoped fact.
    /// </summary>
    public string StatementKey { get; set; } = string.Empty;

    /// <summary>
    /// Wikidata QID of the source entity.
    /// Example: <c>"Q937618"</c> (Paul Atreides).
    /// </summary>
    public string SubjectQid { get; set; } = string.Empty;

    /// <summary>
    /// The type of relationship. One of <see cref="Constants.RelationshipType"/> constants.
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

    /// <summary>Provider that asserted the fact, if known.</summary>
    public string? SourceProvider { get; set; }

    /// <summary>Canonical source classification such as Wikidata or Plugin.</summary>
    public string Provenance { get; set; } = "Wikidata";

    /// <summary>Whether the fact is supplemental rather than canonical graph data.</summary>
    public bool IsSupplemental { get; set; }

    /// <summary>
    /// Optional: the work QID providing context for this relationship.
    /// Used for performer relationships where the actor plays a character
    /// in a specific film (e.g. <c>"Q104686073"</c> = Dune 2021).
    /// </summary>
    public string? ContextWorkQid { get; set; }

    /// <summary>When this relationship edge was first discovered.</summary>
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>ISO 8601 start time qualifier (Wikidata P580) — when this relationship began.</summary>
    public string? StartTime { get; set; }

    /// <summary>ISO 8601 end time qualifier (Wikidata P582) — when this relationship ended.</summary>
    public string? EndTime { get; set; }

    /// <summary>Normalized qualifiers retained with this fact.</summary>
    public List<EntityRelationshipQualifier> Qualifiers { get; set; } = [];
}
