namespace MediaEngine.Domain.Entities;

/// <summary>
/// A queryable, source-aware qualifier on a graph fact. The value is intentionally
/// text so QIDs, ISO times, fictional chronology, and source URLs retain their
/// original semantics without a schema column per Wikidata qualifier.
/// </summary>
public sealed class EntityRelationshipQualifier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RelationshipId { get; set; }
    public string QualifierType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueKind { get; set; } = "Text";
    public string? SourceProvider { get; set; }
    public string Provenance { get; set; } = "Wikidata";
    public bool IsSupplemental { get; set; }
    public double? Confidence { get; set; }
}
