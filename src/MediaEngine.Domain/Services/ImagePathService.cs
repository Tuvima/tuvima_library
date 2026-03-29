namespace MediaEngine.Domain.Services;

/// <summary>
/// Centralizes image path resolution. All images live under {libraryRoot}/.data/images/
/// organized by entity type and QID (or provisional GUID).
///
/// Cover art files are prefixed by media type so different editions sharing a QID
/// (e.g., ebook vs audiobook of the same title) each keep their own artwork.
///
/// Directory layout:
/// <code>
/// {libraryRoot}/
/// └── .data/
///     └── images/
///         ├── works/
///         │   ├── {QID}/                ← e.g., Q190306/
///         │   │   ├── book-cover.jpg
///         │   │   ├── book-hero.jpg
///         │   │   ├── audiobook-cover.jpg
///         │   │   └── audiobook-hero.jpg
///         │   └── _provisional/
///         │       └── {assetId12}/      ← No QID yet
///         │           ├── cover.jpg
///         │           └── hero.jpg
///         ├── people/
///         │   └── {QID}/
///         │       └── headshot.jpg
///         └── universes/
///             └── {QID}/
///                 └── backdrop.jpg
/// </code>
/// </summary>
public sealed class ImagePathService
{
    private readonly string _imagesRoot;

    public ImagePathService(string libraryRoot)
    {
        _imagesRoot = Path.Combine(libraryRoot, ".data", "images");
    }

    public string ImagesRoot => _imagesRoot;

    /// <summary>
    /// Gets the directory for a work's images, using QID if available, else provisional GUID slot.
    /// </summary>
    public string GetWorkImageDir(string? wikidataQid, Guid assetId)
    {
        if (!string.IsNullOrEmpty(wikidataQid) && !wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_imagesRoot, "works", wikidataQid);
        return Path.Combine(_imagesRoot, "works", "_provisional", assetId.ToString("N")[..12]);
    }

    /// <summary>Gets cover path for a work. Media-type-prefixed when QID is present.</summary>
    public string GetWorkCoverPath(string? wikidataQid, Guid assetId, string? mediaType = null)
    {
        var dir = GetWorkImageDir(wikidataQid, assetId);
        var prefix = GetMediaPrefix(wikidataQid, mediaType);
        return Path.Combine(dir, $"{prefix}cover.jpg");
    }

    /// <summary>Gets cover thumbnail path for a work. Media-type-prefixed when QID is present.</summary>
    public string GetWorkCoverThumbPath(string? wikidataQid, Guid assetId, string? mediaType = null)
    {
        var dir = GetWorkImageDir(wikidataQid, assetId);
        var prefix = GetMediaPrefix(wikidataQid, mediaType);
        return Path.Combine(dir, $"{prefix}cover_thumb.jpg");
    }

    /// <summary>Gets hero banner path for a work. Media-type-prefixed when QID is present.</summary>
    public string GetWorkHeroPath(string? wikidataQid, Guid assetId, string? mediaType = null)
    {
        var dir = GetWorkImageDir(wikidataQid, assetId);
        var prefix = GetMediaPrefix(wikidataQid, mediaType);
        return Path.Combine(dir, $"{prefix}hero.jpg");
    }

    /// <summary>Gets the directory for a person's images.</summary>
    public string GetPersonImageDir(string wikidataQid) =>
        Path.Combine(_imagesRoot, "people", wikidataQid);

    /// <summary>Gets the directory for a universe's images.</summary>
    public string GetUniverseImageDir(string wikidataQid) =>
        Path.Combine(_imagesRoot, "universes", wikidataQid);

    /// <summary>
    /// Promotes a provisional asset's images to QID-keyed location with media type prefix.
    /// Call this when an asset gets a confirmed Wikidata QID.
    /// Moves from <c>_provisional/{assetId12}/cover.jpg</c> to <c>{QID}/{mediaType}-cover.jpg</c>.
    /// </summary>
    public void PromoteToQid(Guid assetId, string wikidataQid, string? mediaType = null)
    {
        var provisionalDir = Path.Combine(_imagesRoot, "works", "_provisional", assetId.ToString("N")[..12]);
        if (!Directory.Exists(provisionalDir)) return;

        var qidDir = Path.Combine(_imagesRoot, "works", wikidataQid);
        Directory.CreateDirectory(qidDir);

        var prefix = MediaTypeToSlug(mediaType);
        var hasPrefix = !string.IsNullOrEmpty(prefix);

        foreach (var file in Directory.GetFiles(provisionalDir))
        {
            var fileName = Path.GetFileName(file);
            // Rename from plain name to media-type-prefixed name
            var destName = hasPrefix ? $"{prefix}-{fileName}" : fileName;
            var dest = Path.Combine(qidDir, destName);
            if (!File.Exists(dest))
                File.Move(file, dest);
        }

        // Clean up empty provisional dir
        try { Directory.Delete(provisionalDir, recursive: false); } catch { /* best-effort */ }
    }

    /// <summary>Ensures the directory containing the given file path exists.</summary>
    public static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Returns the media-type prefix for filenames (e.g. "book-").
    /// Returns empty string for provisional paths (no QID) so filenames stay plain.
    /// </summary>
    private static string GetMediaPrefix(string? wikidataQid, string? mediaType)
    {
        // Provisional directories are already per-asset — no prefix needed
        if (string.IsNullOrEmpty(wikidataQid) || wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return "";

        var slug = MediaTypeToSlug(mediaType);
        return string.IsNullOrEmpty(slug) ? "" : $"{slug}-";
    }

    /// <summary>Converts a media type name to a lowercase slug for filename prefixing.</summary>
    public static string? MediaTypeToSlug(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "books"      => "book",
        "audiobooks" => "audiobook",
        "movies"     => "movie",
        "tv"         => "tv",
        "music"      => "music",
        "comics"     => "comic",
        "podcasts"   => "podcast",
        null or ""   => null,
        var other    => other.ToLowerInvariant().Replace(" ", "-"),
    };
}
