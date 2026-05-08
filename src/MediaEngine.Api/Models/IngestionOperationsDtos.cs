using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class IngestionOperationsSnapshotDto
{
    [JsonPropertyName("summary")]
    public IngestionOperationsSummaryDto Summary { get; init; } = new();

    [JsonPropertyName("active_jobs")]
    public List<IngestionOperationsJobDto> ActiveJobs { get; init; } = [];

    [JsonPropertyName("pipeline_stages")]
    public List<IngestionPipelineStageDto> PipelineStages { get; init; } = [];

    [JsonPropertyName("review_reasons")]
    public List<IngestionReviewReasonDto> ReviewReasons { get; init; } = [];

    [JsonPropertyName("source_groups")]
    public List<IngestionSourceGroupDto> SourceGroups { get; init; } = [];

    [JsonPropertyName("provider_health")]
    public List<IngestionProviderHealthDto> ProviderHealth { get; init; } = [];

    [JsonPropertyName("recent_batches")]
    public List<IngestionOperationsBatchDto> RecentBatches { get; init; } = [];

    [JsonPropertyName("organization")]
    public IngestionOrganizationRulesDto Organization { get; init; } = new();

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
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

public sealed class IngestionPipelineStageDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("helper")]
    public string Helper { get; init; } = "";
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

    [JsonPropertyName("registered_count")]
    public int RegisteredCount { get; init; }

    [JsonPropertyName("review_count")]
    public int ReviewCount { get; init; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";
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
