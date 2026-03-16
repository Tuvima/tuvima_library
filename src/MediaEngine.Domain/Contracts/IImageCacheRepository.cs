namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the image content hash cache.
///
/// The <c>image_cache</c> table tracks downloaded images (cover art, headshots)
/// by their SHA-256 content hash.  When the pipeline downloads an image, it:
/// <list type="number">
///   <item>Computes a SHA-256 hash of the downloaded bytes.</item>
///   <item>Checks this cache for an existing entry with that hash.</item>
///   <item>If found — skips the download and uses the cached file path.</item>
///   <item>If not found — saves to disk and inserts a cache entry.</item>
/// </list>
///
/// This ensures no redundant re-downloads when the same image URL appears
/// across multiple entities.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface IImageCacheRepository
{
    /// <summary>
    /// Looks up a cached image by its SHA-256 content hash.
    /// Returns the file path if found, or <c>null</c> if the hash is not cached.
    /// </summary>
    /// <param name="contentHash">The SHA-256 hex string of the image content.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> FindByHashAsync(string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new image cache entry.
    /// If an entry with the same hash already exists, this is a no-op.
    /// </summary>
    /// <param name="contentHash">The SHA-256 hex string of the image content.</param>
    /// <param name="filePath">The absolute path where the image was saved on disk.</param>
    /// <param name="sourceUrl">The original URL the image was downloaded from (nullable).</param>
    /// <param name="ct">Cancellation token.</param>
    Task InsertAsync(
        string contentHash,
        string filePath,
        string? sourceUrl = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if the cached entry for the given content hash was
    /// flagged as a user-supplied override (i.e. manually set cover art).
    /// </summary>
    Task<bool> IsUserOverrideAsync(string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Looks up a cached image by its source URL.
    /// Returns the file path of the first matching entry, or <c>null</c> if none found.
    /// Useful for short-circuiting downloads when the same URL has already been fetched.
    /// </summary>
    Task<string?> FindBySourceUrlAsync(string sourceUrl, CancellationToken ct = default);

    /// <summary>
    /// Sets or clears the user-override flag on a cached image entry.
    /// User-override images are never replaced by automated provider downloads.
    /// </summary>
    Task SetUserOverrideAsync(string contentHash, bool isOverride, CancellationToken ct = default);
}
