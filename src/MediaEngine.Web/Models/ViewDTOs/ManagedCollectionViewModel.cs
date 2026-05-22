using System.Text.Json.Serialization;
using MediaEngine.Domain.Models;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a Content Group returned by GET /collections/content-groups.
/// Represents a Universe-type collection (album, TV series, book series, movie series)
/// shown in the Content Groups section of the collection management surfaces.
/// </summary>
public sealed class ContentGroupViewModel
{
    [JsonPropertyName("collection_id")]
    public Guid CollectionId { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("primary_media_type")]
    public string PrimaryMediaType { get; set; } = string.Empty;

    [JsonPropertyName("work_count")]
    public int WorkCount { get; set; }

    [JsonPropertyName("distinct_title_count")]
    public int? DistinctTitleCount { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("background_url")]
    public string? BackgroundUrl { get; set; }

    [JsonPropertyName("banner_url")]
    public string? BannerUrl { get; set; }

    [JsonPropertyName("hero_url")]
    public string? HeroUrl { get; set; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; set; }

    [JsonPropertyName("cover_aspect_class")]
    public string? CoverAspectClass { get; set; }

    [JsonPropertyName("square_aspect_class")]
    public string? SquareAspectClass { get; set; }

    [JsonPropertyName("background_aspect_class")]
    public string? BackgroundAspectClass { get; set; }

    [JsonPropertyName("banner_aspect_class")]
    public string? BannerAspectClass { get; set; }

    [JsonPropertyName("cover_width_px")]
    public int? CoverWidthPx { get; set; }

    [JsonPropertyName("cover_height_px")]
    public int? CoverHeightPx { get; set; }

    [JsonPropertyName("square_width_px")]
    public int? SquareWidthPx { get; set; }

    [JsonPropertyName("square_height_px")]
    public int? SquareHeightPx { get; set; }

    [JsonPropertyName("background_width_px")]
    public int? BackgroundWidthPx { get; set; }

    [JsonPropertyName("background_height_px")]
    public int? BackgroundHeightPx { get; set; }

    [JsonPropertyName("banner_width_px")]
    public int? BannerWidthPx { get; set; }

    [JsonPropertyName("banner_height_px")]
    public int? BannerHeightPx { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("writer")]
    public string? Writer { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("universe_status")]
    public string UniverseStatus { get; set; } = "Unknown";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("artist_photo_url")]
    public string? ArtistPhotoUrl { get; set; }

    [JsonPropertyName("artist_person_id")]
    public Guid? ArtistPersonId { get; set; }

    [JsonPropertyName("network")]
    public string? Network { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("season_count")]
    public int? SeasonCount { get; set; }

    [JsonPropertyName("album_count")]
    public int? AlbumCount { get; set; }

    // ── Computed display helpers ──

    public string MediaTypeIcon => PrimaryMediaType switch
    {
        "TV"         => "LiveTv",
        "Music"      => "MusicNote",
        "Books"      => "MenuBook",
        "Audiobooks" => "Headphones",
        "Movies"     => "VideoLibrary",
        "Comics"     => "AutoStories",
        _            => "Folder",
    };

    public string MediaTypeColor => PrimaryMediaType switch
    {
        "TV"         => "var(--tl-media-video)",
        "Music"      => "#1ED760",
        "Books"      => "var(--tl-status-success)",
        "Audiobooks" => "#84CC16",
        "Movies"     => "var(--tl-status-info)",
        "Comics"     => "var(--tl-media-comic)",
        _            => "var(--tl-status-info)",
    };
}

/// <summary>
/// Dashboard view model for a managed collection (Smart, System, Mix, Playlist)
/// displayed in the collection management surfaces.
/// </summary>
public sealed class ManagedCollectionViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; set; }

    [JsonPropertyName("square_artwork_url")]
    public string? SquareArtworkUrl { get; set; }

    [JsonPropertyName("collection_type")]
    public string CollectionType { get; set; } = "Smart";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "library";

    [JsonPropertyName("profile_id")]
    public Guid? ProfileId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "private";

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("is_featured")]
    public bool IsFeatured { get; set; }

    [JsonPropertyName("min_items")]
    public int MinItems { get; set; }

    [JsonPropertyName("rule_json")]
    public string? RuleJson { get; set; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "query";

    [JsonPropertyName("rule_hash")]
    public string? RuleHash { get; set; }

    [JsonPropertyName("match_mode")]
    public string MatchMode { get; set; } = "all";

    [JsonPropertyName("sort_field")]
    public string? SortField { get; set; }

    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; set; } = "desc";

    [JsonPropertyName("live_updating")]
    public bool LiveUpdating { get; set; } = true;

    [JsonPropertyName("refresh_schedule")]
    public string? RefreshSchedule { get; set; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; set; }

    [JsonPropertyName("can_share")]
    public bool CanShare { get; set; }

    // ── Computed display helpers ──

    public bool IsShared =>
        string.Equals(Visibility, "shared", StringComparison.OrdinalIgnoreCase);

    public string TypeColor => CollectionType switch
    {
        "Smart"    => "var(--tl-status-info)",
        "System"   => "var(--tl-status-success)",
        "Mix"      => "#A78BFA",
        "Playlist" => "var(--tl-accent-primary)",
        _          => "var(--tl-status-info)",
    };

    public string TypeLabel => CollectionType switch
    {
        "Smart"    => "Smart",
        "System"   => "System",
        "Mix"      => "Mix",
        "Playlist" => "Playlist",
        "PlaylistFolder" => "Folder",
        _          => CollectionType,
    };

    public string StatusColor => Status switch
    {
        "Active"   => "var(--tl-status-success)",
        "Disabled" => "rgba(255,255,255,0.4)",
        "Empty"    => "var(--tl-status-warning)",
        _          => "var(--tl-status-success)",
    };

    public string StatusLabel => Status switch
    {
        "Active"   => "Active",
        "Disabled" => "Disabled",
        "Empty"    => "Empty",
        _          => Status,
    };
}

