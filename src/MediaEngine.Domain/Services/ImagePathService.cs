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

    // ── Per-file image paths (Plex / Jellyfin / ABS conventions) ──────────
    //
    // Side-by-side-with-Plex plan §D. Per-work imagery lives next to the
    // media file, in one of two layouts:
    //
    //   • Dedicated folder — the media file is alone in its folder
    //     (the Plex/Jellyfin convention created by the new templates).
    //     Artwork uses folder-based names: poster.jpg, fanart.jpg, etc.
    //
    //   • Shared folder    — the media file shares its folder with other
    //     media files (the messy `F:\Mess\bladerunner.mkv` case).
    //     Artwork uses filename-based names: bladerunner-poster.jpg, etc.
    //     Both Plex and Jellyfin natively read this format.
    //
    // The decision is made per file by scanning the parent directory for
    // sibling media files. The result is a small value type so callers can
    // resolve the scope once and pass it to multiple path methods.

    /// <summary>
    /// Resolves the artwork scope (dedicated vs shared) for a media file's
    /// parent folder by scanning for sibling media files. Empty or missing
    /// parent folders default to <see cref="MediaFileArtScope.Dedicated"/>.
    /// </summary>
    public static MediaFileArtScope GetMediaFileArtScope(string mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
            return MediaFileArtScope.Dedicated;

        var dir = Path.GetDirectoryName(mediaFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return MediaFileArtScope.Dedicated;

        int siblingCount = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (string.Equals(file, mediaFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsMediaExtension(Path.GetExtension(file))) continue;

                siblingCount++;
                if (siblingCount > 0) break; // any sibling makes it shared
            }
        }
        catch
        {
            // Permissions / IO error — assume dedicated and let callers proceed.
            return MediaFileArtScope.Dedicated;
        }

        return siblingCount > 0 ? MediaFileArtScope.Shared : MediaFileArtScope.Dedicated;
    }

    /// <summary>
    /// Returns the canonical poster path next to the media file. In a
    /// dedicated folder this is <c>poster.jpg</c>; in a shared folder this
    /// is <c>{basename}-poster.jpg</c>. Read by Plex and Jellyfin natively.
    /// </summary>
    public static string GetMediaFilePosterPath(string mediaFilePath)
        => BuildSiblingPath(mediaFilePath, "poster", ".jpg");

    /// <summary>
    /// Returns the canonical backdrop / fanart path next to the media file.
    /// </summary>
    public static string GetMediaFileFanartPath(string mediaFilePath)
        => BuildSiblingPath(mediaFilePath, "fanart", ".jpg");

    /// <summary>
    /// Returns the canonical clear-logo path next to the media file (PNG
    /// for transparency, the Plex/Jellyfin convention).
    /// </summary>
    public static string GetMediaFileLogoPath(string mediaFilePath)
        => BuildSiblingPath(mediaFilePath, "logo", ".png");

    /// <summary>
    /// Returns the canonical banner path next to the media file.
    /// </summary>
    public static string GetMediaFileBannerPath(string mediaFilePath)
        => BuildSiblingPath(mediaFilePath, "banner", ".jpg");

    /// <summary>
    /// Returns the canonical 200px-wide thumbnail path next to the media
    /// file. Tuvima-specific (other media managers ignore it) — used by
    /// the Dashboard for fast list rendering.
    /// </summary>
    public static string GetMediaFileThumbPath(string mediaFilePath)
        => BuildSiblingPath(mediaFilePath, "poster-thumb", ".jpg");

    private static string BuildSiblingPath(string mediaFilePath, string artKind, string extension)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
            throw new ArgumentException("Media file path is required.", nameof(mediaFilePath));

        var dir = Path.GetDirectoryName(mediaFilePath) ?? ".";
        var scope = GetMediaFileArtScope(mediaFilePath);

        if (scope == MediaFileArtScope.Dedicated)
        {
            return Path.Combine(dir, artKind + extension);
        }

        // Shared folder — namespace the artwork by the media file's basename.
        var basename = Path.GetFileNameWithoutExtension(mediaFilePath);
        return Path.Combine(dir, $"{basename}-{artKind}{extension}");
    }

    private static bool IsMediaExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        // Lowercased single-shot match. Covers the formats CLAUDE.md §3.16
        // lists as supported library types. Conservative — anything missing
        // simply means the folder is treated as dedicated, which is safe.
        return extension.ToLowerInvariant() switch
        {
            ".mkv" or ".mp4" or ".m4v" or ".avi" or ".mov" or ".wmv" or ".webm" or ".ts" => true,
            ".m4b" or ".mp3" or ".flac" or ".m4a" or ".ogg" or ".opus" or ".wav" or ".aac" => true,
            ".epub" or ".pdf" => true,
            ".cbz" or ".cbr" or ".cb7" => true,
            _ => false,
        };
    }
}

/// <summary>
/// Whether a media file's parent folder is dedicated to that file (Plex
/// dedicated-folder convention) or shared with sibling media files (the
/// flat-dump case). Drives the artwork naming scheme used by
/// <see cref="ImagePathService"/>.
/// </summary>
public enum MediaFileArtScope
{
    /// <summary>
    /// The media file is alone in its folder. Artwork uses
    /// folder-based names (<c>poster.jpg</c>, <c>fanart.jpg</c>).
    /// </summary>
    Dedicated,

    /// <summary>
    /// The media file shares its folder with other media files. Artwork
    /// uses filename-based names (<c>{basename}-poster.jpg</c>) so it
    /// doesn't collide across siblings.
    /// </summary>
    Shared,
}
