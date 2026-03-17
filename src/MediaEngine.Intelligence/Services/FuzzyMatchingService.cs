using System.Text.RegularExpressions;
using FuzzySharp;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;

namespace MediaEngine.Intelligence.Services;

/// <summary>
/// FuzzySharp-backed fuzzy string matching service.
/// Provides token-set-ratio comparison (word-order insensitive),
/// partial ratio (substring matching), and composite field-by-field
/// scoring with sequel-safe numeric extraction.
/// </summary>
public sealed partial class FuzzyMatchingService : IFuzzyMatchingService
{
    // ── Composite scoring weights ────────────────────────────────────────────
    private const double TitleWeight  = 0.50;
    private const double AuthorWeight = 0.30;
    private const double YearWeight   = 0.15;
    private const double FormatWeight = 0.05;

    // ── Verdict thresholds ───────────────────────────────────────────────────
    private const double ExactThreshold = 0.95;
    private const double CloseThreshold = 0.70;

    /// <inheritdoc/>
    public double ComputeTokenSetRatio(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;
        return Fuzz.TokenSetRatio(a.Trim(), b.Trim()) / 100.0;
    }

    /// <inheritdoc/>
    public double ComputePartialRatio(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;
        return Fuzz.PartialRatio(a.Trim(), b.Trim()) / 100.0;
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

        var compositeScore = ComputeComposite(titleScore, authorScore, yearScore, formatScore);

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
        };
    }

    // ── Title scoring (sequel-safe) ──────────────────────────────────────────

    /// <summary>
    /// Sequel-safe title comparison. Extracts numeric tokens from both strings
    /// and penalises mismatches so "Harry Potter 1" vs "Harry Potter 2" scores
    /// ~0.60 instead of ~0.96.
    /// </summary>
    private double ComputeTitleScore(string localTitle, string candidateTitle)
    {
        if (string.IsNullOrWhiteSpace(localTitle) || string.IsNullOrWhiteSpace(candidateTitle))
            return 0.0;

        var localNums     = ExtractNumbers(localTitle);
        var candidateNums = ExtractNumbers(candidateTitle);

        var localBase     = NumbersRegex().Replace(localTitle, "").Trim();
        var candidateBase = NumbersRegex().Replace(candidateTitle, "").Trim();

        double baseScore = Fuzz.TokenSetRatio(
            string.IsNullOrWhiteSpace(localBase) ? localTitle : localBase,
            string.IsNullOrWhiteSpace(candidateBase) ? candidateTitle : candidateBase) / 100.0;

        // If both titles contain numbers, penalise number mismatches.
        if (localNums.Count > 0 && candidateNums.Count > 0)
        {
            bool numbersMatch = localNums.SequenceEqual(candidateNums);
            return baseScore * 0.6 + (numbersMatch ? 0.4 : 0.0);
        }

        return baseScore;
    }

    // ── Optional field scoring ───────────────────────────────────────────────

    private double ScoreOptionalField(string? localValue, string? candidateValue)
    {
        if (string.IsNullOrWhiteSpace(localValue) || string.IsNullOrWhiteSpace(candidateValue))
            return -1.0; // Not available

        return Fuzz.TokenSetRatio(localValue.Trim(), candidateValue.Trim()) / 100.0;
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
        double titleScore, double authorScore, double yearScore, double formatScore)
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
}
