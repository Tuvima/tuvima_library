namespace MediaEngine.Web.Services.MediaTiles;

public static class MediaTileArtworkUrl
{
    public static string? Sized(string? url, string size)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !url.Contains("/stream/artwork/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hashIndex = url.IndexOf('#', StringComparison.Ordinal);
        var hash = hashIndex >= 0 ? url[hashIndex..] : string.Empty;
        var withoutHash = hashIndex >= 0 ? url[..hashIndex] : url;
        var queryIndex = withoutHash.IndexOf('?', StringComparison.Ordinal);
        var baseUrl = queryIndex >= 0 ? withoutHash[..queryIndex] : withoutHash;
        return $"{baseUrl}?size={size}{hash}";
    }

    public static string? SrcSet(string? smallUrl, string? mediumUrl)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(smallUrl))
            parts.Add($"{smallUrl} 320w");
        if (!string.IsNullOrWhiteSpace(mediumUrl)
            && !string.Equals(smallUrl, mediumUrl, StringComparison.OrdinalIgnoreCase))
            parts.Add($"{mediumUrl} 960w");

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
