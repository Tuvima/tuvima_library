using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Scores retail search results against file metadata using tiered matching.
/// Tiers 1-3 use exact ID comparison. Tier 4 uses fuzzy text matching.
/// Cross-field signals (narrator-in-description, genre overlap, etc.) provide
/// additional boost/penalty to the composite score.
/// Weights are read from <c>config/hydration.json</c> → <c>fuzzy_match_weights</c>.
/// </summary>
public sealed class RetailMatchScoringService : IRetailMatchScoringService
{
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly IConfigurationLoader _configLoader;
    private readonly ICoverArtHashService? _coverArtHash;

    public RetailMatchScoringService(
        IFuzzyMatchingService fuzzy,
        IConfigurationLoader configLoader,
        ICoverArtHashService? coverArtHash = null)
    {
        _fuzzy = fuzzy;
        _configLoader = configLoader;
        _coverArtHash = coverArtHash;
    }

    public FieldMatchScores ScoreCandidate(
        IReadOnlyDictionary<string, string> fileHints,
        string? candidateTitle,
        string? candidateAuthor,
        string? candidateYear,
        MediaType mediaType,
        MatchTierConfig? matchTiers = null,
        CandidateExtendedMetadata? extendedMetadata = null)
    {
        var hydration = _configLoader.LoadHydration();
        var weights = hydration.FuzzyMatchWeights;

        // ── Title score ──────────────────────────────────────────────────
        double titleScore = 0.0;
        var fileTitle = fileHints.GetValueOrDefault("title");
        if (!string.IsNullOrWhiteSpace(fileTitle) && !string.IsNullOrWhiteSpace(candidateTitle))
        {
            titleScore = _fuzzy.ComputeTokenSetRatio(fileTitle, candidateTitle);
        }

        // ── Author score ─────────────────────────────────────────────────
        double authorScore = 0.5; // Neutral when missing
        var fileAuthor = fileHints.GetValueOrDefault("author");
        if (!string.IsNullOrWhiteSpace(fileAuthor) && !string.IsNullOrWhiteSpace(candidateAuthor))
        {
            authorScore = _fuzzy.ComputeTokenSetRatio(fileAuthor, candidateAuthor);
        }

        // ── Year score ───────────────────────────────────────────────────
        double yearScore = 0.5; // Neutral when missing
        var fileYear = fileHints.GetValueOrDefault("year") ?? fileHints.GetValueOrDefault("release_year");
        if (!string.IsNullOrWhiteSpace(fileYear) && !string.IsNullOrWhiteSpace(candidateYear))
        {
            if (fileYear.Length >= 4) fileYear = fileYear[..4];
            if (candidateYear.Length >= 4) candidateYear = candidateYear[..4];

            if (fileYear == candidateYear)
                yearScore = 1.0;
            else if (int.TryParse(fileYear, out var fy) && int.TryParse(candidateYear, out var cy))
                yearScore = Math.Abs(fy - cy) <= 1 ? 0.8 : 0.3;
        }

        // ── Format score (always 1.0 — strategies are media-type-scoped) ─
        double formatScore = 1.0;

        // ── Cross-field signals ──────────────────────────────────────────
        double crossFieldBoost = ComputeCrossFieldBoost(fileHints, mediaType, extendedMetadata);

        // ── Cover art similarity ─────────────────────────────────────────
        double coverBoost = 0.0;
        if (_coverArtHash is not null)
        {
            if (extendedMetadata?.CoverArtSimilarity is > 0.8)
                coverBoost = 0.10; // Strong visual match — same cover, likely same edition
            else if (extendedMetadata?.CoverArtSimilarity is > 0.6)
                coverBoost = 0.05; // Moderate visual match — probably same work
        }

        // ── Weighted composite ───────────────────────────────────────────
        var titleWeight  = weights.GetValueOrDefault("title",  0.45);
        var authorWeight = weights.GetValueOrDefault("author", 0.35);
        var yearWeight   = weights.GetValueOrDefault("year",   0.10);
        var formatWeight = weights.GetValueOrDefault("format", 0.10);

        var composite = (titleScore  * titleWeight)
                      + (authorScore * authorWeight)
                      + (yearScore   * yearWeight)
                      + (formatScore * formatWeight)
                      + crossFieldBoost
                      + coverBoost;

        return new FieldMatchScores
        {
            TitleScore      = titleScore,
            AuthorScore     = authorScore,
            YearScore       = yearScore,
            FormatScore     = formatScore,
            CrossFieldBoost = crossFieldBoost,
            CoverArtScore   = coverBoost,
            CompositeScore  = Math.Clamp(composite, 0.0, 1.0),
        };
    }

