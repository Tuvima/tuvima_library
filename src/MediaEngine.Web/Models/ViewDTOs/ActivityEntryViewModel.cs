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

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private ActivityRichData? _richData;
    private bool _richDataParsed;

    /// <summary>
    /// Structured match data for FileIngested entries.
    /// Lazily deserialized from <see cref="ChangesJson"/>.
    /// </summary>
    [JsonIgnore]
    public ActivityRichData? RichData
    {
        get
        {
            if (_richDataParsed) return _richData;
            _richDataParsed = true;
            if (ActionType == "FileIngested" && !string.IsNullOrWhiteSpace(ChangesJson))
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
}

/// <summary>
/// Structured rich data for FileIngested activity entries.
/// Deserialized from <see cref="ActivityEntryViewModel.ChangesJson"/>.
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
