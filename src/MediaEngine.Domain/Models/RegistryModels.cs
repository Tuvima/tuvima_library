using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Models;

/// <summary>Query parameters for paginated registry listing.</summary>
public sealed record RegistryQuery(
    int Offset = 0,
    int Limit = 50,
    string? Search = null,
    string? MediaType = null,
    string? Status = null,
    double? MinConfidence = null,
    string? MatchSource = null,
    bool DuplicatesOnly = false,
    bool MissingUniverseOnly = false,
    /// <summary>Sort order: "newest" (default), "oldest", "confidence".</summary>
    string? Sort = null,
    /// <summary>When set, only return items first ingested within this many days.</summary>
    int? MaxDays = null,
    /// <summary>When true, includes staging items and all statuses (for diagnostics/testing).</summary>
    bool IncludeAll = false);

/// <summary>A single item in the registry listing.</summary>
public sealed record RegistryItem
{
    [JsonPropertyName("entity_id")]    public Guid EntityId { get; init; }
    [JsonPropertyName("title")]        public string Title { get; init; } = "";
    [JsonPropertyName("year")]         public string? Year { get; init; }
    [JsonPropertyName("media_type")]   public string MediaType { get; init; } = "";
    [JsonPropertyName("cover_url")]    public string? CoverUrl { get; init; }
    [JsonPropertyName("match_source")] public string? MatchSource { get; init; }
    [JsonPropertyName("match_method")] public string? MatchMethod { get; init; }
    [JsonPropertyName("confidence")]   public double Confidence { get; init; }
    [JsonPropertyName("status")]       public string Status { get; init; } = "Identified";
    [JsonPropertyName("has_duplicate")]  public bool HasDuplicate { get; init; }
    [JsonPropertyName("duplicate_of")]   public string? DuplicateOf { get; init; }
    [JsonPropertyName("review_item_id")] public Guid? ReviewItemId { get; init; }
    [JsonPropertyName("review_trigger")] public string? ReviewTrigger { get; init; }
    [JsonPropertyName("has_user_locks")] public bool HasUserLocks { get; init; }
    [JsonPropertyName("created_at")]     public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("file_name")]      public string? FileName { get; init; }
    [JsonPropertyName("file_size_bytes")]public long? FileSizeBytes { get; init; }
    [JsonPropertyName("author")]         public string? Author { get; init; }
    [JsonPropertyName("director")]       public string? Director { get; init; }
    [JsonPropertyName("artist")]         public string? Artist { get; init; }
    [JsonPropertyName("series")]          public string? Series { get; init; }
    [JsonPropertyName("series_position")] public string? SeriesPosition { get; init; }
    [JsonPropertyName("narrator")]        public string? Narrator { get; init; }
    [JsonPropertyName("genre")]           public string? Genre { get; init; }
    [JsonPropertyName("runtime")]         public string? Runtime { get; init; }
    [JsonPropertyName("rating")]          public string? Rating { get; init; }
    [JsonPropertyName("album")]           public string? Album { get; init; }
    [JsonPropertyName("track_number")]    public string? TrackNumber { get; init; }
    [JsonPropertyName("season_number")]   public string? SeasonNumber { get; init; }
    [JsonPropertyName("episode_number")]  public string? EpisodeNumber { get; init; }
    [JsonPropertyName("show_name")]       public string? ShowName { get; init; }
    [JsonPropertyName("duration")]        public string? Duration { get; init; }
    [JsonPropertyName("file_path")]       public string? FilePath { get; init; }

    [JsonPropertyName("wikidata_status")]
    public string? WikidataStatus { get; init; }

    /// <summary>True when wikidata_status is 'missing' or 'manual' (no Wikidata QID resolved).</summary>
    [JsonPropertyName("missing_universe")]
    public bool MissingUniverse => WikidataStatus is "missing" or "manual";

    [JsonPropertyName("wikidata_match")]
    public string WikidataMatch { get; init; } = "none";

    [JsonPropertyName("retail_match")]
    public string RetailMatch { get; init; } = "none";

    /// <summary>Display string showing which retail provider matched and its title, e.g. "Apple API: Dune".</summary>
    [JsonPropertyName("retail_match_detail")]
    public string? RetailMatchDetail { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("hero_url")]
    public string? HeroUrl { get; init; }
}

/// <summary>Paginated result from registry listing.</summary>
public sealed record RegistryPageResult(
    [property: JsonPropertyName("items")]       IReadOnlyList<RegistryItem> Items,
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("has_more")]    bool HasMore);

