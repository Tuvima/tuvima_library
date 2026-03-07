# Feature: Library Dashboard (Home, Navigation, Cinematic Hero, Poster Swimlanes)
> **Mirrors:** `CLAUDE.md` §3.4 (Dashboard UI) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-07 | Auditor: Claude (Product-Led Architect)

---

## User Experience

### Home Page (`/`)

When the user opens the Dashboard, they see a cinematic streaming-style interface:

1. **Cinematic Hero Banner** — A full-width banner with blurred cover art background, dark vignette overlay, and metadata badges (year, media type, work count). Shows the first Hub in the library.
2. **Poster Swimlanes** — Horizontal scrolling rows of poster-art cards (2:3 aspect ratio). Rows include "Continue your Journey" (stub), "Recently Added" (stub), and then media-type-grouped swimlanes (Books, Movies, Comics, Audio).
3. **Selecting a Hub** — Clicking any poster card navigates to the Hub (detail page not yet built).

### Navigation — Dual Architecture

The Dashboard uses a dual navigation system:

- **TopBar** (`TopBar.razor`) — Fixed horizontal bar at top (56px height). Contains: logo (AppLogo wordmark), spacer, search icon, notification bell (MudBadge with review count), profile avatar. Four variants driven by device config: `"full"` (desktop), `"mobile"` (hamburger + logo + compact actions), `"simplified"` (TV: logo only), `"minimal"` (automotive: icon only). Glassmorphic styling with `backdrop-filter: blur(10px)`.
- **LeftDock** (`LeftDock.razor`) — Icon-only vertical rail on the left (52px). Shows virtual library icons from TrayConfig. Active item has 3px amber left bar. Hover-expands to 200px with text labels. Glassmorphic blur(20px), no borders. Hidden on mobile/TV/automotive via `DockVisible` config flag.
- **MobileNavDrawer** (`MobileNavDrawer.razor`) — Slide-out drawer triggered by hamburger in mobile TopBar. Contains AppLogo + nav items matching dock libraries + Settings link.
- **Command Palette (Ctrl+K)** — Global search overlay. Type 2+ characters to search across all Hubs and Works.

### Dark Mode Only

The Dashboard is dark-mode only. All light-mode CSS, the dark/light toggle, and ThemeService light palette have been removed. `IsDarkMode` is always `true`.

### Real-time Updates

The Dashboard maintains a live connection to the Engine via SignalR. When a new file is ingested or metadata arrives from an external provider, the library updates instantly — no page refresh needed.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| LDR-01 | The first Hub is displayed in the cinematic hero banner. | Home.razor |
| LDR-02 | Poster swimlanes group hubs by media type (requires 2+ hubs per type). | Home.razor |
| LDR-03 | The Command Palette requires at least 2 characters before searching. | CommandPalette.razor |
| LDR-04 | Search results are capped at 20 items. | HubEndpoints (server-side cap) |
| LDR-05 | Dock visibility is config-driven via `DockVisible` in UIShellSettingsDto. | MainLayout.razor |
| LDR-06 | TopBar variant is config-driven via `TopBarStyle` in UIShellSettingsDto. | MainLayout.razor |
| LDR-07 | SignalR reconnects automatically with backoff: 0s → 2s → 10s → 30s. | UIOrchestratorService |
| LDR-08 | Hub cache is invalidated when MediaAdded or MetadataHarvested events arrive. | UniverseStateContainer |
| LDR-09 | The Dashboard degrades gracefully when the Engine is offline. | Home.razor, MainLayout.razor |
| LDR-10 | Swimlane card width is device-configurable via `SwimlaneCardWidth`. | Home.razor |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| Cinematic hero banner | **PASS** | Blurred cover art, vignette overlay, metadata badges. Fallback gradient when no cover. |
| Poster swimlanes | **PASS** | Horizontal scrolling, scroll-snap, media-type grouping. |
| TopBar (desktop) | **PASS** | Logo, search, bell, avatar. Review badge count. |
| TopBar (mobile) | **PASS** | Hamburger, logo, compact actions. |
| LeftDock (desktop) | **PASS** | Icon-only 52px rail, hover-expand to 200px, amber active bar. |
| MobileNavDrawer | **PASS** | Slide-out drawer with libraries and settings. |
| Real-time updates (SignalR) | **PASS** | Connection, reconnection, event routing, cache invalidation. |
| Command Palette search | **PASS** | Real-time search with media-type icons and colour coding. |
| Command Palette navigation | **FAIL** | Selecting a result navigates to `/hub/{hubId}`, but no Hub detail page exists. |
| Continue Journey swimlane | **STUB** | Placeholder — UserState API does not exist yet. |
| Recently Added swimlane | **STUB** | Placeholder — `/hubs/recent` endpoint does not exist yet. |
| Cover art in hero/posters | **PARTIAL** | Uses `cover` canonical value (provider URLs). Works when providers have been run. |

---

## PO Summary

The Library Dashboard presents your collection as a cinematic streaming interface. A full-width hero banner shows blurred cover artwork with metadata badges. Poster-art swimlanes replace the old Bento grid, grouping content by media type (Books, Movies, Comics, Audio). Navigation uses a dual system: a horizontal TopBar at the top (logo, search, bell, profile) plus an icon-only LeftDock on desktop. Mobile uses a hamburger menu that opens a slide-out drawer. **The Dashboard is dark-mode only.** Key gaps: Hub detail page doesn't exist yet, "Continue Journey" and "Recently Added" swimlanes are stubs pending API endpoints.
