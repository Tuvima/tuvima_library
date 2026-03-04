using MediaEngine.Domain.Aggregates;
using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Evaluates whether a <see cref="Work"/> should be automatically linked to one
/// of the supplied Hub candidates, flagged for manual review, or left unlinked.
///
/// ──────────────────────────────────────────────────────────────────
/// Decision rules (spec: Phase 6 – Hub Clustering; Threshold Enforcement)
/// ──────────────────────────────────────────────────────────────────
///  For each candidate Hub the arbiter runs <see cref="IIdentityMatcher"/> against
///  the canonical metadata of Works within that Hub.  The best-matching Hub is
///  then evaluated:
///
///  score ≥ AutoLinkThreshold   → <see cref="LinkDisposition.AutoLinked"/>
///  score ∈ [ConflictThreshold, AutoLinkThreshold) → <see cref="LinkDisposition.NeedsReview"/>
///  score &lt; ConflictThreshold  → <see cref="LinkDisposition.Rejected"/>
///
/// ──────────────────────────────────────────────────────────────────
/// Constraints (spec: Phase 6 – Non-Goals; Hub Integrity)
/// ──────────────────────────────────────────────────────────────────
///  • The arbiter MUST NOT create new Hubs; it only links to existing or
///    system-defined Hubs.
///  • Circular link detection: if the Work is already a member of a candidate
///    Hub, that Hub is skipped.
///  • All decisions (including rejections) MUST be logged to
///    <c>transaction_log</c> via <see cref="Storage.Contracts.ITransactionJournal"/>.
///
/// Spec: Phase 6 – Intelligence § Hub Arbiter.
/// </summary>
public interface IHubArbiter
{
    /// <summary>
    /// Evaluates <paramref name="work"/> against all supplied Hub candidates
    /// and returns the best arbitration decision.
    /// </summary>
    /// <param name="work">
    /// The Work to be linked.  Its <see cref="Work.CanonicalValues"/> must be
    /// populated before calling this method (run <see cref="IScoringEngine"/> first).
    /// </param>
    /// <param name="hubCandidates">
    /// Hubs to evaluate as potential parents.  Each Hub's <see cref="Hub.Works"/>
    /// collection must be populated with their <c>CanonicalValues</c>.
    /// </param>
    /// <param name="providerWeights">
    /// Map of <c>ProviderId → weight</c> forwarded to <see cref="IIdentityMatcher"/>.
    /// </param>
    /// <param name="configuration">Scoring thresholds driving the disposition logic.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The best <see cref="ArbiterDecision"/> found; the decision is also written
    /// to <c>transaction_log</c> before returning.
    /// </returns>
    Task<ArbiterDecision> EvaluateAsync(
        Work work,
        IEnumerable<Hub> hubCandidates,
        IReadOnlyDictionary<Guid, double> providerWeights,
        ScoringConfiguration configuration,
        CancellationToken ct = default);
}
