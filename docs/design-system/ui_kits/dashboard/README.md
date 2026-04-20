# Dashboard UI Kit

Cinematic dashboard surface for Tuvima Library. This is the "home" product of the codebase — the library browser with swimlanes, hero carousel, collection detail, and glassmorphic chrome.

## Files

- `index.html` — runs the full clickable prototype (home → collection detail)
- `styles.css` — scoped styles, mirrors `app.css` from `src/MediaEngine.Web/wwwroot/`
- `data.js` — fixture library (hero slides, swimlane items, Dune collection)
- `components.jsx` — reusable React components: `Dock`, `TopBar`, `HeroCarousel`, `SectionHead`, `Poster`, `Lane`, `ActionStrip`, `CollectionDetail`, `Icon`
- `app.jsx` — app shell with simple route state (`home` ↔ `detail/{slug}`), persisted to `localStorage`

## What to copy into a new design

- **`Dock`** — 60px floating left nav, glassmorphic, amber-tinted active state
- **`TopBar`** — sticky glass bar with tabs + count badges + icon actions
- **`HeroCarousel`** — 21:9 hero with auto-advance (6s), dot indicators, dominant-color ambient glow
- **`Poster`** — 2:3 aspect, progress bar, New ribbon, media-type color dots
- **`Lane`** — horizontal swimlane with "See all" header
- **`CollectionDetail`** — fixed blurred hero, scrolling content layer, edition list, detail tabs
- **`cta` / `cta-ghost`** — the amber CTA gradient that shows up everywhere, and its ghost companion

## Interaction behaviors implemented

- Hero auto-advance every 6s; manual prev/next; dot click jumps
- Ambient radial glow behind everything, colored by the current hero/collection's dominant hex
- Click any poster → navigates to the Dune collection detail page (all posters point to the same demo collection for the prototype)
- Back button returns to the Home view; tab state is preserved across reload via `localStorage`

## Known omissions

- No real filter/sort UI yet (the "Sliders" topbar button is a stub)
- Only the Dune collection has full detail data — other posters route to it as a demo
- The Reader, Vault, Settings screens from the codebase are **not** rebuilt in this kit; they live in the production Blazor codebase and can be added later on request
