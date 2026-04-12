using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a content group drill-down page.
/// Deserializes directly from GET /collections/{collectionId}/group-detail (snake_case JSON).
/// </summary>
public sealed class CollectionGroupDetailViewModel
{
    [JsonPropertyName("collection_id")]
    public Guid CollectionId { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("primary_media_type")]
    public string? PrimaryMediaType { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("year_range")]
    public string? YearRange { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_duration")]
    public string? TotalDuration { get; set; }

    /// <summary>Network or streaming service (e.g. "HBO", "Netflix"). Populated for TV shows only.</summary>
    [JsonPropertyName("network")]
    public string? Network { get; set; }

    /// <summary>Number of distinct seasons. Populated for TV shows only.</summary>
    [JsonPropertyName("season_count")]
    public int? SeasonCount { get; set; }

    /// <summary>Artist photo URL (headshot). Populated for artist drill-down only.</summary>
    [JsonPropertyName("artist_photo_url")]
    public string? ArtistPhotoUrl { get; set; }

    /// <summary>Artist's person record ID. Populated for artist drill-down only — used to open the person detail drawer.</summary>
    [JsonPropertyName("artist_person_id")]
    public Guid? ArtistPersonId { get; set; }

    /// <summary>Top billed cast (actors) for TV shows / movies. Capped at 10 entries.</summary>
    [JsonPropertyName("top_cast")]
    public List<CollectionGroupPersonViewModel> TopCast { get; set; } = [];

    /// <summary>TV only — works grouped by season.</summary>
    [JsonPropertyName("seasons")]
    public List<CollectionGroupSeasonViewModel> Seasons { get; set; } = [];

    /// <summary>Flat list for music/books/movies.</summary>
    [JsonPropertyName("works")]
    public List<CollectionGroupWorkViewModel> Works { get; set; } = [];
}

/// <summary>Lightweight person reference used by cast chips on MediaGroupPage.</summary>
public sealed class CollectionGroupPersonViewModel
{
    [JsonPropertyName("person_id")]
    public Guid? PersonId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; set; }
}

/// <summary>A single TV season (or album within an artist view) within a group detail view.</summary>
public sealed class CollectionGroupSeasonViewModel
{
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    /// <summary>Optional display label for the section (e.g. album title when GroupType is "artist").</summary>
    [JsonPropertyName("season_label")]
    public string? SeasonLabel { get; set; }

    /// <summary>Section cover URL (e.g. album art when GroupType is "artist").</summary>
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    /// <summary>Collection ID for this section if it maps to a real collection (e.g. an album collection). Used for tile click navigation.</summary>
    [JsonPropertyName("album_collection_id")]
    public Guid? AlbumCollectionId { get; set; }

    /// <summary>Section year (e.g. album release year).</summary>
    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("episodes")]
    public List<CollectionGroupWorkViewModel> Episodes { get; set; } = [];
}

/// <summary>A single track / episode / book / film in a group detail view.</summary>
public sealed class CollectionGroupWorkViewModel
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("ordinal")]
    public int? Ordinal { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("episode")]
    public string? Episode { get; set; }

    [JsonPropertyName("track_number")]
    public string? TrackNumber { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Whether this work corresponds to an actual file in the library (true) or an unowned track from Wikidata (false).</summary>
    [JsonPropertyName("is_owned")]
    public bool IsOwned { get; set; } = true;
}
