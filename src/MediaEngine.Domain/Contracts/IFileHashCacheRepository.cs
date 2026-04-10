namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Caches SHA-256 hashes of files on disk keyed by <c>(absolute_path, size_bytes, mtime_utc)</c>
/// so the initial sweep and re-sweeps of a library don't re-hash files that haven't
/// changed. Backed by the <c>file_hash_cache</c> table (migration M-085).
///
/// Side-by-side-with-Plex plan §M — "Initial sweep hashes everything up front ...
/// with a persistent (path, size, mtime → sha256) cache, so move detection works
/// correctly from minute one and re-sweeps are cheap."
/// </summary>
public interface IFileHashCacheRepository
{
    /// <summary>
    /// Looks up a cached hash for <paramref name="absolutePath"/>. Returns
    /// <see langword="null"/> if there's no row, or if the stored
    /// <c>size_bytes</c> / <c>mtime_utc</c> don't match the caller-supplied
    /// values (stale row — caller should re-hash and upsert).
    /// </summary>
    Task<string?> TryGetAsync(
        string absolutePath,
        long sizeBytes,
        DateTimeOffset mtimeUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces the cached hash for <paramref name="absolutePath"/>.
    /// </summary>
    Task UpsertAsync(
        string absolutePath,
        long sizeBytes,
        DateTimeOffset mtimeUtc,
        string sha256,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the cache row for <paramref name="absolutePath"/>. Called when
    /// a file disappears from disk so stale entries don't accumulate.
    /// </summary>
    Task DeleteAsync(string absolutePath, CancellationToken ct = default);
}
