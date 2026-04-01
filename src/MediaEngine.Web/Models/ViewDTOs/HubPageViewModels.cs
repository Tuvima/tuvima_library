namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>View model for a hub displayed on the /hubs page.</summary>
public sealed class HubListItemViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? IconName { get; init; }
    public string HubType { get; init; } = "Smart";
    public string Resolution { get; init; } = "query";
    public string Scope { get; init; } = "library";
    public bool IsEnabled { get; init; } = true;
    public bool IsFeatured { get; init; }
    public int ItemCount { get; init; }
    public string? RuleJson { get; init; }
    public string? RuleHash { get; init; }
    public string MatchMode { get; init; } = "all";
    public string? SortField { get; init; }
    public string SortDirection { get; init; } = "desc";
    public bool LiveUpdating { get; init; } = true;
    public string Status => !IsEnabled ? "Disabled" : ItemCount == 0 ? "Empty" : "Active";
    public string? PrimaryMediaType { get; init; }
    public string? CoverUrl { get; init; }
    public string? Creator { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A resolved item returned from hub rule evaluation.</summary>
public sealed class HubResolvedItemViewModel
{
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string? Creator { get; init; }
    public string MediaType { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? Year { get; init; }
}

/// <summary>Result of a hub preview (rule evaluation without saving).</summary>
public sealed class HubPreviewResult
{
    public int Count { get; init; }
    public List<HubResolvedItemViewModel> Items { get; init; } = [];
}

/// <summary>A rule predicate for the hub builder.</summary>
public sealed class HubRulePredicateViewModel
{
    public string Field { get; set; } = "media_type";
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public string[]? Values { get; set; }
}
