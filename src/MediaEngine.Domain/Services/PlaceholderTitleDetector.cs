using System.Text.RegularExpressions;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Centralized rules for deciding when a file's title is a placeholder
/// (e.g. "Untitled", "Track 01", a single character) and therefore can
/// never be reliably matched to a real work — such items must be routed
/// to manual review with the <c>PlaceholderTitle</c> trigger rather than
/// being thrown into the generic retail-failed bucket.
/// </summary>
public static class PlaceholderTitleDetector
{
    private static readonly HashSet<string> ExactMatches =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "unknown",
            "untitled",
            "untitled book",
            "untitled track",
            "untitled audio",
            "untitled video",
            "unnamed",
            "no title",
            "title",
            "new recording",
            "n/a",
            "na",
            "tbd",
        };

    private static readonly Regex TrackPattern =
        new(@"^track\s*\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns <c>true</c> when the title is missing, blank, a known placeholder
    /// string, a single character, or a generic "Track NN" pattern.
    /// </summary>
    public static bool IsPlaceholder(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var trimmed = title.Trim();

        if (trimmed.Length <= 1)
            return true;

        if (ExactMatches.Contains(trimmed))
            return true;

        if (TrackPattern.IsMatch(trimmed))
            return true;

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the canonical/hint dictionary contains at least
    /// one bridge identifier (ISBN, ASIN, Wikidata QID, TMDB ID, IMDB ID,
    /// MusicBrainz ID, Apple Music ID) that could anchor identity even when the
    /// title itself is a placeholder.
    /// </summary>
    public static bool HasBridgeId(IReadOnlyDictionary<string, string> hints)
    {
        if (hints is null) return false;

        string[] keys =
        {
            "isbn", "asin", "wikidata_qid",
            "tmdb_id", "imdb_id",
            "musicbrainz_id", "musicbrainz_release_id", "musicbrainz_recording_id",
            "apple_music_id", "apple_music_collection_id",
        };

        foreach (var key in keys)
        {
            if (hints.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return true;
        }

        return false;
    }
}
