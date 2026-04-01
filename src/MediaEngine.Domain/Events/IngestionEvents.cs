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

/// <summary>
/// Published periodically during an active ingestion run to report incremental progress.
///
/// SignalR method name: <c>"IngestionProgress"</c>
/// </summary>
/// <param name="CurrentFile">Short display name of the file currently being processed.</param>
/// <param name="ProcessedCount">Number of files processed so far in this run.</param>
/// <param name="TotalCount">Total files discovered for this run (0 if still scanning).</param>
/// <param name="Stage">
/// Human-readable stage label. One of:
/// <c>"Scanning"</c> | <c>"Hashing"</c> | <c>"Processing"</c> | <c>"Complete"</c>
/// </param>
public sealed record IngestionProgressEvent(
    string CurrentFile,
    int    ProcessedCount,
    int    TotalCount,
    string Stage);

/// <summary>
/// Published each time a file in an ingestion batch reaches a terminal state.
/// Carries running counters and an estimated time remaining based on elapsed throughput.
///
/// SignalR method name: <c>"BatchProgress"</c>
/// </summary>
/// <param name="BatchId">The ingestion batch identifier.</param>
/// <param name="FilesTotal">Total files in this batch.</param>
/// <param name="FilesProcessed">Files that have reached a terminal state.</param>
/// <param name="FilesIdentified">Successfully matched and identified.</param>
/// <param name="FilesReview">Placed in review queue.</param>
/// <param name="FilesNoMatch">No metadata match found.</param>
/// <param name="FilesFailed">Processing error.</param>
/// <param name="ProgressPercent">0–100 integer progress.</param>
/// <param name="EstimatedSecondsRemaining">Estimated seconds until batch completes, or null if not calculable.</param>
/// <param name="IsComplete">True when the batch has finished.</param>
/// <param name="RecentTitles">Display titles of the most recently processed files, or null.</param>
/// <param name="CurrentStage">Active pipeline stage name, or null.</param>
public sealed record BatchProgressEvent(
    Guid   BatchId,
    int    FilesTotal,
    int    FilesProcessed,
    int    FilesIdentified,
    int    FilesReview,
    int    FilesNoMatch,
    int    FilesFailed,
    int    ProgressPercent,
    int?   EstimatedSecondsRemaining,
    bool   IsComplete,
    IReadOnlyList<string>? RecentTitles = null,
    string? CurrentStage = null);
