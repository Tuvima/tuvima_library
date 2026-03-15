using System.Text.RegularExpressions;

namespace MediaEngine.Processors;

/// <summary>
/// Cleans raw filenames into normalized title strings suitable for
/// metadata claims and Wikidata search queries. Strips quality tags,
/// extracts embedded years, and normalizes separators.
/// </summary>
public static partial class TitleNormalizer
{
    /// <summary>
    /// Quality/encoding tags to remove from filenames (case-insensitive).
    /// Ordered from longest to shortest to prevent partial matches.
    /// </summary>
    private static readonly string[] QualityTags =
    [
        "directors cut", "directors.cut", "director's cut",
        "extended edition", "extended cut",
        "web-dl", "webrip", "bdrip", "brrip", "hdrip", "dvdrip",
        "bluray", "blu-ray",
        "2160p", "1080p", "720p", "480p", "360p",
        "x264", "x265", "h264", "h265", "hevc", "avc",
        "hdr10+", "hdr10", "hdr",
        "atmos", "truehd", "dts-hd", "dts",
        "flac", "aac", "ac3", "eac3",
        "remux", "proper", "repack", "rerip",
        "extended", "unrated", "remastered",
        "10bit", "8bit",
        "5.1", "7.1", "2.0",
    ];

    /// <summary>
    /// TV episode patterns to extract and remove from the title.
    /// Captures season and episode numbers.
    /// </summary>
    [GeneratedRegex(@"[Ss](\d{1,2})[Ee](\d{1,3})", RegexOptions.Compiled)]
    private static partial Regex TvSeasonEpisodeRegex();

    [GeneratedRegex(@"(\d{1,2})[Xx](\d{1,3})", RegexOptions.Compiled)]
    private static partial Regex TvAltFormatRegex();

    /// <summary>
    /// Year pattern: 4-digit year in parentheses, brackets, or standalone.
    /// Only matches years 1900-2099.
    /// </summary>
    [GeneratedRegex(@"[\(\[\{]?((?:19|20)\d{2})[\)\]\}]?", RegexOptions.Compiled)]
    private static partial Regex YearRegex();

    /// <summary>
    /// Multiple whitespace collapse.
    /// </summary>
    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceRegex();

    /// <summary>
    /// Normalizes a raw filename (without extension) into a clean title string.
    /// </summary>
    /// <param name="fileNameWithoutExtension">The filename stem (no extension).</param>
    /// <returns>Normalized result with cleaned title and optional extracted metadata.</returns>
    public static NormalizedTitle Normalize(string? fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            return new NormalizedTitle(string.Empty, null, null, null);

        string working = fileNameWithoutExtension;

        // Step 1: Replace dots, underscores with spaces (common in scene releases).
        working = working.Replace('.', ' ').Replace('_', ' ');

        // Step 2: Extract TV season/episode markers before stripping.
        int? season = null;
        int? episode = null;

        var tvMatch = TvSeasonEpisodeRegex().Match(working);
        if (!tvMatch.Success)
            tvMatch = TvAltFormatRegex().Match(working);

        if (tvMatch.Success)
        {
            season = int.Parse(tvMatch.Groups[1].Value);
            episode = int.Parse(tvMatch.Groups[2].Value);
            working = working.Remove(tvMatch.Index, tvMatch.Length);
        }

        // Step 3: Extract year (take the LAST 4-digit year in the string,
        // as earlier numbers may be part of the title like "2001 A Space Odyssey").
        int? year = null;
        var yearMatches = YearRegex().Matches(working);
        if (yearMatches.Count > 0)
        {
            var lastYearMatch = yearMatches[^1];
            year = int.Parse(lastYearMatch.Groups[1].Value);
            // Only remove the year if it's not the only content
            var withoutYear = working.Remove(lastYearMatch.Index, lastYearMatch.Length).Trim();
            if (withoutYear.Length > 0)
                working = withoutYear;
            else
                year = null; // Don't strip if it would leave nothing
        }

        // Step 4: Remove quality/encoding tags (case-insensitive).
        foreach (var tag in QualityTags)
        {
            int idx;
            while ((idx = working.IndexOf(tag, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                working = working.Remove(idx, tag.Length);
            }
        }

        // Step 5: Remove remaining brackets and their contents if they're now empty.
        working = Regex.Replace(working, @"[\(\[\{]\s*[\)\]\}]", " ");

        // Step 6: Remove stray punctuation (keep hyphens between words, apostrophes).
        working = Regex.Replace(working, @"[^\w\s'-]", " ");

        // Step 7: Collapse multiple spaces and trim.
        working = MultiSpaceRegex().Replace(working, " ").Trim();

        // Step 8: Remove trailing hyphens/dashes left from tag removal.
        working = working.TrimEnd('-', ' ');

        return new NormalizedTitle(working, year, season, episode);
    }
}

/// <summary>
/// Result of title normalization with optional extracted metadata.
/// </summary>
/// <param name="CleanTitle">The normalized, cleaned title string.</param>
/// <param name="Year">Extracted year (1900-2099) if found in filename.</param>
/// <param name="Season">Extracted TV season number if found.</param>
/// <param name="Episode">Extracted TV episode number if found.</param>
public record NormalizedTitle(string CleanTitle, int? Year, int? Season, int? Episode)
{
    /// <summary>
    /// Returns the clean title in lowercase for search queries.
    /// </summary>
    public string SearchTitle => CleanTitle.ToLowerInvariant();
}
