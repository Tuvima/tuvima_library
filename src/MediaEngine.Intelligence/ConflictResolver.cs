using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence;

/// <summary>
/// Selects the winning value for a single metadata field by computing
/// provider-weighted, time-decayed claim scores and choosing the value-group
/// with the highest total normalised weight.
///
/// ──────────────────────────────────────────────────────────────────
/// Weighting algorithm (spec: Phase 6 – Weight Summation invariant)
/// ──────────────────────────────────────────────────────────────────
///  For each claim c in the field:
///    rawWeight(c) = c.Confidence × providerWeight(c.ProviderId) × staleDecay(c.ClaimedAt)
///
///  rawWeights are normalised so they sum to 1.0 across all claims for the field.
///
///  Claims are grouped by their (trimmed, lower-cased) value.
///  Group score = Σ normalised weights of all claims asserting that value.
///
///  Winner = group with highest score.
///
/// ──────────────────────────────────────────────────────────────────
/// Conflict detection (spec: Phase 6 – Conflict Isolation)
/// ──────────────────────────────────────────────────────────────────
///  IsConflicted = true  iff  (runner-up score / winner score) ≥ (1 - epsilon)
///  i.e. the runner-up is within epsilon of the winner.
///  A conflict on one field MUST NOT prevent scoring of other fields.
///
/// ──────────────────────────────────────────────────────────────────
/// Strategy selection
/// ──────────────────────────────────────────────────────────────────
///  Strategies are injected and queried in order; the first strategy
///  whose <see cref="IScoringStrategy.AppliesTo"/> returns true is used.
///  Default stack: ExactMatchStrategy → LevenshteinStrategy.
/// </summary>
public sealed class ConflictResolver : IConflictResolver
{
    private readonly IReadOnlyList<IScoringStrategy> _strategies;

    /// <param name="strategies">
    /// Ordered list of strategies; ExactMatch SHOULD precede Levenshtein so
    /// hard-identifier fields are detected before fuzzy comparison fires.
    /// </param>
    public ConflictResolver(IEnumerable<IScoringStrategy>? strategies = null)
    {
        _strategies = strategies?.ToList() ?? DefaultStrategies();
    }

    private static List<IScoringStrategy> DefaultStrategies() =>
    [
        new ExactMatchStrategy(),
        new LevenshteinStrategy(),
    ];

    // -------------------------------------------------------------------------
    // IConflictResolver
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public ClaimResolution Resolve(
        string claimKey,
        IReadOnlyList<MetadataClaim> claims,
        IReadOnlyDictionary<Guid, double> providerWeights,
        ScoringConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimKey);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(providerWeights);
        ArgumentNullException.ThrowIfNull(configuration);

        if (claims.Count == 0)
            throw new ArgumentException("At least one claim is required.", nameof(claims));

        // ── Step 1: compute raw weight for each claim ──────────────────────
        var weighted = new List<(MetadataClaim Claim, double RawWeight)>(claims.Count);
        foreach (var claim in claims)
        {
            double providerWeight = providerWeights.TryGetValue(claim.ProviderId, out double w) ? w : 1.0;
            double staleDecay     = ComputeStaleDecay(claim.ClaimedAt, configuration);
            double raw            = Math.Max(0.0, claim.Confidence * providerWeight * staleDecay);
            weighted.Add((claim, raw));
        }

        // ── Step 2: normalise so raw weights sum to 1.0 ────────────────────
        double totalRaw = weighted.Sum(x => x.RawWeight);
        if (totalRaw <= 0.0)
        {
            // Degenerate case: all weights are zero; fall back to uniform.
            totalRaw = weighted.Count;
            weighted = weighted.Select(x => (x.Claim, 1.0 / totalRaw)).ToList();
        }
        else
        {
            weighted = weighted.Select(x => (x.Claim, x.RawWeight / totalRaw)).ToList();
        }

        // ── Step 3: group claims by normalised value ───────────────────────
        // Group key = trimmed, lower-cased value (case-insensitive grouping).
        var groups = weighted
            .GroupBy(x => x.Claim.ClaimValue.Trim().ToLowerInvariant())
            .Select(g => new
            {
                NormValue    = g.Key,
                TotalWeight  = g.Sum(x => x.RawWeight),
                BestClaim    = g.OrderByDescending(x => x.RawWeight).First().Claim,
            })
            .OrderByDescending(g => g.TotalWeight)
            .ToList();

        var winner    = groups[0];
        double runnerUpWeight = groups.Count > 1 ? groups[1].TotalWeight : 0.0;

        // ── Step 4: conflict detection ─────────────────────────────────────
        bool isConflicted = winner.TotalWeight > 0.0 &&
                            (runnerUpWeight / winner.TotalWeight) >= (1.0 - configuration.ConflictEpsilon);

        // ── Step 5: build resolution ───────────────────────────────────────
        string reason = claims.Count == 1
            ? $"Single claim, adjusted confidence {winner.TotalWeight:F3}."
            : $"Won with weight {winner.TotalWeight:F3}; {groups.Count - 1} rival(s). " +
              (isConflicted ? $"CONFLICTED (runner-up {runnerUpWeight:F3})." : "No conflict.");

        return new ClaimResolution
        {
            WinningClaim       = winner.BestClaim,
            AdjustedConfidence = winner.TotalWeight,
            Reason             = reason,
            IsConflicted       = isConflicted,
        };
    }

    // -------------------------------------------------------------------------
    // Stale-decay helper
    // -------------------------------------------------------------------------

    private static double ComputeStaleDecay(DateTimeOffset claimedAt, ScoringConfiguration config)
    {
        if (config.StaleClaimDecayDays <= 0) return 1.0;
        double ageInDays = (DateTimeOffset.UtcNow - claimedAt).TotalDays;
        return ageInDays > config.StaleClaimDecayDays ? config.StaleClaimDecayFactor : 1.0;
    }
}
