using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Ingestion;

/// <summary>Wire shape for an ingestion batch.</summary>
public sealed class IngestionBatchResponse
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

public sealed class IngestionBatchItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("media_asset_id")]
    public Guid? MediaAssetId { get; set; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("identity_state")]
    public string? IdentityState { get; set; }

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = "";

    [JsonPropertyName("stage_order")]
    public int StageOrder { get; set; }

    [JsonPropertyName("progress_percent")]
    public int ProgressPercent { get; set; }

    [JsonPropertyName("work_units_total")]
    public int WorkUnitsTotal { get; set; }

    [JsonPropertyName("work_units_completed")]
    public int WorkUnitsCompleted { get; set; }

    [JsonPropertyName("is_terminal")]
    public bool IsTerminal { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; set; }

    [JsonPropertyName("detected_title")]
    public string? DetectedTitle { get; set; }

    [JsonPropertyName("error_detail")]
    public string? ErrorDetail { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
