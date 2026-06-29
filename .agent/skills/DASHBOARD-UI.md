# Skill: Dashboard UI Operations

> **Mirrors:** `CLAUDE.md` Section 6. Keep both in sync per `.agent/SYNC-MAP.md`.

> Last updated: 2026-06-28

---

## Purpose

Use this skill when changing Dashboard visual components, routed pages, navigation, state management, inline editing, or Settings/Admin surfaces.

---

## Current Dashboard Model

Home, Read, Watch, Listen, Collections, and Search are the user-facing discovery and media surfaces. Detail pages and media rows/cards launch inline editing through the shared media editor. Review Queue is only for blocked or uncertain items that need human confirmation. Settings/Admin is for configuration and operational/system concerns.

The removed all-in-one management workflow must not be recreated. Do not add routes, navigation labels, implementation types, or an all-in-one media correction workbench for it.

---

## Key Files

| File | Role |
|---|---|
| `src/MediaEngine.Web/Shared/MainLayout.razor` | Global shell, navigation, search, review notification, profile menu, engine status, command palette, and persistent playback host. |
| `src/MediaEngine.Web/Components/Pages/LibraryBrowsePage.razor` | Home/discovery page. |
| `src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor` | Shared Read, Watch, and Listen browse behavior. |
| `src/MediaEngine.Web/Components/Details/DetailPage.razor` | Detail-page surface and inline edit launcher. |
| `src/MediaEngine.Web/Components/Universe/BookDetailContent.razor` | Read-detail surface for books. |
| `src/MediaEngine.Web/Components/Collections/` | Collections route, cards, sections, and editor shell. |
| `src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor` | Review Queue exception workflow. |
| `src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor` | Ingestion operations dashboard. |
| `src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor` | Normal, Review, and Batch media editing shell. |
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
