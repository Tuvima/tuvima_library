using System.Text.Json.Serialization;
using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Api.Models;

/// <summary>
/// DTO for non-Universe collections displayed in the managed collections surface.
/// Includes management fields (enabled, featured, rules) not present in <see cref="CollectionDto"/>.
/// </summary>
public class ManagedCollectionDto
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

/// <summary>
/// DTO for the rich Collections hub. This intentionally keeps classification
/// decisions server-side so the UI does not guess system/global/user semantics.
/// </summary>
public sealed class CollectionManagementCatalogDto : ManagedCollectionDto
{
    [JsonPropertyName("family")]
    public string Family { get; init; } = "User";

    [JsonPropertyName("system_key")]
    public string? SystemKey { get; init; }

    [JsonPropertyName("primary_lane")]
    public string PrimaryLane { get; init; } = "CrossMedia";

    [JsonPropertyName("is_global")]
    public bool IsGlobal { get; init; }

    [JsonPropertyName("is_system")]
    public bool IsSystem { get; init; }

    [JsonPropertyName("is_cross_media")]
    public bool IsCrossMedia { get; init; }

    [JsonPropertyName("watch_count")]
    public int WatchCount { get; init; }

    [JsonPropertyName("listen_count")]
    public int ListenCount { get; init; }

    [JsonPropertyName("read_count")]
    public int ReadCount { get; init; }

    [JsonPropertyName("other_count")]
    public int OtherCount { get; init; }

    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; init; }

    [JsonPropertyName("can_rename")]
    public bool CanRename { get; init; }

    [JsonPropertyName("can_toggle_global")]
    public bool CanToggleGlobal { get; init; }

    [JsonPropertyName("artwork_items")]
    public IReadOnlyList<CollectionArtworkItemDto> ArtworkItems { get; init; } = [];

    public static CollectionManagementCatalogDto FromDomain(
        Collection collection,
        int itemCount,
        Profile? activeProfile,
        CollectionCatalogClassification classification,
        CollectionMediaCounts mediaCounts,
        IReadOnlyList<CollectionArtworkItemDto>? artworkItems = null)
    {
        var baseDto = FromDomain(collection, itemCount, activeProfile);
        var isGlobal = string.Equals(baseDto.Visibility, CollectionAccessPolicy.SharedVisibility, StringComparison.OrdinalIgnoreCase);
        var canEdit = CollectionAccessPolicy.CanEdit(collection, activeProfile);
        var canManageGlobal = CollectionAccessPolicy.CanManageSharedCollections(activeProfile);

        return new CollectionManagementCatalogDto
        {
            Id = baseDto.Id,
            Name = baseDto.Name,
            Description = baseDto.Description,
            IconName = baseDto.IconName,
            SquareArtworkUrl = baseDto.SquareArtworkUrl,
            CollectionType = classification.CollectionType,
            Scope = baseDto.Scope,
            ProfileId = baseDto.ProfileId,
            Visibility = baseDto.Visibility,
            IsEnabled = baseDto.IsEnabled,
            IsFeatured = baseDto.IsFeatured,
            MinItems = baseDto.MinItems,
            RuleJson = baseDto.RuleJson,
            Resolution = baseDto.Resolution,
            RuleHash = baseDto.RuleHash,
            MatchMode = baseDto.MatchMode,
            SortField = baseDto.SortField,
            SortDirection = baseDto.SortDirection,
            LiveUpdating = baseDto.LiveUpdating,
            RefreshSchedule = baseDto.RefreshSchedule,
            ItemCount = baseDto.ItemCount,
            Status = baseDto.Status,
            CreatedAt = baseDto.CreatedAt,
            ModifiedAt = baseDto.ModifiedAt,
            CanEdit = canEdit,
            CanShare = canManageGlobal,
            Family = classification.Family,
            SystemKey = classification.SystemKey,
            PrimaryLane = classification.PrimaryLaneOverride ?? mediaCounts.PrimaryLane,
            IsGlobal = isGlobal,
            IsSystem = classification.IsSystem,
            IsCrossMedia = classification.PrimaryLaneOverride is null && mediaCounts.IsCrossMedia,
            WatchCount = mediaCounts.WatchCount,
            ListenCount = mediaCounts.ListenCount,
            ReadCount = mediaCounts.ReadCount,
            OtherCount = mediaCounts.OtherCount,
            CanDelete = canEdit && !classification.IsSystem && CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType),
            CanRename = canEdit && !classification.IsSystem,
            CanToggleGlobal = canManageGlobal && !classification.IsSystem && CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType),
            ArtworkItems = artworkItems ?? [],
        };
    }
}

public sealed class CollectionArtworkItemDto
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("artwork_shape")]
    public string ArtworkShape { get; init; } = "square";
}

public sealed record CollectionCatalogClassification(
    string Family,
    string CollectionType,
    string? SystemKey,
    bool IsSystem,
    string? PrimaryLaneOverride = null);

public sealed record CollectionMediaCounts(
    int WatchCount,
    int ListenCount,
    int ReadCount,
    int OtherCount,
    int TvCount = 0)
{
    public int TotalCount => WatchCount + ListenCount + ReadCount + OtherCount;

    public bool IsCrossMedia =>
        new[] { WatchCount, ListenCount, ReadCount, OtherCount }
            .Count(count => count > 0) != 1;

    public string PrimaryLane
    {
        get
        {
            if (IsCrossMedia)
                return "CrossMedia";

            if (WatchCount > 0)
                return "Watch";

            if (ListenCount > 0)
                return "Listen";

            if (ReadCount > 0)
                return "Read";

            return "CrossMedia";
        }
    }
}
