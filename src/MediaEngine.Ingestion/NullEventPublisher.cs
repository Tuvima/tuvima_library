using MediaEngine.Domain.Contracts;

namespace MediaEngine.Ingestion;

/// <summary>
/// No-op implementation of <see cref="IEventPublisher"/> for use in the background
/// worker host, where no real-time broadcast transport is available.
/// Register this in any host that does not include a SignalR or message-bus publisher.
/// </summary>
public sealed class NullEventPublisher : IEventPublisher
{
    public Task PublishAsync<TPayload>(
        string eventName,
        TPayload payload,
        CancellationToken ct = default)
        where TPayload : notnull
        => Task.CompletedTask;
}
