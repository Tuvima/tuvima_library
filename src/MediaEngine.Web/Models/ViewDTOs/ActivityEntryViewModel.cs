using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a single system activity entry.
/// Maps from the Engine's <c>GET /activity/recent</c> response.
/// </summary>
public sealed class ActivityEntryViewModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("occurred_at")]
    public string OccurredAt { get; set; } = string.Empty;

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("hub_name")]
    public string? HubName { get; set; }

    [JsonPropertyName("entity_id")]
    public string? EntityId { get; set; }

    [JsonPropertyName("entity_type")]
    public string? EntityType { get; set; }

    [JsonPropertyName("profile_id")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("changes_json")]
    public string? ChangesJson { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("ingestion_run_id")]
    public string? IngestionRunId { get; set; }

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private ActivityRichData? _richData;
    private bool _richDataParsed;

    /// <summary>
    /// Structured match data for FileIngested and MediaAdded entries.
    /// Lazily deserialized from <see cref="ChangesJson"/>.
    /// </summary>
    [JsonIgnore]
    public ActivityRichData? RichData
    {
        get
        {
            if (_richDataParsed) return _richData;
            _richDataParsed = true;
            if (ActionType is "FileIngested" or "MediaAdded"
                && !string.IsNullOrWhiteSpace(ChangesJson))
            {
                try
                {
                    _richData = JsonSerializer.Deserialize<ActivityRichData>(ChangesJson);
                }
                catch
                {
                    // Gracefully handle malformed JSON.
                }
            }
            return _richData;
        }
    }

    private ReviewRichData? _reviewData;
    private bool _reviewDataParsed;

    /// <summary>
    /// Structured data for ReviewItemResolved entries (cover, title, author, year, desc, action).
    /// Lazily deserialized from <see cref="ChangesJson"/>.
    /// </summary>
    [JsonIgnore]
    public ReviewRichData? ReviewData
    {
        get
        {
            if (_reviewDataParsed) return _reviewData;
            _reviewDataParsed = true;
            if (ActionType == "ReviewItemResolved" && !string.IsNullOrWhiteSpace(ChangesJson))
            {
                try
                {
                    _reviewData = JsonSerializer.Deserialize<ReviewRichData>(ChangesJson);
                }
                catch
                {
                    // Gracefully handle malformed JSON.
                }
            }
            return _reviewData;
        }
    }

    /// <summary>Human-friendly relative timestamp (e.g. "5m ago", "2h ago").</summary>
    public string RelativeTime
    {
        get
        {
            if (!DateTimeOffset.TryParse(OccurredAt, out var ts))
                return "just now";

            var elapsed = DateTimeOffset.UtcNow - ts;

            return elapsed.TotalMinutes switch
            {
                < 1  => "just now",
                < 60 => $"{(int)elapsed.TotalMinutes}m ago",
                < 1440 => $"{(int)elapsed.TotalHours}h ago",
                _ => $"{(int)elapsed.TotalDays}d ago",
            };
        }
    }
}

/// <summary>
/// Response model for <c>POST /activity/prune</c>.
/// </summary>
public sealed class PruneResultViewModel
{
    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; set; }
}

/// <summary>
/// Response model for <c>GET /activity/stats</c>.
/// </summary>
public sealed class ActivityStatsViewModel
{
    [JsonPropertyName("total_entries")]
    public long TotalEntries { get; set; }

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; set; }
}

/// <summary>
/// Structured rich data for ReviewItemResolved activity entries.
/// Deserialized from <see cref="ActivityEntryViewModel.ChangesJson"/>.
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
/// Deserialized from <see cref="ActivityEntryViewModel.ChangesJson"/>.
/// Handles both JSON shapes: FileIngested uses <c>cover_url</c>/<c>organized_to</c>,
/// MediaAdded uses <c>cover</c>/<c>organized_path</c>/<c>hub_name</c>.
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

    // ── MediaAdded-specific fields ────────────────────────────────────────

    [JsonPropertyName("hub_name")]
    public string? HubName { get; set; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("stage1_claims")]
    public int Stage1Claims { get; set; }

    [JsonPropertyName("stage2_claims")]
    public int Stage2Claims { get; set; }

    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; set; }

    // ── Unified accessors ─────────────────────────────────────────────────

    /// <summary>Resolved cover URL — prefers <c>cover_url</c>, falls back to <c>cover</c>.</summary>
    [JsonIgnore]
    public string? ResolvedCoverUrl => CoverUrl ?? Cover;

    /// <summary>Resolved organized path — prefers <c>organized_to</c>, falls back to <c>organized_path</c>.</summary>
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

/// <summary>
/// Response model for <c>POST /ingestion/reconcile</c>.
/// </summary>
public sealed class ReconciliationResultDto
{
    [JsonPropertyName("total_scanned")]
    public int TotalScanned { get; set; }

    [JsonPropertyName("missing_count")]
    public int MissingCount { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; set; }
}
