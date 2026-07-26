using System.Text.Json.Serialization;
using MediaEngine.Contracts.Search;

namespace MediaEngine.Web.Models.ViewDTOs;

// Universe search remains a Web-owned contract until its dedicated consolidation packet.

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
    public UniverseFieldMatchScoresDto? MatchScores { get; set; }
}

public sealed class SearchUniverseResponseDto
{
    [JsonPropertyName("candidates")]
    public List<UniverseCandidateDto> Candidates { get; set; } = [];

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";
}

/// <summary>
/// Frozen Universe-search score shape. Universe/Chronicle contracts remain
/// intentionally unchanged while the active Universe enrichment work continues.
/// </summary>
public sealed class UniverseFieldMatchScoresDto
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

public sealed record ItemEditorPreferencesSaveResultDto(
    bool Saved,
    bool Conflict,
    MediaEngine.Contracts.Items.ItemEditorPreferencesResponse? Preferences,
    string? Error);
