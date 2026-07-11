namespace MediaEngine.Api.Services.Details;

public static class SeriesDisplayFormatter
{
    public static string? NormalizeContainerTitle(string? value, bool isStructuralSeries)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var title = value.Trim();
        if (!isStructuralSeries)
        {
            return title;
        }

        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && IsRedundantStructuralSuffix(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        return words.Count == 0 ? title : string.Join(' ', words);
    }

    public static string? FormatPosition(string itemLabel, string? position, string? containerTitle)
    {
        var title = NormalizeContainerTitle(containerTitle, isStructuralSeries: true);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(position)
            ? $"Part of {title}"
            : $"{itemLabel} {position.Trim()} in {title}";
    }

    public static string? FormatEpisodePosition(string? season, string? episode, string? showTitle)
    {
        var title = NormalizeContainerTitle(showTitle, isStructuralSeries: true);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(season))
        {
            parts.Add($"Season {season.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(episode))
        {
            parts.Add($"Episode {episode.Trim().TrimStart('E', 'e')}");
        }

        return parts.Count == 0
            ? $"Part of {title}"
            : $"{string.Join(", ", parts)} in {title}";
    }

    private static bool IsRedundantStructuralSuffix(string value)
        => string.Equals(value, "collection", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "series", StringComparison.OrdinalIgnoreCase);
}
