namespace MediaEngine.Contracts.Operations;

/// <summary>
/// Named wire responses for Engine status and maintenance operations. Member names preserve
/// the lower_snake_case JSON shape of the anonymous objects replaced.
/// </summary>
public sealed record FileWatcherStatusResponse(
    bool running,
    int directory_count,
    IReadOnlyList<string> directories,
    long event_count,
    DateTimeOffset? last_event_at,
    long error_count,
    DateTimeOffset? last_error_at,
    string? last_error_kind,
    string? last_error_message);

public sealed record AssetStoreSweepResponse(int cleaned, string message);
