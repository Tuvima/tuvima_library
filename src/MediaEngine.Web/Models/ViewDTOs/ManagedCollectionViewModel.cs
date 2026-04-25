using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a Content Group returned by GET /collections/content-groups.
/// Represents a Universe-type collection (album, TV series, book series, movie series)
/// shown in the Content Groups section of the Vault Collections tab.
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
        "TV"         => "#FBBF24",
        "Music"      => "#1ED760",
        "Books"      => "#5DCAA5",
        "Audiobooks" => "#84CC16",
        "Movies"     => "#60A5FA",
        "Comics"     => "#FB923C",
        _            => "#60A5FA",
    };
}

/// <summary>
/// Dashboard view model for a managed collection (Smart, System, Mix, Playlist)
/// displayed in the Vault Collections tab.
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
        "Smart"    => "#60A5FA",
        "System"   => "#5DCAA5",
        "Mix"      => "#A78BFA",
        "Playlist" => "#C9922E",
        _          => "#60A5FA",
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
        "Active"   => "#5DCAA5",
        "Disabled" => "rgba(255,255,255,0.4)",
        "Empty"    => "#EF9F27",
        _          => "#5DCAA5",
    };

    public string StatusLabel => Status switch
    {
        "Active"   => "Active",
        "Disabled" => "Disabled",
        "Empty"    => "Empty",
        _          => Status,
    };
}
