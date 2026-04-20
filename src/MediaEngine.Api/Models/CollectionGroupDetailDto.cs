using MediaEngine.Domain.Models;
using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

/// <summary>Detail view of a collection with all child works for sub-page rendering.</summary>
public sealed class CollectionGroupDetailDto
{
    [JsonPropertyName("collection_id")]
    public required Guid CollectionId { get; init; }

    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("root_work_id")]
    public Guid? RootWorkId { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("primary_media_type")]
    public string? PrimaryMediaType { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("background_url")]
    public string? BackgroundUrl { get; init; }

    [JsonPropertyName("banner_url")]
    public string? BannerUrl { get; init; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tagline")]
    public string? Tagline { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("writer")]
    public string? Writer { get; init; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("year_range")]
    public string? YearRange { get; init; }

    [JsonPropertyName("genre")]
    public string? Genre { get; init; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; init; }

    [JsonPropertyName("total_duration")]
    public string? TotalDuration { get; init; }

    /// <summary>Network or streaming service (e.g. "HBO", "Netflix"). Populated for TV media type only.</summary>
    [JsonPropertyName("network")]
    public string? Network { get; init; }

    /// <summary>Number of distinct seasons. Populated for TV media type only.</summary>
    [JsonPropertyName("season_count")]
    public int? SeasonCount { get; init; }

    /// <summary>Artist photo URL (headshot). Populated for artist drill-down only.</summary>
    [JsonPropertyName("artist_photo_url")]
    public string? ArtistPhotoUrl { get; init; }

    /// <summary>Artist's person record ID. Populated for artist drill-down only â€” used to open the person detail drawer.</summary>
    [JsonPropertyName("artist_person_id")]
    public Guid? ArtistPersonId { get; init; }

    /// <summary>
    /// Top billed cast (actors) for the container, capped at 10 entries.
    /// Populated for TV shows and movies from Wikidata P161 (cast member) stored
    /// in <c>canonical_value_arrays</c> on the root parent Work. Each entry is
    /// resolved to a Person record so the Dashboard can open the people drawer
    /// on click.
    /// </summary>
    [JsonPropertyName("top_cast")]
    public List<CollectionGroupPersonDto> TopCast { get; init; } = [];

    /// <summary>Child works grouped into seasons. Populated for TV media type only.</summary>
    [JsonPropertyName("seasons")]
    public List<CollectionGroupSeasonDto> Seasons { get; init; } = [];

    /// <summary>Flat list of works sorted by ordinal. Populated for non-TV media types.</summary>
    [JsonPropertyName("works")]
    public List<CollectionGroupWorkDto> Works { get; init; } = [];
}

/// <summary>A single TV season within a collection group detail response.</summary>
public sealed class CollectionGroupSeasonDto
{
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; init; }

    [JsonPropertyName("season_label")]
    public string? SeasonLabel { get; init; }

    /// <summary>Section cover URL (e.g. album art for an artist's album). Optional.</summary>
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    /// <summary>Collection ID for this section if it maps to a real collection (e.g. an album collection). Used for tile click navigation.</summary>
    [JsonPropertyName("album_collection_id")]
    public Guid? AlbumCollectionId { get; init; }

    /// <summary>Section year. Optional.</summary>
    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("episodes")]
    public List<CollectionGroupWorkDto> Episodes { get; init; } = [];
}

