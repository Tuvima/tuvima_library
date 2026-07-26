namespace MediaEngine.Contracts.Progress;

/// <summary>
/// Response body for the <c>/progress</c> endpoints. Property names are
/// byte-identical to the anonymous type this record replaces — no
/// <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record UserStateResponse(
    Guid user_id,
    Guid asset_id,
    string content_hash,
    double progress_pct,
    DateTimeOffset last_accessed,
    Dictionary<string, string> extended_properties);
