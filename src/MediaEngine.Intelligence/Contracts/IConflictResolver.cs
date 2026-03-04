using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Selects the winning value for a single metadata field when multiple providers
/// have asserted competing claims.
///
/// ──────────────────────────────────────────────────────────────────
/// Algorithm (spec: Phase 6 – Claim Arbitration)
/// ──────────────────────────────────────────────────────────────────
///  1. For each claim, compute an adjusted weight:
///       adjustedWeight = claim.Confidence × providerWeight × staleDecayFactor
///  2. Normalize all adjusted weights so they sum to 1.0.
///  3. Group claims by their value (after normalization / trimming).
///  4. Sum normalized weights per value-group; the group with the highest
///     total becomes the winner.
///  5. A field is considered "conflicted" when the runner-up's total weight
///     is within <c>ScoringConfiguration.ConflictEpsilon</c> of the winner's.
///
/// ──────────────────────────────────────────────────────────────────
/// Invariants (spec: Phase 6 – Conflict Isolation)
/// ──────────────────────────────────────────────────────────────────
///  • A failure to resolve one field MUST NOT prevent resolution of other fields.
///  • Historical claims MUST NOT be deleted; the resolver is read-only.
///
/// Spec: Phase 6 – Intelligence § IConflictResolver.
/// </summary>
public interface IConflictResolver
{
    /// <summary>
    /// Resolves competing claims for a single metadata field and returns the
    /// winning claim together with its adjusted confidence.
    /// </summary>
    /// <param name="claimKey">The field being resolved (e.g. <c>"title"</c>).</param>
    /// <param name="claims">
    /// All claims for this field across all providers.
    /// Must not be empty; callers must filter before invoking.
    /// </param>
    /// <param name="providerWeights">
    /// Map of <c>ProviderId → weight</c>.  Missing providers default to weight 1.0.
    /// </param>
    /// <param name="configuration">
    /// Scoring thresholds; used for stale-decay computation and conflict detection.
    /// </param>
    ClaimResolution Resolve(
        string claimKey,
        IReadOnlyList<MetadataClaim> claims,
        IReadOnlyDictionary<Guid, double> providerWeights,
        ScoringConfiguration configuration);
}
