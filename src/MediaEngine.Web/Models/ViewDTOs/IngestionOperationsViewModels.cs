using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class IngestionOperationsSnapshotViewModel
{
    [JsonPropertyName("summary")]
    public IngestionOperationsSummaryViewModel Summary { get; set; } = new();

    [JsonPropertyName("active_jobs")]
    public List<IngestionOperationsJobViewModel> ActiveJobs { get; set; } = [];

    [JsonPropertyName("current_activities")]
    public List<IngestionCurrentActivityViewModel> CurrentActivities { get; set; } = [];

    [JsonPropertyName("pipeline_stages")]
    public List<IngestionPipelineStageViewModel> PipelineStages { get; set; } = [];

    [JsonPropertyName("review_reasons")]
    public List<IngestionReviewReasonViewModel> ReviewReasons { get; set; } = [];

    [JsonPropertyName("source_groups")]
    public List<IngestionSourceGroupViewModel> SourceGroups { get; set; } = [];

    [JsonPropertyName("provider_health")]
    public List<IngestionProviderHealthViewModel> ProviderHealth { get; set; } = [];

    [JsonPropertyName("recent_batches")]
    public List<IngestionOperationsBatchViewModel> RecentBatches { get; set; } = [];

    [JsonPropertyName("organization")]
    public IngestionOrganizationRulesViewModel Organization { get; set; } = new();

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; set; }
}

public sealed class IngestionOperationsSummaryViewModel
{
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("registered_items")] public int RegisteredItems { get; set; }
    [JsonPropertyName("provisional_items")] public int ProvisionalItems { get; set; }
    [JsonPropertyName("items_needing_review")] public int ItemsNeedingReview { get; set; }
    [JsonPropertyName("active_jobs")] public int ActiveJobs { get; set; }
    [JsonPropertyName("failed_jobs")] public int FailedJobs { get; set; }
    [JsonPropertyName("provider_warnings")] public int ProviderWarnings { get; set; }
    [JsonPropertyName("last_successful_scan_time")] public DateTimeOffset? LastSuccessfulScanTime { get; set; }
    [JsonPropertyName("engine_status")] public string EngineStatus { get; set; } = "Unknown";
    [JsonPropertyName("health_label")] public string HealthLabel { get; set; } = "Unknown";
}

public sealed class IngestionOperationsJobViewModel
{
    [JsonPropertyName("job_id")] public Guid JobId { get; set; }
    [JsonPropertyName("job_type")] public string JobType { get; set; } = "";
    [JsonPropertyName("media_type")] public string? MediaType { get; set; }
    [JsonPropertyName("source_folder")] public string? SourceFolder { get; set; }
    [JsonPropertyName("current_stage")] public string CurrentStage { get; set; } = "";
    [JsonPropertyName("current_item")] public string? CurrentItem { get; set; }
    [JsonPropertyName("processed_count")] public int ProcessedCount { get; set; }
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    [JsonPropertyName("percent_complete")] public double PercentComplete { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("elapsed")] public TimeSpan? Elapsed { get; set; }
    [JsonPropertyName("last_updated_time")] public DateTimeOffset? LastUpdatedTime { get; set; }
    [JsonPropertyName("warning_summary")] public string? WarningSummary { get; set; }
}

public sealed class IngestionCurrentActivityViewModel
{
    [JsonPropertyName("stage_key")] public string StageKey { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    [JsonPropertyName("current_item")] public string? CurrentItem { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("processed_count")] public int ProcessedCount { get; set; }
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    [JsonPropertyName("count_unit")] public string CountUnit { get; set; } = "files";
    [JsonPropertyName("percent_complete")] public double PercentComplete { get; set; }
    [JsonPropertyName("last_updated_time")] public DateTimeOffset? LastUpdatedTime { get; set; }
    [JsonPropertyName("queued_count")] public int QueuedCount { get; set; }
    [JsonPropertyName("active_count")] public int ActiveCount { get; set; }
    [JsonPropertyName("sample_items")] public List<string> SampleItems { get; set; } = [];
    [JsonPropertyName("metric_label")] public string? MetricLabel { get; set; }
    [JsonPropertyName("metric_value")] public string? MetricValue { get; set; }
    [JsonPropertyName("metric_tone")] public string? MetricTone { get; set; }
    [JsonPropertyName("current_batch")] public IngestionActivityBatchViewModel? CurrentBatch { get; set; }
}

public sealed class IngestionActivityBatchViewModel
{
    [JsonPropertyName("batch_number")] public int BatchNumber { get; set; }
    [JsonPropertyName("batch_size")] public int BatchSize { get; set; }
    [JsonPropertyName("total_batches")] public int TotalBatches { get; set; }
    [JsonPropertyName("completed_count")] public int CompletedCount { get; set; }
    [JsonPropertyName("active_count")] public int ActiveCount { get; set; }
    [JsonPropertyName("pending_count")] public int PendingCount { get; set; }
    [JsonPropertyName("review_count")] public int ReviewCount { get; set; }
    [JsonPropertyName("active_items")] public List<string> ActiveItems { get; set; } = [];
    [JsonPropertyName("completed_preview")] public List<string> CompletedPreview { get; set; } = [];
    [JsonPropertyName("pending_preview")] public List<string> PendingPreview { get; set; } = [];
    [JsonPropertyName("review_preview")] public List<string> ReviewPreview { get; set; } = [];
}

public sealed class IngestionPipelineStageViewModel
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    [JsonPropertyName("helper")] public string Helper { get; set; } = "";
}

