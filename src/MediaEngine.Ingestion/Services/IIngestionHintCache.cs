namespace MediaEngine.Ingestion.Services;

/// <summary>
/// In-memory cache of folder-level metadata hints for sibling-aware ingestion.
/// When the first file in a source folder is successfully hydrated, its resolved
/// metadata becomes a hint for subsequent siblings, reducing redundant Stage 1 lookups.
/// </summary>
public interface IIngestionHintCache
{
    /// <summary>
    /// Attempts to retrieve a non-expired hint for the given folder path.
    /// Returns false if no hint exists or the hint has expired.
    /// </summary>
    bool TryGetHint(string folderPath, out Models.FolderHint? hint);

    /// <summary>
    /// Stores a folder hint. Overwrites any existing hint for the same folder.
    /// </summary>
    void SetHint(string folderPath, Models.FolderHint hint);

    /// <summary>
    /// Removes the hint for a specific folder (e.g. when monitoring stops).
    /// </summary>
    void InvalidateFolder(string folderPath);

    /// <summary>
    /// Removes all expired hints. Called periodically to prevent memory growth.
    /// </summary>
    void PurgeExpired();
}
