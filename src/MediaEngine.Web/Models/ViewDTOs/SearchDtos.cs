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
    public string TitleVerdict { get; set; } = "";

    [JsonPropertyName("author_verdict")]
    public string AuthorVerdict { get; set; } = "";

    [JsonPropertyName("year_verdict")]
    public string YearVerdict { get; set; } = "";

    [JsonPropertyName("format_verdict")]
    public string FormatVerdict { get; set; } = "";
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
