using System.Text.RegularExpressions;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Shared season/episode filename regex. Byte-identical to the pattern
/// previously duplicated between <c>SmartLabeler</c> (the AI-disabled
/// fallback path) and <c>VideoProcessor</c> (ingestion metadata scanning).
/// </summary>
public static partial class EpisodePatterns
{
    /// <summary>
    /// Matches "S01E01" and multi-episode "S01E01E02" style filenames,
    /// case-insensitively (via explicit <c>[Ss]</c>/<c>[Ee]</c> character
    /// classes rather than <see cref="RegexOptions.IgnoreCase"/>). Named
    /// groups: <c>series</c> (the text preceding the pattern), <c>season</c>,
    /// <c>ep1</c>, and an optional <c>ep2</c> present only for double-episode
    /// files.
    /// </summary>
    [GeneratedRegex(
        @"^(?<series>.+?)\s*[.\-_ ]*[Ss](?<season>\d{1,2})\s*[Ee](?<ep1>\d{1,4})(?:\s*[Ee](?<ep2>\d{1,4}))?",
        RegexOptions.Compiled)]
    public static partial Regex SeasonEpisode();
}
