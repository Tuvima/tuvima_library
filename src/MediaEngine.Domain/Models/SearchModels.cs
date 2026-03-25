using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Models;

// ── Universe Search ───────────────────────────────────────────────────────────

/// <summary>Request to search Wikidata for identity candidates.</summary>
public sealed record SearchUniverseRequest(
    [property: JsonPropertyName("query")]          string Query,
    [property: JsonPropertyName("media_type")]     string MediaType,
    [property: JsonPropertyName("max_candidates")] int MaxCandidates = 5,
    [property: JsonPropertyName("local_title")]    string? LocalTitle = null,
    [property: JsonPropertyName("local_author")]   string? LocalAuthor = null,
    [property: JsonPropertyName("local_year")]     string? LocalYear = null);

/// <summary>A single enriched Wikidata candidate with cover art and description chained from retail.</summary>
public sealed class UniverseCandidate
{
    [JsonPropertyName("qid")]
    public required string Qid { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("instance_of")]
    public string? InstanceOf { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    /// <summary>Cover art URL sourced from the chained retail provider (Apple Books, TMDB, etc.).</summary>
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    /// <summary>Wikipedia extract (first paragraph) fetched via QID sitelink.</summary>
    [JsonPropertyName("wikipedia_extract")]
    public string? WikipediaExtract { get; init; }

    /// <summary>How this candidate was resolved: "bridge", "structured_sparql", or "title_search".</summary>
    [JsonPropertyName("resolution_tier")]
    public string? ResolutionTier { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Bridge IDs harvested from Wikidata SPARQL (e.g. tmdb_id, isbn, imdb_id).</summary>
    [JsonPropertyName("bridge_ids")]
    public IReadOnlyDictionary<string, string> BridgeIds { get; init; } = new Dictionary<string, string>();

    /// <summary>Detected or confirmed media type for this candidate (e.g. "Epub", "Movies").</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    /// <summary>Additional media-type-specific metadata (e.g. author for books, director for films).</summary>
    [JsonPropertyName("media_type_metadata")]
    public IReadOnlyDictionary<string, string>? MediaTypeMetadata { get; init; }

    /// <summary>Per-field match scores when compared against local file metadata. Null if no local context provided.</summary>
    [JsonPropertyName("match_scores")]
    public FieldMatchResult? MatchScores { get; set; }
}

/// <summary>Result of a universe search — list of enriched candidates.</summary>
public sealed record SearchUniverseResult(
    [property: JsonPropertyName("candidates")]  IReadOnlyList<UniverseCandidate> Candidates,
    [property: JsonPropertyName("query")]       string Query,
    [property: JsonPropertyName("media_type")]  string MediaType);

// ── Retail Search ─────────────────────────────────────────────────────────────

/// <summary>
/// Request to search retail providers for a media item.
/// When <c>FileHints</c> is provided (narrator, series, publisher, etc.), the Engine runs
/// <c>DescriptionMatchService</c> against candidate descriptions and blends the result
/// into <c>RetailCandidate.CompositeScore</c> for improved ranking.
/// </summary>
public sealed record SearchRetailRequest(
    [property: JsonPropertyName("query")]          string Query,
    [property: JsonPropertyName("media_type")]     string MediaType,
    [property: JsonPropertyName("max_candidates")] int MaxCandidates = 5,
    [property: JsonPropertyName("local_title")]    string? LocalTitle = null,
    [property: JsonPropertyName("local_author")]   string? LocalAuthor = null,
    [property: JsonPropertyName("local_year")]     string? LocalYear = null,
    [property: JsonPropertyName("file_hints")]     IReadOnlyDictionary<string, string>? FileHints = null);

/// <summary>A single retail provider candidate with cover art and basic metadata.</summary>
public sealed class RetailCandidate
{
    [JsonPropertyName("provider_id")]
    public required string ProviderId { get; init; }

    [JsonPropertyName("provider_name")]
    public required string ProviderName { get; init; }

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Per-field match scores when compared against local file metadata. Null if no local context provided.</summary>
    [JsonPropertyName("match_scores")]
    public FieldMatchResult? MatchScores { get; set; }

    /// <summary>
    /// Description-based bonus score (0.0–1.0) from DescriptionMatchService.
    /// Populated when the request includes FileHints. Zero when no hints provided.
    /// </summary>
    [JsonPropertyName("description_match_score")]
    public double DescriptionMatchScore { get; set; }

    /// <summary>
    /// Per-field description match details for UI display.
    /// Shows which file-embedded fields (narrator, series, publisher, …) matched in the candidate's text.
    /// </summary>
    [JsonPropertyName("description_field_matches")]
    public IReadOnlyList<DescriptionFieldMatchEntry>? DescriptionFieldMatches { get; set; }

    /// <summary>
    /// Composite ranking score combining retail fuzzy match (60%) and description match (40%).
    /// Used for sorting when FileHints were provided; equals Confidence otherwise.
    /// </summary>
    [JsonPropertyName("composite_score")]
    public double CompositeScore { get; set; }
}

/// <summary>A single field's description match result — surfaced in the Dashboard for match signal badges.</summary>
public sealed class DescriptionFieldMatchEntry
{
    /// <summary>The file hint key (e.g. "narrator", "publisher", "series").</summary>
    [JsonPropertyName("field_key")]
    public string FieldKey { get; init; } = "";

    /// <summary>The value from the file's embedded metadata.</summary>
    [JsonPropertyName("file_value")]
    public string FileValue { get; init; } = "";

    /// <summary>Whether this field matched in the candidate's text.</summary>
    [JsonPropertyName("matched")]
    public bool Matched { get; init; }

    /// <summary>Raw fuzzy score (0-100).</summary>
    [JsonPropertyName("raw_score")]
    public int RawScore { get; init; }

    /// <summary>The configured weight for this field.</summary>
    [JsonPropertyName("weight")]
    public double Weight { get; init; }
}

/// <summary>Result of a retail search — list of candidates from relevant providers.</summary>
public sealed record SearchRetailResult(
    [property: JsonPropertyName("candidates")]  IReadOnlyList<RetailCandidate> Candidates,
    [property: JsonPropertyName("query")]       string Query,
    [property: JsonPropertyName("media_type")]  string MediaType);

// ── Apply Match ───────────────────────────────────────────────────────────────

/// <summary>Request to apply a manually selected match to a registry item.</summary>
public sealed class ApplyMatchRequest
{
    /// <summary>Wikidata QID for the media item. When provided, the item is registered with this identity.</summary>
    [JsonPropertyName("qid")]
    public string? Qid { get; init; }

    // Metadata to apply as user-locked claims:
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }
}

/// <summary>Response after applying a match.</summary>
public sealed class ApplyMatchResponse
{
    [JsonPropertyName("entity_id")]
    public required Guid EntityId { get; init; }

    [JsonPropertyName("wikidata_status")]
    public required string WikidataStatus { get; init; }

    [JsonPropertyName("claims_written")]
    public int ClaimsWritten { get; init; }

    [JsonPropertyName("hydration_triggered")]
    public bool HydrationTriggered { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

// ── Create Manual Entry ───────────────────────────────────────────────────────

/// <summary>Request to manually create metadata for a registry item with no provider match.</summary>
public sealed class CreateManualRequest
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>Response after creating a manual entry.</summary>
public sealed class CreateManualResponse
{
    [JsonPropertyName("entity_id")]
    public required Guid EntityId { get; init; }

    [JsonPropertyName("wikidata_status")]
    public string WikidataStatus { get; init; } = "manual";

    [JsonPropertyName("claims_written")]
    public int ClaimsWritten { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
