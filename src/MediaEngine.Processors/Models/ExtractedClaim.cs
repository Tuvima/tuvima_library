namespace MediaEngine.Processors.Models;

/// <summary>
/// A single metadata field emitted by an <see cref="Contracts.IMediaProcessor"/>.
///
/// Maps 1-to-1 onto a future <c>metadata_claims</c> row once the ingestion
/// engine decides which <c>entity_id</c> (Work or Edition) the claim belongs to.
///
/// ──────────────────────────────────────────────────────────────────
/// Confidence semantics (spec: Phase 5 – Metadata Claims)
/// ──────────────────────────────────────────────────────────────────
///  1.0  = Authoritative embedded metadata (e.g. EPUB OPF element)
///  0.75 = Inferred from format-specific heuristics
///  0.5  = Generic file-system fallback (e.g. filename stem as title)
///  0.0  = Placeholder / not trusted
///
/// Spec: Phase 5 – Media Processor Architecture § Metadata Extraction.
/// </summary>
public sealed class ExtractedClaim
{
    /// <summary>
    /// Claim key matching the <c>metadata_claims.claim_key</c> column convention.
    /// Examples: <c>"title"</c>, <c>"author"</c>, <c>"publisher"</c>, <c>"language"</c>.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>The extracted value for this claim.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// Confidence score in [0.0, 1.0].  Used by the scoring engine to rank
    /// competing claims from different providers.
    /// </summary>
    public required double Confidence { get; init; }
}
