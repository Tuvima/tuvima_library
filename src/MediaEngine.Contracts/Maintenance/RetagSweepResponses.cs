namespace MediaEngine.Contracts.Maintenance;

/// <summary>
/// Response body for <c>GET /maintenance/retag-sweep/state</c>. Property names are
/// byte-identical to the anonymous type this record replaces — no
/// <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record RetagSweepStateResponse(
    bool has_pending_diff,
    IReadOnlyList<RetagSweepPendingDiffEntry> pending_diff,
    IReadOnlyDictionary<string, string> current_hashes);

/// <summary>One media type's worth of pending writeback field-list delta.</summary>
public sealed record RetagSweepPendingDiffEntry(
    string media_type,
    IReadOnlyList<string> added_fields,
    IReadOnlyList<string> removed_fields);

/// <summary>Response body for <c>POST /maintenance/retag-sweep/apply</c>.</summary>
public sealed record RetagSweepAppliedResponse(bool applied);

/// <summary>Response body for <c>POST /maintenance/retag-sweep/run-now</c>.</summary>
public sealed record RetagSweepTriggeredResponse(bool triggered);

/// <summary>Response body for <c>POST /maintenance/retag-sweep/retry/{assetId}</c>.</summary>
public sealed record RetagSweepRetryResponse(bool requeued);

/// <summary>Response body for <c>POST /maintenance/initial-sweep/run</c>.</summary>
public sealed record InitialSweepStartedResponse(bool started);
