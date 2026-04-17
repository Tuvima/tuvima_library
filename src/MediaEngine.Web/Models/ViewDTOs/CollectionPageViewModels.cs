namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>View model for a collection displayed on the /collections page.</summary>
public sealed class CollectionListItemViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? IconName { get; init; }
    public string CollectionType { get; init; } = "Smart";
    public string Resolution { get; init; } = "query";
    public string Scope { get; init; } = "library";
    public Guid? ProfileId { get; init; }
    public string Visibility { get; init; } = "private";
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
    public bool CanEdit { get; init; }
    public bool CanShare { get; init; }
    public bool IsShared => string.Equals(Visibility, "shared", StringComparison.OrdinalIgnoreCase);
    public bool IsPlaylist => string.Equals(CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A resolved item returned from collection rule evaluation.</summary>
public sealed class CollectionResolvedItemViewModel
{
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string? Creator { get; init; }
    public string MediaType { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? Year { get; init; }
}

/// <summary>Result of a collection preview (rule evaluation without saving).</summary>
public sealed class CollectionPreviewResult
{
    public int Count { get; init; }
    public List<CollectionResolvedItemViewModel> Items { get; init; } = [];
}

/// <summary>A rule predicate for the collection builder.</summary>
public sealed class CollectionRulePredicateViewModel
{
    public string Field { get; set; } = "media_type";
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public string[]? Values { get; set; }
}
