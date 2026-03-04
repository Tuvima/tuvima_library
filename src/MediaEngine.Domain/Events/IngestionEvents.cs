namespace MediaEngine.Domain.Events;

/// <summary>Published when a valid file has been dequeued and is about to be hashed.</summary>
public sealed record IngestionStartedEvent(
    string FilePath,
    DateTimeOffset StartedAt);

/// <summary>Published after the SHA-256 hash has been computed for a file.</summary>
public sealed record IngestionHashedEvent(
    string FilePath,
    string ContentHash,
    long FileSizeBytes,
    TimeSpan Elapsed);

/// <summary>Published after a file has been successfully inserted into the media library.</summary>
public sealed record IngestionCompletedEvent(
    string FilePath,
    string MediaType,
    DateTimeOffset CompletedAt);

/// <summary>Published when a file cannot be ingested (lock timeout, corruption, or duplicate skip).</summary>
public sealed record IngestionFailedEvent(
    string FilePath,
    string Reason,
    DateTimeOffset FailedAt);

/// <summary>
/// Published via SignalR when the Watch Folder is updated at runtime — either on first
/// configuration or after the user changes the path in Settings.
/// </summary>
public sealed record WatchFolderActiveEvent(
    string WatchDirectory,
    DateTimeOffset ActivatedAt);