public sealed class IngestionReviewReasonViewModel
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
}

public sealed class IngestionSourceGroupViewModel
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "";
    [JsonPropertyName("libraries")] public List<IngestionSourceLibraryViewModel> Libraries { get; set; } = [];
}

public sealed class IngestionSourceLibraryViewModel
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("folders")] public List<IngestionSourceFolderViewModel> Folders { get; set; } = [];
}

public sealed class IngestionSourceFolderViewModel
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "";
    [JsonPropertyName("purpose")] public string Purpose { get; set; } = "";
    [JsonPropertyName("scan_mode")] public string ScanMode { get; set; } = "";
    [JsonPropertyName("last_scan")] public DateTimeOffset? LastScan { get; set; }
    [JsonPropertyName("item_count")] public int ItemCount { get; set; }
    [JsonPropertyName("unresolved_count")] public int UnresolvedCount { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("is_reachable")] public bool? IsReachable { get; set; }
    [JsonPropertyName("permissions_valid")] public bool? PermissionsValid { get; set; }
    [JsonPropertyName("music_note")] public string? MusicNote { get; set; }
}

public sealed class IngestionProviderHealthViewModel
{
    [JsonPropertyName("provider_id")] public string ProviderId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("last_successful_call")] public DateTimeOffset? LastSuccessfulCall { get; set; }
    [JsonPropertyName("last_error")] public string? LastError { get; set; }
    [JsonPropertyName("warning")] public string? Warning { get; set; }
}

public sealed class IngestionOperationsBatchViewModel
{
    [JsonPropertyName("batch_id")] public Guid BatchId { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; set; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("media_type")] public string? MediaType { get; set; }
    [JsonPropertyName("total_files")] public int TotalFiles { get; set; }
    [JsonPropertyName("processed_files")] public int ProcessedFiles { get; set; }
    [JsonPropertyName("movies_count")] public int MoviesCount { get; set; }
    [JsonPropertyName("tv_shows_count")] public int TvShowsCount { get; set; }
    [JsonPropertyName("books_count")] public int BooksCount { get; set; }
    [JsonPropertyName("audiobooks_count")] public int AudiobooksCount { get; set; }
    [JsonPropertyName("music_count")] public int MusicCount { get; set; }
    [JsonPropertyName("comics_count")] public int ComicsCount { get; set; }
    [JsonPropertyName("registered_count")] public int RegisteredCount { get; set; }
    [JsonPropertyName("review_count")] public int ReviewCount { get; set; }
    [JsonPropertyName("failed_count")] public int FailedCount { get; set; }
    [JsonPropertyName("people_generated_count")] public int PeopleGeneratedCount { get; set; }
    [JsonPropertyName("artwork_downloaded_count")] public int ArtworkDownloadedCount { get; set; }
    [JsonPropertyName("metadata_updated_count")] public int MetadataUpdatedCount { get; set; }
    [JsonPropertyName("duration_seconds")] public int? DurationSeconds { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
}

public sealed class IngestionOrganizationRulesViewModel
{
    [JsonPropertyName("rename_enabled")] public bool RenameEnabled { get; set; }
    [JsonPropertyName("move_enabled")] public bool MoveEnabled { get; set; }
    [JsonPropertyName("preview_required")] public bool PreviewRequired { get; set; }
    [JsonPropertyName("folder_template_summary")] public string FolderTemplateSummary { get; set; } = "";
    [JsonPropertyName("filename_template_summary")] public string FilenameTemplateSummary { get; set; } = "";
    [JsonPropertyName("last_organization_run")] public DateTimeOffset? LastOrganizationRun { get; set; }
    [JsonPropertyName("music_behavior")] public string MusicBehavior { get; set; } = "";
}

public sealed class MediaOperationViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("operation_type")] public string OperationType { get; set; } = "";
    [JsonPropertyName("operation_kind")] public string OperationKind { get; set; } = "";
    [JsonPropertyName("entity_id")] public Guid? EntityId { get; set; }
    [JsonPropertyName("entity_kind")] public string? EntityKind { get; set; }
    [JsonPropertyName("batch_id")] public Guid? BatchId { get; set; }
    [JsonPropertyName("source_path")] public string? SourcePath { get; set; }
    [JsonPropertyName("capability_id")] public string? CapabilityId { get; set; }
    [JsonPropertyName("capability_version")] public string? CapabilityVersion { get; set; }
    [JsonPropertyName("sub_key")] public string? SubKey { get; set; }
    [JsonPropertyName("plugin_id")] public string? PluginId { get; set; }
    [JsonPropertyName("plugin_version")] public string? PluginVersion { get; set; }
    [JsonPropertyName("provider_id")] public string? ProviderId { get; set; }
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("queue_name")] public string QueueName { get; set; } = "";
    [JsonPropertyName("queue_position")] public int? QueuePosition { get; set; }
    [JsonPropertyName("attempt_count")] public int AttemptCount { get; set; }
    [JsonPropertyName("lease_owner")] public string? LeaseOwner { get; set; }
    [JsonPropertyName("lease_expires_at")] public DateTimeOffset? LeaseExpiresAt { get; set; }
    [JsonPropertyName("heartbeat_at")] public DateTimeOffset? HeartbeatAt { get; set; }
    [JsonPropertyName("next_retry_at")] public DateTimeOffset? NextRetryAt { get; set; }
    [JsonPropertyName("progress_percent")] public int ProgressPercent { get; set; }
    [JsonPropertyName("items_total")] public int ItemsTotal { get; set; }
    [JsonPropertyName("items_completed")] public int ItemsCompleted { get; set; }
    [JsonPropertyName("items_failed")] public int ItemsFailed { get; set; }
    [JsonPropertyName("result_summary")] public string? ResultSummary { get; set; }
    [JsonPropertyName("last_error")] public string? LastError { get; set; }
    [JsonPropertyName("missing_reason")] public string? MissingReason { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class MediaOperationDetailViewModel
{
    [JsonPropertyName("operation")] public MediaOperationViewModel Operation { get; set; } = new();
    [JsonPropertyName("events")] public List<MediaOperationEventViewModel> Events { get; set; } = [];
}

public sealed class MediaOperationEventViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("operation_id")] public Guid OperationId { get; set; }
    [JsonPropertyName("entity_id")] public Guid? EntityId { get; set; }
    [JsonPropertyName("batch_id")] public Guid? BatchId { get; set; }
    [JsonPropertyName("event_type")] public string EventType { get; set; } = "";
    [JsonPropertyName("old_status")] public string? OldStatus { get; set; }
    [JsonPropertyName("new_status")] public string? NewStatus { get; set; }
    [JsonPropertyName("old_stage")] public string? OldStage { get; set; }
    [JsonPropertyName("new_stage")] public string? NewStage { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("detail_json")] public string? DetailJson { get; set; }
    [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; set; }
}

