namespace MediaEngine.Domain.Services;

/// <summary>
/// Centralizes image path resolution. All images live under {libraryRoot}/.data/images/
/// organized by entity type and QID (or provisional GUID).
///
/// Each asset gets its own subdirectory (first 12 hex chars of asset GUID) so that
/// multiple editions sharing a QID (e.g., two audiobook narrations of the same title)
/// each retain their own cover art.
///
/// Directory layout:
/// <code>
/// {libraryRoot}/
/// └── .data/
///     └── images/
///         ├── works/
///         │   ├── {QID}/
///         │   │   ├── {assetId12}/       ← ebook edition
///         │   │   │   ├── cover.jpg
///         │   │   │   ├── cover_thumb.jpg
///         │   │   │   └── hero.jpg
///         │   │   └── {assetId12}/       ← audiobook edition
///         │   │       ├── cover.jpg
///         │   │       ├── cover_thumb.jpg
///         │   │       └── hero.jpg
///         │   └── _pending/
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
    /// Gets the directory for a work's images, using QID + asset slot if available,
    /// else pending GUID slot. Each asset gets its own subdirectory (first 12 hex
    /// chars of the asset GUID) to avoid collisions when multiple editions share a QID.
    /// </summary>
    public string GetWorkImageDir(string? wikidataQid, Guid assetId)
    {
        var assetSlot = assetId.ToString("N")[..12];
        if (!string.IsNullOrEmpty(wikidataQid) && !wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_imagesRoot, "works", wikidataQid, assetSlot);

        // Migration: rename legacy _provisional to _pending on first access
        var legacySlot = Path.Combine(_imagesRoot, "works", "_provisional", assetSlot);
        var pendingSlot = Path.Combine(_imagesRoot, "works", "_pending", assetSlot);
        if (Directory.Exists(legacySlot) && !Directory.Exists(pendingSlot))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pendingSlot)!);
            Directory.Move(legacySlot, pendingSlot);
        }

        return Path.Combine(_imagesRoot, "works", "_pending", assetSlot);
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
    /// Promotes a pending asset's images to QID-keyed location.
    /// Call this when an asset gets a confirmed Wikidata QID.
    /// Moves from <c>_pending/{assetId12}/</c> to <c>{QID}/{assetId12}/</c>.
    /// </summary>
    public void PromoteToQid(Guid assetId, string wikidataQid)
    {
        var assetSlot = assetId.ToString("N")[..12];

        // Migration: rename legacy _provisional to _pending before promoting
        var legacyDir = Path.Combine(_imagesRoot, "works", "_provisional", assetSlot);
        if (Directory.Exists(legacyDir))
        {
            var migrationTarget = Path.Combine(_imagesRoot, "works", "_pending", assetSlot);
            if (!Directory.Exists(migrationTarget))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(migrationTarget)!);
                Directory.Move(legacyDir, migrationTarget);
            }
        }

        var pendingDir = Path.Combine(_imagesRoot, "works", "_pending", assetSlot);
        if (!Directory.Exists(pendingDir)) return;

        var targetDir = Path.Combine(_imagesRoot, "works", wikidataQid, assetSlot);

        if (Directory.Exists(targetDir))
        {
            // Target already exists — merge files, do not overwrite existing
            foreach (var file in Directory.GetFiles(pendingDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Move(file, dest);
            }
            // Clean up empty pending dir
            try { Directory.Delete(pendingDir, recursive: false); } catch { /* best-effort */ }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(pendingDir, targetDir);
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