    /// <summary>
    /// Computes an additive boost (positive or negative) from cross-field signals.
    /// These signals cross-reference file metadata against the candidate's extended
    /// metadata (description, publisher, duration, genres, language).
    /// </summary>
    private double ComputeCrossFieldBoost(
        IReadOnlyDictionary<string, string> fileHints,
        MediaType mediaType,
        CandidateExtendedMetadata? ext)
    {
        if (ext is null) return 0.0;

        double boost = 0.0;
        var description = ext.Description;
        var descLower = description?.ToLowerInvariant();

        // ── Narrator found in description (+0.10, audiobooks only) ────────
        if (mediaType is MediaType.Audiobooks && !string.IsNullOrWhiteSpace(descLower))
        {
            var narrator = fileHints.GetValueOrDefault("narrator");
            if (!string.IsNullOrWhiteSpace(narrator) && descLower.Contains(narrator.ToLowerInvariant()))
                boost += 0.10;
        }

        // ── Author found in description (+0.08, books/audiobooks) ────────
        if (mediaType is MediaType.Books or MediaType.Audiobooks && !string.IsNullOrWhiteSpace(descLower))
        {
            var author = fileHints.GetValueOrDefault("author");
            if (!string.IsNullOrWhiteSpace(author) && descLower.Contains(author.ToLowerInvariant()))
                boost += 0.08;
        }

        // ── Series name found in description (+0.08) ─────────────────────
        if (!string.IsNullOrWhiteSpace(descLower))
        {
            var series = fileHints.GetValueOrDefault("series");
            if (!string.IsNullOrWhiteSpace(series) && descLower.Contains(series.ToLowerInvariant()))
                boost += 0.08;
        }

        // ── Publisher matches (+0.05, books) ─────────────────────────────
        if (mediaType is MediaType.Books && !string.IsNullOrWhiteSpace(ext.Publisher))
        {
            var filePublisher = fileHints.GetValueOrDefault("publisher");
            if (!string.IsNullOrWhiteSpace(filePublisher))
            {
                var ratio = _fuzzy.ComputeTokenSetRatio(filePublisher, ext.Publisher);
                if (ratio >= 0.85)
                    boost += 0.05;
            }
        }

        // ── Page count within 10% (+0.05, books) ────────────────────────
        if (mediaType is MediaType.Books && ext.PageCount.HasValue)
        {
            var filePages = fileHints.GetValueOrDefault("page_count") ?? fileHints.GetValueOrDefault("word_count");
            if (!string.IsNullOrWhiteSpace(filePages) && int.TryParse(filePages, out var fp) && fp > 0)
            {
                var diff = Math.Abs(fp - ext.PageCount.Value) / (double)Math.Max(fp, ext.PageCount.Value);
                if (diff <= 0.10)
                    boost += 0.05;
            }
        }

        // ── Duration within 15% (+0.05, audiobooks) ─────────────────────
        if (mediaType is MediaType.Audiobooks && ext.DurationSeconds.HasValue)
        {
            var fileDur = fileHints.GetValueOrDefault("duration_sec");
            if (!string.IsNullOrWhiteSpace(fileDur) && double.TryParse(fileDur, out var fd) && fd > 0)
            {
                var diff = Math.Abs(fd - ext.DurationSeconds.Value) / Math.Max(fd, ext.DurationSeconds.Value);
                if (diff <= 0.15)
                    boost += 0.05;
                else if (diff > 0.50)
                    boost -= 0.10; // Duration wildly different — penalty
            }
        }

        // ── Genre overlap (+0.05) ────────────────────────────────────────
        if (ext.Genres is { Count: > 0 })
        {
            var fileGenre = fileHints.GetValueOrDefault("genre");
            if (!string.IsNullOrWhiteSpace(fileGenre))
            {
                var fileGenres = fileGenre.Split(',', ';', '|')
                    .Select(g => g.Trim().ToLowerInvariant())
                    .Where(g => g.Length > 0)
                    .ToHashSet();

                var candidateGenres = ext.Genres
                    .Select(g => g.Trim().ToLowerInvariant())
                    .ToHashSet();

                if (fileGenres.Overlaps(candidateGenres))
                    boost += 0.05;
            }
        }

        // ── Language matches (+0.05) or mismatch (-0.10) ────────────────
        if (!string.IsNullOrWhiteSpace(ext.Language))
        {
            var fileLang = fileHints.GetValueOrDefault("language");
            if (!string.IsNullOrWhiteSpace(fileLang))
            {
                var fileLangNorm = fileLang.Split('-', '_')[0].ToLowerInvariant();
                var candLangNorm = ext.Language.Split('-', '_')[0].ToLowerInvariant();

                if (string.Equals(fileLangNorm, candLangNorm, StringComparison.Ordinal))
                    boost += 0.05;
                else
                    boost -= 0.10;
            }
        }

        return boost;
    }
}
