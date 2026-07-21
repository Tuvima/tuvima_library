# Feature: Library Dashboard

> **Mirrors:** `CLAUDE.md` Section 3.11 and Section 6. Keep both in sync per `.agent/SYNC-MAP.md`.

> Last audited: 2026-07-20 | Auditor: Codex

---

## Product Model

The Dashboard is the user-facing surface for a local-first story library. It is organized by user intent rather than by a separate media management workspace:

- **Home** (`/`) gives a discovery overview and recent library activity.
- **Read** (`/read`) is for books and comics.
- **Watch** (`/watch`) is for movies and TV.
- **Listen** uses `/listen` for Discover, `/listen/music` for tiled album browsing, and `/listen/audiobooks` for tiled audiobook browsing; its permanent rail provides Albums, Songs, and Artists shortcuts.
- **Collections** (`/collections`) is for broader rollups where multiple shelves share a series, franchise, or universe relationship.
- **Search** (`/search`) searches across the library.
- **My List** (`/my-list`) shows the active profile's saved shortlist.
- **Detail pages** show item identity, artwork, people, relationships, variants, and inline correction entry points.
- **Settings/Admin** owns configuration and operations, including Review Queue.

Lane-level shelves stay in their lane. A single book series, film series, album, or audio series should not duplicate itself as a top-level Collections tile unless it connects to a broader cross-shelf relationship.

Non-TV series and collection containers render through the dedicated fixed-size landscape `MediaGroupTile`. Rest is artwork-led: two to four representative images use approved count-and-shape templates and retain their natural portrait, square, or wide ratios. Home and Discover hover preserve the artwork and dimensions, add the purple boundary/glow, and reveal only type, title, and one status line in a compact top overlay. Direct wrapping grids instead show a compact title/year caption below the unobstructed art and use glow-only hover. Their filter areas can resize tile width while retaining the artwork ratio, with Music defaulting smaller. The whole surface opens the group; there are no buttons, rotating artwork, carousel, or child-level actions. TV shows render through `MediaTile`; the show-level cinematic backdrop and rich identity hover remain limited to Home and Discover. Every individual or group card is one details link. Individual and Continue cards keep their existing renderers.

Within Watch, TV shows occupy their own `TV Shows` shelf. The separate `Series` shelf contains only dynamically aligned movie series and explains that automatic grouping in its subtitle. Do not combine those shelves or place TV show cards in the Series row.

The five main landings reuse the detail-derived cinematic hero. `DetailHero` and `CinematicHeroCarousel` both compose `DetailHeroContent`, sharing logo/title scale, facts, primary action, progress, and full-paragraph synopsis. The carousel adds a top-right Featured Content or lane-specific Continue context plus rotation controls. Lane filters and detail tabs both use `SurfaceNavigationBar`. Group-opening actions do not use play icons. Watch landing TV slides always use root show artwork and fall back to the settled placeholder when it is absent; episode stills stay on episode-specific surfaces.

---

## Core Entry Points

| Area | Files |
|---|---|
| Shell, search, My List, account menu, unified activity, engine state | `src/MediaEngine.Web/Shared/MainLayout.razor`, `src/MediaEngine.Web/Components/Navigation/` |
| Home/discovery | `src/MediaEngine.Web/Components/Pages/LibraryBrowsePage.razor` |
| Shared lane browsing | `src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor` |
| Read details | `src/MediaEngine.Web/Components/Universe/BookDetailContent.razor` |
| General details | `src/MediaEngine.Web/Components/Details/DetailPage.razor` |
| Collections | `src/MediaEngine.Web/Components/Collections/` |
| Series/collection tiles | `src/MediaEngine.Web/Components/MediaTiles/MediaGroupTile.razor` |
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
| Review attention | Needs Review is permission-gated inside the account menu; there is no standalone navbar notification button. |
| Global activity | Playback, ingestion, AI, enrichment, and durable Engine work share one circular busy/idle indicator. |
| Settings/Admin scope | Settings/Admin is for folders, providers, profiles, roles, ingestion, health, logs, diagnostics, plugins, AI, and review. |
| No removed management workflow | Do not recreate all-in-one management routes, implementation types, navigation labels, or media correction workbenches. |
| Playback controller boundary | Listen playback UI reads the session controller and uses shared transport controls; browser mechanics stay behind the Web audio host and `listenPlayback` bridge. |
| Group tile isolation | Artwork-only series/collection rest and compact top overlay behavior belong in `MediaGroupTile`; do not add group navigation controls to `MediaTile` or change individual-card geometry. |
| Tiled media browse | Media-specific browse result sets wrap vertically and never use horizontally scrolling media rows. |
| Series preference inheritance | `config/ui/library-preferences.json` owns media defaults; SQLite stores only explicit profile-and-series missing-item overrides, and reset deletes the override. |

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
heroes use one left-aligned column for identity, logos, compact facts, actions, and
description. The facts show at most two linked genres on their own non-wrapping line.
Movie synopsis blocks use the movie description.
Continue cards retain the episode target with `Sx Ey` action context. Comic sequence presentation shows issue numbers and owned
counts without treating the current provider run count as a completion target.
