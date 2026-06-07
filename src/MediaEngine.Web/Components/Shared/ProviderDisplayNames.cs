namespace MediaEngine.Web.Components.Shared;

public static class ProviderDisplayNames
{
    public static string Format(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return "-";

        var normalized = provider.Trim().Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var compact = normalized.Replace(" ", "", StringComparison.Ordinal);
        return compact switch
        {
            "tmdb" => "TMDB",
            "wikidata" => "Wikidata",
            "openlibrary" => "Open Library",
            "apple" or "appleapi" or "applebooks" or "applemusic" => "Apple",
            "provider" or "providermatch" => "Retail match",
            _ => SplitWords(provider),
        };
    }

    private static string SplitWords(string value)
    {
        var normalized = value.Replace('_', ' ').Replace('-', ' ');
        var builder = new System.Text.StringBuilder(normalized.Length + 8);
        for (var i = 0; i < normalized.Length; i++)
        {
            if (i > 0 && char.IsUpper(normalized[i]) && !char.IsWhiteSpace(normalized[i - 1]))
                builder.Append(' ');
            else if (i > 0 && char.IsDigit(normalized[i]) && char.IsLetter(normalized[i - 1]))
                builder.Append(' ');

            builder.Append(normalized[i]);
        }

        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(builder.ToString().Trim())
            .Replace(" Api", " API", StringComparison.Ordinal)
            .Replace(" Qid", " QID", StringComparison.Ordinal)
            .Replace(" Tmdb", " TMDB", StringComparison.Ordinal);
    }
}
