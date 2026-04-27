namespace MediaEngine.Web.Components.Browse;

public static class BrowseArtworkRules
{
    public static string? ResolveWideArtwork(string? backgroundUrl, string? bannerUrl) =>
        string.IsNullOrWhiteSpace(backgroundUrl) ? bannerUrl : backgroundUrl;

    public static string ResolveArtworkAspectRatio(
        string? artworkUrl,
        int? width,
        int? height,
        int? squareWidth = null,
        int? squareHeight = null)
    {
        if (width is > 0 && height is > 0)
        {
            return $"{width.Value} / {height.Value}";
        }

        if (squareWidth is > 0 && squareHeight is > 0)
        {
            return $"{squareWidth.Value} / {squareHeight.Value}";
        }

        return !string.IsNullOrWhiteSpace(artworkUrl) && IsLikelySquareArtwork(artworkUrl)
            ? "1 / 1"
            : "2 / 3";
    }

    public static IReadOnlyList<string> CompactFacts(params string?[] values)
    {
        var facts = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var cleaned = value.Trim();
            if (!facts.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            {
                facts.Add(cleaned);
            }
        }

        return facts;
    }

    public static string PreferredDisplayShape(string? mediaType, string? wideArtwork, string? squareArtwork)
    {
        if (!string.IsNullOrWhiteSpace(wideArtwork) && IsWatchMedia(mediaType))
        {
            return "landscape";
        }

        if (!string.IsNullOrWhiteSpace(squareArtwork) || IsListenMedia(mediaType))
        {
            return "square";
        }

        return "portrait";
    }

    private static bool IsLikelySquareArtwork(string artworkUrl) =>
        artworkUrl.Contains("square", StringComparison.OrdinalIgnoreCase)
        || artworkUrl.Contains("album", StringComparison.OrdinalIgnoreCase)
        || artworkUrl.Contains("artist", StringComparison.OrdinalIgnoreCase);

    private static bool IsListenMedia(string? mediaType)
    {
        var value = mediaType ?? string.Empty;
        return value.Contains("music", StringComparison.OrdinalIgnoreCase)
               || value.Contains("audio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWatchMedia(string? mediaType)
    {
        var value = mediaType ?? string.Empty;
        return value.Contains("movie", StringComparison.OrdinalIgnoreCase)
               || value.Contains("tv", StringComparison.OrdinalIgnoreCase)
               || value.Contains("video", StringComparison.OrdinalIgnoreCase);
    }
}
