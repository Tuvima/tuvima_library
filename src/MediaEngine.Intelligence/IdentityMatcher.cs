using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence;

/// <summary>
/// Compares two entities' canonical metadata to determine whether they represent
/// the same intellectual work.
///
/// ──────────────────────────────────────────────────────────────────
/// Matching algorithm (spec: Phase 6 – Identity Matching)
/// ──────────────────────────────────────────────────────────────────
///  Pass 1 – Hard-identifier short-circuit
///  ───────────────────────────────────────
///  For every key present in <see cref="ExactMatchStrategy.HardIdentifierKeys"/>:
///    if entityValue == candidateValue (after normalisation) → immediate 1.0
///  Spec: "Matching of 'Hard Identifiers' MAY bypass fuzzy-logic comparisons."
///
///  Pass 2 – Field-level fuzzy matching
///  ─────────────────────────────────────
///  For each key present in both entities:
///    strategyScore = best-matching strategy.Compute(valueA, valueB)
///  Weighted average is computed with "title" receiving 50 % of the total
///  weight and all other fields sharing the remaining 50 % equally.
///
/// ──────────────────────────────────────────────────────────────────
/// Disposition
/// ──────────────────────────────────────────────────────────────────
///  similarity ≥ AutoLinkThreshold   → AutoLinked
///  similarity ∈ [Conflict, Auto)    → NeedsReview
///  similarity &lt; ConflictThreshold   → Rejected
/// </summary>
public sealed class IdentityMatcher : IIdentityMatcher
{
    private const string TitleKey   = "title";
    private const double TitleWeight = 0.5;      // title gets 50 % of total influence

    private readonly IReadOnlyList<IScoringStrategy> _strategies;

    public IdentityMatcher(IEnumerable<IScoringStrategy>? strategies = null)
    {
        _strategies = strategies?.ToList() ?? DefaultStrategies();
    }

    private static List<IScoringStrategy> DefaultStrategies() =>
    [
        new ExactMatchStrategy(),
        new LevenshteinStrategy(),
    ];

    // -------------------------------------------------------------------------
    // IIdentityMatcher
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<MatchResult> MatchAsync(
        IEnumerable<CanonicalValue> entity,
        IEnumerable<CanonicalValue> candidate,
        ScoringConfiguration configuration,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(configuration);

        var entityMap    = BuildMap(entity);
        var candidateMap = BuildMap(candidate);

        // ── Pass 1: hard-identifier short-circuit ──────────────────────────
        var matchedIds = new List<string>();
        foreach (var key in ExactMatchStrategy.HardIdentifierKeys)
        {
            if (entityMap.TryGetValue(key, out string? evA) &&
                candidateMap.TryGetValue(key, out string? evB))
            {
                var exact = new ExactMatchStrategy();
                if (exact.Compute(evA, evB) >= 1.0)
                    matchedIds.Add(key);
            }
        }

        if (matchedIds.Count > 0)
        {
            return Task.FromResult(new MatchResult
            {
                Similarity           = 1.0,
                MatchedIdentifiers   = matchedIds,
                HardIdentifierMatch  = true,
                Disposition          = LinkDisposition.AutoLinked,
            });
        }

        // ── Pass 2: field-level fuzzy matching ─────────────────────────────
        var commonKeys = entityMap.Keys
            .Intersect(candidateMap.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (commonKeys.Count == 0)
        {
            // No shared fields — cannot determine similarity.
            return Task.FromResult(new MatchResult
            {
                Similarity  = 0.0,
                Disposition = LinkDisposition.Rejected,
            });
        }

        // Separate title from other keys.
        bool hasTitleField = commonKeys.Contains(TitleKey, StringComparer.OrdinalIgnoreCase);
        var  otherKeys     = commonKeys
            .Where(k => !string.Equals(k, TitleKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        double totalWeight = 0.0;
        double weightedSum = 0.0;

        if (hasTitleField)
        {
            double titleSim = ComputeSimilarity(
                TitleKey,
                entityMap[TitleKey],
                candidateMap[TitleKey]);

            double w = otherKeys.Count == 0 ? 1.0 : TitleWeight;
            weightedSum  += w * titleSim;
            totalWeight  += w;
        }

        if (otherKeys.Count > 0)
        {
            double otherWeight = (hasTitleField ? 1.0 - TitleWeight : 1.0) / otherKeys.Count;
            foreach (var key in otherKeys)
            {
                double sim = ComputeSimilarity(key, entityMap[key], candidateMap[key]);
                weightedSum += otherWeight * sim;
                totalWeight += otherWeight;
            }
        }

        double similarity = totalWeight > 0 ? weightedSum / totalWeight : 0.0;
        var disposition = DetermineDisposition(similarity, configuration);

        return Task.FromResult(new MatchResult
        {
            Similarity  = similarity,
            Disposition = disposition,
        });
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private double ComputeSimilarity(string key, string a, string b)
    {
        // Use the first strategy that applies to this key.
        foreach (var strategy in _strategies)
        {
            if (strategy.AppliesTo(key))
                return strategy.Compute(a, b);
        }

        // Fallback: exact string comparison if no strategy matched.
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)
            ? 1.0 : 0.0;
    }

    private static Dictionary<string, string> BuildMap(IEnumerable<CanonicalValue> values)
        => values.ToDictionary(
            v => v.Key,
            v => v.Value,
            StringComparer.OrdinalIgnoreCase);

    private static LinkDisposition DetermineDisposition(
        double similarity,
        ScoringConfiguration config)
    {
        if (similarity >= config.AutoLinkThreshold)  return LinkDisposition.AutoLinked;
        if (similarity >= config.ConflictThreshold)  return LinkDisposition.NeedsReview;
        return LinkDisposition.Rejected;
    }
}
