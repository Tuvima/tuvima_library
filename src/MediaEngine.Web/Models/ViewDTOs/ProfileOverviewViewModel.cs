using System.Text.Json.Serialization;
using MediaEngine.Domain.Models;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class ProfileOverviewViewModel
{
    [JsonPropertyName("profile")]
    public ProfileViewModel Profile { get; set; } = new(
        Guid.Empty,
        "Profile",
        "#7C4DFF",
        "Consumer",
        DateTimeOffset.UtcNow);

    [JsonPropertyName("stats")]
    public ProfileOverviewStatsViewModel Stats { get; set; } = new();

    [JsonPropertyName("recent_items")]
    public List<ProfileOverviewItemViewModel> RecentItems { get; set; } = [];

    [JsonPropertyName("continue_items")]
    public List<ProfileOverviewItemViewModel> ContinueItems { get; set; } = [];

    [JsonPropertyName("completed_items")]
    public List<ProfileOverviewItemViewModel> CompletedItems { get; set; } = [];

    [JsonPropertyName("recently_added_items")]
    public List<ProfileOverviewItemViewModel> RecentlyAddedItems { get; set; } = [];

    [JsonPropertyName("activity")]
    public List<ProfileOverviewActivityViewModel> Activity { get; set; } = [];

    [JsonPropertyName("taste")]
    public TasteProfile? Taste { get; set; }
}

public sealed class ProfileOverviewStatsViewModel
{
    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("in_progress")]
    public int InProgress { get; set; }

    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("recent_activity")]
    public int RecentActivity { get; set; }

    [JsonPropertyName("media_type_mix")]
    public Dictionary<string, int> MediaTypeMix { get; set; } = [];

    [JsonPropertyName("library_counts")]
    public Dictionary<string, int> LibraryCounts { get; set; } = [];

    [JsonPropertyName("activity_buckets")]
    public Dictionary<string, int> ActivityBuckets { get; set; } = [];

    [JsonPropertyName("top_genres")]
    public Dictionary<string, int> TopGenres { get; set; } = [];

    [JsonPropertyName("consumed_seconds")]
    public double ConsumedSeconds { get; set; }

    [JsonPropertyName("consumed_seconds_by_media_type")]
    public Dictionary<string, double> ConsumedSecondsByMediaType { get; set; } = [];
}

public sealed class ProfileOverviewItemViewModel
{
    [JsonPropertyName("asset_id")]
    public Guid AssetId { get; set; }

    [JsonPropertyName("work_id")]
    public Guid? WorkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "Media";

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("collection_name")]
    public string? CollectionName { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }

    [JsonPropertyName("position_seconds")]
    public double? PositionSeconds { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("progress_pct")]
    public double ProgressPct { get; set; }

    [JsonPropertyName("last_accessed")]
    public DateTimeOffset LastAccessed { get; set; }

    [JsonPropertyName("added_at")]
    public DateTimeOffset? AddedAt { get; set; }
}

public sealed class ProfileOverviewActivityViewModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("entity_id")]
    public Guid? EntityId { get; set; }
}
