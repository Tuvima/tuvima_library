using MediaEngine.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Ingestion.Tests;

public sealed class BackgroundWorkerTests
{
    [Fact]
    public async Task DrainAsync_WaitsForWorkStartedAfterWorkerWasPreviouslyIdle()
    {
        await using var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            maxConcurrency: 1,
            queueCapacity: 4);

        await worker.EnqueueAsync(
            "warmup",
            static (_, _) => Task.CompletedTask);
        await WaitUntilAsync(() => worker.PendingCount == 0);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;

        await worker.EnqueueAsync(
            "blocked",
            async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                completed = true;
            });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var drainTask = worker.DrainAsync();

        await Task.Delay(100);
        Assert.False(drainTask.IsCompleted);

        release.SetResult();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(completed);
    }

    [Fact]
    public async Task EnqueueAsync_BackpressuresAndHonorsCallerCancellationWhenQueueIsFull()
    {
        await using var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            maxConcurrency: 1,
            queueCapacity: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new List<int>();

        await worker.EnqueueAsync(1, async (item, _) =>
        {
            started.SetResult();
            await release.Task;
            processed.Add(item);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.EnqueueAsync(2, (item, _) =>
        {
            processed.Add(item);
            return Task.CompletedTask;
        });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await worker.EnqueueAsync(3, static (_, _) => Task.CompletedTask, cancellation.Token));
        Assert.Equal(2, worker.PendingCount);

        release.SetResult();
        await worker.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], processed);
        Assert.Equal(0, worker.PendingCount);
    }

    [Fact]
    public async Task DisposeAsync_CancelsHandlersAndAwaitsTheirCompletion()
    {
        var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            maxConcurrency: 1,
            queueCapacity: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await worker.EnqueueAsync("blocked", async (_, ct) =>
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
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, worker.PendingCount);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("Condition was not reached.");

            await Task.Delay(10);
        }
    }
}
