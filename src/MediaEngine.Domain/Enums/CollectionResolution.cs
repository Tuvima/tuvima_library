namespace MediaEngine.Domain.Enums;

/// <summary>How a collection's items are resolved.</summary>
public enum CollectionResolution
{
    /// <summary>Items resolved by evaluating RuleJson predicates against the database at query time.</summary>
    Query,
    /// <summary>Items pre-assigned via Work.collection_id FK or collection_items junction table.</summary>
    Materialized,
}
