namespace MediaEngine.Contracts.Ai;

/// <summary>
/// Wire responses for <c>AiEnrichmentEndpoints</c> routes that previously returned anonymous
/// types (<c>Results.Ok(new { ... })</c>). Property names are deliberately left exactly as the
/// anonymous types declared them — snake_case, not PascalCase — and carry no
/// <c>[JsonPropertyName]</c> overrides, so the JSON payload these records produce is
/// byte-identical to what the replaced anonymous types produced.
///
/// <para>
/// <c>GET /ai/enrich/tldr/{entityId}</c> returns one of two distinct shapes on 200: a cached
/// or freshly generated summary (<see cref="TldrResponse"/>, just <c>tldr</c>), or a
/// could-not-summarize fallback that also carries a <c>note</c>
/// (<see cref="TldrUnavailableResponse"/>). Keeping these as two records instead of one with
/// an optional <c>note</c> preserves byte-identical output for the common case — a unified
/// record would have serialized a stray <c>"note":null</c> into the cached/generated path.
/// </para>
/// </summary>
public sealed record TldrResponse(string tldr);

public sealed record TldrUnavailableResponse(string? tldr, string note);

public sealed record VibesResponse(IReadOnlyList<string> vibes);
