using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Metadata;

public sealed record DeferredEnrichmentTriggerResponse(
    [property: JsonPropertyName("pending_count")] int PendingCount,
    [property: JsonPropertyName("message")] string Message);

public sealed record DeferredEnrichmentStatusResponse(
    [property: JsonPropertyName("pending_count")] int PendingCount,
    [property: JsonPropertyName("two_pass_enabled")] bool TwoPassEnabled);
