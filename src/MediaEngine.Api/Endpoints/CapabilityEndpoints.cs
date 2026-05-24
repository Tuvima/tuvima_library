using System.Text.Json.Serialization;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

public static class CapabilityEndpoints
{
    public static IEndpointRouteBuilder MapCapabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/assets/{id:guid}/capabilities", async (
            Guid id,
            IEntityCapabilityStateRepository repository,
            CancellationToken ct) =>
        {
            var states = await repository.GetByEntityAsync(id, ct);
            return Results.Ok(states.Select(CapabilityStateDto.From).ToList());
        })
        .WithTags("Capabilities")
        .WithName("GetAssetCapabilities")
        .WithSummary("List explicit capability readiness states for a media asset.")
        .RequireAdminOrCurator();

        app.MapGet("/capabilities/summary", async (
            IEntityCapabilityStateRepository repository,
            CancellationToken ct) =>
        {
            var summary = await repository.GetSummaryAsync(ct);
            return Results.Ok(summary);
        })
        .WithTags("Capabilities")
        .WithName("GetCapabilitySummary")
        .WithSummary("Return counts by capability/status.")
        .RequireAdminOrCurator();

        return app;
    }
}

public sealed record CapabilityStateDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("entity_kind")] public string EntityKind { get; init; } = "";
    [JsonPropertyName("media_type")] public string? MediaType { get; init; }
    [JsonPropertyName("capability_id")] public string CapabilityId { get; init; } = "";
    [JsonPropertyName("capability_kind")] public string CapabilityKind { get; init; } = "";
    [JsonPropertyName("capability_version")] public string? CapabilityVersion { get; init; }
    [JsonPropertyName("sub_key")] public string? SubKey { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("requiredness")] public string Requiredness { get; init; } = "";
    [JsonPropertyName("source")] public string? Source { get; init; }
    [JsonPropertyName("confidence")] public double? Confidence { get; init; }
    [JsonPropertyName("artifact_count")] public int ArtifactCount { get; init; }
    [JsonPropertyName("artifact_summary")] public string? ArtifactSummary { get; init; }
    [JsonPropertyName("result_summary")] public string? ResultSummary { get; init; }
    [JsonPropertyName("last_operation_id")] public Guid? LastOperationId { get; init; }
    [JsonPropertyName("first_attempted_at")] public DateTimeOffset? FirstAttemptedAt { get; init; }
    [JsonPropertyName("last_attempted_at")] public DateTimeOffset? LastAttemptedAt { get; init; }
    [JsonPropertyName("succeeded_at")] public DateTimeOffset? SucceededAt { get; init; }
    [JsonPropertyName("next_retry_at")] public DateTimeOffset? NextRetryAt { get; init; }
    [JsonPropertyName("stale")] public bool Stale { get; init; }
    [JsonPropertyName("needs_rerun")] public bool NeedsRerun { get; init; }
    [JsonPropertyName("missing_reason")] public string? MissingReason { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public static CapabilityStateDto From(EntityCapabilityState state) => new()
    {
        Id = state.Id,
        EntityId = state.EntityId,
        EntityKind = state.EntityKind,
        MediaType = state.MediaType,
        CapabilityId = state.CapabilityId,
        CapabilityKind = state.CapabilityKind,
        CapabilityVersion = state.CapabilityVersion,
        SubKey = state.SubKey,
        Status = state.Status,
        Requiredness = state.Requiredness,
        Source = state.Source,
        Confidence = state.Confidence,
        ArtifactCount = state.ArtifactCount,
        ArtifactSummary = state.ArtifactSummary,
        ResultSummary = state.ResultSummary,
        LastOperationId = state.LastOperationId,
        FirstAttemptedAt = state.FirstAttemptedAt,
        LastAttemptedAt = state.LastAttemptedAt,
        SucceededAt = state.SucceededAt,
        NextRetryAt = state.NextRetryAt,
        Stale = state.Stale,
        NeedsRerun = state.NeedsRerun,
        MissingReason = state.MissingReason,
        LastError = state.LastError,
        CreatedAt = state.CreatedAt,
        UpdatedAt = state.UpdatedAt
    };
}
