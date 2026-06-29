---
title: "JavaScript Interop Lifecycle"
summary: "Rules for safe JavaScript interop lifecycle management in Blazor components."
audience: "developer"
category: "architecture"
product_area: "dashboard"
---

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
- Media tile hover: `registerMediaTileHover` / `unregisterMediaTileHover`.
- Listen playback callbacks: `configure`, `registerStateHandler`, `registerCommandHandler`, `registerAudioStateObserver`, `registerPlayerShortcuts`, and popup unload registration have paired unregister methods where they register listeners or .NET references.
- EPUB reader and Cytoscape registrations expose dispose/destroy methods and should keep using them from component disposal.

Avoid adding fire-and-forget JavaScript cleanup from synchronous `Dispose`; use `IAsyncDisposable` when JS interop is involved.

## Listen Playback Storage

The Listen playback bridge stores only Web client mechanics under `tuvima.playback.v2.*` localStorage keys:

- `tuvima.playback.v2.state`
- `tuvima.playback.v2.command`
- `tuvima.playback.v2.device-id`

Do not read the retired `listen-playback-state` or `listen-playback-command` keys. Browser timing and popup defaults are injected from `config/ui/playback-client.json` through `listenPlayback.configure(...)`; user listening preferences stay in the playback settings API.
