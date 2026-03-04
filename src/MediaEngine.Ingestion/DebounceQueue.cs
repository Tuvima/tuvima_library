using System.Collections.Concurrent;
using System.Threading.Channels;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Receives raw <see cref="FileEvent"/> objects from <see cref="FileWatcher"/>,
/// coalesces rapid-fire OS events per file path, and only emits an
/// <see cref="IngestionCandidate"/> once the file is confirmed to be no longer
/// actively written to.
///
/// ──────────────────────────────────────────────────────────────────
/// Pipeline per file path
/// ──────────────────────────────────────────────────────────────────
///
///  FileWatcher                DebounceQueue                Consumer
///  ───────────                ─────────────                ────────
///  OnCreated ──► Enqueue ──► [settle timer starts]
///  OnChanged ──► Enqueue ──► [settle timer RESETS]          ...
///  OnChanged ──► Enqueue ──► [settle timer RESETS]          ...
///                                      │ SettleDelay elapsed
///                             [file-lock probe: attempt 0]
///                             [IOException → backoff × 2]
///                             [file-lock probe: attempt 1]
///                             [success]
///                                      │
///                              Channel.WriteAsync ──────► ReadAsync
///
/// ──────────────────────────────────────────────────────────────────
/// File-lock probe
/// ──────────────────────────────────────────────────────────────────
/// Opens the file with:
///   FileAccess.Read + FileShare.Read
///
/// This open FAILS while a writing process holds the file with an
/// exclusive lock (e.g. Windows Explorer copy, wget, torrent client).
/// It SUCCEEDS once the writer closes its handle, allowing reads but
/// no concurrent writers.
///
/// Spec: Phase 7 – Invariants § Debounced Ingestion.
/// Spec: Phase 7 – Failure Handling § Lock Contention (exponential backoff).
/// </summary>
public sealed class DebounceQueue : IDisposable
{
    private readonly DebounceOptions _options;
    private readonly Channel<IngestionCandidate> _channel;
    private readonly CancellationTokenSource _shutdownCts = new();

