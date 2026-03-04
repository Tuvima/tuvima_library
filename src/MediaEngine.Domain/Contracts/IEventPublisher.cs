namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Publishes named domain events to interested subscribers.
/// Implementations decide the transport (in-process, SignalR, message bus, etc.).
/// A no-op implementation should be registered in processes that do not require
/// real-time broadcasting (e.g. the background worker host).
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Broadcasts a named event with a typed payload.
    /// The event name becomes the method name on the receiving client
    /// (e.g. "IngestionStarted" maps to a JS/Blazor handler of the same name).
    /// Implementations MUST NOT throw if no subscribers are connected.
    /// </summary>
    Task PublishAsync<TPayload>(
        string eventName,
        TPayload payload,
        CancellationToken ct = default)
        where TPayload : notnull;
}
