using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Core coordinator for the file monitoring and staging pipeline.
/// Spec: Phase 7 – Interfaces § IIngestionEngine.
///
/// Wires together <see cref="IFileWatcher"/>, <see cref="DebounceQueue"/>,
/// and <see cref="IBackgroundWorker"/> into a managed lifecycle.
/// </summary>
public interface IIngestionEngine
{
    /// <summary>Starts the watcher and begins consuming the debounce queue.</summary>
    void Start();

    /// <summary>Drains in-flight operations and stops all background activity.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Simulates the ingestion of all files under <paramref name="rootPath"/>
    /// without executing any file system mutations.
    /// Spec: "MUST provide a simulation mode that reports intended file system
    ///        changes without executing them."
    /// </summary>
    Task<IReadOnlyList<PendingOperation>> DryRunAsync(
        string rootPath, CancellationToken ct = default);

    /// <summary>
    /// Scans a directory for existing files and feeds synthetic "Created" events
    /// into the debounce queue so they are processed through the normal pipeline.
    /// Called after a watch-folder hot-swap to pick up files that were already present.
    /// Duplicates are harmless — the hash-based duplicate check short-circuits them.
    /// </summary>
    void ScanDirectory(string directory, bool includeSubdirectories = true);

    /// <summary>
    /// Stops the FileSystemWatcher and clears the FSW event buffer without
    /// touching the debounce queue or its consumer loop.
    /// Used by the dev wipe operation so that seed files written immediately
    /// after the wipe are not detected as spurious FSW events — they are
    /// instead enqueued deterministically via <see cref="ScanDirectory"/>.
    /// </summary>
    void PauseWatcher();

    /// <summary>
    /// Restarts the FileSystemWatcher after a <see cref="PauseWatcher"/> call.
    /// Clears the per-path dedup tracking so that previously-seen file paths
    /// (wiped and re-seeded) can be enqueued again.
    /// </summary>
    void ResumeWatcher();
}

/// <summary>
/// A file system operation that the ingestion engine intends to perform,
/// returned by <see cref="IIngestionEngine.DryRunAsync"/>.
/// </summary>
public sealed class PendingOperation
{
    public required string SourcePath      { get; init; }
    public required string DestinationPath { get; init; }
    public required string OperationKind   { get; init; } // "Move", "Rename", "WriteTag", etc.
    public string?         Reason          { get; init; }
}
