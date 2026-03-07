# Skill: Dashboard UI Operations

> **Mirrors:** `CLAUDE.md` §6 (Feature-Sliced Dashboard Layout) — keep both in sync per `.agent/SYNC-MAP.md`

> Last updated: 2026-03-01

---

## Purpose

This skill covers all Dashboard visual components, navigation, state management, and theming.

---

## Key files

| File | Role |
|------|------|
| `src/MediaEngine.Web/Shared/MainLayout.razor` | App shell: AppBar (logo left, action cluster right), Ctrl+K, NavigationTray |
| `src/MediaEngine.Web/Components/Pages/Home.razor` | Library overview: Hero + Bento grid |
| `src/MediaEngine.Web/Components/Pages/NotFound.razor` | 404 page |
| `src/MediaEngine.Web/Components/Universe/HubHero.razor` | Last Journey hero tile (artwork + progress) |
| `src/MediaEngine.Web/Components/Universe/ProgressIndicator.razor` | Reusable progress card (icon + bar + label) |
| `src/MediaEngine.Web/Components/Universe/UniverseStack.razor` | Your Universes Bento grid |
| `src/MediaEngine.Web/Components/Bento/BentoGrid.razor` | CSS grid container (responsive) |
| `src/MediaEngine.Web/Components/Bento/BentoItem.razor` | Glassmorphic tile with dynamic glow |
| `src/MediaEngine.Web/Components/Navigation/CommandPalette.razor` | Ctrl+K global search overlay |
| `src/MediaEngine.Web/Components/Navigation/NavigationTray.razor` | Content-aware bottom tray: Virtual Libraries + Search |
| `src/MediaEngine.Web/Services/Integration/UniverseStateContainer.cs` | Per-circuit state cache |
| `src/MediaEngine.Web/Services/Integration/UniverseMapper.cs` | Hub→ViewModel mapping + colour classification |
| `src/MediaEngine.Web/Services/Theming/ThemeService.cs` | Dark/light mode, accent colour |

---

## Adding a new page

1. Create a `.razor` file in `src/MediaEngine.Web/Components/Pages/`.
2. Add `@page "/your-route"` directive.
3. Inject `UIOrchestratorService` for Engine data, `ThemeService` for theming.
4. If the page needs live updates, call `Orchestrator.StartSignalRAsync()` in `OnAfterRenderAsync`.

---

## Adding a new component

1. Place it in the appropriate `Components/` subfolder:
   - `Universe/` for Hub-related visuals
   - `Bento/` for layout grid pieces
   - `Navigation/` for navigation/search
   - `Settings/` for settings tab components
2. Use `@namespace MediaEngine.Web.Components.<Folder>`.
3. Follow the glassmorphic styling pattern (use CSS custom properties `--tuvima-glass-*`).

---

## CSS custom properties (theme)

| Property | Purpose |
|----------|---------|
| `--tuvima-glass-bg` | Glass background (transparent white/black) |
| `--tuvima-glass-border` | Glass border colour |
| `--tuvima-glass-inner-bg` | Inner card background |
| `--tuvima-glass-inner-border` | Inner card border |
| `--mud-palette-primary` | Accent colour (set via ThemeService) |

---

## State management model

- **UniverseStateContainer** — Scoped (one per Blazor Server circuit). Caches hubs, selected hub, universe view, activity log.
- **SignalR events** arrive on background threads → pushed into the state container → `OnStateChanged` fires → components re-render.
- **Cache invalidation** — `MediaAdded` and `MetadataHarvested` events call `Invalidate()`. Next `GetHubsAsync()` call fetches fresh data.

---

## Media type colour palette

| Bucket | Hex | Used for |
|--------|-----|----------|
| Book | `#FF8F00` (amber) | EPUB, books |
| Video | `#00BFA5` (teal) | Movies, MKV, MP4, AVI |
| Comic | `#7C4DFF` (violet) | CBZ, CBR |
| Audio | `#EC407A` (rose) | Audiobooks, MP3, FLAC |
| Unknown | `#9E9E9E` (slate) | Unclassified |

---

## Known gaps

1. **No Hub detail page** — `/hub/{hubId}` route does not exist. Command Palette links to it.
2. **NavigationTray replaces IntentDock** — content-aware Virtual Library filtering is fully wired.
3. **Progress bars are stubs** — UserState API does not exist yet.
4. **Compact List view is a stub** — toggle exists but has no effect.
5. **Universe grouping not surfaced** — domain entity exists but Dashboard shows flat Hub list.
6. **Accent swatch checks dark palette only** — may not highlight correctly in light mode.