/// <summary>
/// Rich catalog item for the Collections hub. Classification is supplied by the
/// Engine so the UI stays aligned with system/global/user rules.
/// </summary>
public sealed class CollectionManagementCatalogViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_name")]
    public string? IconName { get; set; }

    [JsonPropertyName("square_artwork_url")]
    public string? SquareArtworkUrl { get; set; }

    [JsonPropertyName("collection_type")]
    public string CollectionType { get; set; } = "Custom";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "user";

    [JsonPropertyName("profile_id")]
    public Guid? ProfileId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "private";

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("is_featured")]
    public bool IsFeatured { get; set; }

    [JsonPropertyName("rule_json")]
    public string? RuleJson { get; set; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "query";

    [JsonPropertyName("match_mode")]
    public string MatchMode { get; set; } = "all";

    [JsonPropertyName("sort_field")]
    public string? SortField { get; set; }

    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; set; } = "desc";

    [JsonPropertyName("live_updating")]
    public bool LiveUpdating { get; set; } = true;

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; set; }

    [JsonPropertyName("can_share")]
    public bool CanShare { get; set; }

    [JsonPropertyName("family")]
    public string Family { get; set; } = "User";

    [JsonPropertyName("system_key")]
    public string? SystemKey { get; set; }

    [JsonPropertyName("primary_lane")]
    public string PrimaryLane { get; set; } = "CrossMedia";

    [JsonPropertyName("is_global")]
    public bool IsGlobal { get; set; }

    [JsonPropertyName("is_system")]
    public bool IsSystem { get; set; }

    [JsonPropertyName("is_cross_media")]
    public bool IsCrossMedia { get; set; } = true;

    [JsonPropertyName("watch_count")]
    public int WatchCount { get; set; }

    [JsonPropertyName("listen_count")]
    public int ListenCount { get; set; }

    [JsonPropertyName("read_count")]
    public int ReadCount { get; set; }

    [JsonPropertyName("other_count")]
    public int OtherCount { get; set; }

    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; set; }

    [JsonPropertyName("can_rename")]
    public bool CanRename { get; set; }

    [JsonPropertyName("can_toggle_global")]
    public bool CanToggleGlobal { get; set; }

    [JsonPropertyName("artwork_items")]
    public List<CollectionArtworkItemViewModel> ArtworkItems { get; set; } = [];

    [JsonPropertyName("artwork_palette")]
    public ArtworkPalette ArtworkPalette { get; set; } = ArtworkPalette.TuvimaDefault();

    public string ArtworkUrl => SquareArtworkUrl ?? string.Empty;

    public string TypeLabel => CollectionType switch
    {
        "System" => "System",
        "Playlist" => "Playlist",
        "Smart" => "Smart",
        "Mix" => "Mix",
        "Custom" => "Custom Collection",
        "Universe" => "Generated Collection",
        "Series" => "Generated Collection",
        "ContentGroup" => "Generated Collection",
        _ => IsManual ? "Custom Collection" : "Generated Collection",
    };

    public string FamilyLabel => Family switch
    {
        "Global" => "Global",
        "System" => "System",
        "Discover" => "Discover",
        _ => "My Collection",
    };

    public bool IsManual =>
        string.Equals(Resolution, "materialized", StringComparison.OrdinalIgnoreCase);

    public string StatusLabel => !IsEnabled ? "Disabled" : ItemCount == 0 ? "Empty" : "Active";
}

public sealed class CollectionArtworkItemViewModel
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("primary_color")]
    public string? PrimaryColor { get; set; }

    [JsonPropertyName("secondary_color")]
    public string? SecondaryColor { get; set; }

    [JsonPropertyName("accent_color")]
    public string? AccentColor { get; set; }

    [JsonPropertyName("artwork_shape")]
    public string ArtworkShape { get; set; } = "square";
}

