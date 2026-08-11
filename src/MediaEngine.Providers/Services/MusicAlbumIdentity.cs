using System.Text.RegularExpressions;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Compares album names for track-list identity. Retail catalogues commonly append
/// mastering and mix labels that do not change the underlying sequence, while
/// deluxe/expanded/collector editions usually add tracks and must remain distinct.
/// </summary>
internal static partial class MusicAlbumIdentity
{
    private static readonly string[] TrackSetQualifiers =
    [
        "anniversary",
        "bonus",
        "collector",
        "deluxe",
        "expanded",
        "extended",
        "super deluxe",
    ];

    public static bool IsSameTrackList(string? requestedAlbum, string? candidateAlbum)
    {
        if (string.IsNullOrWhiteSpace(requestedAlbum) || string.IsNullOrWhiteSpace(candidateAlbum))
            return false;

        var requested = Normalize(requestedAlbum);
        var candidate = Normalize(candidateAlbum);
        if (string.IsNullOrWhiteSpace(requested.BaseName)
            || string.IsNullOrWhiteSpace(candidate.BaseName))
            return false;

        if (!string.Equals(
                requested.TrackSetQualifier,
                candidate.TrackSetQualifier,
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(requested.BaseName, candidate.BaseName, StringComparison.OrdinalIgnoreCase)
            || RetailTextSimilarity.ComputeWordOverlap(requested.BaseName, candidate.BaseName) >= 0.92)
        {
            return true;
        }

        var requestedSuffix = Normalize(AlbumNameAfterIdentityPrefix(requestedAlbum)).BaseName;
        var candidateSuffix = Normalize(AlbumNameAfterIdentityPrefix(candidateAlbum)).BaseName;
        return string.Equals(requestedSuffix, candidateSuffix, StringComparison.OrdinalIgnoreCase)
            || RetailTextSimilarity.ComputeWordOverlap(requestedSuffix, candidateSuffix) >= 0.92;
    }

    public static double ComputeBaseNameOverlap(string? requestedAlbum, string? candidateAlbum)
    {
        if (string.IsNullOrWhiteSpace(requestedAlbum) || string.IsNullOrWhiteSpace(candidateAlbum))
            return 0;

        var requested = Normalize(requestedAlbum);
        var candidate = Normalize(candidateAlbum);
        if (string.IsNullOrWhiteSpace(requested.BaseName)
            || string.IsNullOrWhiteSpace(candidate.BaseName))
            return 0;

        if (!string.Equals(
                requested.TrackSetQualifier,
                candidate.TrackSetQualifier,
                StringComparison.OrdinalIgnoreCase))
            return 0;

        return RetailTextSimilarity.ComputeWordOverlap(requested.BaseName, candidate.BaseName);
    }

    private static NormalizedAlbum Normalize(string value)
    {
        var comparable = RetailTextSimilarity.NormalizeComparableText(value);
        var trackSetQualifier = string.Join(
            '|',
            TrackSetQualifiers.Where(qualifier =>
                comparable.Contains(qualifier, StringComparison.OrdinalIgnoreCase)));
        var hasHarmlessEditionQualifier = HarmlessEditionQualifierRegex().IsMatch(comparable);

        // These labels describe mastering, mix, or media presentation. They are
        // intentionally removable because the song sequence remains the album.
        comparable = HarmlessEditionQualifierRegex().Replace(comparable, " ");
        comparable = SoundtrackDescriptorRegex().Replace(comparable, " ");
        comparable = TrackSetQualifierRegex().Replace(comparable, " ");
        if (hasHarmlessEditionQualifier)
            comparable = YearRegex().Replace(comparable, " ");
        comparable = string.Join(' ', comparable.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return new NormalizedAlbum(comparable, trackSetQualifier);
    }

    private static string AlbumNameAfterIdentityPrefix(string value)
    {
        var separator = value.LastIndexOf(':');
        return separator >= 0 && separator < value.Length - 1
            ? value[(separator + 1)..].Trim()
            : value;
    }

    [GeneratedRegex(@"\b(remaster(ed)?|remix(ed)?|mix|mono|stereo|digital master)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HarmlessEditionQualifierRegex();

    [GeneratedRegex(@"\b(original motion picture soundtrack|music from the original motion picture|original soundtrack)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoundtrackDescriptorRegex();

    [GeneratedRegex(@"\b(anniversary|bonus|collector'?s?|deluxe|expanded|extended|super deluxe)( edition)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex TrackSetQualifierRegex();

    [GeneratedRegex(@"\b(19|20)\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearRegex();

    private sealed record NormalizedAlbum(string BaseName, string TrackSetQualifier);
}
