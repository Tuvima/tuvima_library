using MudBlazor;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Services.Formatting;

namespace MediaEngine.Web.Components.Activity;

public static class ActivityDisplay
{
    public const string ReviewGroup = "Needs Review";

    public static string ProviderName(string? provider)
        => ProviderDisplayNames.Format(provider);

    public static string MediaIcon(string? mediaType) => NormalizeMediaType(mediaType) switch
    {
        ReviewGroup => Icons.Material.Outlined.ReportProblem,
        "Movies" => Icons.Material.Outlined.Movie,
        "TV" => Icons.Material.Outlined.LiveTv,
        "Music" => Icons.Material.Outlined.Album,
        "Books" => Icons.Material.Outlined.MenuBook,
        "Audiobooks" => Icons.Material.Outlined.Headphones,
        "Comics" => Icons.Material.Outlined.AutoStories,
        _ => Icons.Material.Outlined.Inventory2,
    };

    public static string MediaToneClass(string? mediaType) => NormalizeMediaType(mediaType) switch
    {
        ReviewGroup => "is-review",
        "Movies" => "is-movies",
        "TV" => "is-tv",
        "Music" => "is-music",
        "Books" => "is-books",
        "Audiobooks" => "is-audiobooks",
        "Comics" => "is-comics",
        _ => "is-unknown",
    };

    public static string NormalizeMediaType(string? mediaType)
    {
        var normalized = mediaType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "needs review" => ReviewGroup,
            "movie" or "movies" or "film" or "films" => "Movies",
            "tv" or "tv shows" or "television" or "show" or "shows" => "TV",
            "music" or "album" or "albums" => "Music",
            "audiobook" or "audiobooks" => "Audiobooks",
            "book" or "books" or "ebook" or "ebooks" or "epub" or "pdf" => "Books",
            "comic" or "comics" or "cbz" or "cbr" => "Comics",
            { } value when value.Contains("audio") && value.Contains("book") => "Audiobooks",
            { Length: > 0 } => DisplayFormat.SplitWords(mediaType!),
            _ => "Unknown",
        };
    }

    public static string StatusText(string? status, string? auditStatus = null)
    {
        var audit = auditStatus?.Trim();
        if (string.Equals(audit, "NeedsReview", StringComparison.OrdinalIgnoreCase)
            || string.Equals(audit, ReviewGroup, StringComparison.OrdinalIgnoreCase))
            return "Needs review";

        var raw = status?.Trim() ?? "";
        var normalized = raw.ToLowerInvariant();
        if (normalized.Contains("review", StringComparison.Ordinal) || normalized is "qidneedsreview" or "retailmatchedneedsreview")
            return "Needs review";
        if (normalized is "ready"
            or "readywithoutuniverse"
            or "succeeded"
            or "completed"
            or "complete"
            or "qidresolved"
            or "wikidatamatched"
            or "retailmatched")
            return "Complete";
        if (normalized is "retailnomatch" or "qidnomatch" or "no_result" or "missing_confirmed")
            return "No match";
        if (normalized.Contains("fail", StringComparison.Ordinal) || normalized.Contains("blocked", StringComparison.Ordinal) || normalized.Contains("dead", StringComparison.Ordinal))
            return "Needs attention";
        if (normalized.Contains("running", StringComparison.Ordinal) || normalized.Contains("active", StringComparison.Ordinal))
            return "Running";
        if (normalized.Contains("queued", StringComparison.Ordinal))
            return "Queued";

        return string.IsNullOrWhiteSpace(raw) ? "Unknown" : DisplayFormat.SplitWords(raw);
    }

    public static string StatusTone(string? status, string? auditStatus = null)
    {
        var text = StatusText(status, auditStatus);
        return text switch
        {
            "Complete" => "success",
            "Needs review" => "warning",
            "Needs attention" => "danger",
            "Running" or "Queued" => "info",
            _ => "neutral",
        };
    }

}
