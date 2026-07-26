namespace MediaEngine.Domain.Services;

/// <summary>
/// Shared image MIME-type/extension mapping. Unions the mapping tables
/// previously duplicated across <c>CharacterEndpoints</c>,
/// <c>PersonEndpoints</c>, <c>StreamEndpoints</c>, <c>MetadataHarvestingService</c>,
/// and <c>CoverArtWorker</c>. Covers the image formats the Engine actually
/// downloads, caches, or streams as artwork.
/// </summary>
public static class MediaMimeTypes
{
    /// <summary>
    /// Resolves the MIME content-type for an image given its file name, path,
    /// or bare extension (with or without a leading dot). Unrecognized or
    /// missing extensions resolve to <c>application/octet-stream</c> — callers
    /// migrating from a call site that defaulted to <c>image/jpeg</c> should
    /// apply that fallback explicitly if they need to preserve it.
    /// </summary>
    public static string GetImageMimeType(string fileNameOrExtension)
    {
        ArgumentNullException.ThrowIfNull(fileNameOrExtension);

        return NormalizeExtension(fileNameOrExtension) switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".avif" => "image/avif",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Infers a lowercase file extension (including the leading dot) from either
    /// an image MIME content-type (e.g. <c>"image/png"</c>) or a URL/file path
    /// (e.g. <c>"https://example.com/cover.jpg?size=l"</c>). A value is treated
    /// as a content-type when it starts with <c>"image/"</c>; anything else is
    /// treated as a URL or path and its extension is extracted, stripping any
    /// query string or fragment first. Returns <c>null</c> when no extension
    /// can be determined — callers that previously defaulted to <c>.jpg</c>
    /// should apply that fallback explicitly.
    /// </summary>
    public static string? InferImageExtension(string? contentTypeOrUrl)
    {
        if (string.IsNullOrWhiteSpace(contentTypeOrUrl))
            return null;

        var trimmed = contentTypeOrUrl.Trim();

        if (trimmed.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/svg+xml" => ".svg",
                "image/bmp" => ".bmp",
                "image/avif" => ".avif",
                "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
                "image/jpeg" or "image/jpg" => ".jpg",
                _ => null,
            };
        }

        var withoutQueryOrFragment = trimmed.Split('?', '#')[0];
        var extension = Uri.TryCreate(withoutQueryOrFragment, UriKind.Absolute, out var uri)
            ? Path.GetExtension(uri.AbsolutePath)
            : Path.GetExtension(withoutQueryOrFragment);

        return string.IsNullOrWhiteSpace(extension) ? null : extension.ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a file name, path, or bare extension down to a lowercase
    /// extension including the leading dot (e.g. <c>"COVER.PNG"</c> and
    /// <c>".png"</c> both normalize to <c>".png"</c>). Returns an empty string
    /// when no extension is present.
    /// </summary>
    private static string NormalizeExtension(string fileNameOrExtension)
    {
        var trimmed = fileNameOrExtension.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var looksLikeBareExtension = trimmed.StartsWith('.')
            && !trimmed.Contains('/')
            && !trimmed.Contains('\\');

        var extension = looksLikeBareExtension ? trimmed : Path.GetExtension(trimmed);
        return extension.ToLowerInvariant();
    }
}
