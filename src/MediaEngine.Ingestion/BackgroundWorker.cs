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
///  • A fixed set of owned consumer loops caps concurrent execution. The
///    default consumer count is <c>Environment.ProcessorCount</c>.
///  • <see cref="DrainAsync"/> completes the channel writer and waits for every
///    accepted item to finish.
///
/// Spec: Phase 7 – Interfaces § IBackgroundWorker; Scalability § Resource Semaphores.
/// </summary>
public sealed class BackgroundWorker : IBackgroundWorker, IAsyncDisposable
{
    // Box typed work items into an untyped channel record.
    private sealed record WorkItem(object Payload, Func<object, CancellationToken, Task> Handler);

    private readonly Channel<WorkItem> _channel;
    private readonly ILogger<BackgroundWorker> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task[] _consumerLoops;

    // Tracks items queued but not yet completed (queued + executing).
    private int _pendingCount;
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

        var consumerCount = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;

        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = consumerCount == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        _consumerLoops = new Task[consumerCount];
        for (var index = 0; index < _consumerLoops.Length; index++)
        {
            _consumerLoops[index] = ConsumeLoopAsync();
        }
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

        await Task.WhenAll(_consumerLoops).WaitAsync(ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Consumer loop
    // -------------------------------------------------------------------------

    private async Task ConsumeLoopAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await item.Handler(item.Payload, _shutdownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // Forced disposal after the graceful drain path was not used.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in background work item.");
            }
            finally
            {
                Interlocked.Decrement(ref _pendingCount);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _shutdownCts.Cancel();
        try
        {
            await Task.WhenAll(_consumerLoops).ConfigureAwait(false);
        }
        finally
        {
            _shutdownCts.Dispose();
        }
    }
}
