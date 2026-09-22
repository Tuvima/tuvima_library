namespace MediaEngine.Web.Services.MediaTiles;

public static class MediaTileArtworkUrl
{
    public static string? Sized(string? url, string size)
    {
        var normalizedSize = size.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(url)
            || normalizedSize is not ("s" or "m" or "l")
            || !SupportsRenditions(url))
        {
            return null;
        }

        var hashIndex = url.IndexOf('#', StringComparison.Ordinal);
        var hash = hashIndex >= 0 ? url[hashIndex..] : string.Empty;
        var withoutHash = hashIndex >= 0 ? url[..hashIndex] : url;
        var queryIndex = withoutHash.IndexOf('?', StringComparison.Ordinal);
        var baseUrl = queryIndex >= 0 ? withoutHash[..queryIndex] : withoutHash;
        var query = queryIndex >= 0 ? withoutHash[(queryIndex + 1)..] : string.Empty;
        var parameters = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(parameter => !parameter.StartsWith("size=", StringComparison.OrdinalIgnoreCase))
            .Append($"size={normalizedSize}");
        return $"{baseUrl}?{string.Join('&', parameters)}{hash}";
    }

    public static string? SrcSet(string? smallUrl, string? mediumUrl)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(smallUrl))
        {
            parts.Add($"{smallUrl} 320w");
        }

        if (!string.IsNullOrWhiteSpace(mediumUrl)
            && !string.Equals(smallUrl, mediumUrl, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"{mediumUrl} 960w");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public static string? SrcSet(string? smallUrl, string? mediumUrl, string? largeUrl)
    {
        var parts = new List<string>();
        AddCandidate(parts, smallUrl, 320);
        AddCandidate(parts, mediumUrl, 960);
        AddCandidate(parts, largeUrl, 2160);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static bool SupportsRenditions(string url) =>
        url.Contains("/stream/artwork/", StringComparison.OrdinalIgnoreCase)
        || url.Contains("/api/v1/display/artwork/assets/", StringComparison.OrdinalIgnoreCase)
        || (url.Contains("/stream/entity/", StringComparison.OrdinalIgnoreCase)
            && url.Contains("/cover", StringComparison.OrdinalIgnoreCase))
        || (url.Contains("/persons/", StringComparison.OrdinalIgnoreCase)
            && url.Contains("/headshot", StringComparison.OrdinalIgnoreCase));

    private static void AddCandidate(List<string> parts, string? url, int width)
    {
        if (!string.IsNullOrWhiteSpace(url)
            && !parts.Any(part => part.StartsWith($"{url} ", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add($"{url} {width}w");
        }
    }
}
