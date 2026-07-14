using MediaEngine.Api.Services;
using MediaEngine.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class InitialSweepCommandServiceTests
{
    [Fact]
    public async Task TrySchedule_IsSingleFlight_AndReopensAfterCompletion()
    {
        var sweep = new ControllableSweep();
        var service = new InitialSweepCommandService(
            sweep,
            NullLogger<InitialSweepCommandService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(service.TrySchedule());
            Assert.False(service.TrySchedule());

            await sweep.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(service.IsPendingOrRunning);

            sweep.Release.TrySetResult();
            await WaitUntilAsync(() => !service.IsPendingOrRunning);

            Assert.True(service.TrySchedule());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ControllableSweep : IInitialSweepService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<InitialSweepResult> RunAsync(CancellationToken ct = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new InitialSweepResult(0, 0, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
