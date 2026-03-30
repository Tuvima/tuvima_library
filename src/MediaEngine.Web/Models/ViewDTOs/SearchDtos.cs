using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

// ── Universe Search ───────────────────────────────────────────────────────────

/// <summary>Request sent to POST /search/universe.</summary>
public sealed class SearchUniverseRequestDto
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; set; } = 5;

    [JsonPropertyName("local_author")]
    public string? LocalAuthor { get; set; }
}

/// <summary>A single enriched Wikidata candidate returned from universe search.</summary>
public sealed class UniverseCandidateDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("instance_of")]
    public string? InstanceOf { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("wikipedia_extract")]
    public string? WikipediaExtract { get; set; }

    [JsonPropertyName("resolution_tier")]
    public string? ResolutionTier { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; set; } = [];

    [JsonPropertyName("match_scores")]
    public FieldMatchScoresDto? MatchScores { get; set; }
}

/// <summary>Response from POST /search/universe.</summary>
public sealed class SearchUniverseResponseDto
{
    [JsonPropertyName("candidates")]
    public List<UniverseCandidateDto> Candidates { get; set; } = [];

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";
}

// ── Retail Search ─────────────────────────────────────────────────────────────

/// <summary>Request sent to POST /search/retail.</summary>
public sealed class SearchRetailRequestDto
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; set; } = 5;

    [JsonPropertyName("local_title")]
    public string? LocalTitle { get; set; }

    [JsonPropertyName("local_author")]
    public string? LocalAuthor { get; set; }

    [JsonPropertyName("local_year")]
    public string? LocalYear { get; set; }

    /// <summary>
    /// File-embedded metadata hints for description matching (narrator, series, publisher, etc.).
    /// Sent alongside the search query so the Engine can score description text and re-rank results.
    /// </summary>
    [JsonPropertyName("file_hints")]
    public Dictionary<string, string>? FileHints { get; set; }
}

/// <summary>A single retail provider candidate.</summary>
public sealed class RetailCandidateDto
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = "";

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = "";

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("match_scores")]
    public FieldMatchScoresDto? MatchScores { get; set; }

    /// <summary>Description-based bonus score (0.0–1.0). Populated when file hints were sent.</summary>
    [JsonPropertyName("description_match_score")]
    public double DescriptionMatchScore { get; set; }

    /// <summary>Per-field description match details for rendering match signal badges.</summary>
    [JsonPropertyName("description_field_matches")]
    public List<DescriptionFieldMatchDto>? DescriptionFieldMatches { get; set; }

    /// <summary>Additional fields not covered by typed properties — keyed by metadata field constant (e.g. "narrator", "runtime").</summary>
    [JsonPropertyName("extra_fields")]
    public Dictionary<string, string> ExtraFields { get; set; } = [];

    /// <summary>Composite ranking score (fuzzy 60% + description 40%). Used for display ordering.</summary>
    [JsonPropertyName("composite_score")]
    public double CompositeScore { get; set; }
}

/// <summary>A single field's description match result for UI badge rendering.</summary>
public sealed class DescriptionFieldMatchDto
{
    [JsonPropertyName("field_key")]
    public string FieldKey { get; set; } = "";

    [JsonPropertyName("file_value")]
    public string FileValue { get; set; } = "";

    [JsonPropertyName("matched")]
    public bool Matched { get; set; }

    [JsonPropertyName("raw_score")]
    public int RawScore { get; set; }

    [JsonPropertyName("weight")]
    public double Weight { get; set; }
}

/// <summary>Response from POST /search/retail.</summary>
public sealed class SearchRetailResponseDto
{
    [JsonPropertyName("candidates")]
    public List<RetailCandidateDto> Candidates { get; set; } = [];

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";
}

// ── Resolve Search ────────────────────────────────────────────────────────────

/// <summary>Request sent to POST /search/resolve.</summary>
public sealed class SearchResolveRequestDto
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; set; } = 5;

    /// <summary>
    /// File-embedded metadata hints for description matching and scoring
    /// (e.g. title, author, narrator, year, series, publisher, isbn, asin).
    /// </summary>
    [JsonPropertyName("file_hints")]
    public Dictionary<string, string>? FileHints { get; set; }
}

/// <summary>A single resolve candidate returned from POST /search/resolve.</summary>
public sealed class ResolveCandidateDto
{
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = "";

