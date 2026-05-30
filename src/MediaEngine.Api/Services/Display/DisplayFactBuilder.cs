namespace MediaEngine.Api.Services.Display;

public static class DisplayFactBuilder
{
    public static IReadOnlyList<string> Build(
        string mediaKind,
        string title,
        string? year = null,
        string? genre = null,
        string? author = null,
        string? artist = null,
        string? narrator = null,
        string? series = null,
        string? seriesPosition = null,
        string? showName = null,
        string? season = null,
        string? episode = null,
        string? track = null,
        string? album = null)
    {
        var facts = new List<string>();
        switch (mediaKind)
        {
            case "Movie":
                AddFact(facts, year, title);
                break;
            case "TV":
                AddFact(facts, showName, title);
                AddFact(facts, year, title);
                AddFact(facts, FormatEpisode(season, episode), title);
                break;
            case "Comic":
                AddFact(facts, series, title);
                AddFact(facts, FormatIssue(seriesPosition), title);
                AddFact(facts, author, title);
                break;
            case "Book":
                AddFact(facts, author, title);
                break;
            case "Audiobook":
                AddFact(facts, author, title);
                AddFact(facts, narrator is null ? null : $"Narrated by {narrator}", title, author);
                break;
            case "Music":
                AddFact(facts, artist ?? author, title);
                AddFact(facts, album, title);
                AddFact(facts, track is null ? null : $"Track {track}", title);
                break;
            default:
                AddFact(facts, author ?? artist, title);
                AddFact(facts, year, title);
                break;
        }

        foreach (var item in SplitGenres(genre).Take(3))
        {
            AddFact(facts, item, title);
        }

        return facts;
    }

    private static void AddFact(List<string> facts, string? value, params string?[] exclusions)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var cleaned = value.Trim();
        if (exclusions.Any(exclusion => string.Equals(cleaned, exclusion?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!facts.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
        {
            facts.Add(cleaned);
        }
    }

    private static IEnumerable<string> SplitGenres(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return [];
        }

        return genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? FormatEpisode(string? season, string? episode) =>
        !string.IsNullOrWhiteSpace(season) && !string.IsNullOrWhiteSpace(episode)
            ? $"S{season} E{episode}"
            : null;

    private static string? FormatIssue(string? seriesPosition) =>
        string.IsNullOrWhiteSpace(seriesPosition)
            ? null
            : $"Issue #{seriesPosition}";
}
