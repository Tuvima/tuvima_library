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
///         │   │   │   ├── hero.jpg
///         │   │   │   ├── backdrop.jpg
///         │   │   │   ├── logo.png
///         │   │   │   └── banner.jpg
///         │   │   └── {assetId12}/       ← audiobook edition
///         │   │       ├── cover.jpg
///         │   │       ├── cover_thumb.jpg
///         │   │       ├── hero.jpg
///         │   │       ├── backdrop.jpg
///         │   │       ├── logo.png
///         │   │       └── banner.jpg
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

    /// <summary>Gets backdrop.jpg path for a work.</summary>
    public string GetWorkBackdropPath(string? wikidataQid, Guid assetId) =>
        Path.Combine(GetWorkImageDir(wikidataQid, assetId), "backdrop.jpg");

    /// <summary>Gets logo.png path for a work (transparent PNG).</summary>
    public string GetWorkLogoPath(string? wikidataQid, Guid assetId) =>
        Path.Combine(GetWorkImageDir(wikidataQid, assetId), "logo.png");

    /// <summary>Gets banner.jpg path for a work.</summary>
    public string GetWorkBannerPath(string? wikidataQid, Guid assetId) =>
        Path.Combine(GetWorkImageDir(wikidataQid, assetId), "banner.jpg");

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

    /// <summary>
    /// Sweeps any images already downloaded to the pending slot into the QID-keyed location.
    /// Call this at the start of Quick Hydration, before <see cref="CoverArtWorker"/> runs,
    /// so images downloaded during an earlier (pre-QID) pass become visible immediately.
    /// </summary>
    /// <returns>
    /// <c>true</c> if images were moved; <c>false</c> if there was nothing to move,
    /// the target already had files, or the QID was invalid.
    /// </returns>
    public bool SweepPendingToQid(Guid entityId, string? wikidataQid)
    {
        if (string.IsNullOrEmpty(wikidataQid) ||
            wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return false;

        var assetSlot  = entityId.ToString("N")[..12];
        var pendingDir = Path.Combine(_imagesRoot, "works", "_pending", assetSlot);

        if (!Directory.Exists(pendingDir))
            return false;

        var targetDir = Path.Combine(_imagesRoot, "works", wikidataQid, assetSlot);

        // If the target already has files, don't overwrite — the QID path is authoritative.
        if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir).Length > 0)
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            if (!Directory.Exists(targetDir))
            {
                // Fast path: atomic directory rename.
                Directory.Move(pendingDir, targetDir);
            }
            else
            {
                // Target dir exists but is empty — move files individually.
                foreach (var file in Directory.GetFiles(pendingDir))
                {
                    var dest = Path.Combine(targetDir, Path.GetFileName(file));
                    if (!File.Exists(dest))
                        File.Move(file, dest);
                }
                try { Directory.Delete(pendingDir, recursive: false); } catch { /* best-effort */ }
            }

            return true;
        }
        catch
        {
            // Non-critical — caller continues regardless.
            return false;
        }
    }

    /// <summary>
    /// Async wrapper around <see cref="SweepPendingToQid(Guid, string?)"/> for callers
    /// that prefer the Task-based API. The underlying I/O is synchronous (Directory.Move)
    /// and short, so this simply runs the work and returns a completed task.
    /// </summary>
    public Task SweepPendingToQidAsync(Guid entityId, string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SweepPendingToQid(entityId, qid);
        return Task.CompletedTask;
    }

    /// <summary>Ensures the directory containing the given file path exists.</summary>
    public static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
