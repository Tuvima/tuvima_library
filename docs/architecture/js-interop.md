# JavaScript Interop Lifecycle

Dashboard components that register JavaScript callbacks must also unregister them.

## Pattern

1. JavaScript `register...` functions should replace an existing handler or return/store a handle that can be passed to `unregister...`.
2. JavaScript `unregister...` or `dispose...` functions remove event listeners, clear timers, cancel animation frames, close channels that are owned by that registration, and clear stored .NET references.
3. Razor components that register listeners or create `DotNetObjectReference` should implement `IAsyncDisposable`.
4. `DisposeAsync` calls the JavaScript unregister function and then disposes the `DotNetObjectReference`.
5. Ignore `JSDisconnectedException` during disposal. Log other disposal failures at Debug when a logger is available.

## Current High-Risk Registrations

- Ctrl+K command palette: `registerCtrlK` / `unregisterCtrlK`.
- Discovery card hover: `registerDiscoveryCardHover` / `unregisterDiscoveryCardHover`.
- Listen playback callbacks: `registerStateHandler`, `registerCommandHandler`, and popup unload registration have paired unregister methods.
- EPUB reader and Cytoscape registrations expose dispose/destroy methods and should keep using them from component disposal.

Avoid adding fire-and-forget JavaScript cleanup from synchronous `Dispose`; use `IAsyncDisposable` when JS interop is involved.
