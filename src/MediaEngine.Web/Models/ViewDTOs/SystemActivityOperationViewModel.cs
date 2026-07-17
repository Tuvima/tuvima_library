using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class SystemActivityOperationViewModel
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("operation_type")] public string OperationType { get; set; } = string.Empty;
    [JsonPropertyName("operation_kind")] public string OperationKind { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("progress_percent")] public int ProgressPercent { get; set; }
    [JsonPropertyName("items_total")] public int ItemsTotal { get; set; }
    [JsonPropertyName("items_completed")] public int ItemsCompleted { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}
