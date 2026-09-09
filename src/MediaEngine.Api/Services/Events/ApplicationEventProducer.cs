using MediaEngine.Domain.Events;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Events;

public sealed class ApplicationEventProducer(
    IDatabaseConnection database,
    IApplicationEventOutboxWriter outbox,
    ApplicationEventRegistry registry) : IApplicationEventProducer
{
    public async Task PublishAsync(ApplicationEventDraft value, CancellationToken ct = default)
    {
        if (!registry.TryGet(value.EventType, out _))
        {
            throw new ArgumentException($"Unknown application event type '{value.EventType}'.", nameof(value));
        }

        await database.ExecuteWriteAsync(
            (connection, transaction, token) => outbox.Append(connection, transaction, value, token), ct).ConfigureAwait(false);
    }
}