    [JsonPropertyName("provider_item_id")]
    public string ProviderItemId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("retail_score")]
    public double RetailScore { get; set; }

    [JsonPropertyName("description_score")]
    public double DescriptionScore { get; set; }

    [JsonPropertyName("composite_score")]
    public double CompositeScore { get; set; }

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; set; } = [];

    // ── Wikidata bridge resolution (populated client-side after selection) ──

    [JsonPropertyName("wikidata_resolved")]
    public bool WikidataResolved { get; set; }

    [JsonPropertyName("work_qid")]
    public string? WorkQid { get; set; }

    [JsonPropertyName("edition_qid")]
    public string? EditionQid { get; set; }

    [JsonPropertyName("is_edition")]
    public bool IsEdition { get; set; }

    [JsonPropertyName("wikidata_narrator")]
    public string? WikidataNarrator { get; set; }

    [JsonPropertyName("wikipedia_url")]
    public string? WikipediaUrl { get; set; }

    [JsonPropertyName("field_matches")]
    public List<DescriptionFieldMatchDto>? FieldMatches { get; set; }
}

/// <summary>Response from POST /search/resolve.</summary>
public sealed class SearchResolveResponseDto
{
    [JsonPropertyName("candidates")]
    public List<ResolveCandidateDto> Candidates { get; set; } = [];
}

// ── Apply Match ───────────────────────────────────────────────────────────────

/// <summary>Request sent to POST /registry/items/{entityId}/apply-match.</summary>
public sealed class ApplyMatchRequestDto
{
    [JsonPropertyName("qid")]
    public string? Qid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    /// <summary>Name of the retail provider that supplied this match (e.g. "apple_books").</summary>
    [JsonPropertyName("retail_provider_name")]
    public string? RetailProviderName { get; set; }

    /// <summary>Provider-specific item ID from the retail match (e.g. Apple Books collectionId).</summary>
    [JsonPropertyName("retail_provider_item_id")]
    public string? RetailProviderItemId { get; set; }

    /// <summary>Bridge IDs extracted from the retail result, keyed by type (e.g. "apple_books_id" → "12345").</summary>
    [JsonPropertyName("retail_bridge_ids")]
    public Dictionary<string, string>? RetailBridgeIds { get; set; }

    /// <summary>Sanitized HTML description from the retail provider.</summary>
    [JsonPropertyName("retail_description")]
    public string? RetailDescription { get; set; }
}

/// <summary>Response from POST /registry/items/{entityId}/apply-match.</summary>
public sealed class ApplyMatchResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string WikidataStatus { get; set; } = "";

    [JsonPropertyName("claims_written")]
    public int ClaimsWritten { get; set; }

    [JsonPropertyName("hydration_triggered")]
    public bool HydrationTriggered { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// ── Create Manual Entry ───────────────────────────────────────────────────────

/// <summary>Request sent to POST /registry/items/{entityId}/create-manual.</summary>
public sealed class CreateManualRequestDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>Response from POST /registry/items/{entityId}/create-manual.</summary>
public sealed class CreateManualResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string WikidataStatus { get; set; } = "manual";

    [JsonPropertyName("claims_written")]
    public int ClaimsWritten { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// ── Field Match Scores ───────────────────────────────────────────────────────

/// <summary>Per-field match scores for comparing local file metadata against a search candidate.</summary>
public sealed class FieldMatchScoresDto
{
    [JsonPropertyName("title_score")]
    public double TitleScore { get; set; }

    [JsonPropertyName("author_score")]
    public double AuthorScore { get; set; }

    [JsonPropertyName("year_score")]
    public double YearScore { get; set; }

    [JsonPropertyName("format_score")]
    public double FormatScore { get; set; }

    [JsonPropertyName("composite_score")]
    public double CompositeScore { get; set; }

    [JsonPropertyName("title_verdict")]
    public int TitleVerdict { get; set; }

    [JsonPropertyName("author_verdict")]
    public int AuthorVerdict { get; set; }

    [JsonPropertyName("year_verdict")]
    public int YearVerdict { get; set; }

    [JsonPropertyName("format_verdict")]
    public int FormatVerdict { get; set; }
}

/// <summary>Request to submit a user problem report.</summary>
public sealed class SubmitReportRequestDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("item_title")]
    public string? ItemTitle { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("reporter_name")]
    public string? ReporterName { get; set; }
}

/// <summary>Response from report submission.</summary>
public sealed class SubmitReportResponseDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

// ── Wikidata Aliases ──────────────────────────────────────────────────────────

/// <summary>Response from GET /metadata/{qid}/aliases.</summary>
public sealed class AliasesResponseDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];
}

/// <summary>A single problem report entry.</summary>
public sealed class ReportEntryDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("occurred_at")]
    public string OccurredAt { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("reporter_name")]
    public string ReporterName { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
