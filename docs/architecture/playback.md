# Playback Architecture

Tuvima playback is split into a shared session model plus host-specific transport.
The Dashboard uses Web audio today; future iOS, Android, and video clients should reuse the same command, state, queue, device identity, and progress concepts without depending on DOM routes or browser-only APIs.

## Controller Boundary

The Web UI talks to `PlaybackSessionController` in `src/MediaEngine.Web/Services/Playback/`.

Its intended boundary is:

- `PlaybackSessionState State`
- `Task DispatchAsync(PlaybackCommand command, CancellationToken ct = default)`
- `event Action<PlaybackChangeKind>? Changed`

The controller owns session state and coordinates focused collaborators such as queue behavior, state transitions, transport commands, progress heartbeats, sleep timer state, and client identity. UI code should prefer typed `PlaybackCommand` dispatch for commands and read player state from the controller instead of reaching into storage, JavaScript, or Engine DTOs directly.

`ListenTransportControls.razor` renders the shared play/pause, skip, previous/next, and chapter controls for the bottom bar, side panel, and popup. The hidden browser `<audio>` element remains isolated in the persistent Listen host. A future native app should implement its own native transport host against the same command/state model.

## Shared Primitives

Shared playback primitives use `Playback*` names when they apply beyond Listen:

- `PlaybackExperience`: `Music`, `Audiobook`, `Video`
- `PlaybackPhase`: `Idle`, `Loading`, `Ready`, `Playing`, `Paused`, `Ended`, `Error`, `NeedsGesture`
- `PlaybackCommand`: user/session intent
- `PlaybackTransportCommand`: host transport work, such as start, pause, seek, speed, or volume
- `PlaybackClientContext`: `DeviceId`, `DeviceName`, `Client`, `AppVersion`, and `DeviceClass`

Listen-specific behavior stays under `Listen*` or `Audiobook*` names. Audiobooks intentionally use a single-item queue: choosing another audiobook replaces the current queue rather than creating a multi-book playlist.

## Client Identity

Web stores a stable device id in localStorage under `tuvima.playback.v2.device-id`. Future native clients should store the same concept in Keychain on iOS and Keystore on Android. Engine heartbeats and queue sync should use the supplied device id and client string, not a hardcoded Dashboard id.

## Configuration

Browser transport mechanics live in `config/ui/playback-client.json`: popup dimensions, immediate-action debounce windows, audio observer cadence, seek tolerance, volume step, heartbeat interval, transport UI interval, pending command limits, and default volume.

User-facing listening preferences remain in `UserPlaybackSettingsDto.Listening`. Do not mix profile preferences such as skip seconds, audiobook speed, resume rewind, or sleep timer options with low-level browser transport config.

## Mobile Preparation

The Engine has explicit Android and iOS playback capability profiles. Native clients should plan for:

- background audio
- OS media controls and lock-screen metadata
- audio interruptions and route changes
- Bluetooth and headset events
- secure stream URLs
- byte-range and HLS support
- offline downloads
- resume/progress conflict handling across devices

## Adaptive Video Delivery

The playback manifest keeps direct play when the negotiated client profile supports the source. Otherwise, the Engine creates a source-aware HLS VOD package under the configured variant cache. A package contains independent H.264 video renditions, AAC alternate-audio playlists, managed and embedded WebVTT caption playlists, and a master manifest. Rendition heights never upscale the source.

Packages become visible only after every playlist reference has been validated and the staging directory has been moved into place. The package row records its source hash, profile, size, completion state, and last access. A background cleanup service removes abandoned staging directories, expired/failed packages, and least-recently-used packages above the storage limit while leaving active readers alone.

The manifest returns a short-lived signed path scoped to one asset and one package. Native players request the master, variant playlists, and segments through that same path. The Dashboard exposes the path through `/engine-hls/`; the Engine rejects expired, modified, cross-package, and traversal attempts. HLS playlists use VOD timelines and aligned independent segments, so a native HLS client can seek immediately after preparation. Resume remains the shared persisted playback position applied by `PlaybackSessionController`.

The persistent video host owns video transport, captions, audio-track selection, seeking, and fullscreen. It follows `recommendedDelivery` from the manifest and must not fall back to an incompatible direct source when HLS preparation is pending or failed.

## FFmpeg and Hardware Profiles

Windows installers bundle the checksum-pinned FFmpeg build described in `tools/ffmpeg/README.md`. Container builds verify the HLS muxer plus H.264, AAC, and WebVTT encoders. Engine readiness reports these capabilities separately.

`hardware_acceleration` accepts `auto`, `nvenc`, `quicksync`, `vaapi`, `cpu`, or `none`. Auto mode probes hardware encoders and falls back to `libx264`; explicit software modes still report and use normal HLS capabilities. A failed hardware encode is retried once with `libx264`.
