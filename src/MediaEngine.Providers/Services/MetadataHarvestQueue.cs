using System.Threading.Channels;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Owns metadata-harvest queue admission independently from the hosted worker
/// that consumes requests. This keeps follow-up producers out of the worker's
/// dependency graph while preserving bounded wait-based backpressure.
/// </summary>
public sealed class MetadataHarvestQueue : IMetadataHarvestQueueAdmission
{
    private readonly Channel<HarvestRequest> _channel = Channel.CreateBounded<HarvestRequest>(
        new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private int _executionStarted;
    private int _pendingCount;

    public int PendingCount => Volatile.Read(ref _pendingCount);

    public async ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Increment(ref _pendingCount);
        try
        {
            await _channel.Writer.WriteAsync(request, ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            throw;
        }
    }

    internal Task ExecuteAsync(
        int consumerCount,
        Func<HarvestRequest, CancellationToken, Task> handler,
        Action<HarvestRequest, Exception> onError,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(consumerCount, 1);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(onError);
        if (Interlocked.Exchange(ref _executionStarted, 1) != 0)
        {
            throw new InvalidOperationException("The metadata harvest queue can only be consumed once.");
        }

        return Task.WhenAll(Enumerable.Range(0, consumerCount)
            .Select(_ => ConsumeAsync(handler, onError, ct)));
    }

    internal bool TryComplete(Exception? error = null) => _channel.Writer.TryComplete(error);

    private async Task ConsumeAsync(
        Func<HarvestRequest, CancellationToken, Task> handler,
        Action<HarvestRequest, Exception> onError,
        CancellationToken ct)
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await handler(request, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The owning hosted service is stopping.
                }
                catch (Exception ex)
                {
                    onError(request, ex);
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
