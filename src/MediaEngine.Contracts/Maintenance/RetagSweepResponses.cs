using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Maintenance;

public sealed record RetagSweepStateResponse(
    [property: JsonPropertyName("has_pending_diff")] bool HasPendingDiff,
    [property: JsonPropertyName("pending_diff")] IReadOnlyList<RetagSweepPendingDiffEntry> PendingDiff,
    [property: JsonPropertyName("current_hashes")] IReadOnlyDictionary<string, string> CurrentHashes);

public sealed record RetagSweepPendingDiffEntry(
    [property: JsonPropertyName("media_type")] string MediaType,
    [property: JsonPropertyName("added_fields")] IReadOnlyList<string> AddedFields,
    [property: JsonPropertyName("removed_fields")] IReadOnlyList<string> RemovedFields);

public sealed record RetagSweepAppliedResponse(
    [property: JsonPropertyName("applied")] bool Applied);

public sealed record RetagSweepTriggeredResponse(
    [property: JsonPropertyName("triggered")] bool Triggered);

public sealed record RetagSweepRetryResponse(
    [property: JsonPropertyName("requeued")] bool Requeued);

public sealed record InitialSweepStartedResponse(
    [property: JsonPropertyName("started")] bool Started);
