using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Operations;

public sealed record CapabilityStateDto
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
