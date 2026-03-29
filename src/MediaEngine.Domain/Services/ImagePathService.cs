namespace MediaEngine.Domain.Services;

/// <summary>
/// Centralizes image path resolution. All images live under {libraryRoot}/.data/images/
/// organized by entity type and QID (or provisional GUID).
///
/// Each asset gets its own subdirectory under the QID so that different editions
/// (e.g., ebook vs audiobook of the same title) each retain their own cover art.
///
/// Directory layout:
/// <code>
/// {libraryRoot}/
/// └── .data/
///     └── images/
///         ├── works/
///         │   ├── {QID}/
///         │   │   ├── {assetId12}/   ← e.g., Q190306/abc123def456/
///         │   │   │   ├── cover.jpg
///         │   │   │   └── hero.jpg
///         │   │   └── {assetId12}/   ← another edition of the same work
///         │   │       ├── cover.jpg
///         │   │       └── hero.jpg
///         │   └── _provisional/
///         │       └── {assetId12}/
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
    /// Each asset gets its own subdirectory (first 12 hex chars of the asset GUID) so that
    /// multiple editions sharing a QID (e.g., ebook + audiobook) each keep their own cover art.
    /// </summary>
    public string GetWorkImageDir(string? wikidataQid, Guid assetId)
    {
        var assetSlot = assetId.ToString("N")[..12];
        if (!string.IsNullOrEmpty(wikidataQid) && !wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_imagesRoot, "works", wikidataQid, assetSlot);
        return Path.Combine(_imagesRoot, "works", "_provisional", assetSlot);
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
    /// Moves from <c>_provisional/{assetId12}/</c> to <c>{QID}/{assetId12}/</c>.
    /// </summary>
    public void PromoteToQid(Guid assetId, string wikidataQid)
    {
        var assetSlot = assetId.ToString("N")[..12];
        var provisionalDir = Path.Combine(_imagesRoot, "works", "_provisional", assetSlot);
        if (!Directory.Exists(provisionalDir)) return;

        var targetDir = Path.Combine(_imagesRoot, "works", wikidataQid, assetSlot);

        if (Directory.Exists(targetDir))
        {
            // Target already exists — merge files, do not overwrite existing
            foreach (var file in Directory.GetFiles(provisionalDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Move(file, dest);
            }
            // Clean up empty provisional dir
            try { Directory.Delete(provisionalDir, recursive: false); } catch { /* best-effort */ }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(provisionalDir, targetDir);
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
