namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Classifies the raw OS event reported by <see cref="System.IO.FileSystemWatcher"/>.
/// </summary>
public enum FileEventType
{
    /// <summary>A new file or directory appeared in a watched location.</summary>
    Created,

    /// <summary>An existing file's content or attributes changed.</summary>
    Modified,

    /// <summary>
    /// A file or directory was removed from a watched location.
    /// No file-lock probe is performed for this event type; the candidate
    /// is promoted after the settle delay.
    /// </summary>
    Deleted,

    /// <summary>A file or directory was renamed or moved within a watched tree.</summary>
    Renamed,
}
