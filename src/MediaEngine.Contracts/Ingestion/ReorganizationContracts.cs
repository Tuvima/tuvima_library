using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Ingestion;

public sealed record CreateReorganizationPlanRequest(
    [property: JsonPropertyName("items")] IReadOnlyList<ReorganizationCandidateDto> Items);

public sealed record ReorganizationCandidateDto(
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("destination_source_id")] Guid DestinationSourceId,
    [property: JsonPropertyName("current_path")] string CurrentPath,
    [property: JsonPropertyName("proposed_path")] string? ProposedPath,
    [property: JsonPropertyName("unresolved_reason")] string? UnresolvedReason = null);

public sealed record ReorganizationPlanDto(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("library_id")] Guid LibraryId,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("can_execute")] bool CanExecute,
    [property: JsonPropertyName("summary")] ReorganizationPlanSummaryDto Summary,
    [property: JsonPropertyName("items")] IReadOnlyList<ReorganizationPlanItemDto> Items);

public sealed record ReorganizationPlanSummaryDto(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("unchanged")] int Unchanged,
    [property: JsonPropertyName("renamed")] int Renamed,
    [property: JsonPropertyName("moved")] int Moved,
    [property: JsonPropertyName("conflicts")] int Conflicts,
    [property: JsonPropertyName("unresolved")] int Unresolved,
    [property: JsonPropertyName("blocked")] int Blocked,
    [property: JsonPropertyName("errors")] int Errors);

public sealed record ReorganizationPlanItemDto(
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("destination_source_id")] Guid DestinationSourceId,
    [property: JsonPropertyName("current_path")] string CurrentPath,
    [property: JsonPropertyName("proposed_path")] string? ProposedPath,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record ExecuteReorganizationPlanRequest(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("fingerprint")] string Fingerprint);

public sealed record ReorganizationExecutionDto(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("library_id")] Guid LibraryId,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("succeeded")] int Succeeded,
    [property: JsonPropertyName("blocked")] int Blocked,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("items")] IReadOnlyList<ReorganizationExecutionItemDto> Items);

public sealed record ReorganizationExecutionItemDto(
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("current_path")] string CurrentPath,
    [property: JsonPropertyName("proposed_path")] string ProposedPath,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("reason")] string? Reason);
