using MediaEngine.Web.Services.Formatting;

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
            _ => DisplayFormat.SplitWords(provider),
        };
    }
}
