using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class IngestionOperationsSnapshotViewModel
{
    [JsonPropertyName("summary")]
    public IngestionOperationsSummaryViewModel Summary { get; set; } = new();

    [JsonPropertyName("active_jobs")]
    public List<IngestionOperationsJobViewModel> ActiveJobs { get; set; } = [];

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

public sealed class IngestionPipelineStageViewModel
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
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
    [JsonPropertyName("registered_count")] public int RegisteredCount { get; set; }
    [JsonPropertyName("review_count")] public int ReviewCount { get; set; }
    [JsonPropertyName("failed_count")] public int FailedCount { get; set; }
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
