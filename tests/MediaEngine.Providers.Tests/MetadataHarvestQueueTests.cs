using System.Collections.Concurrent;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Services;

namespace MediaEngine.Providers.Tests;

public sealed class MetadataHarvestQueueTests
{
    [Fact]
    public async Task AdmissionAndHostedConsumption_ShareOwnershipAndDrainOnCompletion()
    {
        var queue = new MetadataHarvestQueue();
        var processed = new ConcurrentQueue<Guid>();
        var execution = queue.ExecuteAsync(
            consumerCount: 3,
            (request, _) =>
            {
                processed.Enqueue(request.EntityId);
                return Task.CompletedTask;
            },
            (_, ex) => throw new Xunit.Sdk.XunitException($"Unexpected queue error: {ex}"),
            CancellationToken.None);
        var requests = Enumerable.Range(0, 12)
            .Select(_ => new HarvestRequest
            {
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Work,
                MediaType = MediaType.Unknown,
            })
            .ToArray();

        foreach (var request in requests)
        {
            await queue.EnqueueAsync(request);
        }

        queue.TryComplete();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(
            requests.Select(request => request.EntityId).Order(),
            processed.Order());
    }

    [Fact]
    public async Task QueueConsumption_CanOnlyBeOwnedByOneHostedWorker()
    {
        var queue = new MetadataHarvestQueue();
        var execution = queue.ExecuteAsync(
            1,
            (_, _) => Task.CompletedTask,
            (_, _) => { },
            CancellationToken.None);

        Action startSecondConsumer = () =>
        {
            _ = queue.ExecuteAsync(
                1,
                (_, _) => Task.CompletedTask,
                (_, _) => { },
                CancellationToken.None);
        };
        Assert.Throws<InvalidOperationException>(startSecondConsumer);

        queue.TryComplete();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
