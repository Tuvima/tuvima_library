using MediaEngine.Api.Services;

namespace MediaEngine.Api.Tests;

public sealed class EnrichmentPipelineExecutionGateTests
{
    [Fact]
    public async Task PauseAndDrain_WaitsForActiveWorkersAndBlocksNewWorkUntilResume()
    {
        var gate = new EnrichmentPipelineExecutionGate();
        var firstLease = await gate.EnterAsync();
        var secondLease = await gate.EnterAsync();

        var drained = gate.PauseAndDrainAsync();
        Assert.False(drained.IsCompleted);
        Assert.True(firstLease.PauseCancellationToken.IsCancellationRequested);
        Assert.True(secondLease.PauseCancellationToken.IsCancellationRequested);

        firstLease.Dispose();
        Assert.False(drained.IsCompleted);

        secondLease.Dispose();
        await drained.WaitAsync(TimeSpan.FromSeconds(1));

        var blockedEntry = gate.EnterAsync().AsTask();
        Assert.False(blockedEntry.IsCompleted);

        gate.Resume();
        using var resumedLease = await blockedEntry.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecutionLease_CanBeDisposedMoreThanOnce()
    {
        var gate = new EnrichmentPipelineExecutionGate();
        var lease = await gate.EnterAsync();

        lease.Dispose();
        lease.Dispose();

        await gate.PauseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(1));
        gate.Resume();
    }
}
