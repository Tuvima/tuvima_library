using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

/// <summary>Detail view of a hub with all child works for sub-page rendering.</summary>
public sealed class HubGroupDetailDto
{
    [JsonPropertyName("hub_id")]
    public required Guid HubId { get; init; }

    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("primary_media_type")]
    public string? PrimaryMediaType { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

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

    /// <summary>Artist photo URL (headshot). Populated for artist drill-down only.</summary>
    [JsonPropertyName("artist_photo_url")]
    public string? ArtistPhotoUrl { get; init; }

    /// <summary>Artist's person record ID. Populated for artist drill-down only — used to open the person detail drawer.</summary>
    [JsonPropertyName("artist_person_id")]
    public Guid? ArtistPersonId { get; init; }

    /// <summary>Child works grouped into seasons. Populated for TV media type only.</summary>
    [JsonPropertyName("seasons")]
    public List<HubGroupSeasonDto> Seasons { get; init; } = [];

    /// <summary>Flat list of works sorted by sequence_index. Populated for non-TV media types.</summary>
    [JsonPropertyName("works")]
    public List<HubGroupWorkDto> Works { get; init; } = [];
}

/// <summary>A single TV season within a hub group detail response.</summary>
public sealed class HubGroupSeasonDto
{
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; init; }

    [JsonPropertyName("season_label")]
    public string? SeasonLabel { get; init; }

    /// <summary>Section cover URL (e.g. album art for an artist's album). Optional.</summary>
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    /// <summary>Hub ID for this section if it maps to a real hub (e.g. an album hub). Used for tile click navigation.</summary>
    [JsonPropertyName("album_hub_id")]
    public Guid? AlbumHubId { get; init; }

    /// <summary>Section year. Optional.</summary>
    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("episodes")]
    public List<HubGroupWorkDto> Episodes { get; init; } = [];
}

/// <summary>A single work within a hub group detail response.</summary>
public sealed class HubGroupWorkDto
{
    [JsonPropertyName("work_id")]
    public required Guid WorkId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("sequence_index")]
    public int? SequenceIndex { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

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

    /// <summary>Whether this work corresponds to an actual file in the library (true) or an unowned track surfaced from Wikidata child entity data (false).</summary>
    [JsonPropertyName("is_owned")]
    public bool IsOwned { get; init; } = true;

    /// <summary>Stage 1 (retail identification) pipeline status for this work's primary asset.</summary>
    [JsonPropertyName("stage1")]
    public VaultPipelineStageDto? Stage1 { get; init; }

    /// <summary>Stage 2 (Wikidata bridge) pipeline status for this work's primary asset.</summary>
    [JsonPropertyName("stage2")]
    public VaultPipelineStageDto? Stage2 { get; init; }

    /// <summary>Stage 3 (universe enrichment) pipeline status for this work's primary asset.</summary>
    [JsonPropertyName("stage3")]
    public VaultPipelineStageDto? Stage3 { get; init; }
}

/// <summary>Pipeline stage indicator (state + label) for a single hydration stage.</summary>
public sealed class VaultPipelineStageDto
{
    [JsonPropertyName("state")]
    public string State { get; init; } = "pending";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";
}

/// <summary>
/// A Content Group hub — a Universe-type hub (album, TV series, book series, movie series)
/// that contains works of a single media type.
/// </summary>
public sealed class ContentGroupDto
{
    [JsonPropertyName("hub_id")]
    public Guid HubId { get; init; }

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

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("universe_status")]
    public string UniverseStatus { get; init; } = "Unknown";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Artist headshot URL — populated only when GroupByField == "artist".</summary>
    [JsonPropertyName("artist_photo_url")]
    public string? ArtistPhotoUrl { get; init; }

    /// <summary>Person ID of the matched artist — populated alongside ArtistPhotoUrl.</summary>
    [JsonPropertyName("artist_person_id")]
    public Guid? ArtistPersonId { get; init; }
}
