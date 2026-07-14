using System.Collections.Concurrent;
using MediaEngine.Providers.Services;

namespace MediaEngine.Providers.Tests;

public sealed class BoundedChannelExecutorTests
{
    [Fact]
    public async Task EnqueueAsync_BackpressuresAndHonorsCancellationWithoutDroppingAcceptedItems()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new ConcurrentQueue<int>();
        var executor = new BoundedChannelExecutor<int>(
            capacity: 1,
            consumerCount: 1,
            async (item, _) =>
            {
                if (item == 1)
                {
                    started.SetResult();
                    await release.Task;
                }

                processed.Enqueue(item);
            },
            (_, ex) => throw new Xunit.Sdk.XunitException($"Unexpected handler failure: {ex}"));
        var execution = executor.ExecuteAsync(CancellationToken.None);

        await executor.EnqueueAsync(1, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await executor.EnqueueAsync(2, CancellationToken.None);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await executor.EnqueueAsync(3, cancellation.Token));
        Assert.Equal(2, executor.PendingCount);

        release.SetResult();
        executor.TryComplete();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], processed.ToArray());
        Assert.Equal(0, executor.PendingCount);
    }

    [Fact]
    public async Task Completion_DrainsEveryAcceptedItemBeforeConsumersStop()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new ConcurrentQueue<int>();
        var executor = new BoundedChannelExecutor<int>(
            capacity: 4,
            consumerCount: 2,
            async (item, _) =>
            {
                await release.Task;
                processed.Enqueue(item);
            },
            (_, ex) => throw new Xunit.Sdk.XunitException($"Unexpected handler failure: {ex}"));

        for (var item = 1; item <= 4; item++)
        {
            await executor.EnqueueAsync(item, CancellationToken.None);
        }

        var execution = executor.ExecuteAsync(CancellationToken.None);
        executor.TryComplete();
        await Task.Delay(100);
        Assert.False(execution.IsCompleted);

        release.SetResult();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2, 3, 4], processed.Order().ToArray());
        Assert.Equal(0, executor.PendingCount);
    }

    [Fact]
    public async Task ExecutionCancellation_IsObservedByInFlightHandler()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new BoundedChannelExecutor<int>(
            capacity: 1,
            consumerCount: 1,
            async (_, ct) =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                finally
                {
                    cancellationObserved.SetResult();
                }
            },
            (_, ex) => throw new Xunit.Sdk.XunitException($"Unexpected handler failure: {ex}"));
        using var cancellation = new CancellationTokenSource();
        var execution = executor.ExecuteAsync(cancellation.Token);

        await executor.EnqueueAsync(1, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, executor.PendingCount);
    }
}
