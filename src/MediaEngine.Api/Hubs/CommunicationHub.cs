using Microsoft.AspNetCore.SignalR;

namespace MediaEngine.Api.Hubs;

/// <summary>
/// The central SignalR hub for broadcasting real-time events to connected clients
/// (Blazor WASM, desktop shells, or any SignalR-compatible consumer).
///
/// Clients connect to this hub to receive server-push events. There is no
/// client-to-server messaging at this stage; all traffic flows server → client.
///
/// Mapped to: <c>/hubs/intercom</c>
/// </summary>
public sealed class CommunicationHub : Hub
{
    // No client-invokable methods yet.
    // To add server-side methods callable from clients, add public Task methods here.
}
