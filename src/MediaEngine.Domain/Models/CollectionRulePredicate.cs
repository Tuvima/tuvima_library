namespace MediaEngine.Domain.Models;

/// <summary>
/// A single filter predicate in a collection's rule set.
/// Rules are stored as a JSON array of predicates in Collection.RuleJson.
/// </summary>
public sealed class CollectionRulePredicate
{
    /// <summary>The metadata field to filter on (e.g. "media_type", "genre", "artist").</summary>
    public string Field { get; set; } = "";

    /// <summary>Comparison operator: eq, neq, contains, gt, lt, gte, lte, in, between, like.</summary>
    public string Op { get; set; } = "eq";

    /// <summary>Single comparison value.</summary>
    public string? Value { get; set; }

    /// <summary>Multiple comparison values (for "in", "between" operators).</summary>
    public string[]? Values { get; set; }

    /// <summary>Returns the effective value(s) — prefers Values array, falls back to single Value.</summary>
    public string[] GetEffectiveValues() =>
        Values is { Length: > 0 } ? Values : (Value is not null ? [Value] : []);
}
