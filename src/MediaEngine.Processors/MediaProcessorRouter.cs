using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors;

/// <summary>
/// Thread-safe libraryItem that routes files to the highest-priority processor
/// that claims them, falling back to a generic processor when no specific match
/// is found.
///
/// ──────────────────────────────────────────────────────────────────
/// Dispatch algorithm (spec: Phase 5 – Format Fallback)
/// ──────────────────────────────────────────────────────────────────
///  1. On each <see cref="ProcessAsync"/> call, the list of registered processors
///     is iterated in descending <see cref="IMediaProcessor.Priority"/> order.
///  2. The first processor whose <see cref="IMediaProcessor.CanProcess"/> returns
///     <see langword="true"/> is selected.
///  3. If no specific processor matches, the generic fallback
///     (registered at <see cref="int.MinValue"/> priority) is used
///     unconditionally — it does not call <c>CanProcess</c>.
///
/// ──────────────────────────────────────────────────────────────────
/// Concurrency (spec: Phase 5 – Processor LibraryItem)
/// ──────────────────────────────────────────────────────────────────
///  A <see cref="SemaphoreSlim"/> with a configurable <c>maxDegreeOfParallelism</c>
///  cap limits simultaneous <c>ProcessAsync</c> executions to prevent
///  unbounded memory use when large ingestion batches arrive.
///  Default cap = <see cref="Environment.ProcessorCount"/>.
/// </summary>
public sealed class MediaProcessorRouter : IProcessorRouter, IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly List<IMediaProcessor> _processors = [];
    // Cached sorted view; rebuilt lazily when _dirty is true.
    private IMediaProcessor[]? _sorted;
    private bool _dirty = true;
    private bool _disposed;

    /// <param name="maxDegreeOfParallelism">
    /// Maximum number of concurrent <see cref="ProcessAsync"/> invocations.
    /// Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </param>
    public MediaProcessorRouter(int maxDegreeOfParallelism = 0)
    {
        int cap = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Environment.ProcessorCount;
        _semaphore = new SemaphoreSlim(cap, cap);
    }

    // -------------------------------------------------------------------------
    // IProcessorRouter
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Register(IMediaProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        lock (_processors)
        {
            _processors.Add(processor);
            _dirty = true;
        }
    }

    /// <inheritdoc/>
    public IMediaProcessor? Resolve(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        IMediaProcessor[] sorted = GetSorted();

        // Walk from highest to lowest priority.
        // Skip the last entry (generic fallback, int.MinValue) during the
        // specific-match pass — it is tried unconditionally below.
        IMediaProcessor? fallback = null;
        foreach (var processor in sorted)
        {
            if (processor.Priority == int.MinValue)
            {
                // Record the fallback but don't call CanProcess on it.
                fallback ??= processor;
                continue;
            }

            if (processor.CanProcess(filePath))
                return processor;
        }

        return fallback;
    }

    /// <inheritdoc/>
    public async Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var processor = Resolve(filePath)
            ?? throw new InvalidOperationException(
                "No processor registered. Register at least a GenericFileProcessor as fallback.");

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await processor.ProcessAsync(filePath, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a stable sorted snapshot; rebuilds only when <see cref="_dirty"/> is set.
    /// </summary>
    private IMediaProcessor[] GetSorted()
    {
        // Fast path: no lock needed if already sorted and not dirty.
        if (!_dirty && _sorted is not null)
            return _sorted;

        lock (_processors)
        {
            if (!_dirty && _sorted is not null)
                return _sorted;

            // Sort descending by Priority so highest-priority processors are tried first.
            _sorted = [.. _processors.OrderByDescending(p => p.Priority)];
            _dirty  = false;
            return _sorted;
        }
    }
}
