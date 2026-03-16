namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The resolved score for a single metadata field after the priority cascade
/// has evaluated all competing claims.
///
/// Collected into <see cref="ScoringResult.FieldScores"/> by
/// <see cref="Contracts.IScoringEngine"/>.
///
/// Spec: Phase 6 – Claim Arbitration; Priority Cascade.
/// </summary>
public sealed class FieldScore
{
    /// <summary>The metadata field this score describes (e.g. <c>"title"</c>).</summary>
    public required string Key { get; init; }

    /// <summary>
    /// The winning value for this field after claim arbitration.
    /// Maps to <see cref="MediaEngine.Domain.Entities.CanonicalValue.Value"/>.
    /// </summary>
    public required string WinningValue { get; init; }

    /// <summary>
    /// Normalised confidence for the winning value in [0.0, 1.0].
    /// Computed as the sum of normalised weights for all claims that asserted
    /// <see cref="WinningValue"/>.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// The provider that contributed most weight to the winning value.
    /// Null when no provider weight information was available.
    /// </summary>
    public Guid? WinningProviderId { get; init; }

    /// <summary>
    /// Always <see langword="false"/> in the priority cascade model.
    /// Preserved for interface compatibility with callers that check this flag.
    /// Spec: Phase 6 – Priority Cascade (no conflict detection; Wikidata wins unconditionally).
    /// </summary>
    public bool IsConflicted { get; init; }
}
