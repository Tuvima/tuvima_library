using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Operations;

public sealed class EnrichmentRefreshScheduleDto
{
    [JsonPropertyName("entity_type")] public string EntityType { get; init; } = string.Empty;
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("entity_name")] public string EntityName { get; init; } = string.Empty;
    [JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;
    [JsonPropertyName("provider_id")] public string ProviderId { get; init; } = string.Empty;
    [JsonPropertyName("policy_key")] public string PolicyKey { get; init; } = string.Empty;
    [JsonPropertyName("interval_days")] public int IntervalDays { get; init; }
    [JsonPropertyName("last_success_at")] public DateTimeOffset? LastSuccessAt { get; init; }
    [JsonPropertyName("last_attempt_at")] public DateTimeOffset? LastAttemptAt { get; init; }
    [JsonPropertyName("next_due_at")] public DateTimeOffset NextDueAt { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "Scheduled";
    [JsonPropertyName("failure_count")] public int FailureCount { get; init; }
    [JsonPropertyName("retry_after")] public DateTimeOffset? RetryAfter { get; init; }
    [JsonPropertyName("operation_id")] public Guid? OperationId { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed class EnrichmentRefreshScheduleResponse
{
    [JsonPropertyName("items")] public IReadOnlyList<EnrichmentRefreshScheduleDto> Items { get; init; } = [];
    [JsonPropertyName("total_count")] public int TotalCount { get; init; }
    [JsonPropertyName("overdue_count")] public int OverdueCount { get; init; }
    [JsonPropertyName("due_next_seven_days_count")] public int DueNextSevenDaysCount { get; init; }
    [JsonPropertyName("generated_at")] public DateTimeOffset GeneratedAt { get; init; }
}

public sealed class EnrichmentRefreshQueuedResponse
{
    [JsonPropertyName("entity_type")] public string EntityType { get; init; } = string.Empty;
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("operation_id")] public Guid? OperationId { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "Queued";
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}
