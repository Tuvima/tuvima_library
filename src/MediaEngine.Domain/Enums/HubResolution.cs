namespace MediaEngine.Domain.Enums;

/// <summary>How a hub's items are resolved.</summary>
public enum HubResolution
{
    /// <summary>Items resolved by evaluating RuleJson predicates against the database at query time.</summary>
    Query,
    /// <summary>Items pre-assigned via Work.hub_id FK or hub_items junction table.</summary>
    Materialized,
}
