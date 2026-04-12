namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The output of a single <see cref="Contracts.IScoringEngine.ScoreEntityAsync"/> call.
///
/// Carries all resolved field values plus an overall confidence score that the
/// <see cref="Contracts.ICollectionArbiter"/> uses to make its linking decision.
///
/// Spec: Phase 6 – Claim Arbitration; Weight Summation invariant.
/// </summary>
public sealed class ScoringResult
{
    /// <summary>
    /// The entity that was scored — mirrors <see cref="ScoringContext.EntityId"/>.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// Per-field scoring outcomes, one entry per distinct <c>ClaimKey</c> present
    /// in the input context.  Empty when no claims were provided.
    /// </summary>
    public IReadOnlyList<FieldScore> FieldScores { get; init; } = [];

    /// <summary>
    /// Average confidence across all <see cref="FieldScores"/>.
    /// 0.0 when <see cref="FieldScores"/> is empty.
    /// Range: [0.0, 1.0].
    /// </summary>
    public required double OverallConfidence { get; init; }

    /// <summary>When the scoring engine produced this result.</summary>
    public required DateTimeOffset ScoredAt { get; init; }
}
