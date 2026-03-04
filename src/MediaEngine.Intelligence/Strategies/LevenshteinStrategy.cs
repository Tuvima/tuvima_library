using MediaEngine.Intelligence.Contracts;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Computes similarity between two free-text values using normalised
/// Levenshtein (edit) distance.
///
/// ──────────────────────────────────────────────────────────────────
/// Algorithm
/// ──────────────────────────────────────────────────────────────────
///  1. Normalise both strings: Unicode-lowercase + trim.
///  2. Compute Levenshtein distance d(a, b) via a standard DP matrix.
///  3. Normalise to [0, 1]:
///       similarity = 1 - d(a, b) / max(len(a), len(b))
///  Empty strings are handled explicitly:
///   • Both empty → 1.0 (identical)
///   • One empty  → 0.0 (completely different)
///
/// ──────────────────────────────────────────────────────────────────
/// Applicability
/// ──────────────────────────────────────────────────────────────────
///  This strategy applies to all free-text keys that are NOT hard
///  identifiers.  See <see cref="ExactMatchStrategy.HardIdentifierKeys"/>
///  for the exclusion list.
///
/// Spec: Phase 6 – Hub Clustering; IScoringStrategy extension point.
/// </summary>
public sealed class LevenshteinStrategy : IScoringStrategy
{
    // Keys handled by ExactMatchStrategy; excluded from fuzzy matching.
    private static readonly HashSet<string> ExcludedKeys =
        new(ExactMatchStrategy.HardIdentifierKeys, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string Name => "Levenshtein";

    /// <inheritdoc/>
    public bool AppliesTo(string claimKey) =>
        !string.IsNullOrWhiteSpace(claimKey) &&
        !ExcludedKeys.Contains(claimKey);

    /// <inheritdoc/>
    /// <remarks>
    /// Both values are lowercased and trimmed before comparison so that
    /// "The Hobbit" and "the hobbit" score 1.0.
    /// </remarks>
    public double Compute(string a, string b)
    {
        var normA = a?.Trim().ToLowerInvariant() ?? string.Empty;
        var normB = b?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normA.Length == 0 && normB.Length == 0) return 1.0;
        if (normA.Length == 0 || normB.Length == 0) return 0.0;
        if (normA == normB) return 1.0;

        int distance = ComputeDistance(normA, normB);
        int maxLen   = Math.Max(normA.Length, normB.Length);
        return 1.0 - (double)distance / maxLen;
    }

    // -------------------------------------------------------------------------
    // Standard O(n×m) Levenshtein DP with two-row rolling-array optimisation.
    // Title strings are typically ≤ 200 chars; the two-row approach keeps
    // allocations small compared to an n×m matrix.
    // -------------------------------------------------------------------------

    private static int ComputeDistance(string a, string b)
    {
        int lenA = a.Length;
        int lenB = b.Length;

        // Ensure a is the shorter string for the inner loop.
        if (lenA > lenB)
        {
            (a, b) = (b, a);
            (lenA, lenB) = (lenB, lenA);
        }

        int[] prev = new int[lenA + 1];
        int[] curr = new int[lenA + 1];

        // Initialise first row: distance from empty prefix of a to each prefix of b.
        for (int i = 0; i <= lenA; i++) prev[i] = i;

        for (int j = 1; j <= lenB; j++)
        {
            curr[0] = j;
            for (int i = 1; i <= lenA; i++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[i] = Math.Min(
                    Math.Min(prev[i] + 1,       // deletion
                             curr[i - 1] + 1),  // insertion
                    prev[i - 1] + cost);         // substitution
            }

            // Swap rows.
            (prev, curr) = (curr, prev);
        }

        return prev[lenA];
    }
}
