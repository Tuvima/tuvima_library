# Feature: Library Dashboard (Home, Navigation, Bento Grid)
> **Mirrors:** `CLAUDE.md` §3.4 (Dashboard UI) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## User Experience

### Home Page (`/`)

When the user opens the Dashboard, they see their entire library presented as a visual grid:

1. **Last Journey (Hero Tile)** — The most prominent tile at the top, showing the user's most recently selected Hub with artwork, title, and three progress indicators (Watch, Read, Listen).
2. **Your Universes (Bento Grid)** — A responsive, asymmetric card grid showing all remaining Hubs. Each tile displays the Hub name, work count, and media-type icons (book, video, comic, audio).
3. **Selecting a Hub** — Clicking any tile in the grid promotes it to the Hero position and shifts the app's accent colour to match that Hub's dominant colour.

### Navigation

- **Command Palette (Ctrl+K)** — A global search overlay. Type 2+ characters to search across all Hubs and Works. Results show media-type icons and colour coding. Selecting a result attempts to navigate to the Hub detail page.
- **Intent Dock** — A floating bottom bar with four buttons: Hubs, Watch, Read, Listen. Designed to filter the library view by content intent.
- **Avatar Menu** — Top-left dropdown with links to Preferences and (for admins) Server Settings.
- **Dark/Light Toggle** — Top-right button to switch themes instantly.

### Real-time Updates

The Dashboard maintains a live connection to the Engine via SignalR. When a new file is ingested or metadata arrives from an external provider, the library updates instantly — no page refresh needed.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| LDR-01 | The first Hub in the list is displayed as the Hero tile. | Home.razor |
| LDR-02 | Clicking a Hub tile promotes it to Hero and updates the accent colour. | Home.razor + ThemeService |
| LDR-03 | The Command Palette requires at least 2 characters before searching. | CommandPalette.razor |
| LDR-04 | Search results are capped at 20 items. | HubEndpoints (server-side cap) |
| LDR-05 | In Automotive Mode, only audio-type Hubs are shown in the grid. | UniverseStack.razor (IsAudio filter) |
| LDR-06 | The activity log is capped at 100 entries (oldest dropped first). | UniverseStateContainer |
| LDR-07 | SignalR reconnects automatically with backoff: 0s → 2s → 10s → 30s. | UIOrchestratorService |
| LDR-08 | Hub cache is invalidated when MediaAdded or MetadataHarvested events arrive. | UniverseStateContainer |
| LDR-09 | The Dashboard degrades gracefully when the Engine is offline — loading states and error alerts are shown. | Home.razor, MainLayout.razor |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| Home page layout | **PASS** | Hero + Bento grid working with responsive columns (1-col mobile, 3-col desktop). |
| Hub tiles (BentoItem) | **PASS** | Glassmorphic styling with dynamic glow, hover effects, and colour coding. |
| Real-time updates (SignalR) | **PASS** | Connection, reconnection, event routing, and cache invalidation all working. |
| Command Palette search | **PASS** | Real-time search with media-type icons and colour coding. |
| Command Palette navigation | **FAIL** | Selecting a search result navigates to `/hub/{hubId}`, but **no Hub detail page exists**. Users land on the 404 page. |
| Intent Dock (filtering) | **FAIL** | The dock renders correctly, but the `OnIntentChanged` callback is never wired in the layout. Clicking Watch / Read / Listen has no effect. |
| Progress indicators (Hero) | **FAIL** | All three bars (Watch, Read, Listen) show "--" and 0%. The UserState API required to populate them does not exist. |
| Compact List view | **WARN** | Toggle button exists but has no effect — list view is not implemented. |
| Automotive Mode | **PASS** | Audio-only filtering works in the UniverseStack component. |
| Universe grouping | **WARN** | Universe entity exists in the domain but is never surfaced in the Dashboard. All Hubs display in a flat list. |

---

## PO Summary

The Library Dashboard presents your collection as a beautiful, responsive Bento grid with live updates from the Engine. Hubs display correctly with media-type icons and accent colours. **Three features are visually present but not yet connected: (1) the Command Palette's search results lead to a non-existent Hub detail page, (2) the Intent Dock's filter buttons do nothing, and (3) the Hero tile's progress bars are always empty because progress tracking isn't built yet.**
