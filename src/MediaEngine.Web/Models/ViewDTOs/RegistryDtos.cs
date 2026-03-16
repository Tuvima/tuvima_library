using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>A single item in the registry listing.</summary>
public sealed class RegistryItemViewModel
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("match_source")]
    public string? MatchSource { get; set; }

    [JsonPropertyName("match_method")]
    public string? MatchMethod { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Auto";

    [JsonPropertyName("has_duplicate")]
    public bool HasDuplicate { get; set; }

    [JsonPropertyName("duplicate_of")]
    public string? DuplicateOf { get; set; }

    [JsonPropertyName("review_item_id")]
    public Guid? ReviewItemId { get; set; }

    [JsonPropertyName("review_trigger")]
    public string? ReviewTrigger { get; set; }

    [JsonPropertyName("has_user_locks")]
    public bool HasUserLocks { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("file_size_bytes")]
    public long? FileSizeBytes { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string? WikidataStatus { get; set; }

    /// <summary>True when wikidata_status is 'missing' or 'manual' (no Wikidata QID resolved).</summary>
    [JsonPropertyName("missing_universe")]
    public bool MissingUniverse => WikidataStatus is "missing" or "manual";
}

/// <summary>Paginated registry response.</summary>
public sealed class RegistryPageResponse
{
    [JsonPropertyName("items")]
    public List<RegistryItemViewModel> Items { get; set; } = [];

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

/// <summary>Status counts for tab badges.</summary>
public sealed class RegistryStatusCountsDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("needs_review")]
    public int NeedsReview { get; set; }

    [JsonPropertyName("auto_approved")]
    public int AutoApproved { get; set; }

    [JsonPropertyName("edited")]
    public int Edited { get; set; }

    [JsonPropertyName("duplicate")]
    public int Duplicate { get; set; }

    [JsonPropertyName("staging")]
    public int Staging { get; set; }
}

/// <summary>Full detail for expanded row.</summary>
public sealed class RegistryItemDetailViewModel
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Auto";

    [JsonPropertyName("match_source")]
    public string? MatchSource { get; set; }

    [JsonPropertyName("match_method")]
    public string? MatchMethod { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("cast")]
    public string? Cast { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("series")]
    public string? Series { get; set; }

    [JsonPropertyName("series_position")]
    public string? SeriesPosition { get; set; }

    [JsonPropertyName("narrator")]
    public string? Narrator { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string? WikidataStatus { get; set; }

    [JsonPropertyName("missing_universe")]
    public bool MissingUniverse => WikidataStatus is "missing" or "manual";

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("file_size_bytes")]
    public long? FileSizeBytes { get; set; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("review_item_id")]
    public Guid? ReviewItemId { get; set; }

    [JsonPropertyName("review_trigger")]
    public string? ReviewTrigger { get; set; }

    [JsonPropertyName("review_detail")]
    public string? ReviewDetail { get; set; }

    [JsonPropertyName("candidates_json")]
    public string? CandidatesJson { get; set; }

    [JsonPropertyName("has_user_locks")]
    public bool HasUserLocks { get; set; }

    [JsonPropertyName("canonical_values")]
    public List<RegistryCanonicalValueDto> CanonicalValues { get; set; } = [];

    [JsonPropertyName("claim_history")]
    public List<RegistryClaimRecordDto> ClaimHistory { get; set; } = [];

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; set; } = [];
}

/// <summary>Canonical value with conflict info.</summary>
public sealed class RegistryCanonicalValueDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("is_conflicted")]
    public bool IsConflicted { get; set; }

    [JsonPropertyName("winning_provider_id")]
    public string? WinningProviderId { get; set; }

    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; set; }

    [JsonPropertyName("last_scored_at")]
    public DateTimeOffset LastScoredAt { get; set; }
}

/// <summary>A single claim from voting history.</summary>
public sealed class RegistryClaimRecordDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; set; } = "";

    [JsonPropertyName("claim_value")]
    public string ClaimValue { get; set; } = "";

    [JsonPropertyName("provider_id")]
    public Guid ProviderId { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("is_user_locked")]
    public bool IsUserLocked { get; set; }

    [JsonPropertyName("claimed_at")]
    public DateTimeOffset ClaimedAt { get; set; }
}
