namespace MediaEngine.Web.Services.Theming;

/// <summary>
/// Converts raw social media handles into platform-specific URIs.
/// Mobile and Automotive device classes get native app URI schemes;
/// Web and Television get HTTPS fallback links.
/// </summary>
public static class SocialUriHelper
{
    /// <summary>
    /// Builds the appropriate URI for a social platform link.
    /// </summary>
    /// <param name="platform">Platform key: "instagram", "twitter", "tiktok", "mastodon", "website".</param>
    /// <param name="rawValue">Raw handle or URL from Wikidata (e.g. "frank_herbert" or "https://instagram.com/frank_herbert").</param>
    /// <param name="deviceClass">Active device class: "web", "mobile", "television", "automotive".</param>
    /// <returns>The appropriate URI string, or the raw value if no transformation applies.</returns>
    public static string? GetSocialUri(string platform, string? rawValue, string deviceClass = "web")
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        bool useNativeScheme = deviceClass is "mobile" or "automotive";

        // If the value is already a full URL, use it directly for web/TV.
        // For mobile/automotive, try to extract the handle and build a native URI.
        bool isFullUrl = rawValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || rawValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        return platform.ToLowerInvariant() switch
        {
            "instagram" => BuildInstagramUri(rawValue, isFullUrl, useNativeScheme),
            "twitter"   => BuildTwitterUri(rawValue, isFullUrl, useNativeScheme),
            "tiktok"    => BuildTikTokUri(rawValue, isFullUrl, useNativeScheme),
            "mastodon"  => BuildMastodonUri(rawValue, isFullUrl),
            "website"   => rawValue, // Always direct URL
            _           => rawValue,
        };
    }

    private static string BuildInstagramUri(string value, bool isFullUrl, bool useNativeScheme)
    {
        var handle = isFullUrl ? ExtractHandleFromUrl(value) : value.TrimStart('@');
        if (string.IsNullOrEmpty(handle)) return value;

        return useNativeScheme
            ? $"instagram://user?username={handle}"
            : $"https://instagram.com/{handle}";
    }

    private static string BuildTwitterUri(string value, bool isFullUrl, bool useNativeScheme)
    {
        var handle = isFullUrl ? ExtractHandleFromUrl(value) : value.TrimStart('@');
        if (string.IsNullOrEmpty(handle)) return value;

        return useNativeScheme
            ? $"twitter://user?screen_name={handle}"
            : $"https://x.com/{handle}";
    }

    private static string BuildTikTokUri(string value, bool isFullUrl, bool useNativeScheme)
    {
        var handle = isFullUrl ? ExtractHandleFromUrl(value) : value.TrimStart('@');
        if (string.IsNullOrEmpty(handle)) return value;

        return useNativeScheme
            ? $"tiktok://user?username={handle}"
            : $"https://tiktok.com/@{handle}";
    }

    private static string BuildMastodonUri(string value, bool isFullUrl)
    {
        // Mastodon is always web-native (no universal app URI scheme).
        if (isFullUrl) return value;

        // Mastodon addresses look like "user@instance.social"
        if (value.Contains('@') && value.Contains('.'))
        {
            var parts = value.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                return $"https://{parts[1]}/@{parts[0]}";
        }

        return value;
    }

    /// <summary>
    /// Extracts the last path segment from a social media URL as the handle.
    /// E.g., "https://instagram.com/frank_herbert" → "frank_herbert".
    /// </summary>
    private static string? ExtractHandleFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.TrimEnd('/');
            var lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path[(lastSlash + 1)..] : path.TrimStart('/');
        }
        catch
        {
            return null;
        }
    }
}
