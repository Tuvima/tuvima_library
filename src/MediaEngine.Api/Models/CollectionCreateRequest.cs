using System.Text.Json.Serialization;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Models;

/// <summary>Request body for creating a new collection.</summary>
public sealed class CollectionCreateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; init; }

    [JsonPropertyName("collection_type")]
    public string CollectionType { get; init; } = "Custom";

    [JsonPropertyName("rules")]
    public List<CollectionRulePredicate> Rules { get; init; } = [];

    [JsonPropertyName("match_mode")]
    public string MatchMode { get; init; } = "all";

    [JsonPropertyName("sort_field")]
    public string? SortField { get; init; }

    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; init; } = "desc";

    [JsonPropertyName("display_limit")]
    public int DisplayLimit { get; init; }

    [JsonPropertyName("live_updating")]
    public bool LiveUpdating { get; init; } = true;

    [JsonPropertyName("placements")]
    public List<PlacementRequest>? Placements { get; init; }
}

public sealed class PlacementRequest
{
    [JsonPropertyName("location")]
    public string Location { get; init; } = "";

    [JsonPropertyName("position")]
    public int Position { get; init; }

    [JsonPropertyName("display_limit")]
    public int DisplayLimit { get; init; }

    [JsonPropertyName("display_mode")]
    public string DisplayMode { get; init; } = "swimlane";
}

/// <summary>Request body for updating a collection.</summary>
public sealed class CollectionUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; init; }

    [JsonPropertyName("rules")]
    public List<CollectionRulePredicate>? Rules { get; init; }

    [JsonPropertyName("match_mode")]
    public string? MatchMode { get; init; }

    [JsonPropertyName("sort_field")]
    public string? SortField { get; init; }

    [JsonPropertyName("sort_direction")]
    public string? SortDirection { get; init; }

    [JsonPropertyName("live_updating")]
    public bool? LiveUpdating { get; init; }

    [JsonPropertyName("is_enabled")]
    public bool? IsEnabled { get; init; }

    [JsonPropertyName("is_featured")]
    public bool? IsFeatured { get; init; }
}

/// <summary>Preview request -- evaluate rules without saving.</summary>
public sealed class CollectionPreviewRequest
{
    [JsonPropertyName("rules")]
    public List<CollectionRulePredicate> Rules { get; init; } = [];

    [JsonPropertyName("match_mode")]
    public string MatchMode { get; init; } = "all";

    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;
}
