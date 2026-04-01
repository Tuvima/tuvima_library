using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a content group drill-down page.
/// Deserializes directly from GET /hubs/{hubId}/group-detail (snake_case JSON).
/// </summary>
public sealed class HubGroupDetailViewModel
{
    [JsonPropertyName("hub_id")]
    public Guid HubId { get; set; }

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

    /// <summary>TV only — works grouped by season.</summary>
    [JsonPropertyName("seasons")]
    public List<HubGroupSeasonViewModel> Seasons { get; set; } = [];

    /// <summary>Flat list for music/books/movies.</summary>
    [JsonPropertyName("works")]
    public List<HubGroupWorkViewModel> Works { get; set; } = [];
}

/// <summary>A single TV season (or album within an artist view) within a group detail view.</summary>
public sealed class HubGroupSeasonViewModel
{
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    /// <summary>Optional display label for the section (e.g. album title when GroupType is "artist").</summary>
    [JsonPropertyName("season_label")]
    public string? SeasonLabel { get; set; }

    [JsonPropertyName("episodes")]
    public List<HubGroupWorkViewModel> Episodes { get; set; } = [];
}

/// <summary>A single track / episode / book / film in a group detail view.</summary>
public sealed class HubGroupWorkViewModel
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("sequence_index")]
    public int? SequenceIndex { get; set; }

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
}
