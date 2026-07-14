using System.Threading.Channels;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Owns a bounded channel and a fixed set of consumer loops. Queue admission
/// applies backpressure and every accepted item remains owned until its handler
/// completes, fails, or the supplied execution token is cancelled.
/// </summary>
internal sealed class BoundedChannelExecutor<T>
{
    private readonly Channel<T> _channel;
    private readonly int _consumerCount;
    private readonly Func<T, CancellationToken, Task> _handler;
    private readonly Action<T, Exception> _onError;
    private int _executionStarted;
    private int _pendingCount;

    public BoundedChannelExecutor(
        int capacity,
        int consumerCount,
        Func<T, CancellationToken, Task> handler,
        Action<T, Exception> onError)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(consumerCount, 1);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(onError);

        _consumerCount = consumerCount;
        _handler = handler;
        _onError = onError;
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = consumerCount == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Number of callers waiting for admission plus queued or executing items.</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    public async ValueTask EnqueueAsync(T item, CancellationToken ct)
    {
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

    public Task ExecuteAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _executionStarted, 1) != 0)
            throw new InvalidOperationException("The bounded channel executor can only be started once.");

        var consumers = new Task[_consumerCount];
        for (var index = 0; index < consumers.Length; index++)
        {
            consumers[index] = ConsumeAsync(ct);
        }

        return Task.WhenAll(consumers);
    }

    public bool TryComplete(Exception? error = null) => _channel.Writer.TryComplete(error);

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _handler(item, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The owning hosted service is stopping.
                }
                catch (Exception ex)
                {
                    _onError(item, ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingCount);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Forced host shutdown after the graceful drain deadline elapsed.
        }
    }
}
