using System.Text.Json.Serialization;
using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Api.Models;

/// <summary>
/// DTO for non-Universe collections displayed in the managed collections surface.
/// Includes management fields (enabled, featured, rules) not present in <see cref="CollectionDto"/>.
/// </summary>
public sealed class ManagedCollectionDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; init; }

    [JsonPropertyName("square_artwork_url")]
    public string? SquareArtworkUrl { get; init; }

    [JsonPropertyName("collection_type")]
    public string CollectionType { get; init; } = "Smart";

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "library";

    [JsonPropertyName("profile_id")]
    public Guid? ProfileId { get; init; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = CollectionAccessPolicy.PrivateVisibility;

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; } = true;

    [JsonPropertyName("is_featured")]
    public bool IsFeatured { get; init; }

    [JsonPropertyName("min_items")]
    public int MinItems { get; init; }

    [JsonPropertyName("rule_json")]
    public string? RuleJson { get; init; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; init; } = "query";

    [JsonPropertyName("rule_hash")]
    public string? RuleHash { get; init; }

    [JsonPropertyName("match_mode")]
    public string MatchMode { get; init; } = "all";

    [JsonPropertyName("sort_field")]
    public string? SortField { get; init; }

    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; init; } = "desc";

    [JsonPropertyName("live_updating")]
    public bool LiveUpdating { get; init; } = true;

    [JsonPropertyName("refresh_schedule")]
    public string? RefreshSchedule { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Active";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; init; }

    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; init; }

    [JsonPropertyName("can_share")]
    public bool CanShare { get; init; }

    public static ManagedCollectionDto FromDomain(
        Collection collection,
        int itemCount,
        Profile? activeProfile) => new()
    {
        Id              = collection.Id,
        Name            = collection.DisplayName ?? $"Collection {collection.Id.ToString("N")[..8]}",
        Description     = collection.Description,
        IconName        = collection.IconName,
        SquareArtworkUrl = string.IsNullOrWhiteSpace(collection.SquareArtworkPath)
            ? null
            : activeProfile is null
                ? $"/collections/{collection.Id}/square-artwork"
                : $"/collections/{collection.Id}/square-artwork?profileId={activeProfile.Id:D}",
        CollectionType         = collection.CollectionType,
        Scope           = collection.Scope,
        ProfileId       = collection.ProfileId,
        Visibility      = CollectionAccessPolicy.ResolveVisibility(collection),
        IsEnabled       = collection.IsEnabled,
        IsFeatured      = collection.IsFeatured,
        MinItems        = collection.MinItems,
        RuleJson        = collection.RuleJson,
        Resolution      = collection.Resolution,
        RuleHash        = collection.RuleHash,
        MatchMode       = collection.MatchMode,
        SortField       = collection.SortField,
        SortDirection   = collection.SortDirection,
        LiveUpdating    = collection.LiveUpdating,
        RefreshSchedule = collection.RefreshSchedule,
        ItemCount       = itemCount,
        Status          = !collection.IsEnabled ? "Disabled" : itemCount == 0 ? "Empty" : "Active",
        CreatedAt       = collection.CreatedAt,
        ModifiedAt      = collection.ModifiedAt,
        CanEdit         = CollectionAccessPolicy.CanEdit(collection, activeProfile),
        CanShare        = CollectionAccessPolicy.CanManageSharedCollections(activeProfile),
    };
}
