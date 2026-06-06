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
