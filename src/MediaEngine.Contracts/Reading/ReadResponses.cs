namespace MediaEngine.Contracts.Reading;

/// <summary>
/// Wire response for the <c>ReadEndpoints</c> route that previously returned an anonymous
/// type (<c>Results.Ok(new { ... })</c>). The property name is deliberately left exactly as
/// the anonymous type declared it and carries no <c>[JsonPropertyName]</c> override, so the
/// JSON payload this record produces is byte-identical to what the replaced anonymous type
/// produced.
/// </summary>
public sealed record ResolveWorkToAssetResponse(Guid assetId);
