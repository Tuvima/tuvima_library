namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Diagnostic event emitted when the underlying file-system watcher reports an
/// error, such as an internal buffer overflow or an inaccessible watched path.
/// </summary>
public sealed record FileWatcherErrorEvent(
    string Kind,
    string Message,
    DateTimeOffset OccurredAt,
    bool IsBufferOverflow);
