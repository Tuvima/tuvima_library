# Tuvima Design System

Design system for **Tuvima Library** — a unified media intelligence platform that organizes books, audiobooks, movies, TV, music, and comics by story rather than by file type. The visual language is **cinematic, minimalist, dark-only**, built around deep-navy surfaces, glassmorphic panels, and a single golden amber accent.

## Sources

- **Source repo:** `Tuvima/tuvima_library` (https://github.com/Tuvima/tuvima_library) — .NET 10 / Blazor + MudBlazor 9 codebase. The Dashboard ships as `src/MediaEngine.Web`.
- **Core stylesheet referenced:** `src/MediaEngine.Web/wwwroot/app.css`
- **Icon system:** FontAwesome solid SVGs copied from `src/MediaEngine.Web/wwwroot/icons/fontawesome/solid/`
- **Fonts:** Montserrat (variable), Merriweather, JetBrains Mono — all self-hosted in `src/MediaEngine.Web/wwwroot/fonts/`
- **Logos:** `assets/images/tuvima-logo.svg`, `tuvima-logo-dark.svg`, `tuvima-icon.svg`
- **Screenshots:** `assets/screenshots/epub-reader.png`

## Products

Tuvima Library is a single-product system with two distinct UI surfaces:

1. **Dashboard** — the cinematic library browser (home, swimlanes, collection detail, settings, vault). Dark, glassy, full-bleed hero banners.
2. **EPUB Reader** — the in-app reader surface (`/read/{assetId}`). Serif, page-like, softer tones.

This system covers both.

---

## Content Fundamentals

The voice is **warm, literary, and a little reverent** — it treats a media collection as a personal archive worth presenting, not just a folder tree to sort. Marketing lines ("Make your media collection discoverable", "You already own the stories. Tuvima makes them discoverable.") set the frame: Tuvima doesn't *create* a library, it **presents** one. Every feature copy choice should preserve that philosophy.

**Tone**
- Confident, direct, never marketing-hype. No exclamation marks in UI copy.
- Slightly literary — favors evocative nouns (library, story, vault, universe, chronicle) over generic SaaS words (workspace, project, dashboard panel).
- Privacy is framed as a promise, not a feature: "no accounts", "no telemetry", "no cloud AI".

**Casing**
- **Sentence case** for body copy, descriptions, toast messages, and menu items ("Resolving reviews", "Adding media").
- **Title Case** for page titles, tab labels, section headings, and CTAs ("Continue Your Journey", "Recently Added", "Library Vault").
- Proper nouns are capitalized and capitalized consistently: **Library**, **Universe**, **Series**, **Work**, **Edition**, **Media Asset**, **Vault**, **Action Center**, **Engine**, **Dashboard**, **Chronicle**.

**Person**
- Default to **you** ("Drop your files into a folder", "Your Dune ebook lives here"). Never "users".
- Speak about the product in the third person ("Tuvima reads each one", "The Engine benchmarks your hardware").

**Emoji & symbols**
- **No emoji** in UI. Icons are FontAwesome solid SVGs.
- Middle-dot `·` used as meta separator ("Neil Gaiman · Audiobook · 2019").
- En dash `—` used for copy beats, never `--`.

**Concrete examples**
- Empty state: *"Your library is empty. Drop files into your watched folder to begin."*
- CTA: *"Continue Reading"*, *"Open Collection"*, *"Seed Library"* — verb + specific object.
- Hero subtitle: *"You've read 38% of Dune — pick up where you left off."* (resolved from phrase templates)
- Toast: *"Library seeded."* / *"Could not reach the Engine."*
- Section labels (small caps): *"IN PROGRESS"*, *"RECENTLY ADDED"*, *"YOU MIGHT LIKE"*.

---

## Visual Foundations

**Palette** — dark-only, cinematic. Deep navy base (`#0B1220 → #111827`) with soft radial glows of blue (`rgba(59,130,246,.12)`) top-left and cyan (`rgba(14,165,233,.10)`) top-right. Every card is a **glassmorphic** layer over this base: `rgba(255,255,255,0.02)` fill, `rgba(255,255,255,0.06)` border. The single accent is **golden amber** (`#EAB308` bright, `#C9922E` deep) — used for active nav, primary CTAs, in-progress indicators, and nothing else. Media-type accents (books indigo, audiobooks blue, movies pink, TV emerald, music amber, comics orange) are reserved for provider badges and media-type chips, never page chrome.

**Typography** — **Montserrat** (variable) is the UI face, set tight (letter-spacing `-0.02em` on headings). Weights used: 400 body, 500 labels, 600 card titles, 700 section headings, 800 page titles / small-caps labels. **Merriweather** serif is scoped to the EPUB reader surface only. **JetBrains Mono** for inline code / technical values. Small-caps metadata labels are a signature: `font-weight: 800; font-size: 10px; letter-spacing: 0.10em; text-transform: uppercase; color: rgba(248,248,248,0.35)`.

**Spacing** — roughly 8pt: `4 / 8 / 12 / 16 / 20 / 24 / 32 / 40 / 48 / 64`. Page gutters are generous (`padding: 24px` min). Swimlanes use tight horizontal gaps (`gap: 12px`) so covers feel like a continuous shelf.

**Backgrounds** — the base page uses the fixed navy gradient. Detail pages overlay a **fixed, full-bleed blurred cover image** (`filter: blur(40px) saturate(0.6)`) with a vertical fade into the page background (`transparent → rgba(6,10,22,1)` by 80%). A **page ambient glow** radial (`ellipse 110% 55% at 50% 0%`) tinted by the hero collection's dominant color washes the viewport at 14% opacity. No patterns, no textures, no illustrations. Just fades, blur, and light.

**Animation** — restrained. Hover/focus transitions `150–200ms` ease. Hero slide enter: `opacity 0 → 1` + `translateY(6px → 0)` over `550ms`. Carousel auto-advance every `6s`, pauseable on hover. Dot indicator active: `background: #C9922E; transform: scale(1.35)` with `250ms ease`. Ambient glow crossfade `600ms ease`. No bounces, no springs, no parallax.

**Hover states** — surfaces gain `background: rgba(255,255,255,0.05)` (aka `--tv-hover-overlay`). Ghost buttons add a subtle border-color shift. Primary amber CTAs brighten the gradient (`#D4A03C→#B87820` becomes `#DDA840→#C08428`) and strengthen shadow (`0 4px 18px → 0 6px 24px rgba(185,120,32,0.50)`). Icons lift from 35% to ~88% opacity on hover.

**Press states** — no visible shrink. Amber active overlay (`rgba(234,179,8,0.22)`) applies to active nav, selected toggle items.

**Borders** — almost always `1px solid rgba(255,255,255,0.06)` (inner-card) or `rgba(255,255,255,0.08)` (outer shell). Focus rings on text inputs flip to `rgba(255,255,255,0.8)`. No colored borders except the amber CTA's subtle `1.5px solid rgba(0,0,0,0.15)` inner edge.

**Shadow system**
- `--tv-shadow-sm: 0 2px 8px rgba(0,0,0,0.30)` — card rest
- `--tv-shadow: 0 8px 32px rgba(0,0,0,0.50)` — glass panels
- `--tv-shadow-md: 0 16px 36px rgba(15,23,42,0.40)` — modals
- `--tv-shadow-lg: 0 22px 48px rgba(15,23,42,0.60)` — hero
- `--tv-inset-glow: 0 0 0 1px rgba(255,255,255,0.04) inset` — subtle rim light on glass
- `--tv-amber-glow: 0 4px 18px rgba(185,120,32,0.30)` — amber CTA rest
- `--tv-amber-glow-strong: 0 6px 24px rgba(185,120,32,0.50)` — amber CTA hover

**Protection gradients** — used on hero banners to keep text legible against cover art: vertical linear-gradient from `rgba(6,10,22,0.10) 0%` through `0.55 @ 35%`, `0.90 @ 60%`, `1.00 @ 80%`. No capsule/pill backgrounds around hero text — the gradient does the work.

**Layout rules**
- **Top bar** is fixed at `72px`, z-index above MudBlazor app bar (`calc(appbar + 150)`).
- **Left dock** is floating glass — does not offset content (`--tv-dock-width: 0px` in flow).
- **Swimlanes** scroll horizontally, covers are fixed-width (`--cardWidth`, typically 160–200px), gap 12px.
- **Detail pages** scroll a transparent content layer on top of the fixed hero.
- Mobile (`≤768px`) hides TopBar actions (bell/avatar); nav drawer slides from left with 100dvh height.

**Transparency & blur** — used intentionally, not decoratively:
- Glass panels: `rgba(12,16,36,0.80)` + `backdrop-filter: blur(8px)`.
- Carousel arrows: `rgba(0,0,0,0.55)` + `blur(8px)`, opacity `0 → 1` on carousel hover.
- Hero cover blur fallback: `filter: blur(40px) saturate(0.6)` on cover art when no dedicated hero asset exists.

**Imagery tone** — covers are shown as-is, never cropped beyond a 2:3 poster ratio, never filtered except the deliberate 40px-blur hero fallback. No b&w, no duotone. Trust the source art.

**Corner radii** — `xs 6px · sm 8px · md 10px · lg 12px · xl 16px · 2xl 20px · pill 999px`. Cards use `12px` (`--tv-radius-lg`). CTAs use `8px`. Toggles use `14px` outer / `12px` inner. Pills for counts use `999px`.

**Cards** — glassmorphic surfaces. Default: `background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 12px; padding: 20px 24px;`. Accent (featured) variant uses `1px solid rgba(201,146,46,0.2)` amber border and `color: #C9922E` for the title. No drop shadow at rest — shadow shows only on hovered/floating surfaces (hero, modals).

---

## Iconography

The codebase uses **FontAwesome Solid** SVGs served statically from `/icons/fontawesome/solid/{name}.svg`. An `AppIconCatalog` (in `src/MediaEngine.Web/Components/Shared/AppIconCatalog.cs`) aliases semantic keys (`search`, `home`, `read`, `watch`, `listen`, `collections`, `settings`, `intelligence`, `server`, etc.) to specific solid icons (`magnifying-glass`, `house`, `book-open-reader`, `film`, `headphones`, `boxes-stacked`, `gear`, `wand-magic-sparkles`, `server`…). MudBlazor Material Icons are used only for provider-accent icons (TMDB, Wikidata, MusicBrainz) and MudBlazor's own internal widgets — the design system itself is FontAwesome-first.

**Rules**
- No emoji anywhere in the UI.
- No unicode dingbats as icons. The middle-dot `·` is used as a meta separator only.
- No hand-drawn/custom SVG illustrations. The **Tuvima mark** (the golden spiral) is the only bespoke glyph — used as logo, favicon, and empty-state hero.
- Default icon size: `16px` in nav / inline, `20px` in buttons, `24–32px` in cards, `80px` in empty states.
- Default icon color: `rgba(248,248,248,0.55)` muted, `rgba(248,248,248,0.88)` hover. Accent icons on active nav flip to the amber.
- Provider icons (TMDB, Wikidata, etc.) use their brand colors (`#01B4E4` TMDB, `#339966` Wikidata, `#BA478F` MusicBrainz) — never the UI amber.

**Copied icons** (in `assets/icons/`): `book-open`, `book-open-reader`, `boxes-stacked`, `cart-shopping`, `chevron-down/left/right`, `circle-info`, `clipboard-check`, `clock`, `film`, `folder-open`, `folder-tree`, `gear`, `headphones`, `house`, `layer-group`, `list-check`, `magnifying-glass`, `microchip`, `music`, `play`, `server`, `share-nodes`, `shield-halved`, `sliders`, `table-list`, `timeline`, `toggle-on`, `triangle-exclamation`, `tv`, `user`, `users`, `wand-magic-sparkles`, `wrench`, `xmark`.

**Logo usage**
- Full horizontal logo (`assets/images/tuvima-logo.svg`) — headers, login screens, marketing.
- Icon mark (`assets/images/tuvima-icon.svg`) — favicon, empty-state centerpiece, app icon.
- Never replace logo placements with hand-typed "TUVIMA" text.

---

## Index

Root files:
- `README.md` — this file
- `SKILL.md` — agent skill manifest
- `colors_and_type.css` — design tokens (CSS custom properties) + typography roles
- `assets/` — logos, icons, favicons, screenshots
- `fonts/` — Montserrat, Merriweather, JetBrains Mono TTFs
- `preview/` — design-system cards rendered for the Design System tab
- `ui_kits/dashboard/` — the Cinematic Dashboard UI kit (JSX components + index.html)

UI kits:
- **Dashboard** (`ui_kits/dashboard/`) — home, hero carousel, swimlanes, top bar, left dock, cards, search, collection detail, settings panel.
