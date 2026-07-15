# Feature: Library Dashboard

> **Mirrors:** `CLAUDE.md` Section 3.11 and Section 6. Keep both in sync per `.agent/SYNC-MAP.md`.

> Last audited: 2026-06-28 | Auditor: Codex

---

## Product Model

The Dashboard is the user-facing surface for a local-first story library. It is organized by user intent rather than by a separate media management workspace:

- **Home** (`/`) gives a discovery overview and recent library activity.
- **Read** (`/read`) is for books and comics.
- **Watch** (`/watch`) is for movies and TV.
- **Listen** (`/listen`) is for music, albums, artists, tracks, and audiobooks.
- **Collections** (`/collections`) is for broader rollups where multiple shelves share a series, franchise, or universe relationship.
- **Search** (`/search`) searches across the library.
- **Detail pages** show item identity, artwork, people, relationships, variants, and inline correction entry points.
- **Settings/Admin** owns configuration and operations, including Review Queue.

Lane-level shelves stay in their lane. A single book series, film series, album, or audio series should not duplicate itself as a top-level Collections tile unless it connects to a broader cross-shelf relationship.

---

## Core Entry Points

| Area | Files |
|---|---|
| Shell, search, profile, review badge, engine state | `src/MediaEngine.Web/Shared/MainLayout.razor` |
| Home/discovery | `src/MediaEngine.Web/Components/Pages/LibraryBrowsePage.razor` |
| Shared lane browsing | `src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor` |
| Read details | `src/MediaEngine.Web/Components/Universe/BookDetailContent.razor` |
| General details | `src/MediaEngine.Web/Components/Details/DetailPage.razor` |
| Collections | `src/MediaEngine.Web/Components/Collections/` |
| Review Queue | `src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor` |
| Shared editor | `src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor` |
| Ingestion dashboard | `src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor` |
| Listen playback controller | `src/MediaEngine.Web/Services/Playback/PlaybackSessionController.cs` |
| Shared Listen transport controls | `src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor` |

---

## Business Rules

| Rule | Meaning |
|---|---|
| Surface-per-concern | Browsing lives on Home, Read, Watch, Listen, Collections, Search, and details. |
| Inline correction | Normal media fixes launch from the surface where the user found the issue. |
| Shared editor | Use `MediaEditorLauncherService` and `SharedMediaEditorShell` for Normal, Review, and Batch modes. |
| Review exception | Review Queue is only for blocked, uncertain, low-confidence, or unresolved items. |
| Settings/Admin scope | Settings/Admin is for folders, providers, profiles, roles, ingestion, health, logs, diagnostics, plugins, AI, and review. |
| No removed management workflow | Do not recreate all-in-one management routes, implementation types, navigation labels, or media correction workbenches. |
| Playback controller boundary | Listen playback UI reads the session controller and uses shared transport controls; browser mechanics stay behind the Web audio host and `listenPlayback` bridge. |

---

## Platform Health

| Area | Status | Notes |
|---|---|---|
| Home | Live | Discovery and overview surface. |
| Read/Watch/Listen | Live | Shared browse behavior with lane-specific content. |
| Collections | Live/partial | Broader rollups are present; deeper collection creation/editing remains Early Access. |
| Search | Live | Cross-library discovery. |
| Detail pages | Live | Inline correction entry points use the shared editor path. |
| Review Queue | Live | Exception workflow under Settings/Admin. |
| Ingestion dashboard | Live | Uses `GET /ingestion/operations` plus SignalR progress. |
| Local AI/admin areas | Partial | Model management and feature surfaces exist; some workflows remain in progress. |

---

## Product Owner Summary

The Dashboard is no longer a general management workspace. It is a story-first library experience: people browse through Home, Read, Watch, Listen, Collections, Search, and detail pages, and they only go to Review Queue when Tuvima needs human confirmation. Normal corrections happen where the user finds the problem, using one shared editor.

TV detail is show-centered: seasons contain owned episode rows, and each episode
has a show-scoped detail page opened from its still. An unstarted show may keep the
series hero while its facts and action target the first owned episode; after progress,
the hero switches to that episode's still and separated `Sx Ey` synopsis. Short
provider show copy appears under the owned summary in Series Description. Detail
heroes keep written identity and credits left-aligned while centering logos, compact
facts, and actions. The facts show at most two linked genres as a non-wrapping pair.
Movie synopsis blocks use the movie description.
Continue cards retain the episode target with `Sx Ey` action context. Comic sequence presentation shows issue numbers and owned
counts without treating the current provider run count as a completion target.
