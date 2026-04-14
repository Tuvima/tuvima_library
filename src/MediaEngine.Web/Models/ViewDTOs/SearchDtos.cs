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
    [JsonPropertyName("candidate_id")]
    public string CandidateId { get; set; } = "";

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

    [JsonPropertyName("media_type_metadata")]
    public Dictionary<string, string> MediaTypeMetadata { get; set; } = [];

    [JsonPropertyName("link_state")]
    public string LinkState { get; set; } = "linked";

    [JsonPropertyName("link_status_label")]
    public string LinkStatusLabel { get; set; } = "Linked to Wikidata";

    [JsonPropertyName("is_applicable")]
    public bool IsApplicable { get; set; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; set; }

    [JsonPropertyName("required_fields")]
    public Dictionary<string, string> RequiredFields { get; set; } = [];

    [JsonPropertyName("suggested_fields")]
    public Dictionary<string, string> SuggestedFields { get; set; } = [];

    [JsonPropertyName("qid_fields")]
    public Dictionary<string, string> QidFields { get; set; } = [];

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

    /// <summary>
    /// Structured search fields from multi-field search UI (e.g. "title" → "Yesterday", "artist" → "Beatles").
    /// When populated, the Engine uses these to build field-specific provider queries instead of the single query string.
    /// </summary>
    [JsonPropertyName("search_fields")]
    public Dictionary<string, string>? SearchFields { get; set; }
}

/// <summary>A single retail provider candidate.</summary>
public sealed class RetailCandidateDto
{
    [JsonPropertyName("candidate_id")]
    public string CandidateId { get; set; } = "";

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

    [JsonPropertyName("link_state")]
    public string LinkState { get; set; } = "provider_only";

    [JsonPropertyName("link_status_label")]
    public string LinkStatusLabel { get; set; } = "Linked to provider only";

    [JsonPropertyName("is_applicable")]
    public bool IsApplicable { get; set; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; set; }

    [JsonPropertyName("required_fields")]
    public Dictionary<string, string> RequiredFields { get; set; } = [];

    [JsonPropertyName("suggested_fields")]
    public Dictionary<string, string> SuggestedFields { get; set; } = [];

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; set; } = [];

    [JsonPropertyName("qid_fields")]
    public Dictionary<string, string> QidFields { get; set; } = [];
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

public sealed class ItemPreferencesRequestDto
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = [];
}

public sealed class ItemPreferencesResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("fields_updated")]
    public int FieldsUpdated { get; set; }

    [JsonPropertyName("updated_keys")]
    public List<string> UpdatedKeys { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class ItemCanonicalSearchRequestDto
{
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("target_kind")]
    public string TargetKind { get; set; } = "";

    [JsonPropertyName("target_field_group")]
    public string TargetFieldGroup { get; set; } = "";

    [JsonPropertyName("draft_fields")]
    public Dictionary<string, string> DraftFields { get; set; } = [];

    [JsonPropertyName("query_override")]
    public string? QueryOverride { get; set; }

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; set; } = 6;
}

public sealed class ItemCanonicalSearchResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("target_kind")]
    public string TargetKind { get; set; } = "";

    [JsonPropertyName("target_field_group")]
    public string TargetFieldGroup { get; set; } = "";

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("retail_candidates")]
    public List<RetailCandidateDto> RetailCandidates { get; set; } = [];

    [JsonPropertyName("linked_candidates")]
    public List<UniverseCandidateDto> LinkedCandidates { get; set; } = [];

    [JsonPropertyName("fallback_actions")]
    public List<string> FallbackActions { get; set; } = [];

    [JsonPropertyName("no_result_message")]
    public string? NoResultMessage { get; set; }

    [JsonPropertyName("can_apply_unlinked_canonical")]
    public bool CanApplyUnlinkedCanonical { get; set; }

    [JsonPropertyName("missing_required_fields")]
    public List<string> MissingRequiredFields { get; set; } = [];

    [JsonPropertyName("unlinked_fields")]
    public Dictionary<string, string> UnlinkedFields { get; set; } = [];

    [JsonPropertyName("draft_fields")]
    public Dictionary<string, string> DraftFields { get; set; } = [];
}

public sealed class ItemCanonicalApplyRequestDto
{
    [JsonPropertyName("target_kind")]
    public string TargetKind { get; set; } = "";

    [JsonPropertyName("target_field_group")]
    public string TargetFieldGroup { get; set; } = "";

    [JsonPropertyName("link_state")]
    public string LinkState { get; set; } = "";

    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; set; }

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; set; }

    [JsonPropertyName("required_fields")]
    public Dictionary<string, string> RequiredFields { get; set; } = [];

    [JsonPropertyName("suggested_fields")]
    public Dictionary<string, string> SuggestedFields { get; set; } = [];

    [JsonPropertyName("accepted_suggested_keys")]
    public List<string> AcceptedSuggestedKeys { get; set; } = [];

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; set; } = [];

    [JsonPropertyName("qid_fields")]
    public Dictionary<string, string> QidFields { get; set; } = [];
}

public sealed class ItemCanonicalApplyResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("link_state")]
    public string LinkState { get; set; } = "";

    [JsonPropertyName("link_status_label")]
    public string LinkStatusLabel { get; set; } = "";

    [JsonPropertyName("fields_applied")]
    public int FieldsApplied { get; set; }

    [JsonPropertyName("ids_cleared")]
    public List<string> IdsCleared { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
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
