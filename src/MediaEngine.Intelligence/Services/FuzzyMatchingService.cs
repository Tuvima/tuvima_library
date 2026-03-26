using System.Text.RegularExpressions;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;

namespace MediaEngine.Intelligence.Services;

/// <summary>
/// Native fuzzy string matching service.
/// Provides token-set-ratio comparison (word-order insensitive),
/// partial ratio (substring matching), and composite field-by-field
/// scoring with sequel-safe numeric extraction.
/// No third-party dependencies — implemented using Levenshtein distance.
/// </summary>
public sealed partial class FuzzyMatchingService : IFuzzyMatchingService
{
    // ── Composite scoring weights ────────────────────────────────────────────
    private const double TitleWeight  = 0.45;
    private const double AuthorWeight = 0.25;
    private const double YearWeight   = 0.10;
    private const double FormatWeight = 0.05;
    private const double CoverWeight  = 0.15;

    // ── Verdict thresholds ───────────────────────────────────────────────────
    private const double ExactThreshold = 0.95;
    private const double CloseThreshold = 0.70;

    /// <inheritdoc/>
    public double ComputeTokenSetRatio(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;
        return NativeTokenSetRatio(a.Trim(), b.Trim());
    }

    /// <inheritdoc/>
    public double ComputePartialRatio(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;
        return NativePartialRatio(a.Trim(), b.Trim());
    }

    /// <inheritdoc/>
    public FieldMatchResult ScoreCandidate(LocalMetadata local, CandidateMetadata candidate)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(candidate);

        var titleScore  = ComputeTitleScore(local.Title, candidate.Title);
        var authorScore = ScoreOptionalField(local.Author, candidate.Author);
        var yearScore   = ScoreYear(local.Year, candidate.Year);
        var formatScore = ScoreFormat(local.MediaType, candidate.MediaType);
        var coverScore  = candidate.CoverSimilarity; // -1.0 if not available

        var compositeScore = ComputeComposite(titleScore, authorScore, yearScore, formatScore, coverScore);

