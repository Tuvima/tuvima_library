using Microsoft.AspNetCore.SignalR;

namespace MediaEngine.Api.Realtime;

/// <summary>
/// The central SignalR collection for broadcasting real-time events to connected clients
/// (Blazor WASM, desktop shells, or any SignalR-compatible consumer).
///
/// Clients connect to this collection to receive server-push events. There is no
/// client-to-server messaging at this stage; all traffic flows server → client.
///
/// Mapped to: <c>/intercom</c>
/// </summary>
public sealed class Intercom : Hub
{
    // No client-invokable methods yet.
    // To add server-side methods callable from clients, add public Task methods here.
}