/// <summary>Detailed view of a single registry item for expanded row.</summary>
public sealed record RegistryItemDetail
{
    [JsonPropertyName("entity_id")]      public Guid EntityId { get; init; }
    [JsonPropertyName("title")]          public string Title { get; init; } = "";
    [JsonPropertyName("year")]           public string? Year { get; init; }
    [JsonPropertyName("media_type")]     public string MediaType { get; init; } = "";
    [JsonPropertyName("cover_url")]      public string? CoverUrl { get; init; }
    [JsonPropertyName("hero_url")]       public string? HeroUrl { get; init; }
    [JsonPropertyName("confidence")]     public double Confidence { get; init; }
    [JsonPropertyName("status")]         public string Status { get; init; } = "Identified";
    [JsonPropertyName("match_source")]   public string? MatchSource { get; init; }
    [JsonPropertyName("match_method")]   public string? MatchMethod { get; init; }

    // Metadata
    [JsonPropertyName("author")]          public string? Author { get; init; }
    [JsonPropertyName("director")]        public string? Director { get; init; }
    [JsonPropertyName("cast")]            public string? Cast { get; init; }
    [JsonPropertyName("language")]        public string? Language { get; init; }
    [JsonPropertyName("genre")]           public string? Genre { get; init; }
    [JsonPropertyName("runtime")]         public string? Runtime { get; init; }
    [JsonPropertyName("description")]     public string? Description { get; init; }
    [JsonPropertyName("series")]          public string? Series { get; init; }
    [JsonPropertyName("series_position")] public string? SeriesPosition { get; init; }
    [JsonPropertyName("narrator")]        public string? Narrator { get; init; }
    [JsonPropertyName("rating")]          public string? Rating { get; init; }
    [JsonPropertyName("wikidata_qid")]    public string? WikidataQid { get; init; }

    [JsonPropertyName("wikidata_status")]
    public string? WikidataStatus { get; init; }

    [JsonPropertyName("missing_universe")]
    public bool MissingUniverse => WikidataStatus is "missing" or "manual";

    // Original input
    [JsonPropertyName("file_name")]       public string? FileName { get; init; }
    [JsonPropertyName("file_path")]       public string? FilePath { get; init; }
    [JsonPropertyName("file_size_bytes")] public long? FileSizeBytes { get; init; }
    [JsonPropertyName("content_hash")]    public string? ContentHash { get; init; }

    // Review data
    [JsonPropertyName("review_item_id")]  public Guid? ReviewItemId { get; init; }
    [JsonPropertyName("review_trigger")]  public string? ReviewTrigger { get; init; }
    [JsonPropertyName("review_detail")]   public string? ReviewDetail { get; init; }
    [JsonPropertyName("candidates_json")] public string? CandidatesJson { get; init; }
    [JsonPropertyName("has_user_locks")]  public bool HasUserLocks { get; init; }

    [JsonPropertyName("match_level")]      public string MatchLevel { get; init; } = "work";

    [JsonPropertyName("canonical_values")] public IReadOnlyList<RegistryCanonicalValue> CanonicalValues { get; init; } = [];
    [JsonPropertyName("claim_history")]    public IReadOnlyList<RegistryClaimRecord> ClaimHistory { get; init; } = [];
    [JsonPropertyName("bridge_ids")]       public Dictionary<string, string> BridgeIds { get; init; } = [];
}

/// <summary>A canonical value with conflict and provider info.</summary>
public sealed record RegistryCanonicalValue(
    [property: JsonPropertyName("key")]                string Key,
    [property: JsonPropertyName("value")]              string Value,
    [property: JsonPropertyName("is_conflicted")]      bool IsConflicted,
    [property: JsonPropertyName("winning_provider_id")]string? WinningProviderId,
    [property: JsonPropertyName("needs_review")]       bool NeedsReview,
    [property: JsonPropertyName("last_scored_at")]     DateTimeOffset LastScoredAt);

/// <summary>A single claim from the voting history.</summary>
public sealed record RegistryClaimRecord(
    [property: JsonPropertyName("id")]             Guid Id,
    [property: JsonPropertyName("claim_key")]      string ClaimKey,
    [property: JsonPropertyName("claim_value")]    string ClaimValue,
    [property: JsonPropertyName("provider_id")]    Guid ProviderId,
    [property: JsonPropertyName("confidence")]     double Confidence,
    [property: JsonPropertyName("is_user_locked")] bool IsUserLocked,
    [property: JsonPropertyName("claimed_at")]     DateTimeOffset ClaimedAt);
