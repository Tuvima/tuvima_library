using MediaEngine.Providers.Services;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Tests;

public sealed class ProviderRateLimiterCoordinatorTelemetryTests
{
    [Fact]
    public async Task SnapshotReportsWaitingRequestsBeforeProviderLeaseStarts()
    {
        var coordinator = new ProviderRateLimiterCoordinator();
        var rateLimit = new ProviderRateLimitConfiguration
        {
            MaxConcurrency = 1,
            Burst = 1,
        };
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.ExecuteAsync(
            "apple_api",
            rateLimit,
            async ct =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task.WaitAsync(ct);
                return 1;
            },
            CancellationToken.None);

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = coordinator.ExecuteAsync(
            "apple_api",
            rateLimit,
            _ => Task.FromResult(2),
            CancellationToken.None);

        var waiting = await WaitForSnapshotAsync(
            coordinator,
            snapshot => snapshot.ActiveRequests == 1 && snapshot.WaitingRequests == 1);

        Assert.Equal(1, waiting.ActiveRequests);
        Assert.Equal(1, waiting.WaitingRequests);

        await Task.Delay(75);
        releaseFirst.TrySetResult(true);

        await Task.WhenAll(first, second);

        var final = coordinator.GetSnapshots().Single();
        Assert.Equal(0, final.ActiveRequests);
        Assert.Equal(0, final.WaitingRequests);
        Assert.Equal(2, final.RequestsTotal);
        Assert.True(final.MaxActiveLastMinute >= 1);
        Assert.True(final.WaitMsLastMinute > 0);
        Assert.True(final.AverageWaitMs > 0);
        Assert.NotNull(final.LastSuccessAt);
    }

    [Fact]
    public async Task SnapshotKeepsShortSuccessfulRequestsVisibleAfterActiveReturnsToZero()
    {
        var coordinator = new ProviderRateLimiterCoordinator();

        var result = await coordinator.ExecuteAsync(
            "tmdb",
            new ProviderRateLimitConfiguration { MaxConcurrency = 1, Burst = 1 },
            _ => Task.FromResult(42),
            CancellationToken.None);

        var snapshot = coordinator.GetSnapshots().Single();
        Assert.Equal(42, result);
        Assert.Equal(0, snapshot.ActiveRequests);
        Assert.Equal(0, snapshot.WaitingRequests);
        Assert.Equal(1, snapshot.RequestsTotal);
        Assert.Equal(1, snapshot.RequestsLastMinute);
        Assert.True(snapshot.MaxActiveLastMinute >= 1);
        Assert.NotNull(snapshot.LastRequestAt);
        Assert.NotNull(snapshot.LastSuccessAt);
    }

    [Fact]
    public async Task CanceledWaitDoesNotLeaveStaleWaitingCount()
    {
        var coordinator = new ProviderRateLimiterCoordinator();
        var rateLimit = new ProviderRateLimitConfiguration
        {
            MaxConcurrency = 1,
            Burst = 1,
        };
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.ExecuteAsync(
            "comicvine",
            rateLimit,
            async ct =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task.WaitAsync(ct);
                return 1;
            },
            CancellationToken.None);

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource();
        var second = coordinator.ExecuteAsync(
            "comicvine",
            rateLimit,
            _ => Task.FromResult(2),
            cts.Token);

        await WaitForSnapshotAsync(
            coordinator,
            snapshot => snapshot.ActiveRequests == 1 && snapshot.WaitingRequests == 1);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        var afterCancel = await WaitForSnapshotAsync(
            coordinator,
            snapshot => snapshot.ActiveRequests == 1 && snapshot.WaitingRequests == 0);

        Assert.Equal(1, afterCancel.ActiveRequests);
        Assert.Equal(0, afterCancel.WaitingRequests);

        releaseFirst.TrySetResult(true);
        await first;

        var final = coordinator.GetSnapshots().Single();
        Assert.Equal(0, final.ActiveRequests);
        Assert.Equal(0, final.WaitingRequests);
        Assert.Equal(1, final.RequestsTotal);
    }

    private static async Task<ProviderActivitySnapshot> WaitForSnapshotAsync(
        ProviderRateLimiterCoordinator coordinator,
        Func<ProviderActivitySnapshot, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        ProviderActivitySnapshot? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = coordinator.GetSnapshots().SingleOrDefault();
            if (last is not null && predicate(last))
                return last;

            await Task.Delay(10);
        }

        throw new TimeoutException($"Provider activity snapshot did not reach expected state. Last snapshot: {last}");
    }
}
