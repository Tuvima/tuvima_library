using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class IngestionOperationsSnapshotDto
{
    [JsonPropertyName("summary")]
    public IngestionOperationsSummaryDto Summary { get; init; } = new();

    [JsonPropertyName("active_jobs")]
    public List<IngestionOperationsJobDto> ActiveJobs { get; init; } = [];

    [JsonPropertyName("current_activities")]
    public List<IngestionCurrentActivityDto> CurrentActivities { get; init; } = [];

    [JsonPropertyName("pipeline_stages")]
    public List<IngestionPipelineStageDto> PipelineStages { get; init; } = [];

    [JsonPropertyName("stage_progress")]
    public List<IngestionStageProgressDto> StageProgress { get; init; } = [];

    [JsonPropertyName("review_reasons")]
    public List<IngestionReviewReasonDto> ReviewReasons { get; init; } = [];

    [JsonPropertyName("source_groups")]
    public List<IngestionSourceGroupDto> SourceGroups { get; init; } = [];

    [JsonPropertyName("provider_health")]
    public List<IngestionProviderHealthDto> ProviderHealth { get; init; } = [];

    [JsonPropertyName("provider_activity")]
    public List<IngestionProviderActivityDto> ProviderActivity { get; init; } = [];

    [JsonPropertyName("recent_batches")]
    public List<IngestionOperationsBatchDto> RecentBatches { get; init; } = [];

    [JsonPropertyName("organization")]
    public IngestionOrganizationRulesDto Organization { get; init; } = new();

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class IngestionProviderActivityDto
{
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = "";

    [JsonPropertyName("active_requests")]
    public int ActiveRequests { get; init; }

    [JsonPropertyName("requests_total")]
    public long RequestsTotal { get; init; }

    [JsonPropertyName("requests_last_minute")]
    public int RequestsLastMinute { get; init; }

    [JsonPropertyName("errors_total")]
    public long ErrorsTotal { get; init; }

    [JsonPropertyName("errors_last_minute")]
    public int ErrorsLastMinute { get; init; }

    [JsonPropertyName("throttle_wait_ms_total")]
    public long ThrottleWaitMsTotal { get; init; }

    [JsonPropertyName("average_latency_ms")]
    public double AverageLatencyMs { get; init; }

    [JsonPropertyName("last_request_at")]
    public DateTimeOffset? LastRequestAt { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }
}

public sealed class IngestionOperationsSummaryDto
{
    [JsonPropertyName("total_items")]
    public int TotalItems { get; init; }

    [JsonPropertyName("registered_items")]
    public int RegisteredItems { get; init; }

    [JsonPropertyName("provisional_items")]
    public int ProvisionalItems { get; init; }

    [JsonPropertyName("items_needing_review")]
    public int ItemsNeedingReview { get; init; }

    [JsonPropertyName("active_jobs")]
    public int ActiveJobs { get; init; }

    [JsonPropertyName("failed_jobs")]
    public int FailedJobs { get; init; }

    [JsonPropertyName("provider_warnings")]
    public int ProviderWarnings { get; init; }

    [JsonPropertyName("last_successful_scan_time")]
    public DateTimeOffset? LastSuccessfulScanTime { get; init; }

    [JsonPropertyName("engine_status")]
    public string EngineStatus { get; init; } = "Online";

    [JsonPropertyName("health_label")]
    public string HealthLabel { get; init; } = "Healthy";

    [JsonPropertyName("expected_outcomes")]
    public IngestionExpectedOutcomesDto? ExpectedOutcomes { get; init; }
}

public sealed class IngestionExpectedOutcomesDto
{
    [JsonPropertyName("total_files")]
    public int TotalFiles { get; init; }

    [JsonPropertyName("expected_resolved")]
    public int ExpectedResolved { get; init; }

    [JsonPropertyName("expected_exact_qid")]
    public int ExpectedExactQid { get; init; }

    [JsonPropertyName("expected_any_qid")]
    public int ExpectedAnyQid { get; init; }

    [JsonPropertyName("expected_review")]
    public int ExpectedReview { get; init; }

    [JsonPropertyName("expected_known_no_qid")]
    public int ExpectedKnownNoQid { get; init; }

    [JsonPropertyName("expected_duplicate")]
    public int ExpectedDuplicate { get; init; }

    [JsonPropertyName("expected_skipped")]
    public int ExpectedSkipped { get; init; }

    [JsonPropertyName("expected_corrupt")]
    public int ExpectedCorrupt { get; init; }
}

public sealed class IngestionOperationsJobDto
{
    [JsonPropertyName("job_id")]
    public Guid JobId { get; init; }

    [JsonPropertyName("job_type")]
    public string JobType { get; init; } = "Ingestion batch";

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("source_folder")]
    public string? SourceFolder { get; init; }

    [JsonPropertyName("current_stage")]
    public string CurrentStage { get; init; } = "Processing";

    [JsonPropertyName("current_item")]
    public string? CurrentItem { get; init; }

    [JsonPropertyName("processed_count")]
    public int ProcessedCount { get; init; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    [JsonPropertyName("count_unit")]
    public string CountUnit { get; init; } = "files";

    [JsonPropertyName("percent_complete")]
    public double PercentComplete { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "running";

    [JsonPropertyName("elapsed")]
    public TimeSpan? Elapsed { get; init; }

    [JsonPropertyName("last_updated_time")]
    public DateTimeOffset? LastUpdatedTime { get; init; }

    [JsonPropertyName("warning_summary")]
    public string? WarningSummary { get; init; }
}

public sealed class IngestionCurrentActivityDto
{
    [JsonPropertyName("stage_key")]
    public string StageKey { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = "";

    [JsonPropertyName("current_item")]
    public string? CurrentItem { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("processed_count")]
    public int ProcessedCount { get; init; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    [JsonPropertyName("count_unit")]
    public string CountUnit { get; init; } = "files";

    [JsonPropertyName("percent_complete")]
    public double PercentComplete { get; init; }

    [JsonPropertyName("last_updated_time")]
    public DateTimeOffset? LastUpdatedTime { get; init; }

    [JsonPropertyName("queued_count")]
    public int QueuedCount { get; init; }

    [JsonPropertyName("active_count")]
    public int ActiveCount { get; init; }

    [JsonPropertyName("sample_items")]
    public List<string> SampleItems { get; init; } = [];

    [JsonPropertyName("metric_label")]
    public string? MetricLabel { get; init; }

    [JsonPropertyName("metric_value")]
    public string? MetricValue { get; init; }

    [JsonPropertyName("metric_tone")]
    public string? MetricTone { get; init; }

    [JsonPropertyName("current_batch")]
    public IngestionActivityBatchDto? CurrentBatch { get; init; }
}

public sealed class IngestionActivityBatchDto
{
    [JsonPropertyName("batch_number")]
    public int BatchNumber { get; init; }

    [JsonPropertyName("batch_size")]
    public int BatchSize { get; init; }

    [JsonPropertyName("total_batches")]
    public int TotalBatches { get; init; }

    [JsonPropertyName("completed_count")]
    public int CompletedCount { get; init; }

    [JsonPropertyName("active_count")]
    public int ActiveCount { get; init; }

    [JsonPropertyName("pending_count")]
    public int PendingCount { get; init; }

    [JsonPropertyName("review_count")]
    public int ReviewCount { get; init; }

    [JsonPropertyName("active_items")]
    public List<string> ActiveItems { get; init; } = [];

    [JsonPropertyName("completed_preview")]
    public List<string> CompletedPreview { get; init; } = [];

    [JsonPropertyName("pending_preview")]
    public List<string> PendingPreview { get; init; } = [];

    [JsonPropertyName("review_preview")]
    public List<string> ReviewPreview { get; init; } = [];
}

public sealed class IngestionPipelineStageDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    [JsonPropertyName("helper")]
    public string Helper { get; init; } = "";
}

public sealed class IngestionStageProgressDto
{
    [JsonPropertyName("stage_number")]
    public int StageNumber { get; init; }

    [JsonPropertyName("stage_key")]
    public string StageKey { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("completed_files")]
    public int CompletedFiles { get; init; }

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; init; }

    [JsonPropertyName("percent_complete")]
    public double PercentComplete { get; init; }

    [JsonPropertyName("active_count")]
    public int ActiveCount { get; init; }

    [JsonPropertyName("queued_count")]
    public int QueuedCount { get; init; }

    [JsonPropertyName("status_label")]
    public string? StatusLabel { get; init; }

    [JsonPropertyName("active_item_label")]
    public string? ActiveItemLabel { get; init; }

    [JsonPropertyName("active_group_label")]
    public string? ActiveGroupLabel { get; init; }

    [JsonPropertyName("active_group_count")]
    public int ActiveGroupCount { get; init; }

    [JsonPropertyName("label_accuracy")]
    public string LabelAccuracy { get; init; } = "None";

    [JsonPropertyName("artifact_label")]
    public string? ArtifactLabel { get; init; }

    [JsonPropertyName("artifact_count")]
    public int? ArtifactCount { get; init; }

    [JsonPropertyName("last_updated_time")]
    public DateTimeOffset? LastUpdatedTime { get; init; }

    [JsonPropertyName("is_stale")]
    public bool IsStale { get; init; }

    [JsonPropertyName("detail_items")]
    public List<IngestionStageDetailItemDto> DetailItems { get; init; } = [];
}

public sealed class IngestionStageDetailItemDto
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("value")]
    public string Value { get; init; } = "";

    [JsonPropertyName("tone")]
    public string? Tone { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
}

public sealed class IngestionReviewReasonDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; init; } = "";
}

