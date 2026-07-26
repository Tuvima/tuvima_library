using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.System;

public sealed record SystemActivityOperationDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("operation_type")] public string OperationType { get; init; } = string.Empty;
    [JsonPropertyName("operation_kind")] public string OperationKind { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("stage")] public string? Stage { get; init; }
    [JsonPropertyName("progress_percent")] public int ProgressPercent { get; init; }
    [JsonPropertyName("items_total")] public int ItemsTotal { get; init; }
    [JsonPropertyName("items_completed")] public int ItemsCompleted { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
}
