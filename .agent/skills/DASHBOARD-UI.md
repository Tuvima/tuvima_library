# Skill: Dashboard UI Operations

> **Mirrors:** `CLAUDE.md` Section 6. Keep both in sync per `.agent/SYNC-MAP.md`.

> Last updated: 2026-07-21

---

## Purpose

Use this skill when changing Dashboard visual components, routed pages, navigation, state management, inline editing, or Settings/Admin surfaces.

---

## Current Dashboard Model

Home, Read, Watch, Listen, Collections, and Search are the user-facing discovery and media surfaces. Detail pages and media rows/cards launch inline editing through the shared media editor. Review Queue is only for blocked or uncertain items that need human confirmation. Settings/Admin is for configuration and operational/system concerns.

Home is the only cinematic landing and shares `CinematicHeroCarousel`/`CinematicHeroSurface` with detail presentation through `DetailHeroContent`. Read, Watch, and Listen use compact rail-navigated Discover pages, while Collections opens as a browse-first grid. `MediaLaneHeader` keeps only the compact `SurfaceNavigationBar` mounted at the top across each lane's Discover and scoped browse routes; do not repeat the lane identity above it or as a Read/Watch rail title. On desktop, keep the rail viewport-anchored and scroll only the adjacent lane content pane. Filters and tiled results replace only the content beneath it. Direct filters form one elevated responsive surface close beneath the menu, use larger shared control typography, and wrap search, applicable facets, stacked booleans, sort, tile sizing, and layout controls within that panel. Read/Watch genres use a searchable multi-select checklist, not an exposed chip row. Lane scope controls navigate directly to complete tiled libraries; only recommendation shelves use **View all**, and direct browse filters do not repeat the media switcher. Detail tabs retain their existing state behavior.

The removed all-in-one management workflow must not be recreated. Do not add routes, navigation labels, implementation types, or an all-in-one media correction workbench for it.

Non-TV series and collection containers use `Components/MediaTiles/MediaGroupTile.razor`, a dedicated fixed-size landscape card. Rest shows only two to four representative artworks in one slightly angled, overlapping cluster built from real portrait, square, or wide metadata. Home and Discover shelf hover may add the purple boundary/glow plus the compact top type/title/status overlay without changing the composition. Vertically wrapping grids use `MediaTileHoverMode.GlowOnly`: individual and group cards retain their exact artwork and geometry, show only a compact title/year caption below the art, omit redundant contextual pills such as `TV Show` on the TV Shows route, and reveal no hover identity strip, shading, background replacement, or popover. Direct browse filter areas include a tile-size slider; the width changes while portrait, square, and landscape ratios remain stable, and Music defaults smaller. TV shows on Home and Discover remain on `MediaTile`, with a show cover at rest and show-level cinematic background and rich identity on hover. Do not give TV shows the generic container summary merely because their storage identity is series-backed. Individual and Continue cards must retain their existing renderers and shelf geometry. Every card is one semantic link to details, and cinematic expansion remains action-free.

Media-specific browse results use wrapping tiled grids and must not become horizontally scrolling media rows. Series detail keeps proven-adjacency connectors behind the numbered nodes, uses a stronger purple frame glow as the only visible current-item state, and exposes current context through `aria-current`. Missing-item visibility inherits media defaults from `config/ui/library-preferences.json`; the database stores only explicit profile-and-series overrides, and reset deletes the override.

---

## Key Files

