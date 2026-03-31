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

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("series")]
    public string? Series { get; set; }

    [JsonPropertyName("series_position")]
    public string? SeriesPosition { get; set; }

    [JsonPropertyName("narrator")]
    public string? Narrator { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("track_number")]
    public string? TrackNumber { get; set; }

    [JsonPropertyName("season_number")]
    public string? Season { get; set; }

    [JsonPropertyName("episode_number")]
    public string? Episode { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("disc_number")]
    public string? DiscNumber { get; set; }

    [JsonPropertyName("show_name")]
    public string? ShowName { get; set; }

    [JsonPropertyName("track_count")]
    public string? TrackCount { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string? WikidataStatus { get; set; }

    /// <summary>True when wikidata_status is 'missing' or 'manual' (no Wikidata QID resolved).</summary>
    [JsonPropertyName("missing_universe")]
    public bool MissingUniverse => WikidataStatus is "missing" or "manual";

    [JsonPropertyName("wikidata_match")]
    public string WikidataMatch { get; set; } = "none";

    [JsonPropertyName("retail_match")]
    public string RetailMatch { get; set; } = "none";

    [JsonPropertyName("retail_match_detail")]
    public string? RetailMatchDetail { get; set; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("hero_url")]
    public string? HeroUrl { get; set; }

    [JsonPropertyName("failed_provider_name")]
    public string? FailedProviderName { get; set; }
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

    [JsonPropertyName("missing_images")]
    public int MissingImages { get; set; }

    [JsonPropertyName("recently_updated")]
    public int RecentlyUpdated { get; set; }

    [JsonPropertyName("low_confidence")]
    public int LowConfidence { get; set; }

    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }
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

    [JsonPropertyName("hero_url")]
    public string? HeroUrl { get; set; }

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

    [JsonPropertyName("match_level")]
    public string MatchLevel { get; set; } = "work";

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

/// <summary>Response from batch registry operations.</summary>
public sealed class BatchRegistryResponse
{
    [JsonPropertyName("processed_count")]
    public int ProcessedCount { get; set; }

    [JsonPropertyName("total_requested")]
    public int TotalRequested { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>A single entry in the processing history timeline for a registry item.</summary>
public sealed class RegistryItemHistoryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

/// <summary>Four-state counts for the Registry lifecycle: Identified, InReview, Provisional, Rejected.</summary>
public sealed class RegistryFourStateCountsDto
{
    [JsonPropertyName("identified")]
    public int Identified { get; set; }

    [JsonPropertyName("in_review")]
    public int InReview { get; set; }

    [JsonPropertyName("provisional")]
    public int Provisional { get; set; }

    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }

    [JsonPropertyName("person_count")]
    public int PersonCount { get; set; }

    [JsonPropertyName("hub_count")]
    public int HubCount { get; set; }

    [JsonPropertyName("waiting_for_provider")]
    public int WaitingForProvider { get; set; }

    [JsonPropertyName("trigger_counts")]
    public Dictionary<string, int> TriggerCounts { get; set; } = [];

    /// <summary>Convenience: total across all four states.</summary>
    public int All => Identified + InReview + Provisional + Rejected;
}

/// <summary>Dashboard view model for an ingestion batch.</summary>
public sealed class IngestionBatchViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("files_total")]
    public int FilesTotal { get; set; }

    [JsonPropertyName("files_processed")]
    public int FilesProcessed { get; set; }

    [JsonPropertyName("files_identified")]
    public int FilesIdentified { get; set; }

    [JsonPropertyName("files_review")]
    public int FilesReview { get; set; }

    [JsonPropertyName("files_no_match")]
    public int FilesNoMatch { get; set; }

    [JsonPropertyName("files_failed")]
    public int FilesFailed { get; set; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Request body for marking an item as provisional with curator-entered metadata.</summary>
public sealed class ProvisionalMetadataRequestDto
{
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("narrator")]
    public string? Narrator { get; set; }

    [JsonPropertyName("isbn")]
    public string? Isbn { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; set; }

    [JsonPropertyName("seasons")]
    public string? Seasons { get; set; }

    [JsonPropertyName("track_count")]
    public string? TrackCount { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("writer")]
    public string? Writer { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("page_count")]
    public string? PageCount { get; set; }
}

/// <summary>Person list item from GET /persons endpoint.</summary>
public sealed class PersonListItemDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = [];

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; set; }

    [JsonPropertyName("has_local_headshot")]
    public bool HasLocalHeadshot { get; set; }

    [JsonPropertyName("biography")]
    public string? Biography { get; set; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; set; }

    [JsonPropertyName("is_pseudonym")]
    public bool IsPseudonym { get; set; }

    [JsonPropertyName("is_group")]
    public bool IsGroup { get; set; }
}

/// <summary>A group member or parent group for display in PersonBiographyDrawer.</summary>
public sealed class GroupMemberView
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? DateRange { get; init; }

    public GroupMemberView(Guid id, string name, string? dateRange = null)
    {
        Id = id;
        Name = name;
        DateRange = dateRange;
    }
}

/// <summary>A single alias entry from GET /persons/{id}/aliases.</summary>
public sealed class PersonAliasDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; set; }

    [JsonPropertyName("is_pseudonym")]
    public bool IsPseudonym { get; set; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "";
}

/// <summary>Response from GET /persons/{id}/aliases.</summary>
public sealed class PersonAliasesResponseDto
{
    [JsonPropertyName("person_id")]
    public Guid PersonId { get; set; }

    [JsonPropertyName("person_name")]
    public string PersonName { get; set; } = "";

    [JsonPropertyName("is_pseudonym")]
    public bool IsPseudonym { get; set; }

    [JsonPropertyName("aliases")]
    public List<PersonAliasDto> Aliases { get; set; } = [];
}

/// <summary>Media type presence counts for a single person.</summary>
public sealed class PersonPresenceDto
{
    [JsonPropertyName("person_id")]
    public string PersonId { get; set; } = "";

    [JsonPropertyName("counts")]
    public Dictionary<string, int> Counts { get; set; } = new();

    /// <summary>Total media items this person appears in.</summary>
    public int Total => Counts.Values.Sum();
}
