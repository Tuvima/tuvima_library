namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// One media-type's field-list delta as reported by the Engine.
/// Corresponds to <c>MediaEngine.Ingestion.Services.WritebackFieldDiff</c>.
/// </summary>
public sealed record RetagFieldDiffDto(
    string MediaType,
    IReadOnlyList<string> AddedFields,
    IReadOnlyList<string> RemovedFields);

/// <summary>
/// Current state of the re-tag sweep subsystem — returned by
/// <c>GET /maintenance/retag-sweep/state</c>.
/// </summary>
public sealed record RetagSweepStateDto(
    bool HasPendingDiff,
    IReadOnlyList<RetagFieldDiffDto> PendingDiff,
    IReadOnlyDictionary<string, string> CurrentHashes);

/// <summary>
/// Live progress payload broadcast over SignalR while a sweep pass runs.
/// Matches the Engine-side <c>RetagSweepProgressEvent</c>.
/// </summary>
public sealed record RetagSweepProgressDto(
    int Processed,
    int Succeeded,
    int Transient,
    int Terminal,
    bool IsFinal);