| File | Role |
|---|---|
| `src/MediaEngine.Web/Shared/MainLayout.razor` | Global shell, navigation, search, My List, unified activity indicator, account menu, engine status, command palette, and persistent playback host. |
| `src/MediaEngine.Web/Components/Navigation/TopNavAccountMenu.razor` | Profile switching, permission-gated Needs Review, Settings, Help, and conditional sign-out. |
| `src/MediaEngine.Web/Components/Navigation/SystemActivityIndicator.razor` | Busy/idle shell status for playback, ingestion, AI, enrichment, and durable Engine operations. |
| `src/MediaEngine.Web/Components/Pages/LibraryBrowsePage.razor` | Home/discovery page. |
| `src/MediaEngine.Web/Components/Cinematic/` | Shared hero stage/carousel and `SurfaceNavigationBar` used by lane filters and detail tabs. |
| `src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor` | Shared Read, Watch, and Listen browse behavior. |
| `src/MediaEngine.Web/Components/Details/DetailPage.razor` | Detail-page surface and inline edit launcher. |
| `src/MediaEngine.Web/Components/Universe/BookDetailContent.razor` | Read-detail surface for books. |
| `src/MediaEngine.Web/Components/Collections/` | Collections route, cards, sections, and editor shell. |
| `src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor` | Review Queue exception workflow. |
| `src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor` | Ingestion operations dashboard. |
| `src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor` | Normal, Review, and Batch media editing shell. |
| `src/MediaEngine.Web/Components/MediaTiles/MediaGroupTile.razor` | Artwork-only fixed-size series/collection card with a compact top hover/focus overlay. |
| `src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor` | Shared Listen transport controls for bottom bar, side panel, and popup. |
| `src/MediaEngine.Web/Services/Editing/MediaEditorLauncherService.cs` | Central editor launch and return path. |
| `src/MediaEngine.Web/Services/Playback/PlaybackSessionController.cs` | Listen playback session controller, command dispatch, queue/session state, and transport command boundary. |
| `src/MediaEngine.Web/Services/Integration/EngineApiClient.cs` | Engine HTTP and SignalR integration. |

---

## Component Placement

| What you are adding | Preferred location |
|---|---|
| Routed page | `Components/Pages/` or the existing feature folder if the route already lives there |
| Shared lane browsing | `Components/Browse/` |
| Detail composition | `Components/Details/` |
| Collection UI | `Components/Collections/` |
| Read/watch/listen-specific UI | `Components/Universe/`, `Components/Watch/`, or `Components/Listen/` according to existing usage |
| Media correction UI | `Components/MediaEditor/` |
| Settings/Admin tab | `Components/Settings/` |
| Cross-cutting primitive | `Components/Shared/` |
| Navigation/search shell | `Components/Navigation/` or `Shared/MainLayout.razor` |
| Listen transport controls | `Components/Listen/ListenTransportControls.razor` |

Use existing feature folders before introducing new abstractions.

---

## Editing Rules

1. Normal corrections launch from the current media surface or detail page.
2. Review corrections launch from Review Queue.
3. Batch corrections only launch from a real selected set.
4. All three paths use `MediaEditorLauncherService` and `SharedMediaEditorShell`.
5. After a successful save, refresh the current surface and keep the user in context.
6. Canceled edits must not mutate UI state.

---

## State and Integration

- `EngineApiClient` is the typed Dashboard client for Engine endpoints.
- SignalR events keep ingestion, activity, enrichment, and review indicators current.
- Keep review attention inside the permission-aware account menu; do not reintroduce a standalone navbar notification button.
- Use `ShellActivityState` for cross-cutting busy/idle state instead of adding one-off navbar progress controls.
- Dashboard view models live under `Models/ViewDTOs/`; do not pass storage implementation models into UI.
- Razor components must not contain direct SQL.
- Settings/Admin pages should call Engine APIs or typed services, not storage repositories.
- Listen playback UI should use `PlaybackSessionController` state and typed commands. Browser transport work stays behind the persistent Web audio host and the `listenPlayback` JS bridge.
- Do not duplicate Listen play/pause, skip, previous/next, or chapter controls outside `ListenTransportControls.razor`.

---

## Quality Gates

- Keep removed all-in-one management workflows out of active routes, navigation, docs, and CSS.
- Keep media correction inline through the shared editor.
- Keep Review Queue focused on blocked, uncertain, low-confidence, or unresolved items.
- Keep Settings/Admin focused on configuration, operations, diagnostics, users, providers, plugins, AI, ingestion, and review.
- Run restore, build, tests, and relevant docs checks before closing UI or documentation work.