public sealed class IngestionSourceGroupDto
{
    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "";

    [JsonPropertyName("libraries")]
    public List<IngestionSourceLibraryDto> Libraries { get; init; } = [];
}

public sealed class IngestionSourceLibraryDto
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("folders")]
    public List<IngestionSourceFolderDto> Folders { get; init; } = [];
}

public sealed class IngestionSourceFolderDto
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "";

    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = "primary";

    [JsonPropertyName("scan_mode")]
    public string ScanMode { get; init; } = "automatic";

    [JsonPropertyName("last_scan")]
    public DateTimeOffset? LastScan { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("unresolved_count")]
    public int UnresolvedCount { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Unknown";

    [JsonPropertyName("is_reachable")]
    public bool? IsReachable { get; init; }

    [JsonPropertyName("permissions_valid")]
    public bool? PermissionsValid { get; init; }

    [JsonPropertyName("music_note")]
    public string? MusicNote { get; init; }
}

public sealed class IngestionProviderHealthDto
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Unknown";

    [JsonPropertyName("last_successful_call")]
    public DateTimeOffset? LastSuccessfulCall { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }

    [JsonPropertyName("warning")]
    public string? Warning { get; init; }
}

public sealed class IngestionOperationsBatchDto
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; init; }

    [JsonPropertyName("processed_files")]
    public int ProcessedFiles { get; init; }

    [JsonPropertyName("movies_count")]
    public int MoviesCount { get; init; }

    [JsonPropertyName("tv_shows_count")]
    public int TvShowsCount { get; init; }

    [JsonPropertyName("books_count")]
    public int BooksCount { get; init; }

    [JsonPropertyName("audiobooks_count")]
    public int AudiobooksCount { get; init; }

    [JsonPropertyName("music_count")]
    public int MusicCount { get; init; }

    [JsonPropertyName("comics_count")]
    public int ComicsCount { get; init; }

    [JsonPropertyName("registered_count")]
    public int RegisteredCount { get; init; }

    [JsonPropertyName("review_count")]
    public int ReviewCount { get; init; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; init; }

    [JsonPropertyName("people_generated_count")]
    public int PeopleGeneratedCount { get; init; }

    [JsonPropertyName("artwork_downloaded_count")]
    public int ArtworkDownloadedCount { get; init; }

    [JsonPropertyName("metadata_updated_count")]
    public int MetadataUpdatedCount { get; init; }

    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("stage_progress")]
    public List<IngestionStageProgressDto> StageProgress { get; init; } = [];
}

public sealed class IngestionOrganizationRulesDto
{
    [JsonPropertyName("rename_enabled")]
    public bool RenameEnabled { get; init; }

    [JsonPropertyName("move_enabled")]
    public bool MoveEnabled { get; init; }

    [JsonPropertyName("preview_required")]
    public bool PreviewRequired { get; init; } = true;

    [JsonPropertyName("folder_template_summary")]
    public string FolderTemplateSummary { get; init; } = "Not configured";

    [JsonPropertyName("filename_template_summary")]
    public string FilenameTemplateSummary { get; init; } = "Not configured";

    [JsonPropertyName("last_organization_run")]
    public DateTimeOffset? LastOrganizationRun { get; init; }

    [JsonPropertyName("music_behavior")]
    public string MusicBehavior { get; init; } = "Music keeps album folder structure conservative.";
}
