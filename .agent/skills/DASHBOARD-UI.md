# Skill: Dashboard UI Operations

> **Mirrors:** `CLAUDE.md` §6 (Feature-Sliced Dashboard Layout) — keep both in sync per `.agent/SYNC-MAP.md`

> Last updated: 2026-03-07

---

## Purpose

This skill covers all Dashboard visual components, navigation, state management, and theming.

---

## Key files

| File | Role |
|------|------|
| `src/MediaEngine.Web/Shared/MainLayout.razor` | App shell: TopBar + LeftDock (desktop) or TopBar + MobileNavDrawer (mobile). Dark-mode only. Review badge on profile avatar. |
| `src/MediaEngine.Web/Components/Pages/Home.razor` | Library overview: Cinematic hero + poster swimlanes grouped by media type |
| `src/MediaEngine.Web/Components/Pages/Settings.razor` | Unified settings: sidebar + content, tabs in 3 groups |
| `src/MediaEngine.Web/Components/Pages/NotFound.razor` | 404 page |
| `src/MediaEngine.Web/Components/Universe/CollectionHero.razor` | Cinematic hero banner: blurred cover art + vignette + metadata badges |
| `src/MediaEngine.Web/Components/Universe/PosterCard.razor` | Poster art card (2:3 aspect ratio, cover image, title, badges) |
| `src/MediaEngine.Web/Components/Universe/PosterSwimlane.razor` | Horizontal scrolling row of PosterCards with section title |
| `src/MediaEngine.Web/Components/Universe/ProgressIndicator.razor` | Reusable progress card (icon + bar + label) |
| `src/MediaEngine.Web/Components/Bento/BentoGrid.razor` | CSS grid container (responsive) — used in settings, not home page |
| `src/MediaEngine.Web/Components/Bento/BentoItem.razor` | Glassmorphic tile with dynamic glow |
| `src/MediaEngine.Web/Components/Navigation/CommandPalette.razor` | Ctrl+K global search overlay |
| `src/MediaEngine.Web/Components/Navigation/TopBar.razor` | Horizontal top bar: logo, search, bell, profile avatar. 4 variants (full/mobile/simplified/minimal) |
| `src/MediaEngine.Web/Components/Navigation/LeftDock.razor` | Icon-only 52px vertical rail. Hover-expands to 200px with labels. Desktop only. |
| `src/MediaEngine.Web/Components/Navigation/MobileNavDrawer.razor` | Slide-out drawer for mobile: logo + nav items + settings link |
| `src/MediaEngine.Web/Components/Navigation/AppLogo.razor` | SVG logo component: wordmark or icon variant |
| `src/MediaEngine.Web/Services/Integration/UniverseStateContainer.cs` | Per-circuit state cache |
| `src/MediaEngine.Web/Services/Integration/UniverseMapper.cs` | Collection→ViewModel mapping + colour classification |
| `src/MediaEngine.Web/Services/Theming/ThemeService.cs` | Dark-mode only theme, accent colour (fixed golden amber #C9922E) |
| `src/MediaEngine.Web/Services/Theming/DeviceContextService.cs` | Per-circuit device class + resolved UI settings |

---

## Adding a new page

1. Create a `.razor` file in `src/MediaEngine.Web/Components/Pages/`.
2. Add `@page "/your-route"` directive.
3. Inject `UIOrchestratorService` for Engine data, `DeviceContextService` for device-aware layout.
4. If the page needs live updates, call `Orchestrator.StartSignalRAsync()` in `OnAfterRenderAsync`.

---

## Adding a new component

1. Place it in the appropriate `Components/` subfolder:
   - `Universe/` for Collection-related visuals (hero, poster cards, swimlanes)
   - `Bento/` for layout grid pieces
   - `Navigation/` for navigation/search (TopBar, LeftDock, MobileNavDrawer, CommandPalette)
   - `Settings/` for settings tab components
   - `Playback/` for media player components (target state)
2. Use `@namespace MediaEngine.Web.Components.<Folder>`.
3. Follow dark-mode-only styling. No light-mode CSS needed.

---

## CSS custom properties (theme)

| Property | Purpose |
|----------|---------|
| `--tuvima-glass-bg` | Glass background (transparent dark) |
| `--tuvima-glass-border` | Glass border colour |
| `--tuvima-glass-inner-bg` | Inner card background |
| `--tuvima-glass-inner-border` | Inner card border |
| `--mud-palette-primary` | Accent colour (fixed golden amber #C9922E) |

---

## Navigation architecture

| Device | TopBar variant | Dock | Mobile drawer |
|--------|---------------|------|---------------|
| Desktop web | `"full"` (logo + search + bell + avatar) | 52px icon rail, hover-expand 200px | No |
| Mobile | `"mobile"` (hamburger + logo + compact) | Hidden | Yes |
| Television | `"simplified"` (logo only) | Hidden | No |
| Automotive | `"minimal"` (icon only) | Hidden | No |

Config-driven: `UIShellSettingsDto.TopBarStyle`, `UIShellSettingsDto.DockVisible`

---

## State management model

- **UniverseStateContainer** — Scoped (one per Blazor Server circuit). Caches collections, selected collection, universe view, activity log.
- **SignalR events** arrive on background threads → pushed into the state container → `OnStateChanged` fires → components re-render.
- **Cache invalidation** — `MediaAdded` and `MetadataHarvested` events call `Invalidate()`. Next `GetCollectionsAsync()` call fetches fresh data.

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

## Deleted components

| Component | Replaced by | When |
|-----------|------------|------|
| `UniverseStack.razor` | PosterSwimlane rows | 2026-03-07 |
| `NavigationTray.razor` | LeftDock + TopBar | 2026-03-06 |
| Light-mode CSS (`:root` block) | Dark-only `:root` | 2026-03-07 |
| Dark/light toggle (GeneralTab) | Removed entirely | 2026-03-07 |

---

## Known gaps

1. **No Collection detail page** — `/collection/{collectionId}` route does not exist. Command Palette links to it.
2. **Progress bars are stubs** — UserState API does not exist yet.
3. **Continue Journey / Recently Added swimlanes** — Stubs pending API endpoints.
4. **Cover art availability** — Depends on metadata providers having been run. No cover = gradient fallback.
