namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The outcome produced by <see cref="Contracts.ICollectionArbiter.EvaluateAsync"/> for
/// a single Work against the full set of Collection candidates.
///
/// Logged to <c>transaction_log</c> before being returned to the caller.
///
/// Spec: Phase 6 – Collection Clustering; Threshold Enforcement.
/// </summary>
public sealed class ArbiterDecision
{
    /// <summary>The Work that was evaluated.</summary>
    public required Guid WorkId { get; init; }

    /// <summary>
    /// The Collection selected as the best match.
    /// For <see cref="LinkDisposition.AutoLinked"/> and
    /// <see cref="LinkDisposition.NeedsReview"/> this is the candidate Collection.
    /// For <see cref="LinkDisposition.Rejected"/> this is <see cref="Guid.Empty"/>
    /// (no Collection was selected).
    /// </summary>
    public required Guid CollectionId { get; init; }

    /// <summary>
    /// The best similarity score achieved against any candidate Collection.
    /// 0.0 when no candidates were evaluated.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// The arbitration outcome based on the score and configured thresholds.
    /// </summary>
    public required LinkDisposition Disposition { get; init; }

    /// <summary>
    /// Human-readable explanation of the decision for audit trail purposes
    /// (e.g. "Auto-linked: score 0.92 ≥ threshold 0.85 via ISBN match").
    /// Logged to <c>transaction_log</c> as part of the event context.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>When the arbiter produced this decision.</summary>
    public required DateTimeOffset DecidedAt { get; init; }
}
