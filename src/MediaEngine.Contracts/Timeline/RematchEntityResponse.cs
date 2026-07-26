namespace MediaEngine.Contracts.Timeline;

/// <summary>
/// Response body for <c>POST /timeline/{entityId}/rematch</c>. Property names are
/// byte-identical to the anonymous type this record replaces — no
/// <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record RematchEntityResponse(bool queued, Guid entityId);
