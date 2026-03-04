using Microsoft.AspNetCore.SignalR;
using MediaEngine.Api.Hubs;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Relay service that implements <see cref="IEventPublisher"/> by forwarding
/// every published event to all connected SignalR clients via
/// <see cref="CommunicationHub"/>.
///
/// The event name becomes the SignalR method name; the payload is serialised
/// to JSON by the default System.Text.Json hub protocol. Blazor clients subscribe
/// using <c>hubConnection.On("IngestionStarted", handler)</c>.
///
/// Safe to use as a Singleton — <see cref="IHubContext{T}"/> is thread-safe
/// and designed for long-lived singleton injection.
/// </summary>
public sealed class SignalREventPublisher : IEventPublisher
{
    private readonly IHubContext<CommunicationHub> _hub;

    public SignalREventPublisher(IHubContext<CommunicationHub> hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// If no clients are connected, <see cref="IHubContext.Clients.All.SendAsync"/>
    /// completes immediately without error — SignalR handles the zero-subscriber case.
    /// </remarks>
    public Task PublishAsync<TPayload>(
        string eventName,
        TPayload payload,
        CancellationToken ct = default)
        where TPayload : notnull
        => _hub.Clients.All.SendAsync(eventName, payload, ct);
}
