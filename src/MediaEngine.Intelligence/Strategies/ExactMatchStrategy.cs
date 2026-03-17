using MediaEngine.Intelligence.Contracts;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Strict-equality comparator for hard identifiers (ISBN, IMDb, TMDb, EAN, ASIN).
///
/// ──────────────────────────────────────────────────────────────────
/// Algorithm
/// ──────────────────────────────────────────────────────────────────
///  Both values are normalised (whitespace stripped, lower-cased, common
///  prefixes removed) before comparison.  The result is binary: 1.0 or 0.0.
///
///  Normalisation removes well-known prefix schemes so that:
///   • "ISBN:978-1-2345-6789-7"  == "9781234567897"   → 1.0
///   • "urn:isbn:9781234567897"  == "9781234567897"   → 1.0
///   • "isbn:9781234567897"      == "ISBN 9781234567897" → 1.0
///
/// ──────────────────────────────────────────────────────────────────
/// Short-circuit guarantee (spec: Phase 6 – Short-Circuit Evaluation)
/// ──────────────────────────────────────────────────────────────────
///  When any ExactMatchStrategy key yields 1.0, <see cref="IdentityMatcher"/>
///  immediately returns a <c>HardIdentifierMatch</c> result without running
///  further comparisons.
///
/// Spec: Phase 6 – Hub Clustering; IScoringStrategy extension point.
/// </summary>
public sealed class ExactMatchStrategy : IScoringStrategy
{
    /// <summary>
    /// Claim keys treated as hard identifiers.
    /// Used by IdentityMatcher to route hard identifiers to exact match.
    /// </summary>
    public static readonly IReadOnlyList<string> HardIdentifierKeys =
        ["isbn", "imdbid", "tmdbid", "ean", "asin", "musicbrainzid", "openlibrary_id", "wikidata_qid"];

    private static readonly HashSet<string> _keySet =
        new(HardIdentifierKeys, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string Name => "ExactMatch";

    /// <inheritdoc/>
    public bool AppliesTo(string claimKey) =>
        !string.IsNullOrWhiteSpace(claimKey) &&
        _keySet.Contains(claimKey);

    /// <inheritdoc/>
    public double Compute(string a, string b)
    {
        var normA = Normalize(a);
        var normB = Normalize(b);

        if (normA.Length == 0 || normB.Length == 0) return 0.0;

        // NF placeholders are not real identifiers — never match
        if (normA.StartsWith("nf", StringComparison.OrdinalIgnoreCase) ||
            normB.StartsWith("nf", StringComparison.OrdinalIgnoreCase))
            return 0.0;

        return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
    }

    // -------------------------------------------------------------------------
    // Normalisation
    // -------------------------------------------------------------------------

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Strip all whitespace and hyphens (ISBN formatting artifacts).
        var s = raw.Replace(" ", "").Replace("-", "").Trim();

        // Remove well-known URI/scheme prefixes iteratively so that
        // compound prefixes like "imdb:tt" are fully stripped.
        bool changed;
        do
        {
            changed = false;
            foreach (var prefix in _prefixes)
            {
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    s = s[prefix.Length..];
                    changed = true;
                    break;
                }
            }
        } while (changed);

        return s.ToLowerInvariant();
    }

    private static readonly string[] _prefixes =
    [
        "urn:isbn:", "isbn:", "isbn", "imdb:", "tt",
        "urn:ean:", "ean:",
        "asin:",
    ];
}