        return new FieldMatchResult
        {
            TitleScore     = titleScore,
            AuthorScore    = authorScore,
            YearScore      = yearScore,
            FormatScore    = formatScore,
            CompositeScore = compositeScore,
            TitleVerdict   = ToVerdict(titleScore),
            AuthorVerdict  = ToVerdict(authorScore),
            YearVerdict    = ToVerdict(yearScore),
            FormatVerdict  = ToVerdict(formatScore),
            CoverScore     = coverScore,
            CoverVerdict   = ToVerdict(coverScore),
        };
    }

    // ── Title scoring (sequel-safe) ──────────────────────────────────────────

    /// <summary>
    /// Sequel-safe title comparison. Extracts numeric tokens from both strings
    /// and penalises mismatches so "Harry Potter 1" vs "Harry Potter 2" scores
    /// ~0.60 instead of ~0.96.
    /// </summary>
    private static double ComputeTitleScore(string localTitle, string candidateTitle)
    {
        if (string.IsNullOrWhiteSpace(localTitle) || string.IsNullOrWhiteSpace(candidateTitle))
            return 0.0;

        var localNums     = ExtractNumbers(localTitle);
        var candidateNums = ExtractNumbers(candidateTitle);

        var localBase     = NumbersRegex().Replace(localTitle, "").Trim();
        var candidateBase = NumbersRegex().Replace(candidateTitle, "").Trim();

        double baseScore = NativeTokenSetRatio(
            string.IsNullOrWhiteSpace(localBase) ? localTitle : localBase,
            string.IsNullOrWhiteSpace(candidateBase) ? candidateTitle : candidateBase);

        // If both titles contain numbers, penalise number mismatches.
        if (localNums.Count > 0 && candidateNums.Count > 0)
        {
            bool numbersMatch = localNums.SequenceEqual(candidateNums);
            return baseScore * 0.6 + (numbersMatch ? 0.4 : 0.0);
        }

        return baseScore;
    }

    // ── Optional field scoring ───────────────────────────────────────────────

    private static double ScoreOptionalField(string? localValue, string? candidateValue)
    {
        if (string.IsNullOrWhiteSpace(localValue) || string.IsNullOrWhiteSpace(candidateValue))
            return -1.0; // Not available

        return NativeTokenSetRatio(localValue.Trim(), candidateValue.Trim());
    }

    // ── Year scoring ─────────────────────────────────────────────────────────

    private static double ScoreYear(string? localYear, string? candidateYear)
    {
        if (string.IsNullOrWhiteSpace(localYear) || string.IsNullOrWhiteSpace(candidateYear))
            return -1.0;

        if (!int.TryParse(localYear.Trim(), out var ly) ||
            !int.TryParse(candidateYear.Trim(), out var cy))
            return -1.0;

        int diff = Math.Abs(ly - cy);
        return diff switch
        {
            0 => 1.0,
            1 => 0.5, // Off-by-one (common for release year discrepancies)
            _ => 0.0,
        };
    }

    // ── Format scoring ───────────────────────────────────────────────────────

    private static double ScoreFormat(string? localType, string? candidateType)
    {
        if (string.IsNullOrWhiteSpace(localType) || string.IsNullOrWhiteSpace(candidateType))
            return -1.0;

        var l = localType.Trim().ToUpperInvariant();
        var c = candidateType.Trim().ToUpperInvariant();

        if (l == c) return 1.0;

        // Related formats get partial credit
        if (IsBookFamily(l) && IsBookFamily(c)) return 0.5;
        if (IsVideoFamily(l) && IsVideoFamily(c)) return 0.3;

        return 0.0;
    }

    private static bool IsBookFamily(string type) =>
        type is "EPUB" or "BOOK" or "BOOKS" or "AUDIOBOOK" or "AUDIOBOOKS";

    private static bool IsVideoFamily(string type) =>
        type is "MOVIE" or "MOVIES" or "TV" or "VIDEO";

    // ── Composite scoring ────────────────────────────────────────────────────

    private static double ComputeComposite(
        double titleScore, double authorScore, double yearScore, double formatScore, double coverScore)
    {
        double weightSum = 0.0;
        double scoreSum  = 0.0;

        // Title is always present (required field)
        weightSum += TitleWeight;
        scoreSum  += TitleWeight * titleScore;

        if (authorScore >= 0.0)
        {
            weightSum += AuthorWeight;
            scoreSum  += AuthorWeight * authorScore;
        }

        if (yearScore >= 0.0)
        {
            weightSum += YearWeight;
            scoreSum  += YearWeight * yearScore;
        }

        if (formatScore >= 0.0)
        {
            weightSum += FormatWeight;
            scoreSum  += FormatWeight * formatScore;
        }

        if (coverScore >= 0.0)
        {
            weightSum += CoverWeight;
            scoreSum  += CoverWeight * coverScore;
        }

        return weightSum > 0 ? scoreSum / weightSum : 0.0;
    }

    // ── Verdict mapping ──────────────────────────────────────────────────────

    private static FieldMatchVerdict ToVerdict(double score) => score switch
    {
        < 0               => FieldMatchVerdict.NotAvailable,
        >= ExactThreshold => FieldMatchVerdict.Exact,
        >= CloseThreshold => FieldMatchVerdict.Close,
        _                 => FieldMatchVerdict.Mismatch,
    };

    // ── Number extraction ────────────────────────────────────────────────────

    private static List<string> ExtractNumbers(string text)
    {
        var matches = NumbersRegex().Matches(text);
        return matches.Select(m => m.Value).ToList();
    }

    [GeneratedRegex(@"\d+", RegexOptions.Compiled)]
    private static partial Regex NumbersRegex();

    // ── Native fuzzy matching algorithms ────────────────────────────────────

    /// <summary>
    /// Token-set ratio: tokenizes both strings, computes Levenshtein ratio between
    /// sorted intersection, intersection+remainderA, and intersection+remainderB.
    /// Word-order insensitive — "Frank Herbert" vs "Herbert, Frank" scores ~1.0.
    /// Returns a value in [0.0, 1.0].
    /// </summary>
    private static double NativeTokenSetRatio(string a, string b)
    {
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);

        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0.0;

        // Intersection and remainders
        var intersection = tokensA.Intersect(tokensB, StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
        var remainderA = tokensA.Except(intersection, StringComparer.OrdinalIgnoreCase)
                                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                .ToList();
        var remainderB = tokensB.Except(intersection, StringComparer.OrdinalIgnoreCase)
                                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                .ToList();

        var sorted  = string.Join(" ", intersection);
        var sortedA = string.Join(" ", intersection.Concat(remainderA));
        var sortedB = string.Join(" ", intersection.Concat(remainderB));

        // Best ratio among the three pairwise comparisons
        var r1 = LevenshteinRatio(sorted, sortedA);
        var r2 = LevenshteinRatio(sorted, sortedB);
        var r3 = LevenshteinRatio(sortedA, sortedB);

        return Math.Max(r1, Math.Max(r2, r3));
    }

    /// <summary>
    /// Partial ratio: slides the shorter string across the longer string and
    /// returns the best Levenshtein ratio over all substring positions.
    /// "Dune" vs "Dune: Part One" scores high because "Dune" is a perfect substring.
    /// Returns a value in [0.0, 1.0].
    /// </summary>
    private static double NativePartialRatio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0.0;

        // Ensure 'shorter' is the shorter string
        var shorter = a.Length <= b.Length ? a : b;
        var longer  = a.Length > b.Length  ? a : b;

        double bestRatio = 0.0;
        for (int i = 0; i <= longer.Length - shorter.Length; i++)
        {
            var substring = longer.Substring(i, shorter.Length);
            var ratio = LevenshteinRatio(shorter, substring);
            if (ratio > bestRatio)
                bestRatio = ratio;
            if (bestRatio >= 1.0) break; // Perfect match found — no need to continue
        }
        return bestRatio;
    }

    /// <summary>
    /// Computes the normalised Levenshtein similarity ratio in [0.0, 1.0].
    /// Both strings are lowercased before comparison.
    /// </summary>
    private static double LevenshteinRatio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var aLower = a.ToLowerInvariant();
        var bLower = b.ToLowerInvariant();

        int distance = LevenshteinDistance(aLower, bLower);
        int maxLen   = Math.Max(aLower.Length, bLower.Length);
        return 1.0 - (double)distance / maxLen;
    }

    /// <summary>
    /// Standard Levenshtein edit distance (insert / delete / substitute).
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        var matrix = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[a.Length, b.Length];
    }

    /// <summary>
    /// Tokenizes a string into lowercase words, splitting on whitespace and common punctuation.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        return text.ToLowerInvariant()
            .Split([' ', '\t', ',', '.', ';', ':', '-', '_', '(', ')', '[', ']'],
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToList();
    }
}
