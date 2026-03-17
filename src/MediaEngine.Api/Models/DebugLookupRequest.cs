namespace MediaEngine.Api.Models;

/// <summary>
/// Request body for the <c>POST /debug/lookup</c> endpoint.
/// Triggers a live Wikidata Reconciliation + Data Extension lookup without
/// persisting anything to the database — for testing and validation only.
/// </summary>
public sealed record DebugLookupRequest(
    string Title,
    string MediaType,
    string? Author = null);

/// <summary>
/// Request body for the <c>POST /debug/enrich</c> endpoint.
/// Takes a confirmed QID and runs full Data Extension + enrichment.
/// </summary>
public sealed record DebugEnrichRequest(
    string Qid,
    string MediaType,
    string? Author = null);