public sealed class EntityCapabilityStateViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; set; }
    [JsonPropertyName("entity_kind")] public string EntityKind { get; set; } = "";
    [JsonPropertyName("media_type")] public string? MediaType { get; set; }
    [JsonPropertyName("capability_id")] public string CapabilityId { get; set; } = "";
    [JsonPropertyName("capability_kind")] public string CapabilityKind { get; set; } = "";
    [JsonPropertyName("capability_version")] public string? CapabilityVersion { get; set; }
    [JsonPropertyName("sub_key")] public string? SubKey { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("requiredness")] public string Requiredness { get; set; } = "";
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("confidence")] public double? Confidence { get; set; }
    [JsonPropertyName("artifact_count")] public int ArtifactCount { get; set; }
    [JsonPropertyName("artifact_summary")] public string? ArtifactSummary { get; set; }
    [JsonPropertyName("result_summary")] public string? ResultSummary { get; set; }
    [JsonPropertyName("last_operation_id")] public Guid? LastOperationId { get; set; }
    [JsonPropertyName("first_attempted_at")] public DateTimeOffset? FirstAttemptedAt { get; set; }
    [JsonPropertyName("last_attempted_at")] public DateTimeOffset? LastAttemptedAt { get; set; }
    [JsonPropertyName("succeeded_at")] public DateTimeOffset? SucceededAt { get; set; }
    [JsonPropertyName("next_retry_at")] public DateTimeOffset? NextRetryAt { get; set; }
    [JsonPropertyName("stale")] public bool Stale { get; set; }
    [JsonPropertyName("needs_rerun")] public bool NeedsRerun { get; set; }
    [JsonPropertyName("missing_reason")] public string? MissingReason { get; set; }
    [JsonPropertyName("last_error")] public string? LastError { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IngestionBatchItemViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("file_path")] public string FilePath { get; set; } = "";
    [JsonPropertyName("file_name")] public string FileName { get; set; } = "";
    [JsonPropertyName("media_asset_id")] public Guid? MediaAssetId { get; set; }
    [JsonPropertyName("content_hash")] public string? ContentHash { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("identity_state")] public string? IdentityState { get; set; }
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("stage_order")] public int StageOrder { get; set; }
    [JsonPropertyName("progress_percent")] public int ProgressPercent { get; set; }
    [JsonPropertyName("work_units_total")] public int WorkUnitsTotal { get; set; }
    [JsonPropertyName("work_units_completed")] public int WorkUnitsCompleted { get; set; }
    [JsonPropertyName("is_terminal")] public bool IsTerminal { get; set; }
    [JsonPropertyName("media_type")] public string? MediaType { get; set; }
    [JsonPropertyName("confidence_score")] public double? ConfidenceScore { get; set; }
    [JsonPropertyName("detected_title")] public string? DetectedTitle { get; set; }
    [JsonPropertyName("error_detail")] public string? ErrorDetail { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}
