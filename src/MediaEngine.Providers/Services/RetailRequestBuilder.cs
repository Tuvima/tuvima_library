namespace MediaEngine.Providers.Services;

public sealed class RetailRequestBuilder
{
    public string BuildAppleTrackSearchUrl(string searchQuery, string country, string language)
    {
        var query = Uri.EscapeDataString(searchQuery);
        return $"https://itunes.apple.com/search?term={query}&entity=musicTrack&limit=10&country={country}&lang={language}_{country}";
    }

    public string BuildAppleAlbumSearchUrl(string? artist, string album, string country, string language)
    {
        var query = Uri.EscapeDataString($"{artist} {album}".Trim());
        return $"https://itunes.apple.com/search?term={query}&entity=album&limit=10&country={country}&lang={language}_{country}";
    }

    public string BuildAppleAlbumLookupUrl(string collectionId, string country, string language)
        => $"https://itunes.apple.com/lookup?id={collectionId}&entity=song&country={country}&lang={language}_{country}";

    public string BuildTmdbTvSearchUrl(string showName, int? yearHint, string apiKey, string language, string country)
    {
        var query = Uri.EscapeDataString(showName.Trim());
        var baseUrl = $"https://api.themoviedb.org/3/search/tv?query={query}&include_adult=false&language={language}-{country}&page=1&api_key={apiKey}";
        return yearHint.HasValue
            ? $"{baseUrl}&first_air_date_year={yearHint.Value}"
            : baseUrl;
    }

    public string BuildTmdbTvDetailsUrl(string tvId, string apiKey, string language, string country)
        => $"https://api.themoviedb.org/3/tv/{tvId}?language={language}-{country}&append_to_response=aggregate_credits,content_ratings&api_key={apiKey}";

    public string BuildTmdbSeasonUrl(string tvId, int seasonNumber, string apiKey, string language, string country)
        => $"https://api.themoviedb.org/3/tv/{tvId}/season/{seasonNumber}?language={language}-{country}&api_key={apiKey}";

    public static string? BuildAppleCoverUrl(string? artworkUrl100)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl100))
            return null;

        return artworkUrl100
            .Replace("100x100bb", "9999x9999bb", StringComparison.OrdinalIgnoreCase)
            .Replace("60x60bb", "9999x9999bb", StringComparison.OrdinalIgnoreCase);
    }

    public static string? BuildTmdbImageUrl(string? stillPath)
    {
        if (string.IsNullOrWhiteSpace(stillPath))
            return null;

        return stillPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? stillPath
            : $"https://image.tmdb.org/t/p/w500{stillPath}";
    }

    public static string InferTmdbStillExtension(string imageUrl)
    {
        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
        return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
    }
}
