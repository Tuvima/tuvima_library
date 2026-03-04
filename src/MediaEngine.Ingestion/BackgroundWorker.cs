using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MediaEngine.Ingestion.Contracts;

namespace MediaEngine.Ingestion;

/// <summary>
/// Bounded-concurrency task queue that limits the number of simultaneously
/// executing ingestion pipeline jobs to prevent I/O saturation.
///
/// ──────────────────────────────────────────────────────────────────
/// Concurrency model
/// ──────────────────────────────────────────────────────────────────
///  • A <see cref="Channel{T}"/> holds queued work items with back-pressure
///    (<c>FullMode=Wait</c>) so the debounce queue cannot flood the pipeline.
///  • A <see cref="SemaphoreSlim"/> caps the number of items that run
///    concurrently.  The default is <c>Environment.ProcessorCount</c>.
///  • Each dequeued item acquires the semaphore, runs its handler in a
///    fire-and-forget <c>Task.Run</c>, then releases the semaphore.
///  • <see cref="DrainAsync"/> completes the channel writer, waits for the
///    consumer loop to exit, then waits for the in-flight counter to reach 0.
///
/// Spec: Phase 7 – Interfaces § IBackgroundWorker; Scalability § Resource Semaphores.
/// </summary>
public sealed class BackgroundWorker : IBackgroundWorker, IAsyncDisposable
{
    // Box typed work items into an untyped channel record.
    private sealed record WorkItem(object Payload, Func<object, CancellationToken, Task> Handler);

    private readonly Channel<WorkItem> _channel;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<BackgroundWorker> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _consumerLoop;

    // Tracks items queued but not yet completed (queued + executing).
    private int _pendingCount;
    // Tracks items currently executing (semaphore held).
    private int _inFlight;
    private readonly TaskCompletionSource _drainedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="logger">Logger for unhandled handler exceptions.</param>
    /// <param name="maxConcurrency">
    /// Maximum simultaneous executions.  Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </param>
    /// <param name="queueCapacity">Maximum buffered-but-not-yet-executing items. Default 1 000.</param>
    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        int maxConcurrency = 0,
        int queueCapacity  = 1_000)
    {
        _logger = logger;

        int concurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
        _semaphore = new SemaphoreSlim(concurrency, concurrency);

        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        _consumerLoop = Task.Run(ConsumeLoopAsync);
    }

    // -------------------------------------------------------------------------
    // IBackgroundWorker
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <inheritdoc/>
    public async ValueTask EnqueueAsync<T>(
        T workItem,
        Func<T, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // Box typed handler into untyped delegate to fit the channel's element type.
        var item = new WorkItem(
            workItem!,
            (obj, token) => handler((T)obj, token));

        Interlocked.Increment(ref _pendingCount);

        try
        {
            await _channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DrainAsync(CancellationToken ct = default)
    {
        // Signal no more items will be enqueued.
        _channel.Writer.TryComplete();

        // Wait for the consumer loop (reads channel until empty + complete).
        await _consumerLoop.ConfigureAwait(false);

        // Wait for all in-flight executions to finish.
        if (Volatile.Read(ref _inFlight) > 0)
            await _drainedTcs.Task.ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Consumer loop
    // -------------------------------------------------------------------------

    private async Task ConsumeLoopAsync()
    {
        var ct = _shutdownCts.Token;

        await foreach (var item in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Increment(ref _inFlight);

            _ = Task.Run(async () =>
            {
                try
                {
                    await item.Handler(item.Payload, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Normal shutdown — swallow.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in background work item.");
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingCount);
                    _semaphore.Release();

                    if (Interlocked.Decrement(ref _inFlight) == 0)
                        _drainedTcs.TrySetResult();
                }
            }, CancellationToken.None); // don't cancel the fire-and-forget task itself
        }
    }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        _channel.Writer.TryComplete();
        _drainedTcs.TrySetResult();

        try { await _consumerLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        _semaphore.Dispose();
        _shutdownCts.Dispose();
    }
}
