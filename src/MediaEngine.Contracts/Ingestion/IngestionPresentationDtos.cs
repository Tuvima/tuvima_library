using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Ingestion;

public sealed class IngestionPresentationSnapshotDto
{
    [JsonPropertyName("batch_progress")]
    public MediaEngine.Contracts.Realtime.BatchProgressEvent? BatchProgress { get; set; }

    [JsonPropertyName("is_running")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle";

    [JsonPropertyName("files_discovered")]
    public int FilesDiscovered { get; set; }

    [JsonPropertyName("files_processed")]
    public int FilesProcessed { get; set; }

    [JsonPropertyName("library_groups")]
    public int LibraryGroups { get; set; }

    [JsonPropertyName("ready_groups")]
    public int ReadyGroups { get; set; }

    [JsonPropertyName("finishing_groups")]
    public int FinishingGroups { get; set; }

    [JsonPropertyName("review_groups")]
    public int ReviewGroups { get; set; }

    [JsonPropertyName("active_operations")]
    public int ActiveOperations { get; set; }

    [JsonPropertyName("queued_operations")]
    public int QueuedOperations { get; set; }

    [JsonPropertyName("retry_waiting_operations")]
    public int RetryWaitingOperations { get; set; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("last_activity_at")]
    public DateTimeOffset? LastActivityAt { get; set; }

    [JsonPropertyName("current_media")]
    public List<IngestionMediaGroupDto> CurrentMedia { get; set; } = [];

    [JsonPropertyName("current_media_total")]
    public int CurrentMediaTotal { get; set; }

    [JsonPropertyName("attention")]
    public List<IngestionAttentionItemDto> Attention { get; set; } = [];

    [JsonPropertyName("recent_days")]
    public List<IngestionRecentDayDto> RecentDays { get; set; } = [];

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IngestionMediaGroupDto
{
    [JsonPropertyName("group_id")]
    public Guid GroupId { get; set; }

    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; set; }

    [JsonPropertyName("work_id")]
    public Guid? WorkId { get; set; }

    [JsonPropertyName("edition_id")]
    public Guid? EditionId { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "Unknown";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Identifying media";

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("availability")]
    public string Availability { get; set; } = "adding";

    [JsonPropertyName("status_label")]
    public string StatusLabel { get; set; } = "Adding";

    [JsonPropertyName("child_completed")]
    public int ChildCompleted { get; set; }

    [JsonPropertyName("child_expected")]
    public int? ChildExpected { get; set; }

    [JsonPropertyName("child_unit")]
    public string? ChildUnit { get; set; }

    [JsonPropertyName("detail_route")]
    public string? DetailRoute { get; set; }

    [JsonPropertyName("people")]
    public IngestionFacetStateDto People { get; set; } = IngestionFacetStateDto.NotApplicable("People");

    [JsonPropertyName("artwork")]
    public IngestionFacetStateDto Artwork { get; set; } = IngestionFacetStateDto.NotApplicable("Artwork");

    [JsonPropertyName("metadata")]
    public IngestionFacetStateDto Metadata { get; set; } = IngestionFacetStateDto.NotApplicable("Metadata");

    [JsonPropertyName("relationships")]
    public IngestionFacetStateDto Relationships { get; set; } = IngestionFacetStateDto.NotApplicable("Relationships");

    [JsonPropertyName("text_tracks")]
    public IngestionFacetStateDto TextTracks { get; set; } = IngestionFacetStateDto.NotApplicable("Text tracks");

    [JsonPropertyName("added_at")]
    public DateTimeOffset AddedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IngestionFacetStateDto
{
    [JsonPropertyName("state")]
    public string State { get; set; } = "notApplicable";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    public static IngestionFacetStateDto NotApplicable(string label) => new()
    {
        State = "notApplicable",
        Label = label,
    };
}

public sealed class IngestionMediaChildDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled";

    [JsonPropertyName("sequence_label")]
    public string? SequenceLabel { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("duration_label")]
    public string? DurationLabel { get; set; }
}

public sealed class IngestionAttentionItemDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "attention";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "Needs attention";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }
}

public sealed class IngestionRecentDayDto
{
    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<IngestionMediaGroupDto> Items { get; set; } = [];
}

public sealed class ActivityHistorySummaryDto
{
    [JsonPropertyName("completed_runs_this_week")]
    public int CompletedRunsThisWeek { get; set; }

    [JsonPropertyName("items_added_today")]
    public int ItemsAddedToday { get; set; }

    [JsonPropertyName("items_needing_follow_up")]
    public int ItemsNeedingFollowUp { get; set; }

    [JsonPropertyName("last_activity_at")]
    public DateTimeOffset? LastActivityAt { get; set; }

    [JsonPropertyName("last_files_processed")]
    public int? LastFilesProcessed { get; set; }

    [JsonPropertyName("last_groups_added")]
    public int? LastGroupsAdded { get; set; }
}

public sealed class ActivityBatchPresentationDto
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "Library update";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "This run completed.";

    [JsonPropertyName("files_processed")]
    public int? FilesProcessed { get; set; }

    [JsonPropertyName("groups_added")]
    public int? GroupsAdded { get; set; }

    [JsonPropertyName("people_updated")]
    public int? PeopleUpdated { get; set; }

    [JsonPropertyName("artwork_added")]
    public int? ArtworkAdded { get; set; }

    [JsonPropertyName("text_tracks_added")]
    public int? TextTracksAdded { get; set; }

    [JsonPropertyName("items_needing_follow_up")]
    public int? ItemsNeedingFollowUp { get; set; }

    [JsonPropertyName("added_preview")]
    public List<IngestionMediaGroupDto> AddedPreview { get; set; } = [];

    [JsonPropertyName("added_total")]
    public int AddedTotal { get; set; }

    [JsonPropertyName("follow_up")]
    public List<IngestionAttentionItemDto> FollowUp { get; set; } = [];

    [JsonPropertyName("timeline")]
    public List<ActivityBatchMilestoneDto> Timeline { get; set; } = [];
}

public sealed class ActivityBatchMilestoneDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Processing";

    [JsonPropertyName("state")]
    public string State { get; set; } = "complete";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; set; }
}
