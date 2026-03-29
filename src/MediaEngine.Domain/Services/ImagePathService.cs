namespace MediaEngine.Domain.Services;

/// <summary>
/// Centralizes image path resolution. All images live under {libraryRoot}/.data/images/
/// organized by entity type and QID (or provisional GUID).
///
/// Directory layout:
/// <code>
/// {libraryRoot}/
/// └── .data/
///     └── images/
///         ├── works/
///         │   ├── {QID}/           ← e.g., Q190306/
///         │   │   ├── cover.jpg
///         │   │   └── hero.jpg
///         │   └── _provisional/
///         │       └── {assetId12}/  ← No QID yet (first 12 hex chars of asset GUID)
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
    /// The provisional slot uses the first 12 hex characters of the asset GUID.
    /// </summary>
    public string GetWorkImageDir(string? wikidataQid, Guid assetId)
    {
        if (!string.IsNullOrEmpty(wikidataQid) && !wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_imagesRoot, "works", wikidataQid);
        return Path.Combine(_imagesRoot, "works", "_provisional", assetId.ToString("N")[..12]);
    }

    /// <summary>Gets cover.jpg path for a work.</summary>
    public string GetWorkCoverPath(string? wikidataQid, Guid assetId) =>
        Path.Combine(GetWorkImageDir(wikidataQid, assetId), "cover.jpg");

    /// <summary>Gets cover_thumb.jpg path for a work.</summary>
    public string GetWorkCoverThumbPath(string? wikidataQid, Guid assetId) =>
        Path.Combine(GetWorkImageDir(wikidataQid, assetId), "cover_thumb.jpg");

    /// <summary>Gets hero.jpg path for a work.</summary>
    public string GetWorkHeroPath(string? wikidataQid, Guid assetId) =>
        Path.Combine(GetWorkImageDir(wikidataQid, assetId), "hero.jpg");

    /// <summary>Gets the directory for a person's images.</summary>
    public string GetPersonImageDir(string wikidataQid) =>
        Path.Combine(_imagesRoot, "people", wikidataQid);

    /// <summary>Gets the directory for a universe's images.</summary>
    public string GetUniverseImageDir(string wikidataQid) =>
        Path.Combine(_imagesRoot, "universes", wikidataQid);

    /// <summary>
    /// Promotes a provisional asset's images to QID-keyed location.
    /// Call this when an asset gets a confirmed Wikidata QID.
    /// </summary>
    public void PromoteToQid(Guid assetId, string wikidataQid)
    {
        var provisionalDir = Path.Combine(_imagesRoot, "works", "_provisional", assetId.ToString("N")[..12]);
        if (!Directory.Exists(provisionalDir)) return;

        var qidDir = Path.Combine(_imagesRoot, "works", wikidataQid);
        Directory.CreateDirectory(qidDir);

        if (Directory.Exists(qidDir))
        {
            // QID dir already exists — merge files, do not overwrite existing
            foreach (var file in Directory.GetFiles(provisionalDir))
            {
                var dest = Path.Combine(qidDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Move(file, dest);
            }
            // Clean up empty provisional dir
            try { Directory.Delete(provisionalDir, recursive: false); } catch { /* best-effort */ }
        }
        else
        {
            Directory.Move(provisionalDir, qidDir);
        }
    }

    /// <summary>Ensures the directory containing the given file path exists.</summary>
    public static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
