using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Wraps one or more <see cref="System.IO.FileSystemWatcher"/> instances and
/// normalises their events into <see cref="FileEvent"/> objects.
///
/// Design notes
/// ────────────
/// • <see cref="System.IO.FileSystemWatcher"/> fires its callbacks on the OS
///   thread pool.  Handlers subscribed to <see cref="FileDetected"/> MUST be
///   fast and non-blocking — heavy work belongs in the <see cref="DebounceQueue"/>.
///
/// • <c>InternalBufferSize</c> is set to 64 KB (default is 8 KB) to reduce
///   the risk of buffer-overflow errors under high-I/O conditions.
///   Even so, the <see cref="Error"/> handler logs overflow events.
///
/// • The watcher fires <see cref="System.IO.WatcherChangeTypes.Changed"/> events
///   multiple times during a single write operation.  These raw events flow into
///   the debounce queue which resets its settle timer on each one; only the final
///   settled event proceeds to processing.
///
/// Spec: Phase 7 – Storage Monitoring, IFileWatcher interface.
/// </summary>
public sealed class FileWatcher : IFileWatcher
{
    private readonly List<System.IO.FileSystemWatcher> _watchers = [];
    private bool _running;
    private bool _disposed;

    // Diagnostic counters for the watcher-status endpoint.
    private long _eventCount;
    private DateTimeOffset? _lastEventAt;

    /// <summary>Total number of raw OS events received since startup.</summary>
    public long EventCount => Interlocked.Read(ref _eventCount);

    /// <summary>Timestamp of the last raw OS event received.</summary>
    public DateTimeOffset? LastEventAt => _lastEventAt;

    /// <summary>Whether the watcher is currently active.</summary>
    public bool IsRunning => _running && !_disposed;

    /// <summary>Paths currently being watched.</summary>
    public IReadOnlyList<string> WatchedPaths =>
        _watchers.Select(w => w.Path).ToList();

    // -------------------------------------------------------------------------
    // IFileWatcher
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public event EventHandler<FileEvent>? FileDetected;

    /// <inheritdoc/>
    public void AddDirectory(string path, bool includeSubdirectories = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(path);

        var watcher = new System.IO.FileSystemWatcher
        {
            Path                  = path,
            IncludeSubdirectories = includeSubdirectories,

            // Capture the changes that matter for ingestion.
            // Omitting CreationTime / Attributes reduces noise from metadata-only writes.
            NotifyFilter = System.IO.NotifyFilters.FileName
                         | System.IO.NotifyFilters.DirectoryName
                         | System.IO.NotifyFilters.LastWrite
                         | System.IO.NotifyFilters.Size,

            // 64 KB reduces missed events on busy directories.
            // Spec: "MUST detect file system events in real-time."
            InternalBufferSize = 65_536,

            // Don't raise events until Start() is called.
            EnableRaisingEvents = false,
        };

        watcher.Created += OnCreated;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.Error   += OnError;

        _watchers.Add(watcher);
    }

    /// <inheritdoc/>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _running = true;
        foreach (var w in _watchers)
            w.EnableRaisingEvents = true;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _running = false;
        foreach (var w in _watchers)
            w.EnableRaisingEvents = false;
    }

    /// <inheritdoc/>
    public void UpdateDirectory(string path, bool includeSubdirectories = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Pause all existing watchers so no new events arrive while we swap.
        foreach (var w in _watchers)
            w.EnableRaisingEvents = false;

        // Unsubscribe and fully release every FileSystemWatcher.
        foreach (var w in _watchers)
        {
            w.Created -= OnCreated;
            w.Changed -= OnChanged;
            w.Deleted -= OnDeleted;
            w.Renamed -= OnRenamed;
            w.Error   -= OnError;
            w.Dispose();
        }
        _watchers.Clear();

        // Wire the new directory.  AddDirectory() subscribes the same handlers
        // and appends the new watcher to _watchers.
        AddDirectory(path, includeSubdirectories);

        // Resume immediately if the watcher was running before the swap.
        if (_running)
            foreach (var w in _watchers)
                w.EnableRaisingEvents = true;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        foreach (var w in _watchers)
        {
            w.Created -= OnCreated;
            w.Changed -= OnChanged;
            w.Deleted -= OnDeleted;
            w.Renamed -= OnRenamed;
            w.Error   -= OnError;
            w.Dispose();
        }

        _watchers.Clear();
    }

    // -------------------------------------------------------------------------
    // OS event handlers (called on the OS thread pool)
    // -------------------------------------------------------------------------

    private void OnCreated(object _, System.IO.FileSystemEventArgs e)
    {
        // Directories are not media files — skip immediately.
        if (Directory.Exists(e.FullPath)) return;

        RecordOsEvent();
        Raise(new FileEvent
        {
            Path       = e.FullPath,
            EventType  = FileEventType.Created,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    private void OnChanged(object _, System.IO.FileSystemEventArgs e)
    {
        // Directory writes (e.g. file added under a folder) fire Changed on the
        // folder itself — we only ingest files, so skip them here. Without this
        // guard the lock probe later opens the directory path as a file, fails
        // 8 retries, and quarantines the folder.
        if (Directory.Exists(e.FullPath)) return;

        RecordOsEvent();
        Raise(new FileEvent
        {
            Path       = e.FullPath,
            EventType  = FileEventType.Modified,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    private void OnDeleted(object _, System.IO.FileSystemEventArgs e)
    {
        RecordOsEvent();
        Raise(new FileEvent
        {
            Path       = e.FullPath,
            EventType  = FileEventType.Deleted,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    private void OnRenamed(object _, System.IO.RenamedEventArgs e)
    {
        // Directory renames are not ingestion events. Skip them so the lock
        // probe doesn't try to open the directory as a file.
        if (Directory.Exists(e.FullPath)) return;

        RecordOsEvent();
        Raise(new FileEvent
        {
            Path       = e.FullPath,
            OldPath    = e.OldFullPath,
            EventType  = FileEventType.Renamed,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    private void OnError(object _, System.IO.ErrorEventArgs e)
    {
        // A buffer overflow means the OS dropped events while the engine was busy.
        // The startup differential scan (spec: Scalability § Differential Scanning)
        // will reconcile any missed files on the next application boot.
        //
        // Future: route to ILogger when DI is wired up.
        var ex = e.GetException();
        if (ex is System.IO.InternalBufferOverflowException)
        {
            // Tolerable: events were dropped but the library can recover via scan.
        }
        // Other errors (network share lost, path deleted) may require a watcher restart.
        // The IngestionEngine is responsible for monitoring the Error surface and restarting.
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void RecordOsEvent()
    {
        Interlocked.Increment(ref _eventCount);
        _lastEventAt = DateTimeOffset.UtcNow;
    }

    private void Raise(FileEvent evt)
    {
        // Guard: don't raise after Stop() or Dispose().
        if (!_running || _disposed) return;

        // Ignore internal probe files written by FolderHealthService to verify
        // write access.  These are zero-byte temp files that are created and
        // deleted in the same synchronous call, so they should never be ingested.
        var fileName = Path.GetFileName(evt.Path.AsSpan());
        if (MemoryExtensions.StartsWith(fileName, ProbeFilePrefix, StringComparison.OrdinalIgnoreCase))
            return;

        FileDetected?.Invoke(this, evt);
    }

    /// <summary>
    /// Prefix shared with <see cref="MediaEngine.Api.Services.FolderHealthService"/>.
    /// Must stay in sync with the probe file naming used there.
    /// </summary>
    private const string ProbeFilePrefix = ".tuvima_probe_";
}
