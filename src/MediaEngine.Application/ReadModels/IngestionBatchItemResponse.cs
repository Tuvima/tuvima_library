using System.Text.Json.Serialization;

namespace MediaEngine.Application.ReadModels;

public sealed class IngestionBatchItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; init; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = "";

    [JsonPropertyName("media_asset_id")]
    public Guid? MediaAssetId { get; init; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("identity_state")]
    public string? IdentityState { get; init; }

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "";

    [JsonPropertyName("stage_order")]
    public int StageOrder { get; init; }

    [JsonPropertyName("progress_percent")]
    public int ProgressPercent { get; init; }

    [JsonPropertyName("work_units_total")]
    public int WorkUnitsTotal { get; init; }

    [JsonPropertyName("work_units_completed")]
    public int WorkUnitsCompleted { get; init; }

    [JsonPropertyName("is_terminal")]
    public bool IsTerminal { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; init; }

    [JsonPropertyName("detected_title")]
    public string? DetectedTitle { get; init; }

    [JsonPropertyName("error_detail")]
    public string? ErrorDetail { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}
