namespace MediaEngine.Contracts.Progress;

/// <summary>
/// Response body for the <c>/progress</c> endpoints. Property names are
/// byte-identical to the anonymous type this record replaces — no
/// <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record UserStateResponse(
    [property: global::System.Text.Json.Serialization.JsonPropertyName("user_id")] Guid UserId,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("asset_id")] Guid AssetId,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("content_hash")] string ContentHash,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("progress_pct")] double ProgressPct,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("last_accessed")] DateTime? LastAccessed,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("extended_properties")] Dictionary<string, string> ExtendedProperties);

/// <summary>Request body for creating or replacing an asset progress state.</summary>
public sealed record ProgressUpdateRequest(
    [property: global::System.Text.Json.Serialization.JsonPropertyName("user_id")] string? UserId,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("progress_pct")] double ProgressPct,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("extended_properties")] Dictionary<string, string>? ExtendedProperties);