    // Keyed by normalised, lower-case absolute path.
    // _latestEvents: the most recent event seen for that path.
    // _timers: the CTS that controls the current settle+probe task for that path.
    private readonly ConcurrentDictionary<string, FileEvent>
        _latestEvents = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, CancellationTokenSource>
        _timers = new(StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public DebounceQueue(DebounceOptions? options = null)
    {
        _options = options ?? new DebounceOptions();

        _channel = Channel.CreateBounded<IngestionCandidate>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            // Multiple FileWatcher threads may write; multiple ingestion consumers may read.
            SingleWriter                = false,
            SingleReader                = false,
            FullMode                    = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// The channel reader from which the ingestion engine dequeues
    /// <see cref="IngestionCandidate"/> instances that are ready for processing.
    /// </summary>
    public ChannelReader<IngestionCandidate> Reader => _channel.Reader;

    /// <summary>
    /// Accepts a raw file event and schedules (or resets) the settle timer for
    /// the file at <see cref="FileEvent.Path"/>.
    ///
    /// Thread-safe — may be called concurrently from multiple OS event callbacks.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    public void Enqueue(FileEvent fileEvent)
    {
        ObjectDisposedException.ThrowIf(_shutdownCts.IsCancellationRequested, this);
        ArgumentNullException.ThrowIfNull(fileEvent);

        var key = NormalizePath(fileEvent.Path);

        // Store the latest known event for this path (overwriting any earlier version).
        _latestEvents[key] = fileEvent;

        // Cancel the existing settle/probe task for this path and start a fresh one.
        // This is the core debounce reset: every new event restarts the clock.
        CancelExistingTimer(key);

        // Create a linked CTS so shutdown also terminates this path's task.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        _timers[key] = cts;

        // Fire-and-forget: the settle task runs on the thread pool.
        _ = SettleAndProbeAsync(key, cts.Token);
    }

    /// <summary>
    /// Signals that no further events will be enqueued and completes the output channel.
    /// Existing settle tasks continue until they finish or are cancelled.
    /// </summary>
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        _shutdownCts.Cancel();

        // Cancel all outstanding settle/probe tasks.
        foreach (var cts in _timers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _timers.Clear();
        _latestEvents.Clear();

        _channel.Writer.TryComplete();
        _shutdownCts.Dispose();
    }

    // -------------------------------------------------------------------------
    // Settle + probe pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Step 1: wait for the settle delay; any new event for the same path will
    /// cancel this token and restart the clock via a new <see cref="SettleAndProbeAsync"/> task.
    /// </summary>
    private async Task SettleAndProbeAsync(string key, CancellationToken ct)
    {
        try
        {
            // The settle delay is the debounce window.
            // If the same file fires another event before this delay elapses, the
            // CancellationToken is cancelled and this task exits silently.
            await Task.Delay(_options.SettleDelay, ct);
        }
        catch (OperationCanceledException)
        {
            return; // Superseded by a newer event, or shutting down.
        }

        // Retrieve the latest event for this path.
        // (It may have been updated while we were waiting, which is fine — we
        //  process whichever event was the most recent when the timer fired.)
        if (!_latestEvents.TryGetValue(key, out var fileEvent))
            return;

        // Deleted files have no content to probe; promote them immediately.
        if (fileEvent.EventType == FileEventType.Deleted)
        {
            await PromoteAsync(key, IngestionCandidate.FromEvent(fileEvent), ct);
            return;
        }

        // Step 2: probe the file with exponential backoff.
        await ProbeWithBackoffAsync(key, fileEvent, ct);
    }

    /// <summary>
    /// Step 2: repeatedly attempt to open the file until either:
    ///   (a) the probe succeeds → the candidate is promoted to the output channel, or
    ///   (b) all attempts are exhausted → a failed candidate is written so the engine
    ///       can log the event and schedule a recovery scan.
    /// </summary>
    private async Task ProbeWithBackoffAsync(string key, FileEvent fileEvent, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= _options.MaxProbeAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            // Apply exponential backoff before retries (not before the first attempt).
            if (attempt > 0)
            {
                // Delay = ProbeInterval × 2^(attempt-1), capped at MaxProbeDelay.
                // Example with defaults (500 ms base, 30 s cap):
                //   attempt 1:  500 ms
                //   attempt 2:  1 s
                //   attempt 3:  2 s  …  attempt 7: 32 s → capped to 30 s
                var rawMs    = _options.ProbeInterval.TotalMilliseconds * Math.Pow(2, attempt - 1);
                var backoff  = TimeSpan.FromMilliseconds(rawMs);
                var capped   = backoff < _options.MaxProbeDelay ? backoff : _options.MaxProbeDelay;

                try
                {
                    await Task.Delay(capped, ct);
                }
                catch (OperationCanceledException)
                {
                    return; // Superseded or shutting down.
                }
            }

            // Guard: if a newer event for this path arrived while we were waiting,
            // a fresh SettleAndProbeAsync task is already running — abandon this one.
            if (!_latestEvents.TryGetValue(key, out var current)
                || current.OccurredAt != fileEvent.OccurredAt)
            {
                return;
            }

            if (IsFileAccessible(fileEvent.Path))
            {
                await PromoteAsync(key, IngestionCandidate.FromEvent(fileEvent), ct);
                return;
            }
        }

        // All probe attempts exhausted.
        // Emit a failed candidate so the ingestion engine can log it and the
        // recovery journal can schedule a retry on next startup.
        CleanupEntry(key);
        var failed = IngestionCandidate.Failed(
            fileEvent,
            $"File-lock probe exhausted after {_options.MaxProbeAttempts} attempts.");

        // TryWrite is best-effort here; if the channel is full and shutting down,
        // the failed event will be surfaced by the recovery journal instead.
        _channel.Writer.TryWrite(failed);
    }

    // -------------------------------------------------------------------------
    // File-lock probe
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to open the file with read access, allowing other readers but
    /// not concurrent writers.
    ///
    /// Why <c>FileShare.Read</c>:
    ///   Most write-intensive processes (file copy, download, torrent client)
    ///   open their destination file with exclusive or write-only sharing
    ///   (<c>FileShare.None</c> or <c>FileShare.Write</c>).  Attempting to open
    ///   the same file with <c>FileShare.Read</c> will be denied by the OS
    ///   until the writer closes its handle, which is the exact signal we need.
    ///
    /// Returns <see langword="false"/> when:
    ///   • The file no longer exists (deleted between events).
    ///   • Another process holds an exclusive or write-only lock.
    ///   • The caller lacks read permissions.
    /// </summary>
    private static bool IsFileAccessible(string path)
    {
        if (!File.Exists(path)) return false;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,          // minimal — we are not reading content here
                FileOptions.None);

            return true;
        }
        catch (IOException)               { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>Writes a ready candidate to the output channel.</summary>
    /// <remarks>
    /// Uses <c>_shutdownCts.Token</c> for the channel write instead of the per-path
    /// <paramref name="ct"/>, because <see cref="CleanupEntry"/> cancels the per-path
    /// CTS (which owns <paramref name="ct"/>) — writing with an already-cancelled
    /// token would silently drop every candidate.
    /// </remarks>
    private async Task PromoteAsync(
        string key,
        IngestionCandidate candidate,
        CancellationToken ct)
    {
        try
        {
            // Write FIRST, before cleaning up the timer that owns our token.
            // CleanupEntry cancels + disposes the per-path CTS, so we must use
            // the global shutdown token for the channel write.
            await _channel.Writer.WriteAsync(candidate, _shutdownCts.Token);
        }
        catch (OperationCanceledException) { /* shutting down — drop silently */ }
        catch (ChannelClosedException)     { /* shutting down — drop silently */ }

        // Clean up AFTER the write has completed so the per-path CTS is not
        // cancelled while the write is in progress.
        CleanupEntry(key);
    }

    private void CancelExistingTimer(string key)
    {
        if (_timers.TryRemove(key, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
    }

    private void CleanupEntry(string key)
    {
        _latestEvents.TryRemove(key, out _);
        CancelExistingTimer(key);
    }

    /// <summary>
    /// Returns a stable, case-insensitive key for the given file path.
    /// Normalises separators and removes trailing slashes so that
    /// <c>C:\Media\file.mkv</c> and <c>C:/media/file.mkv</c> map to the same entry.
    /// </summary>
    private static string NormalizePath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
}