/// <summary>A single work within a collection group detail response.</summary>
public sealed class CollectionGroupWorkDto
{
    [JsonPropertyName("work_id")]
    public required Guid WorkId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("ordinal")]
    public int? Ordinal { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("background_url")]
    public string? BackgroundUrl { get; init; }

    [JsonPropertyName("banner_url")]
    public string? BannerUrl { get; init; }

    [JsonPropertyName("hero_url")]
    public string? HeroUrl { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("season")]
    public string? Season { get; init; }

    [JsonPropertyName("episode")]
    public string? Episode { get; init; }

    [JsonPropertyName("track_number")]
    public string? TrackNumber { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("writer")]
    public string? Writer { get; init; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("playback_summary")]
    public PlaybackTechnicalSummary? PlaybackSummary { get; init; }

    /// <summary>Whether this work corresponds to an actual file in the library (true) or an unowned track surfaced from Wikidata child entity data (false).</summary>
    [JsonPropertyName("is_owned")]
    public bool IsOwned { get; init; } = true;

    /// <summary>Stage 1 (retail identification) pipeline status for this work's primary asset.</summary>
    [JsonPropertyName("stage1")]
    public LibraryPipelineStageDto? Stage1 { get; init; }

    /// <summary>Stage 2 (Wikidata bridge) pipeline status for this work's primary asset.</summary>
    [JsonPropertyName("stage2")]
    public LibraryPipelineStageDto? Stage2 { get; init; }

    /// <summary>Stage 3 (universe enrichment) pipeline status for this work's primary asset.</summary>
    [JsonPropertyName("stage3")]
    public LibraryPipelineStageDto? Stage3 { get; init; }
}

/// <summary>
/// A lightweight person reference for cast/crew chips on group detail views.
/// The <see cref="PersonId"/> is null when no local Person record exists for
/// the name (e.g. Wikidata returned a cast member we haven't reconciled yet);
/// the Dashboard still renders the chip but click-through is disabled.
/// </summary>
public sealed class CollectionGroupPersonDto
{
    [JsonPropertyName("person_id")]
    public Guid? PersonId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("actor_person_id")]
    public Guid? ActorPersonId { get; init; }

    [JsonPropertyName("actor_name")]
    public string? ActorName { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; init; }

    [JsonPropertyName("actor_headshot_url")]
    public string? ActorHeadshotUrl { get; init; }

    [JsonPropertyName("character_name")]
    public string? CharacterName { get; init; }

    [JsonPropertyName("character_qid")]
    public string? CharacterQid { get; init; }

    [JsonPropertyName("character_image_url")]
    public string? CharacterImageUrl { get; init; }
}

/// <summary>Pipeline stage indicator (state + label) for a single hydration stage.</summary>
public sealed class LibraryPipelineStageDto
{
    [JsonPropertyName("state")]
    public string State { get; init; } = "pending";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";
}

/// <summary>
/// A Content Group collection â€” a Universe-type collection (album, TV series, book series, movie series)
/// that contains works of a single media type.
/// </summary>
public sealed class ContentGroupDto
{
    [JsonPropertyName("collection_id")]
    public Guid CollectionId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("primary_media_type")]
    public string PrimaryMediaType { get; init; } = string.Empty;

    [JsonPropertyName("work_count")]
    public int WorkCount { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("background_url")]
    public string? BackgroundUrl { get; init; }

    [JsonPropertyName("banner_url")]
    public string? BannerUrl { get; init; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tagline")]
    public string? Tagline { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("writer")]
    public string? Writer { get; init; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("universe_status")]
    public string UniverseStatus { get; init; } = "Unknown";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Artist headshot URL â€” populated only when GroupByField == "artist".</summary>
    [JsonPropertyName("artist_photo_url")]
    public string? ArtistPhotoUrl { get; init; }

    /// <summary>Person ID of the matched artist â€” populated alongside ArtistPhotoUrl.</summary>
    [JsonPropertyName("artist_person_id")]
    public Guid? ArtistPersonId { get; init; }

    /// <summary>Network name â€” populated for TV show groups.</summary>
    [JsonPropertyName("network")]
    public string? Network { get; init; }

    /// <summary>Year â€” first air date year for the group.</summary>
    [JsonPropertyName("year")]
    public string? Year { get; init; }

    /// <summary>Number of distinct seasons â€” populated for TV show groups.</summary>
    [JsonPropertyName("season_count")]
    public int? SeasonCount { get; init; }

    /// <summary>Number of distinct albums â€” populated for Music artist groups.</summary>
    [JsonPropertyName("album_count")]
    public int? AlbumCount { get; init; }
}
