using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Models;

/// <summary>
/// A single condition in a collection rule group.
/// </summary>
public sealed class CollectionRulePredicate
{
    /// <summary>The metadata field to filter on (e.g. "media_type", "genre", "artist").</summary>
    public string Field { get; set; } = "";

    /// <summary>Comparison operator: eq, neq, contains, gt, lt, gte, lte, in, between, like.</summary>
    public string Op { get; set; } = "eq";

    /// <summary>Single comparison value.</summary>
    public string? Value { get; set; }

    /// <summary>Human-readable label retained for portable QID-backed rules.</summary>
    public string? DisplayValue { get; set; }

    /// <summary>Multiple comparison values (for "in", "between" operators).</summary>
    public string[]? Values { get; set; }

    /// <summary>Returns the effective value(s) — prefers Values array, falls back to single Value.</summary>
    public string[] GetEffectiveValues() =>
        Values is { Length: > 0 } ? Values : (Value is not null ? [Value] : []);
}

/// <summary>A group of conditions evaluated using either ALL (AND) or ANY (OR).</summary>
public sealed class CollectionRuleGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MatchMode { get; set; } = "all";
    public List<CollectionRulePredicate> Conditions { get; set; } = [];
}

/// <summary>
/// Versioned smart-collection definition. Groups are combined with OR; conditions
/// inside a group use the group's match mode.
/// </summary>
public sealed class CollectionRuleDefinition
{
    public int Version { get; set; } = 1;
    public List<CollectionRuleGroup> Groups { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<CollectionRulePredicate> AllConditions =>
        Groups.SelectMany(group => group.Conditions).ToList();

    public static CollectionRuleDefinition SingleGroup(
        IEnumerable<CollectionRulePredicate> conditions,
        string matchMode = "all") => new()
    {
        Groups =
        [
            new CollectionRuleGroup
            {
                MatchMode = string.Equals(matchMode, "any", StringComparison.OrdinalIgnoreCase) ? "any" : "all",
                Conditions = conditions.ToList(),
            },
        ],
    };
}
