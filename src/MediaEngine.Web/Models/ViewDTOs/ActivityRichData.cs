using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Structured rich data for ReviewItemResolved activity entries.
/// Deserialized from <see cref="ActivityEntryResponse.ChangesJson"/>.
/// </summary>
public sealed class ReviewRichData
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("entity_id")]
    public string? EntityId { get; set; }

    /// <summary>"resolved", "dismissed", or "skipped".</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("qid")]
    public string? Qid { get; set; }

    [JsonPropertyName("field_overrides")]
    public int FieldOverrides { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }
}

/// <summary>
/// Structured rich data for FileIngested and MediaAdded activity entries.
/// Deserialized from <see cref="ActivityEntryResponse.ChangesJson"/>.
/// Handles both JSON shapes: FileIngested uses <c>cover_url</c>/<c>organized_to</c>,
/// MediaAdded uses <c>cover</c>/<c>organized_path</c>/<c>collection_name</c>.
/// </summary>
public sealed class ActivityRichData
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_file")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("entity_id")]
    public string? EntityId { get; set; }

    // FileIngested uses "organized_to"; MediaAdded uses "organized_path".
    [JsonPropertyName("organized_to")]
    public string? OrganizedTo { get; set; }

    [JsonPropertyName("organized_path")]
    public string? OrganizedPath { get; set; }

    [JsonPropertyName("hero_url")]
    public string? HeroUrl { get; set; }

    // FileIngested uses "cover_url"; MediaAdded uses "cover".
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("match_method")]
    public string? MatchMethod { get; set; }

    [JsonPropertyName("field_sources")]
    public List<FieldSourceEntry>? FieldSources { get; set; }

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("tags_written")]
    public List<string>? TagsWritten { get; set; }

    [JsonPropertyName("cover_written")]
    public bool CoverWritten { get; set; }

    // -- MediaAdded-specific fields ----------------------------------------

    [JsonPropertyName("collection_name")]
    public string? CollectionName { get; set; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("stage1_claims")]
    public int Stage1Claims { get; set; }

    [JsonPropertyName("stage2_claims")]
    public int Stage2Claims { get; set; }

    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; set; }

    // -- Unified accessors -------------------------------------------------

    /// <summary>Resolved cover URL  -  prefers <c>cover_url</c>, falls back to <c>cover</c>.</summary>
    [JsonIgnore]
    public string? ResolvedCoverUrl => CoverUrl ?? Cover;

    /// <summary>Resolved organized path  -  prefers <c>organized_to</c>, falls back to <c>organized_path</c>.</summary>
    [JsonIgnore]
    public string? ResolvedOrganizedTo => OrganizedTo ?? OrganizedPath;

    /// <summary>Human-friendly match method label.</summary>
    [JsonIgnore]
    public string MatchMethodLabel => MatchMethod switch
    {
        "embedded_metadata" => "Matched from embedded tags",
        "provider_match"    => "Matched via provider",
        "filename_fallback" => "Matched from filename",
        _                   => "Unknown match method",
    };
}

/// <summary>
/// Per-field provenance entry showing which source won each metadata field.
/// Deserialized from <c>field_sources</c> in the activity JSON.
/// </summary>
public sealed class FieldSourceEntry
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("conflicted")]
    public bool Conflicted { get; set; }
}
