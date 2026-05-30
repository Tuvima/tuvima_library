using System.Collections.Concurrent;
using MediaEngine.Providers.Contracts;

namespace MediaEngine.Providers.Services;

public sealed class IdentityPipelineSignal : IIdentityPipelineSignal
{
    private readonly ConcurrentDictionary<IdentityPipelineSignalKind, SemaphoreSlim> _signals = new();

    public void Signal(IdentityPipelineSignalKind kind)
    {
        var signal = _signals.GetOrAdd(kind, _ => new SemaphoreSlim(0));
        signal.Release();
    }

    public async Task WaitAsync(
        IdentityPipelineSignalKind kind,
        TimeSpan fallbackDelay,
        CancellationToken ct = default)
    {
        if (fallbackDelay <= TimeSpan.Zero)
            return;

        var signal = _signals.GetOrAdd(kind, _ => new SemaphoreSlim(0));
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var signalTask = signal.WaitAsync(waitCts.Token);
        var delayTask = Task.Delay(fallbackDelay, ct);

        var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
        if (completed == signalTask)
        {
            await signalTask.ConfigureAwait(false);
            return;
        }

        waitCts.Cancel();
        try
        {
            await signalTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The fallback timer won; cancel the outstanding semaphore waiter.
        }

        await delayTask.ConfigureAwait(false);
    }
}
