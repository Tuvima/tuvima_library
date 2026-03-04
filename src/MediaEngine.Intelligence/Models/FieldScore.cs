namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The resolved score for a single metadata field after <see cref="Contracts.IConflictResolver"/>
/// has evaluated all competing claims.
///
/// Collected into <see cref="ScoringResult.FieldScores"/> by
/// <see cref="Contracts.IScoringEngine"/>.
///
/// Spec: Phase 6 – Claim Arbitration; Conflict Isolation.
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
    /// <see langword="true"/> when the runner-up value's normalised weight
    /// is within <see cref="ScoringConfiguration.ConflictEpsilon"/> of the
    /// winner's weight, indicating that two providers are nearly equally confident
    /// in different values.
    /// Spec: Phase 6 – Conflict Isolation (field-level conflict does not block
    /// scoring of other fields).
    /// </summary>
    public bool IsConflicted { get; init; }
}
