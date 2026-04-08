using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<RetailMatchScoringService>? _logger;

    public RetailMatchScoringService(
        IFuzzyMatchingService fuzzy,
        IConfigurationLoader configLoader,
        ICoverArtHashService? coverArtHash = null,
        ILogger<RetailMatchScoringService>? logger = null)
    {
        _fuzzy = fuzzy;
        _configLoader = configLoader;
        _coverArtHash = coverArtHash;
        _logger = logger;
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
        // For TV episodes, prefer episode_title so we score against the candidate
        // episode name (e.g. "Pilot") rather than the show name (e.g. "Breaking Bad").
        // Show name is still used for the upstream provider URL via ShowName/{title}
        // substitution and is verified separately via the structural S/E boost.
        double titleScore = 0.0;
        var fileTitle = mediaType == MediaType.TV
            ? (fileHints.GetValueOrDefault("episode_title") ?? fileHints.GetValueOrDefault("title"))
            : fileHints.GetValueOrDefault("title");

        if (MediaEngine.Domain.Services.PlaceholderTitleDetector.IsPlaceholder(fileTitle))
        {
            return new FieldMatchScores
            {
                TitleScore      = 0.0,
                AuthorScore     = 0.0,
                YearScore       = 0.0,
                FormatScore     = 0.0,
                CrossFieldBoost = 0.0,
                CoverArtScore   = 0.0,
                CompositeScore  = 0.0,
            };
        }

        if (!string.IsNullOrWhiteSpace(fileTitle) && !string.IsNullOrWhiteSpace(candidateTitle))
        {
            titleScore = _fuzzy.ComputeTokenSetRatio(fileTitle, candidateTitle);
        }

        // ── Author score ─────────────────────────────────────────────────
        double authorScore = 0.0;
        // For music files, "artist" is the primary creator field, not "author".
        // For video/comics, "director" or "writer" may be the primary creator.
        var fileAuthor = fileHints.GetValueOrDefault("author")
            ?? fileHints.GetValueOrDefault("artist")
            ?? fileHints.GetValueOrDefault("director")
            ?? fileHints.GetValueOrDefault("writer");
        if (!string.IsNullOrWhiteSpace(fileAuthor) && !string.IsNullOrWhiteSpace(candidateAuthor))
        {
            // First try a full-string comparison (handles single-author and matching order).
            authorScore = _fuzzy.ComputeTokenSetRatio(fileAuthor, candidateAuthor);

            // When the full-string score is low, split on common multi-author
            // separators and compare each individual name independently.
            // "Neil Gaiman & Terry Pratchett" vs "Terry Pratchett" scores 0.5 (1 of 2).
            if (authorScore < 0.70)
            {
                var fileAuthors = SplitAuthors(fileAuthor);
                var candidateAuthors = SplitAuthors(candidateAuthor);

                if (fileAuthors.Count > 1 || candidateAuthors.Count > 1)
                {
                    // For each file author, find the best matching candidate author.
                    int matched = 0;
                    var usedCandidates = new HashSet<int>();
                    foreach (var fa in fileAuthors)
                    {
                        double bestMatch = 0.0;
                        int bestIdx = -1;
                        for (int i = 0; i < candidateAuthors.Count; i++)
                        {
                            if (usedCandidates.Contains(i)) continue;
                            var sim = _fuzzy.ComputeTokenSetRatio(fa, candidateAuthors[i]);
                            if (sim > bestMatch)
                            {
                                bestMatch = sim;
                                bestIdx = i;
                            }
                        }
                        if (bestMatch >= 0.70 && bestIdx >= 0)
                        {
                            matched++;
                            usedCandidates.Add(bestIdx);
                        }
                    }

                    // Proportional: 2 of 2 = 1.0, 1 of 2 = 0.5, 0 of 2 = 0.0
                    var splitScore = (double)matched / Math.Max(fileAuthors.Count, candidateAuthors.Count);
                    if (splitScore > authorScore)
                        authorScore = splitScore;
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(candidateAuthor) && !string.IsNullOrWhiteSpace(fileAuthor))
        {
            // File has a creator but the provider returned no author data — 0.0 per spec
            // (weak evidence: this candidate can't be verified against the known creator).
            authorScore = 0.0;
        }
        // When BOTH file and candidate have no author data (e.g. Movies, TV, Comics where
        // creator fields are absent from file metadata), we flag this for weight redistribution
        // below — see the composite calculation.

        // ── Year score ───────────────────────────────────────────────────
        double yearScore = 0.0; // Penalised when missing
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

        // When NEITHER the file NOR the candidate carries any creator data (common for
        // Movies, TV, and Comics where "author/artist/director/writer" are absent on both
        // sides), the author weight would penalise every candidate equally and unfairly.
        // In that case, redistribute the 35% author weight proportionally to the other
        // three fields so scoring is driven entirely by title, year, and format.
        double effectiveTitleWeight  = titleWeight;
        double effectiveAuthorWeight = authorWeight;
        double effectiveYearWeight   = yearWeight;
        double effectiveFormatWeight = formatWeight;

        bool bothLackAuthor = string.IsNullOrWhiteSpace(fileAuthor)
                           && string.IsNullOrWhiteSpace(candidateAuthor);
        if (bothLackAuthor)
        {
            double remaining = 1.0 - authorWeight; // 0.65
            effectiveTitleWeight  = titleWeight  / remaining;
            effectiveYearWeight   = yearWeight   / remaining;
            effectiveFormatWeight = formatWeight / remaining;
            effectiveAuthorWeight = 0.0;
        }

        var composite = (titleScore  * effectiveTitleWeight)
                      + (authorScore * effectiveAuthorWeight)
                      + (yearScore   * effectiveYearWeight)
                      + (formatScore * effectiveFormatWeight)
                      + crossFieldBoost
                      + coverBoost;

        _logger?.LogDebug("RetailScoring: title={TitleScore:F2} author={AuthorScore:F2} year={YearScore:F2} cross={CrossField:F2} cover={Cover:F2} composite={Composite:F2} — file='{FileTitle}' candidate='{CandidateTitle}'",
            titleScore, authorScore, yearScore, crossFieldBoost, coverBoost, composite, fileTitle, candidateTitle);

        return new FieldMatchScores
        {
            TitleScore      = titleScore,
            AuthorScore     = authorScore,
            YearScore       = yearScore,
            FormatScore     = formatScore,
            CrossFieldBoost = crossFieldBoost,
            CoverArtScore   = coverBoost,
            CompositeScore  = Math.Round(Math.Clamp(composite, 0.0, 1.0), 4),
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

    /// <summary>
    /// Splits a multi-author string on common separators: " &amp; ", " and ", ", ".
    /// Returns individual author names, trimmed and non-empty.
    /// Single-author strings return a list with one element.
    /// </summary>
    private static List<string> SplitAuthors(string authors)
    {
        // Split on " & ", " and " (case-insensitive), and ", "
        var parts = System.Text.RegularExpressions.Regex.Split(
            authors,
            @"\s+&\s+|\s+and\s+|,\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return parts
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }
}
