using System.Text.RegularExpressions;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Fuzzy-matches embedded file metadata fields against retail provider description
/// text to improve candidate ranking during Stage 2 retail scoring.
///
/// <para>
/// Behaviour is entirely config-driven via <c>config/description_matching.json</c>.
/// Each media category defines which file hint keys to check, the match algorithm
/// (partial_ratio, token_set_ratio, contains, regex), which candidate text fields
/// to search (description, title, copyright), and the weight of each field.
/// </para>
///
/// <para>
/// Returns a composite bonus score (0.0–1.0) computed as a weighted average of
/// matched fields. Fields with no value in the file hints are skipped. The score
/// is intended to be applied as a bonus on top of title/author/year scoring,
/// not as a standalone signal.
/// </para>
///
/// Spec: §3.2 (Priority Cascade) — description matching supplements Tier 4
/// fuzzy scoring for fields the base scorer does not cover (narrator, series,
/// edition type, cast, etc.).
/// </summary>
public sealed partial class DescriptionMatchService : IDescriptionMatchService
{
    private readonly IConfigurationLoader _configLoader;
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly ILogger<DescriptionMatchService> _logger;

    public DescriptionMatchService(
        IConfigurationLoader configLoader,
        IFuzzyMatchingService fuzzy,
        ILogger<DescriptionMatchService> logger)
    {
        _configLoader = configLoader;
        _fuzzy        = fuzzy;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public DescriptionMatchResult Score(
        IReadOnlyDictionary<string, string> fileHints,
        string candidateTitle,
        string? candidateDescription,
        string? candidateCopyright,
        string mediaType)
    {
        // 1. Load config — fall back to empty result if config is missing or corrupt.
        DescriptionMatchingSettings? settings = null;
        try
        {
            settings = _configLoader.LoadConfig<DescriptionMatchingSettings>("", "description_matching");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DescriptionMatchService: failed to load config — returning empty result.");
        }

        if (settings is null)
            return DescriptionMatchResult.Empty;

        // 2. Find the category config for the requested media type.
        if (!settings.Categories.TryGetValue(mediaType, out var category))
        {
            _logger.LogDebug(
                "DescriptionMatchService: no category config for media type '{MediaType}' — returning empty result.",
                mediaType);
            return DescriptionMatchResult.Empty;
        }

        // 3. Prepare the candidate text fields.
        //    Strip HTML from description so fuzzy matching works on plain text.
        var cleanDesc = StripHtml(candidateDescription ?? "");
        if (cleanDesc.Length > settings.GlobalSettings.DescriptionMaxChars)
            cleanDesc = cleanDesc[..settings.GlobalSettings.DescriptionMaxChars];

        var minScore = settings.GlobalSettings.MinFuzzyScore;

        // 4. Score each configured field.
        var fieldMatches = new List<DescriptionFieldMatch>();
        var totalWeight  = 0.0;
        var weightedSum  = 0.0;

        foreach (var field in category.Fields)
        {
            // Skip reserved/disabled fields.
            if (string.Equals(field.MatchType, "none", StringComparison.OrdinalIgnoreCase))
                continue;

            // Only score fields that have a value in the file hints.
            if (!fileHints.TryGetValue(field.FileHintKey, out var fileValue)
                || string.IsNullOrWhiteSpace(fileValue))
                continue;

            // Determine which candidate text targets to search.
            var matchAgainst = field.MatchAgainst ?? ["description"];
            var bestScore    = 0;
            var bestTarget   = "description";

            foreach (var target in matchAgainst)
            {
                var text = target switch
                {
                    "title"     => candidateTitle,
                    "copyright" => candidateCopyright ?? "",
                    _           => cleanDesc,
                };

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var score = ComputeFieldScore(
                    field, fileValue, text, settings.GlobalSettings.CaseSensitive, minScore);

                if (score > bestScore)
                {
                    bestScore  = score;
                    bestTarget = target;
                }
            }

            var matched        = bestScore >= minScore;
            var normalizedScore = matched ? bestScore / 100.0 : 0.0;
            var weighted        = normalizedScore * field.Weight;

            totalWeight  += field.Weight;
            weightedSum  += weighted;

            fieldMatches.Add(new DescriptionFieldMatch
            {
                FieldKey       = field.FileHintKey,
                FileValue      = fileValue,
                Matched        = matched,
                RawScore       = bestScore,
                WeightedScore  = weighted,
                Weight         = field.Weight,
                MatchedAgainst = bestTarget,
            });
        }

        // 5. Normalize composite to 0–1 based on total weight of fields that had values.
        var composite = totalWeight > 0 ? weightedSum / totalWeight : 0.0;

        _logger.LogDebug(
            "DescriptionMatchService: {MediaType} — composite={Composite:F3} from {FieldCount} fields (totalWeight={TotalWeight:F2})",
            mediaType, composite, fieldMatches.Count, totalWeight);

        return new DescriptionMatchResult
        {
            CompositeScore = composite,
            FieldMatches   = fieldMatches,
        };
    }

    // ── Per-field scoring ────────────────────────────────────────────────────

    private int ComputeFieldScore(
        DescriptionMatchField field,
        string fileValue,
        string targetText,
        bool caseSensitive,
        int minScore)
    {
        return field.MatchType.ToLowerInvariant() switch
        {
            "partial_ratio"   => ScorePartialRatio(fileValue, targetText, caseSensitive),
            "token_set_ratio" => ScoreTokenSetRatio(fileValue, targetText, caseSensitive),
            "contains"        => ScoreContains(fileValue, targetText, field.MatchTerms, caseSensitive),
            "regex"           => ScoreRegex(fileValue, targetText, field.Pattern),
            _                 => 0,
        };
    }

    private int ScorePartialRatio(string fileValue, string text, bool caseSensitive)
    {
        var a = caseSensitive ? fileValue : fileValue.ToLowerInvariant();
        var b = caseSensitive ? text      : text.ToLowerInvariant();
        // IFuzzyMatchingService returns 0.0–1.0; convert to 0–100.
        return (int)Math.Round(_fuzzy.ComputePartialRatio(a, b) * 100.0);
    }

    private int ScoreTokenSetRatio(string fileValue, string text, bool caseSensitive)
    {
        var a = caseSensitive ? fileValue : fileValue.ToLowerInvariant();
        var b = caseSensitive ? text      : text.ToLowerInvariant();
        return (int)Math.Round(_fuzzy.ComputeTokenSetRatio(a, b) * 100.0);
    }

    private static int ScoreContains(
        string fileValue,
        string text,
        List<string>? matchTerms,
        bool caseSensitive)
    {
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (matchTerms is null || matchTerms.Count == 0)
        {
            // Simple: does the file value appear anywhere in the text?
            return text.Contains(fileValue, comparison) ? 100 : 0;
        }

        // For lists of terms: the term must appear in the text AND the file value
        // must indicate the same term (e.g. file says "unabridged", text says "unabridged").
        var fileValueLower = fileValue.ToLowerInvariant();
        foreach (var term in matchTerms)
        {
            if (text.Contains(term, comparison)
                && fileValueLower.Contains(term.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return 100;
            }
        }

        return 0;
    }

    private static int ScoreRegex(string fileValue, string text, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return 0;

        try
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            if (!match.Success)
                return 0;

            // Extract the first captured group value and compare to the file value.
            var extracted = match.Groups
                .Cast<Group>()
                .Skip(1)
                .FirstOrDefault(g => g.Success)?.Value ?? match.Value;

            return string.Equals(
                extracted.Trim(), fileValue.Trim(), StringComparison.OrdinalIgnoreCase)
                ? 100 : 50;
        }
        catch (RegexMatchTimeoutException)
        {
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    // ── HTML stripping ───────────────────────────────────────────────────────

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        // Replace <br> variants with a space so words don't run together.
        var result = BrTagRegex().Replace(html, " ");
        // Strip all remaining tags.
        result = HtmlTagRegex().Replace(result, "");
        // Decode HTML entities (e.g. &amp; → &, &#39; → ').
        result = System.Net.WebUtility.HtmlDecode(result);
        return result;
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
