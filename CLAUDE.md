# CLAUDE.md — Tuvima Library Project Memory

> **Who reads this file?**
> Every Claude session working on this repository reads this file automatically before doing anything else.
> It is the single, authoritative source of truth for what Tuvima Library is, how it is built, and how to work on it.
> It bridges the Product Owner's business goals with the technical team's execution.

---

## 1. Project Overview

### What is Tuvima Library?

#### Name & Vision

**Tuvima Library** is the product name. **Tuvima** is the company. Code namespaces use `MediaEngine.*` intentionally — decoupled from branding for future resilience.

The project's core philosophy is **Presentation** — the act of bringing something forward and making it whole.

Tuvima Library does not create a library. It **presents** one. The stories already exist on the hard drive, fragmented across formats and folders. The Library's job is to find them, understand them, unify them, and surface the result as something coherent and beautiful — as if it always belonged together.

Every feature exists in service of that word:
- The **Intelligence Engine** works invisibly so the library is already whole when you look at it.
- The **Hub** is the act of presentation made structural — the book, film, and audiobook of the same story brought forward as one.
- The **Cinematic Dashboard** is the presentation layer made visible — the interface where the Engine's understanding reaches the screen.

> **All future sessions must preserve this creative context.** When writing copy, naming features, or explaining the product, the Presentation philosophy should be the frame.

#### What it does

**Tuvima Library** is a **unified media intelligence platform** that runs entirely on your own machine — no cloud account, no subscription, no data leaving your home.

Its core job is to bring order to a large, messy media collection spread across folders. You point it at your hard drive, and it automatically:

1. **Watches** your folders for new files — books, audiobooks, comics, TV shows, movies, music, and podcasts.
2. **Fingerprints** each file with a unique identifier (like a barcode), so it can track files even if you rename or move them.
3. **Reads the embedded information** inside each file — title, author, year, cover art, series name — and uses a *Weighted Voter* system to determine the most trustworthy version of each piece of information.
4. **Groups everything into Hubs** — a single, intelligent home for all versions of the same story.
5. **Serves a visual dashboard** in your browser for browsing, searching, and managing the library.
6. **Broadcasts instant updates** to your dashboard the moment a new file is detected, with no page refresh.

### The Hub Concept

The central idea in Tuvima Library is the **Hub**.

A Hub is a **virtual container** — not a folder on disk, not a single title, but an intelligent grouping that links multiple media forms through their shared contextual metadata. A Hub represents a creative universe: the books, films, audiobooks, comics, and podcasts that belong together because their metadata says so.

The matching is automatic. When the Engine discovers that a novel, its film adaptation, and a podcast discussion share the same author, franchise identifiers, or Wikidata Q-identifier, it groups them into a single Hub. You browse by universe, not by file type.

This can be as simple as grouping books by the same author, or as rich as following a story from an ebook into its movie adaptation, or linking a podcast that covers the same topic.

> *Example: The "Dune" Hub might contain:*
> - *Frank Herbert's novels (EPUB ebooks)*
> - *The Denis Villeneuve film adaptations (MP4 videos)*
> - *The audiobook narrations (M4B)*
> - *The graphic novel adaptations (CBZ comics)*
> - *A related podcast series discussing the Dune universe*
>
> *These are linked not because they share a filename, but because their metadata — author, franchise QID, series identifiers — connects them to the same creative universe.*

**A Hub is not limited to a series.** While a Hub often represents a book series or film franchise, it is a flexible virtual container for *any* creative grouping — film adaptations of a novel, spin-off works, thematic collections, or cross-media narrative links. What defines a Hub is shared contextual metadata, not a shared format or filesystem location.

The Hub hierarchy works like this:

```
Universe  (your entire library — all Hubs)
  └── Parent Hub  (franchise/universe — e.g. "Dune" — groups related Series Hubs)
        └── Hub  (series or thematic collection — e.g. "Dune Novels" or "Dune Films")
              └── Work  (one title — e.g. "Dune Part One")
                    └── Edition  (one physical version — e.g. "4K HDR Blu-ray Remux")
                          └── Media Asset  (one file on disk)
```

**Parent Hubs** are optional. A Hub that belongs to no larger franchise sits directly under the Universe. When the Engine discovers franchise-level relationships (Wikidata P8345 franchise, P179 series, or shared narrative roots from §3.22), it can promote a group of related Hubs under a common Parent Hub. For example, the "Dune" Parent Hub would contain the "Dune Novels" Hub, the "Dune Films" Hub, and potentially a "Dune Audiobooks" Hub — each a distinct series or thematic collection, all sharing the same creative universe.

**Important:** Both Hubs and Parent Hubs are resolved at metadata-scoring time by the Intelligence Engine. They have no presence on the filesystem — files are organized by category and title, not by Hub. Parent Hubs add a layer to Dashboard navigation (breadcrumbs become Universe → Parent Hub → Hub → Work) but do not affect file organization.

### Who is it for?

A single power user who wants complete, private control over a large media collection — without depending on services like Plex, Jellyfin, or any subscription platform.

---

## 2. Technical Stack

> **Note to Claude:** When speaking to the Product Owner, always use the plain-English column, not the technical column.
> The technical column is for implementation reference only.

### Core Tools

| Plain-English name | Technical name | Purpose |
|---|---|---|
| Programming language | C# / .NET 10 | Everything is written in this language |
| Local database | SQLite | A single file on disk that stores the entire library catalogue — no separate database server needed |
| Visual interface library | MudBlazor 9 | Pre-built visual building blocks (cards, grids, buttons) for the browser dashboard |
| Real-time intercom | SignalR | Pushes live updates (e.g. "new book added") to the dashboard without a page refresh |
| Book file reader | VersOne.Epub | Reads the information embedded inside EPUB book files |
| Engine API documentation | Swashbuckle | Auto-generates an interactive, explorable menu of all the Engine's capabilities at `/swagger` |
| Structured logging | Serilog | Writes rolling log files so the Engine's actions can be reviewed after the fact |
| Resilient HTTP calls | Polly (Microsoft.Extensions.Http.Resilience) | Automatically retries failed external API calls with backoff and circuit-breaking |
| Wikidata/Wikipedia API client | Tuvima.WikidataReconciliation | Unified client for Wikidata reconciliation, entity/property fetching, Wikipedia summaries, and image URLs — with built-in rate limiting, retry, and maxlag support |
| Cron scheduling | Cronos | Runs background maintenance tasks at specific times (e.g. 3 AM) instead of fixed intervals |
| Standard health probe | ASP.NET Core Health Checks | Exposes `/health` for Docker, monitoring tools, and container orchestrators |
| Data access layer | Dapper | Lightweight data-access helper that maps database rows to C# objects by column name |
| Automated quality checks | xUnit + coverlet | Runs a set of automated tests after every change to catch mistakes early |
| Version control | Git + GitHub | Tracks every change made to the code, with full history |

### Headless Design — Engine and Dashboard are Separate

**This is a critical architectural decision.** The Tuvima Library *Engine* (the intelligence and data layer) is completely separate from the *Dashboard* (the visual interface).

**Why this matters to the business:**
- **Extensibility:** Other applications — media managers, automation tools, or a custom mobile app — can connect directly to the Engine without needing the Dashboard. The Engine speaks a universal language (HTTP/JSON) that any app can understand.
- **Maintenance:** If the Dashboard needs to be redesigned, the Engine keeps running untouched. Each part can be updated independently.
- **Privacy:** The Engine can run as a silent background service with no interface at all, while the Dashboard is opened only when needed.

| Part | Technical project | Role |
|---|---|---|
| The Engine | `MediaEngine.Api` | Handles all intelligence, data, and file operations. Exposes an API that any app can talk to. |
| The Dashboard | `MediaEngine.Web` | The browser interface. Purely visual — it asks the Engine for data and displays it. |

### Source Code Layout (Plain English)

| Folder | What it is | Plain-English role |
|---|---|---|
| `src/MediaEngine.Domain` | The Rulebook | Defines what a Hub, Work, and Edition *are*. No external tools — pure business logic. |
| `src/MediaEngine.Storage` | The Filing Clerk | Reads and writes the SQLite database. Keeps all library data safe. |
| `src/MediaEngine.Intelligence` | The Analyst | Runs the Weighted Voter system. Scores metadata Claims. Detects duplicates. |
| `src/MediaEngine.Processors` | The Scanner | Opens each file type (EPUB, video, comic) and extracts its embedded information. |
| `src/MediaEngine.Providers` | The Research Team | Fetches enriched metadata from external sources (Apple API, Wikidata Reconciliation, TMDB, Open Library, Google Books). Runs non-blocking on a background channel. |
| `src/MediaEngine.Ingestion` | The Mail Room | Monitors Library Folders (watch and import modes). Queues new files. Manages the safe move or copy of files into the organised library. |
| `src/MediaEngine.AI` | The Brain | Local AI inference (LLamaSharp + Whisper.net). Smart Labeling, media type classification, QID disambiguation, vibe tagging, intent search, and all 16 AI features. |
| `src/MediaEngine.Api` | The Reception Desk | The Engine's public interface. Exposes all features over HTTP. Hosts the real-time intercom. |
| `src/MediaEngine.Web` | The Showroom | The browser Dashboard. Uses the Feature-Sliced layout (see Section 6). |
| `tests/` | The Quality Inspector | Automated checks for every module. |

### Dashboard Internal Layout (Feature-Sliced Design)

Inside `src/MediaEngine.Web`, the code is organised by *what it does*, not by *what type of code it is*. This matches industry best practice for maintainable interfaces.

| Folder | Purpose |
|---|---|
| `Services/Integration/` | All communication with the Engine (HTTP calls, SignalR intercom) |
| `Services/Theming/` | Visual appearance — dark/light mode, colour palette, corner radii |
| `Components/Universe/` | Hub grid, hero card, media item cards |
| `Components/Bento/` | Legacy grid wrappers — primary layout is Poster Swimlanes in `Components/Universe/` |
| `Components/Navigation/` | Command palette, navigation menu |
| `Models/ViewDTOs/` | The data shapes used by the Dashboard (separate from the Engine's data shapes) |
| `Shared/` | Top-level layout, navigation shell, shared imports |

---

## 3. Technical Strategy & Key Features

### 3.1 — Library Folders & Intake (Ingestion Engine)

**Plain English:** You configure one or more **Library Folders** — each one tells the Engine where to look for files and what kind of media to expect. The Engine monitors these folders and automatically processes anything that appears.

**Library Folders** — each folder entry has:
- **Category** — the content category this folder belongs to (Books, TV, Movies, Music, Comics, Podcasts). This is the top-level grouping that determines how files are organized on disk.
- **Media Type** — the expected media type(s) within this category. Different media types trigger different processing behaviours and metadata providers. For example, a "Books" category folder may contain both `Epub` and `Audiobook` media types — each processed by its own scanner and matched against different provider pipelines.
- **Source Path** — the folder the Engine monitors or imports from.
- **Intake Mode:**
  - **Watch** (default) — monitors for new files, **always moves** them into the organised library structure. This is the active inbox.
  - **Import** — scans an existing collection. The user chooses whether to **move** files (relocate into organised structure) or **copy** them (leave originals in place, create organised copies).
- **Library Root** — the destination folder where organised files are placed. Can be shared across library folders or separate per folder.

The category tells the Engine *where* to organise files. The media type tells it *how* to process them — which scanner to use, which metadata providers to query, and what fields to expect. Knowing the expected media type provides a strong confidence prior (0.80) for disambiguation — if a library folder is designated for Movies, an MP4 file skips heuristic guessing and goes straight to TMDB for metadata.

**Configuration** (`config/libraries.json`):
```json
{
  "libraries": [
    {
      "category": "Movies",
      "media_types": ["Movies"],
      "source_path": "/media/downloads/movies",
      "library_root": "/media/library",
      "intake_mode": "watch"
    },
    {
      "category": "Books",
      "media_types": ["Epub", "Audiobook"],
      "source_path": "/media/existing-audiobooks",
      "library_root": "/media/library",
      "intake_mode": "import",
      "import_action": "copy"
    },
    {
      "category": "TV",
      "media_types": ["TV"],
      "source_path": "/media/tv-shows",
      "library_root": "/media/library",
      "intake_mode": "watch"
    }
  ]
}
```

**Backward compatibility:** When `config/libraries.json` is absent, the single `WatchDirectory`/`LibraryRoot` from `core.json` creates an implicit default library folder with Watch mode and all media types enabled (disambiguation heuristics apply).

**What happens when a file arrives (Watch mode):**

1. **Settle** — The Engine waits briefly to make sure the file has finished copying (prevents reading half-written files).
2. **Lock check** — It confirms no other program is currently using the file.
3. **Fingerprint** — It computes a unique hash of the file's contents (like a barcode). This is the file's permanent identity — it survives renaming and moving.
4. **Scan** — The appropriate Scanner (book/video/audio/comic) opens the file and reads all embedded metadata.
5. **Identify** — The Analyst runs the Weighted Voter to assign the file to a Hub (or create a new one).
6. **Move** — The file is moved from the inbox to a clean, organised folder structure in the library.

**Import mode** follows steps 1–5 identically, then either **moves** or **copies** the file depending on `import_action`. After import completes, the folder can optionally switch to Watch mode for ongoing monitoring.

**Why this matters to the business:**
- **Reliability** — Once configured, the library manages itself. Drop files in; the Library does the rest.
- **Extensibility** — Each media type routes to its own provider pipeline (Books → Apple API/Google Books, Movies → TMDB, Music → MusicBrainz, etc.).
- **Privacy** — All processing happens locally. Import mode with "copy" preserves originals untouched.

### 3.2 — The Priority Cascade Engine (Intelligence Engine)

**Plain English:** Different information sources disagree about a file's title, author, or year. The Priority Cascade Engine resolves these disputes using a tiered priority system — and each source's authority is judged *separately for each type of data*.

Each piece of metadata (e.g. the title "Dune") is a **Claim**. Claims come from multiple sources:

| Claim source | Example | Trust weight |
|---|---|---|
| File's internal metadata (OPF/ID3) | `title = "Dune"` | High (0.9) |
| Filename | `dune_part1.epub` | Medium (0.5) |
| External metadata provider | `Dune (Frank Herbert, 1965)` | Configurable per field |

Each provider carries a **per-field trust weight** that reflects how reliable it is *for that specific kind of data*. Wikidata is the definitive authority for franchise identifiers (weight 1.0). Apple API is a single provider that supports multiple media types (ebooks and audiobooks) through media-type-scoped search strategies and field mappings.

**Priority Cascade Tiers (evaluated in order):**

| Tier | Name | What it does |
|---|---|---|
| **A** | User Locks | User-locked claims always win (confidence 1.0). Never overridden. |
| **B** | Per-Field Provider Priority | For fields with a priority override in `config/field_priorities.json`, walks the provider list and picks the first provider that has a claim. Skips Tier C for this field. |
| **C** | Wikidata Authority | For all other fields, Wikidata claims win unconditionally when present. |
| **D** | Confidence Cascade | When no authority claim exists, the highest-confidence claim wins. |

**Per-Field Provider Priorities (`config/field_priorities.json`):**

Certain fields benefit from a specific provider rather than the default Wikidata-always-wins rule. The config file defines up to 3 provider choices per field:

```json
{
  "field_overrides": {
    "description": {
      "priority": ["wikipedia", "apple_api", "wikidata_reconciliation"],
      "note": "Rich Wikipedia summaries preferred over Wikidata one-liners"
    },
    "biography": {
      "priority": ["wikipedia", "wikidata_reconciliation"],
      "note": "Rich Wikipedia bios for persons"
    },
    "cover": {
      "priority": ["apple_api", "tmdb", "wikidata_reconciliation"],
      "note": "Retail providers have high-res commercial art"
    },
    "rating": {
      "priority": ["apple_api", "tmdb"],
      "note": "Wikidata doesn't carry ratings"
    }
  }
}
```

No additional API calls are needed — this is purely a scoring/selection change after both hydration stages have deposited their claims. Fields NOT listed default to Wikidata-first (Tier C).

**Field Count Scaling:**

Files with very few metadata fields (e.g. only a filename-derived title) receive a confidence penalty to prevent inflated scores:

```
overallConfidence *= Math.Min(1.0, fieldCount / 3.0)
```

A file with only 1 field scores at 1/3 of its raw confidence (~0.17 instead of ~0.50). A file with 3+ fields is unaffected (multiplier = 1.0). This ensures corrupt or near-empty files are correctly routed to staging instead of being auto-promoted.

**User-Locked Claims:** When you manually set a metadata value, that Claim is permanently locked. The engine gives it a confidence of 1.0 and never overrides it on any future re-score — regardless of what any external provider says.

The engine tallies all Claims for each metadata field independently. The winning Claim becomes the **Canonical Value** — the single trusted answer used by the Dashboard.

If two Claims are too close to pick a clear winner, the field is flagged as **Conflicted** and surfaced to the user for manual resolution.

**Why this matters to the business:**
- **No human help needed** for well-tagged files — the library builds itself.
- **Transparent conflicts** — the user is only bothered when the machine genuinely cannot decide.
- **Provenance preserved** — every Claim is kept forever (append-only). History is never lost.
- **Reliability** — user-set values can never be silently overridden by the scoring engine. Field count scaling prevents inflated confidence for near-empty files.
- **Maintenance** — per-field provider priorities live in `config/field_priorities.json`; zero code changes needed to re-tune which provider wins for each field.
- **Extensibility** — future providers simply declare their field weights in the JSON; the engine picks them up with no new code. New field priority overrides are one JSON entry.

### 3.3 — Security: Secret Store & Guest Keys

**Plain English:** The Engine protects sensitive information and controls who can talk to it.

**Secret Store**
Private API keys (e.g. for external metadata providers like TMDB or MusicBrainz) are encrypted at rest using the operating system's built-in protection layer. They are never stored as plain text in the configuration file.

**Guest Key System**
Any application that wants to talk to the Engine must present a valid API key. Keys are:
- Generated inside the Engine with an assigned role (Administrator, Curator, or Consumer)
- Labelled (e.g. "Media manager integration", "Mobile app")
- Revocable individually without affecting other keys

**Mandatory Authentication (Phase A Security Foundation)**
Every Engine endpoint requires authentication, with two exceptions:
- `/system/status` — health probe, always open.
- Localhost requests — when `MediaEngine:Security:LocalhostBypass` is `true` (the default), requests from the local machine are treated as Administrator without needing a key. This preserves the local development experience.

All other unauthenticated requests receive 401 Unauthorized.

**Role-Based Authorization**
Each API key carries one of three roles:
- **Administrator** — Full access to all endpoints.
- **Curator** — Can browse the library, stream files, read/write metadata claims, and view provider status. Cannot access admin operations, folder settings, ingestion, or profile management.
- **Consumer** — Can browse the library, stream files, and read metadata claim history. Cannot modify metadata or access any settings.

**Rate Limiting**
Three rate-limiting policies protect the Engine:
- Key generation: 5 requests/minute per IP.
- File streaming: 100 requests/minute per IP.
- General API: 60 requests/minute per IP.

**Path Traversal Protection**
Folder-related endpoints (`/settings/folders`, `/settings/test-path`) reject paths containing `..` traversal segments or targeting known system directories (`C:\Windows`, `/etc`, etc.).

**SignalR Hub Authentication**
The real-time intercom (`/hubs/intercom`) requires authentication via `X-Api-Key` header, `access_token` query string parameter, or localhost bypass. Unauthenticated connections from non-localhost are rejected.

**Why this matters to the business:**
- **Privacy:** No third-party app gets free access. Each connection is authorised with a specific role, and the Engine can now be safely exposed beyond localhost.
- **Reliability:** Role-based access prevents external tools from accidentally calling admin operations.
- **Maintenance:** A compromised key can be revoked in seconds. The rest of the system is unaffected.

### 3.4 — Dashboard UI Features

**Dark-Mode-Only Cinematic Design**
The Dashboard is dark-mode only — light mode has been fully removed. Background uses an ambient gradient system: base color `#080B14` with three radial gradients (purple top-left `rgba(127,119,221,0.10)`, warm amber top-right `rgba(201,146,46,0.06)`, blue bottom-center `rgba(55,138,221,0.05)`) applied at the `MudLayout` root via `.app-bg` class, plus a subtle SVG noise overlay at 1.5% opacity for film-grain texture. Page root containers use `transparent` backgrounds to let the gradient show through; sticky headers and modal overlays remain opaque. All CSS custom properties are set for dark surfaces with glassmorphic blur overlays.

**Dual Navigation: TopBar + LeftDock**
The Dashboard uses a horizontal top bar and an icon-only left dock:
- **TopBar** (`TopBar.razor`): Fixed horizontal bar at top (`height: 56px`). Contains: AppLogo wordmark (left), spacer, search icon + notification bell (with review count badge) + profile avatar (right). Four layout variants: `full` (desktop), `mobile` (hamburger + centered logo), `simplified` (TV: logo only), `minimal` (automotive: icon only). Glassmorphic styling: `rgba(0,0,0,0.4)` with `backdrop-filter: blur(12px)`.
- **LeftDock** (`LeftDock.razor`): Floating icon-only panel (`64px` wide, desktop only). Contains fixed content-type lane icons (Home, Search, Books, Video, Music, Podcasts, Comics) + Settings + Profile Avatar + Live Clock. Lanes are defined in `ContentLanes.cs` — no longer user-configurable Virtual Libraries. 3px amber active bar on left edge. Dark blue glassmorphic styling (`rgba(8,13,30,0.92)`).
- **MobileNavDrawer** (`MobileNavDrawer.razor`): Slide-out drawer for mobile. Triggered by hamburger in TopBar. Contains AppLogo + nav items matching dock. `MudDrawer` with `DrawerVariant.Temporary`.

Content area: `padding-top: 56px` (TopBar), `padding-left: 68px` (dock, desktop only).

**Fixed Golden Amber Accent**
The accent colour is fixed to golden amber (#C9922E, derived from the logo gradient). The `ThemeService.SetHubAccent()` method is a no-op. The Settings General tab offers avatar colour selection only.

**Cinematic Hero Banner**
The Home and lane page heroes use a full-width cinematic banner. When a pre-rendered `hero.jpg` exists (generated by `HeroBannerGenerator` during ingestion using SkiaSharp: blur + vignette + grain), it is served directly. Otherwise, falls back to CSS-blurred cover art (`filter: blur(24px)`) with a dark vignette overlay. Falls back to a gradient when no cover art is available. Metadata badges (year, media type), Hub title + author are shown.

**Poster Swimlanes**
The Hub overview uses horizontal-scrolling poster-art rows (`PosterSwimlane.razor` + `PosterCard.razor`). Rows: "Continue your Journey", "Recently Added", then media-type-grouped swimlanes. Cards show cover art (2:3 aspect ratio) with title and metadata badges below. Hidden scrollbar, scroll-snap for touch. Card width driven by device config (`swimlane_card_width`).

**Automotive Mode** *(planned)*
A high-contrast, large-button display mode designed for use at a distance (e.g. on a media room TV or tablet mounted in a vehicle). All text is enlarged, all touch targets are oversized, and non-essential interface elements are hidden. Activated by a single toggle.

**Real-time updates (Intercom)**
The Dashboard is connected to the Engine via a persistent live channel (the "Intercom"). When a new file is ingested or processing progresses, the Dashboard updates instantly — no manual refresh, no polling. The progress bar is live.

### 3.5 — Brand Assets

Three official SVG logo files exist. **Never replace logo placements with hand-written text.** Always use the correct file for the context.

| File | Location in repo | Use when… |
|---|---|---|
| `tuvima-logo.svg` | `src/MediaEngine.Web/wwwroot/images/` and `assets/images/` | Full horizontal logo — mark + "TUVIMA" wordmark. Use in the expanded LeftDock and anywhere a full branded header is needed. |
| `tuvima-icon.svg` | `src/MediaEngine.Web/wwwroot/images/`, `wwwroot/favicon.svg`, and `assets/images/` | Square icon mark only. Use as favicon, narrow LeftDock icon, app icon, or any small/square slot. |
| `tuvima-hero.svg` | `assets/images/` | Mark + wordmark + subtitle ("The Unified Media Intelligence Kernel"). Use in README hero and marketing contexts only. |

**Color note:** All three SVGs use hardcoded fills: white (`#fff`) for highlight layers and black (default) for the main strokes. They are designed primarily for dark backgrounds (as used in the Dashboard). On light backgrounds, the mark renders as a black design — which is clean and intentional.

**Source files** are in `C:\Users\shaya\OneDrive\Documents\Projects\Tuvima\Graphics\` (outside the repo — designer originals). Do not modify the SVGs in the repo directly; request updated exports from the source files.

### 3.6 — External Metadata Adapters & Recursive Person Enrichment (Phase 9)

> **Wikidata is the sole identity authority.** Every media item in the Library is identified by its Wikidata Q-identifier. All other providers (Apple API, TMDB, Open Library, Google Books) exist solely to supply media assets — cover art, ratings, descriptions, and other supplementary data that Wikidata does not carry. If an item is not in Wikidata, it does not have a verified identity; the user must create a manual entry or wait for Wikidata to catalogue it.

**Primary Sources: Wikidata & Wikipedia.** Tuvima embraces Wikidata and Wikipedia as its primary knowledge sources. Their mission to organise human knowledge mirrors Tuvima's mission to organise personal media. Every metadata lookup starts with Wikidata's authority. Wikipedia provides human-readable context. Person and human data is always gathered from Wikidata/Wikipedia. Universe information — complex relationships, narratives between different media types, fictional entities — is populated from Wikidata.

**Why secondary sources exist:** New releases take time to catalogue. Cover art and promotional imagery are copyright-restricted on Wikimedia. Secondary retail providers (Apple API, TMDB, etc.) fill these gaps: commercial identifiers, cover art, ratings, and metadata for recent releases.

**Plain English:** After a file lands in the library, the Engine runs retail providers first (Apple API, Open Library, Google Books) to gather cover art, ratings, and bridge IDs — then uses those bridge IDs to look up the Wikidata identity precisely. Wikipedia descriptions are fetched via the Wikidata Reconciliation client as part of the Wikidata stage. This happens entirely in the background; the file appears on the Dashboard immediately, and the richer information pops in moments later without any page refresh.

**The zero-key providers (no accounts, no API keys required):**

| Provider | What it contributes | Trust weights |
|---|---|---|
| **Apple API** | Cover art (600×600), description, rating, title. Single config supporting both ebooks and audiobooks via media-type-scoped search strategies. | cover 0.85, description 0.85, rating 0.8, title 0.7 |
| **Open Library** | Title, author, year, cover art, ISBN, series | title 0.75, author 0.8, year 0.85, cover 0.7, isbn 0.9 |
| **Google Books** | Title, author, year, cover art, ISBN, description, page count, publisher | title 0.75, author 0.8, year 0.85, cover 0.7, isbn 0.9 |
| **Wikidata** | Person headshot (Wikimedia Commons), biography, Q-identifier | qid/headshot/biography 1.0 |
| **Wikidata Reconciliation** | QID resolution, core properties, bridge IDs, person data, pen names, audiobook editions, Wikipedia descriptions (via `GetWikipediaSummariesAsync`) | All properties via OpenRefine Reconciliation API + Data Extension API; description 0.90 |

**How it works (the harvest pipeline):**

1. After every file is ingested and scored, a lightweight `HarvestRequest` is placed on a background queue (`Channel<HarvestRequest>`, bounded to 500 items). Ingestion never waits for this.
2. A background worker (`MetadataHarvestingService`) pulls from the queue with up to 3 concurrent provider calls. First provider that returns data wins for each field; later providers are skipped.
3. New claims are written to the database and the scoring engine re-runs for those fields. The updated canonical values are upserted.
4. A `MetadataHarvested` SignalR event fires, and the Dashboard card updates live.

**Recursive Person Enrichment (`RecursiveIdentityService`):**

When a file's metadata contains an author or narrator name, the Engine:
1. Looks up whether a `Person` record already exists for that name and role. If not, creates one.
2. Links the person to the media asset (idempotent — safe to run repeatedly).
3. If the person has not yet been Wikidata-enriched (`EnrichedAt is null`), enqueues a `HarvestRequest` with `EntityType.Person` for Wikidata lookup.
4. When Wikidata responds, the person record is updated with `WikidataQid`, `HeadshotUrl`, and `Biography`, and a `PersonEnriched` SignalR event fires.

**Config-Driven Universal Adapter:**

The REST+JSON providers (Apple API, Open Library, Google Books) are powered by a single `ConfigDrivenAdapter` class (`src/MediaEngine.Providers/Adapters/ConfigDrivenAdapter.cs`) that reads its entire behaviour from the provider's JSON config file in `config/providers/`. No individual adapter classes exist for these providers — they were retired in favour of the universal adapter.

**Wikidata Reconciliation adapter** (`ReconciliationAdapter`) replaces the former `WikidataAdapter` SPARQL implementation. It uses the OpenRefine Reconciliation API for QID resolution and the Data Extension API for property fetching — both config-driven via `config/providers/wikidata_reconciliation.json`. All property codes and class mappings are read from configuration, not hardcoded.

Each config file declares:
- `adapter_type: "config_driven"` — tells the DI registration loop to use the universal adapter
- `provider_id` — stable GUID for `metadata_claims.provider_id` foreign keys
- `can_handle.media_types[]` — which media types this provider supports (e.g. `["Epub", "Audiobook"]`)
- `search_strategies[]` — ordered URL template strategies with `required_fields`, `tolerate_404`, `results_path`, and optional `media_types` scoping
- `field_mappings[]` — JSON path extraction rules with named transforms, confidence values, and optional `media_types` scoping

**Media-type scoping on strategies and field mappings:** A single provider config can support multiple media types by declaring `media_types` arrays on individual strategies and mappings. When a search or fetch request includes a media type, only matching strategies/mappings are used. Strategies and mappings with no `media_types` array are universal (apply to all media types). `MediaType.Unknown` acts as a wildcard, returning all strategies/mappings regardless of scoping.

Adding a new REST+JSON provider is a **zero-code operation**: drop a config file in `config/providers/`, restart, done.

**Wikidata Reconciliation adapter** (`ReconciliationAdapter`) uses the W3C Reconciliation API and Data Extension API, replacing the former SPARQL-based `WikidataAdapter`. Property configuration lives in `config/providers/wikidata_reconciliation.json`.

**Bridge ID Normalization:** Identifiers flow between Wikidata (dashed ISBNs, mixed-case ASINs, full URLs for IMDb) and retail providers (bare digits, uppercase codes). `IdentifierNormalizationService` (`MediaEngine.Domain.Services`) normalizes 12 identifier types across three directions: `NormalizeRaw` (clean up from any source, with ISBN-13 Mod10 checksum validation), `ToWikidataFormat` (for Wikidata comparison), and `ToRetailFormat` (stripped for API lookups). Key aliases (`isbn_13` → `isbn`, `isbn_10` → `isbn`) are provided by `GetClaimKeyAlias`. Edition bridge ID resolution in `ReconciliationAdapter` filters editions by media type via P31 (instance_of) — audiobooks get audiobook-edition ISBNs, books get print ISBNs.

**Key types:**
- `ConfigDrivenAdapter` (`MediaEngine.Providers.Adapters`) — universal adapter implementing `IExternalMetadataProvider`
- `IdentifierNormalizationService` (`MediaEngine.Domain.Services`) — static utility normalizing 12 identifier types (ISBN-13, ISBN-10, ASIN, IMDb, Apple Books ID, TMDB, MusicBrainz, Goodreads, ComicVine, ISRC, LCCN, Apple Podcasts) with checksum validation
- `JsonPathEvaluator` (`MediaEngine.Providers.Models`) — static utility navigating `System.Text.Json.Nodes.JsonNode` with dot-notation, array indexing, and wildcard iteration
- `ValueTransformRegistry` (`MediaEngine.Providers.Models`) — named transform functions (to_string, strip_html, url_template, regex_replace, prefer_isbn13, array_join, array_nested_join, first_n_chars, fallback_key, title_case)

**Key architectural rules for this subsystem:**
- `MediaEngine.Ingestion` has **zero new project references**. All interfaces (`IMetadataHarvestingService`, `IRecursiveIdentityService`, `IMetadataClaimRepository`, `ICanonicalValueRepository`) live in `MediaEngine.Domain.Contracts` — which Ingestion already references.
- All provider configuration lives in `config/providers/{name}.json` — endpoints, trust weights, search strategies, field mappings, throttle, concurrency. Changing any of these requires only a config edit, never a recompile.
- Provider GUIDs are stable strings in each config file's `provider_id` field (not looked up from the DB at runtime) so new `MetadataClaim` rows can be written without a DB round-trip.
- Throttle rules and concurrency limits are per-provider in their config files. The `ConfigDrivenAdapter` enforces them via `SemaphoreSlim` + timestamp gap.
- Required-field short-circuits: each search strategy declares `required_fields`. If a required field is missing, the strategy is skipped immediately — no HTTP call made.

**Why this matters to the business:**
- **Reliability** — Providers are never in the critical path. A failed network call returns an empty list; the file remains in the library with its local metadata intact.
- **Performance** — The harvest queue is non-blocking. File ingestion completes in milliseconds regardless of network conditions.
- **Maintenance** — Adding a new REST+JSON provider is a zero-code operation: one JSON config file. The Wikidata Reconciliation adapter is config-driven via `config/providers/wikidata_reconciliation.json`.
- **Extensibility** — The config-driven architecture supports any REST+JSON API that returns structured data. URL templates, JSON path extraction, and named transforms cover the common patterns.
- **Privacy** — Only titles, authors, and ASINs are sent to external services — no personal data, no usage telemetry, no library structure.

### 3.7 — Library Organization & File-First Architecture

**Plain English:** After a file is ingested and scored with sufficient confidence, the Library organises it into a clean, human-readable folder structure. User metadata edits are written back into the file's embedded metadata (EPUB OPF, ID3 tags, M4B atoms) via `IMetadataTagger` implementations. Wikidata properties are re-fetchable via batch Reconciliation API. On a database rebuild (Great Inhale v2), the Engine scans media files, extracts embedded metadata via processors, and batch-reconciles with Wikidata.

**Data architecture:**
- The **database is the authoritative data store** for all metadata, relationships, and universe data. Sidecar files (universe.xml, person.xml) have been eliminated.
- User metadata edits are written back into the file's embedded metadata (EPUB OPF, ID3 tags) via `IMetadataTagger` for portability.
- Wikidata properties are re-fetchable via batch Reconciliation API as a recovery fallback.
- **Recovery model:** Scheduled SQLite backups by domain (universe, people, library) as primary recovery. Wikidata re-fetch as fallback. Great Inhale deprecated — replaced by backup/restore.
- Cover art is **never stored in the database.** `cover.jpg` lives alongside the file on disk and is always read from there.

**Folder Structure:**

The default organisation template places files in a category folder with the title and Wikidata QID as the unique identifier. The Hub is a virtual container (§1) and has no presence on the filesystem.

**Default template:** `{LibraryRoot}/{Category}/{Title} - {QID}/{Title}{Ext}`

**Per-media-type overrides:**
- **Books/Audiobooks:** `{Category}/{Title} - {QID}/{Format}/{Title}{Ext}` — format subfolder distinguishes ebook from audiobook in the same title folder
- **TV:** `{Category}/{Title} - {QID}/S{Season}E{Episode} - {Title}{Ext}`
- **Music:** `{Category}/{Artist}/{Album} - {QID}/{TrackNumber} - {Title}{Ext}`
- **Movies, Comics, Podcasts:** `{Category}/{Title} - {QID}/{Title}{Ext}` (flat — single format per title)

**Example (Books library with both formats):**
```
{LibraryRoot}/Books/Dune - Q190159/Epub/Dune.epub
{LibraryRoot}/Books/Dune - Q190159/Audiobook/Dune.m4b
{LibraryRoot}/Books/Dune - Q190159/cover.jpg          ← cover art
```

**Example (Movies — flat):**
```
{LibraryRoot}/Movies/Dune Part Two - Q104686073/Dune Part Two.mkv
{LibraryRoot}/Movies/Dune Part Two - Q104686073/cover.jpg
```

**`{Category}` mapping** from `MediaType` enum:
- Books + Audiobooks → `Books`
- TV → `TV`
- Movies → `Movies`
- Music → `Music`
- Comics → `Comics`
- Podcasts → `Podcasts`
- Unknown → `Other`

**Migration note:** Existing libraries organised under older patterns (e.g., `{Title} ({Qid})/{Format}/`) continue to work. On the next hydration pass or a manual "Re-organise Library" action, files are moved to the new structure automatically.

**Staging-First Flow:**

All ingested files land in `{LibraryRoot}/.staging/` first, regardless of confidence. The Library only receives files that have been hydrated and promoted by `AutoOrganizeService`. This ensures the Library invariant: every file in the Library has been hydrated, has a real QID (or confirmed bridge IDs), and has cover art + hero banner.

```
Watch Folder  ──(detect + process)──>  .staging/  ──(hydration + promote)──>  Library
                                           │
                                      stays here if:
                                      - low confidence
                                      - unidentifiable
                                      - needs review
```

**Staging subcategories:**

| Subcategory | Condition | What happens next |
|---|---|---|
| `.staging/pending/` | High confidence (≥ 0.85 or user-locked) | AutoOrganizeService promotes after hydration |
| `.staging/low-confidence/` | Confidence 0.40–0.85, no user locks | Awaits hydration improvement or manual review |
| `.staging/unidentifiable/` | Confidence < 0.40, no user locks | Needs manual title/match from user |
| `.staging/other/` | Resolves to "Other" category | Needs media type classification |

**AutoOrganize confidence gate:**
AutoOrganize is gated on:
```
scored.OverallConfidence >= 0.85  ||  claims.Any(c => c.IsUserLocked)
```
Files that pass this gate go to `.staging/pending/`. Files below the gate go to `.staging/low-confidence/` or `.staging/unidentifiable/` depending on their confidence. The threshold reuses `AutoLinkThreshold = 0.85` from `ScoringConfiguration` (single source of truth).

**Cover art timing:** `cover.jpg` is written alongside the file in `.staging/` during initial ingestion (the processor's cover image byte array is only available at that time). Hero banner generation happens during promotion by `AutoOrganizeService`.

Staged files retain their fingerprint in the database and can be manually reclaimed at any time via the Dashboard (drag to a Hub, or provide a user-locked title). When a user resolves a staged file, it is promoted from `.staging/` into the organised library structure. The `.staging/` directory is excluded from Watch Folder monitoring to prevent re-ingestion loops.

**Migration:** On startup, if `{LibraryRoot}/.orphans/` exists and `{LibraryRoot}/.staging/` does not, the Engine automatically renames the directory and updates all DB file paths.

**Key types and services:**
- `ILibraryScanner` / `LibraryScanner` (`MediaEngine.Ingestion`) — Great Inhale v2 implementation. Recursively scans Library Root for media files, verifies content hashes, and reconciles with Wikidata in batch for database recovery.
- `LibraryScanResult` (`MediaEngine.Ingestion.Models`) — scan outcome counts: `HubsUpserted`, `EditionsUpserted`, `Errors`, `Elapsed`.
- `IHubRepository.FindByDisplayNameAsync` + `IHubRepository.UpsertAsync` — added to support Hub hydration in Great Inhale.
- `Hub.DisplayName` (`MediaEngine.Domain.Aggregates`) — human-readable hub name; populated at organise time.
- Migration **M-004** — `ALTER TABLE hubs ADD COLUMN display_name TEXT;` — applied in `DatabaseConnection.RunStartupChecks()`.

**API endpoint:**
- `POST /ingestion/library-scan` — triggers Great Inhale. Returns `LibraryScanResponse` with hub/edition counts and elapsed time. Requires `LibraryRoot` to be configured; returns 400 if unset or if the directory does not exist.
- Dashboard client method: `TriggerLibraryScanAsync()` on `ILibraryApiClient` / `LibraryApiClient`.

**Great Inhale scope constraints:**
Edition-level hydration **requires the MediaAsset to already exist in the database** (matched by content hash). It cannot create a `Hub → Work → Edition → MediaAsset` chain from scratch after a complete wipe — that requires a full re-ingestion pass. Hub-level hydration creates Hub records unconditionally.

**Why this matters to the business:**
- **Reliability** — A complete database wipe is recoverable. Re-ingest from files; embedded metadata and batch Wikidata reconciliation rebuild the library.
- **Maintenance** — Embedded metadata is portable and readable by any standard tool.
- **Privacy** — All data lives on disk under the Library Root. No external dependency, no cloud sync.
- **Performance** — Great Inhale scans files by content hash; batch Reconciliation API calls minimise network round-trips.

### 3.8 — System Activity Ledger & Maintenance (Phase 1 — Audit Engine)

**Plain English:** Every significant action the Engine takes — ingesting a file, refreshing metadata, pruning old records — is permanently recorded in a system activity ledger. This provides a complete audit trail visible in the Maintenance tab.

**Schema:** A dedicated `system_activity` table (migration M-008) stores entries with: timestamp, action type, optional hub name, entity reference, user attribution, a JSON changes snippet, and a human-readable detail string. Indices on `occurred_at` and `action_type` for fast queries.

**Retention:** Configurable via `activity_retention_days` in `tuvima_master.json` (default: 60 days). A daily `ActivityPruningService` (BackgroundService) runs every 24 hours and deletes entries older than the retention period.

**API Endpoints:**
- `GET /activity/recent?limit=50` — most recent entries (Admin or Curator)
- `POST /activity/prune` — manual prune trigger (Admin only)
- `GET /activity/stats` — total entry count + retention setting (Admin or Curator)

**Dashboard:** The Maintenance tab displays a vertical timeline of recent activity, each entry with an action-type icon, relative timestamp, detail text, and hub-name chip. A Retention Policy section shows the current setting and offers a manual Prune Now button. Stubs for Library Crawl and Weekly Sync sections are ready for future phases.

**Design decision: New table vs extending `transaction_log`.** The existing `transaction_log` has a synchronous `void Log()` interface used throughout the codebase. A new `ISystemActivityRepository` with async-first `Task LogAsync()` is cleaner and avoids breaking existing callers. Both tables coexist.

**Key types:**
- `SystemActivityEntry` (`MediaEngine.Domain.Entities`) — domain entity
- `SystemActionType` (`MediaEngine.Domain.Enums`) — string constants (FileIngested, MetadataHydrated, HashVerified, PathUpdated, SyncCompleted, CrawlStarted, CrawlFinished, MetadataRefreshed, SidecarUpdated, ActivityPruned)
- `ISystemActivityRepository` (`MediaEngine.Domain.Contracts`) — async-first contract
- `SystemActivityRepository` (`MediaEngine.Storage`) — SQLite implementation
- `ActivityPruningService` (`MediaEngine.Api.Services`) — daily BackgroundService
- `ActivityEndpoints` (`MediaEngine.Api.Endpoints`) — minimal API group

**Why this matters to the business:**
- **Reliability** — Complete audit trail of every automated action the Engine performs.
- **Maintenance** — Configurable retention prevents unbounded table growth; manual prune available for immediate cleanup.
- **Extensibility** — Every future phase (Wikidata hydration, library crawl, weekly sync) writes to this ledger, creating a unified activity stream.

### 3.9 — Universal Metadata Hydrator: Foundation (Phase A)

**Plain English:** This phase lays the groundwork for a complete Wikidata-powered enrichment system. Wikidata is treated as the absolute source of truth — it knows the authoritative Q-identifier for every book, film, person, and franchise, plus 50+ structured properties that link to every major external platform. Phase A builds the configurable property map, expands the data store to hold person social links, and teaches the sidecar XML to carry external bridge identifiers.

**Configurable Wikidata Property Map:**

A `WikidataSparqlPropertyMap` contains the Master Authority Table — 50+ Wikidata property entries across 8 categories: Core Identity, People (Work-scoped), People (Person-scoped), Lore & Narrative, Bridges: Books, Bridges: Movies/TV, Bridges: Comics/Anime, Bridges: Music/Audio, and Social Pivot. Each entry maps a Wikidata P-code (e.g. `P179` → `series`) to a Library claim key with a configured confidence and scope (Work, Person, or Both).

The defaults live in code (`WikidataSparqlPropertyMap.DefaultMap`) and are exported to `config/universe/wikidata.json` on first run. Users can override confidence values, remap claim keys, reorder bridge lookups, or disable properties entirely by editing the universe config file — zero code changes needed. The adapter loads the universe config at runtime and falls back to compiled defaults if the file is missing or corrupt. (See §3.11 for the full configuration architecture.)

The property map is exported to `config/universe/wikidata.json` on first run. Static helpers build Data Extension API queries:
- `BuildWorkPropertyRequest(qid)` — fetches all Work-scoped properties via Data Extension API
- `BuildPersonPropertyRequest(qid)` — fetches all Person-scoped properties (including P18 headshot — **Person-only**, never for media items)

**Copyright constraint — P18 (Image):** Wikidata P18 (Image) is exclusively for Person entities (author/director headshots from Wikimedia Commons — public figures, not copyrighted). Media cover art is sourced exclusively from Apple API and TMDB. The Work property request deliberately excludes P18.

**Schema migrations:**
- **M-009:** Rebuilds the `persons` table to expand the role CHECK constraint, adding `Illustrator`, `Cast Member`, `Voice Actor`, `Screenwriter`, and `Composer` to the existing `Author`, `Narrator`, `Director` list. Uses the SQLite table-recreation pattern (PRAGMA foreign_keys=OFF → CREATE new → INSERT INTO → DROP old → RENAME).
- **M-010:** Adds six nullable TEXT columns to `persons`: `occupation` (Wikidata P106), `instagram` (P2003), `twitter` (P2002), `tiktok` (P7085), `mastodon` (P4033), `website` (P856). These power the Social Pivot — direct links to official creator feeds.

**Social Pivot — Actionable URI Schemes:**
Social link handles are stored as **Actionable URI Schemes** to enable native app launching on Mobile and Automotive device profiles (§3.12). When the Engine receives a raw handle from Wikidata (e.g. `"frankherbertofficial"` from P2003), it stores both the raw handle and the platform-specific URI:

| Platform | Stored URI | Fallback (web) |
|---|---|---|
| Instagram | `instagram://user?username={handle}` | `https://instagram.com/{handle}` |
| Twitter/X | `twitter://user?screen_name={handle}` | `https://x.com/{handle}` |
| TikTok | `tiktok://user?username={handle}` | `https://tiktok.com/@{handle}` |
| Mastodon | `https://{instance}/@{user}` (web-native) | Same |
| Website | Direct URL as-is | Same |

The Dashboard's `DeviceContextService` selects the appropriate URI format at render time: URI scheme for Mobile/Automotive (triggers native app), HTTPS fallback for Web/Television. The raw handle is always preserved in the database column for portability.

**Provider lookup expansion:**
`ProviderLookupRequest` now carries bridge hint fields (`AppleBooksId`, `AudibleId`, `TmdbId`, `ImdbId`) — allowing the Reconciliation adapter to resolve QIDs from external IDs and run Data Extension property fetches.

**New activity ledger entries:**
Four new `SystemActionType` constants log hydrator actions: `BridgeSyncUpdated` (external bridge ID synced), `PersonHydrated` (person enriched with social links), `WeeklySyncStarted` (weekly refresh cycle began), `AffiliateGenerated` (affiliate link built from bridge ID).

**tuvima_master.json configuration additions:**
- `wikidata_property_map` — list of property overrides (P-code, claim key, confidence, enabled)
- `maintenance.weekly_sync_interval_days` (default: 7), `weekly_sync_batch_size` (default: 50), `weekly_sync_batch_delay_ms` (default: 2000)
- `affiliate_settings.amazon_affiliate_tag`, `affiliate_settings.show_affiliate_disclosure` (default: true)

**Key types introduced:**
- `WikidataProperty` (`MediaEngine.Providers.Models`) — property descriptor record (PCode, ClaimKey, Category, EntityScope, Confidence, IsBridge, Enabled)
- `WikidataPropertyMap` (`MediaEngine.Providers.Models`) — static property map + Data Extension API query builders
- `WikidataPropertyMapOverride` (`MediaEngine.Storage.Models`) — JSON-configurable override shape
- `AffiliateSettings` (`MediaEngine.Storage.Models`) — affiliate tag configuration

**Why this matters to the business:**
- **Extensibility** — Adding a new Wikidata property is one JSON entry in provider config. Zero code changes. The Data Extension API fetches all configured properties in one call.
- **Reliability** — All 50+ property defaults are compiled into the code. If the config file is missing or corrupt, the defaults still work.
- **Privacy** — Only titles, ISBNs, and ASINs leave the machine during Reconciliation queries. Everything hydrated lives on disk.
- **Maintenance** — The property map is editable via settings. Confidence values, claim keys, and enabled flags can all be changed without touching code.

### 3.10 — Universal Metadata Hydrator: Librarian Workflow (Phase B)

**Plain English:** Phase B replaces the placeholder Wikidata work lookup with a full Reconciliation API-powered enrichment engine. You can now click a button on the Dashboard — or call a single Engine action — and the Library reaches out to Wikidata, finds the matching creative work by its bridge identifiers or title search, and pulls back every known property: series name, franchise, characters, narrative location, and dozens of external platform links (TMDB, IMDb, Goodreads, Apple Books, etc.). The Wikidata provider is also now pinned at the top of the Metadata tab as the "Universe Provider" — reflecting its unique role as the one source that spans all media types.

**QID Resolution via Reconciliation API (`ReconciliationAdapter.FetchWorkAsync`):**

The adapter posts a query (title + optional property constraints like P50=author) to the OpenRefine Reconciliation API. Results are filtered by media type using P31 (instance_of) + P279 (subclass_of) hierarchy walking. Auto-accept when score ≥ 95 and `match: true`. Multiple candidates go to the review queue.

After QID confirmation, the Data Extension API fetches all configured properties in a single POST call. No separator issues — native JSON arrays for multi-valued properties.

**Conservative matching rule:** Multiple candidates without auto-accept result in a `MultipleQidMatches` review queue entry, not auto-accept.

**Instance_of class mappings** (`config/universe/wikidata.json`):
```json
{
  "instance_of_classes": {
    "Books": ["Q7725634", "Q571", "Q8261", "Q47461344", "Q277759"],
    "Audiobooks": ["Q106833962", "Q7725634", "Q571", "Q8261", "Q47461344"],
    "Movies": ["Q11424", "Q24869", "Q24862"],
    "TV": ["Q5398426", "Q581714", "Q21191270"],
    "Music": ["Q482994", "Q134556", "Q208569"],
    "Comics": ["Q1004", "Q838795", "Q21198342"],
    "Podcasts": ["Q24634210"]
  }
}
```

**ASIN extraction from audio tags:** The `AudioProcessor` extracts ASINs from M4B/MP3 files — commonly embedded by Audible — to feed files into the reliable bridge-ID resolution path. Extraction sources (priority order): M4B iTunes custom atoms (`----:com.audible.asin`, `----:com.apple.iTunes:ASIN`), MP3 ID3v2 TXXX frames (`ASIN`, `AUDIBLE_ASIN`, `AMAZON_ASIN`), Vorbis/FLAC custom fields (`ASIN`), and comment field regex (`B0[A-Z0-9]{8}`).

**Review queue media type dropdown:** The NeedsReviewTab includes a media type dropdown pre-selected from the auto-detected type. When the user changes the dropdown, search results are filtered using the instance_of classes for that media type. This allows users to correct misclassifications (e.g. an MP4 detected as Movies but actually a TV episode) and get relevant Wikidata candidates.

**Value transformation rules:**
- **P577 (year):** ISO dates are reduced to 4-digit year strings
- **P1545 (series position):** Numeric portion extracted from ordinal strings
- **Entity-valued properties:** Wikidata entity URIs are stripped to bare QIDs
- **Multi-valued properties** (characters, cast members): Joined with `"; "`
- **wikidata_qid:** Always emitted at confidence 1.0 as a claim

**Copyright constraint reminder:** P18 (Image) is **never emitted for Work entities** — it is Person-only. The Reconciliation adapter's property config excludes P18 for Work entities. Media cover art comes exclusively from Apple API and TMDB.

**Hydration Engine action:**

`POST /metadata/hydrate/{entityId}` — a user-triggered, synchronous action available to Administrators and Curators:
1. Loads existing canonical values as lookup hints (title, ISBN, ASIN, bridge IDs)
2. Resolves Reconciliation API and Data Extension endpoint URLs from provider config
3. Calls the ReconciliationAdapter directly (not through the background queue — immediate response)
4. Persists all returned claims (append-only)
5. Re-scores the entity through the full scoring pipeline
6. Upserts canonical values
7. Logs `MetadataHydrated` to the activity ledger
8. Broadcasts `MetadataHarvested` via the Intercom so the Dashboard refreshes live
9. Returns a result with the QID, number of claims added, and a human-readable message

**Dashboard wiring:**
- `TriggerHydrationAsync(Guid entityId)` added to the Dashboard client contract
- The Orchestrator invalidates the state cache on successful hydration so the UI reflects changes immediately

**Universe Provider UI treatment:**

Wikidata is now pinned as a standalone card above the grouped provider panels in the Metadata settings tab. It carries a "Universe Provider" chip and a distinctive accent border — a 4px solid primary-colour left border plus a 1px primary outline. This visual treatment reflects Wikidata's unique role as the only provider that spans all media types and all categories.

The `MetadataProviderCard` gained an `IsUniverseProvider` parameter that triggers this special styling.

**Capability icon expansion:**

The provider card's capability icon set expanded from 12 entries to 60+ entries, covering every claim key in the Master Authority Table. Icons are drawn from the Material Design icon set and organised by category (core identity, people, lore, bridges, social).

**Key types introduced:**
- `HydrateResultViewModel` (`MediaEngine.Web.Models.ViewDTOs`) — Dashboard DTO for hydration results
- `HydrateResponse` (`MediaEngine.Api.Models`) — Engine DTO matching the Dashboard shape

**Why this matters to the business:**
- **Extensibility** — The Data Extension API fetches all configured properties in one call. Adding support for a new Wikidata property is still one JSON entry in provider config.
- **Reliability** — Bridge ID cross-reference → title search → review queue maximises match rate. If no match is found, the file keeps its existing metadata untouched.
- **Performance** — User-triggered hydration bypasses the background queue for immediate results. The background pipeline continues to handle automatic post-ingestion enrichment.
- **Privacy** — Only titles, ISBNs, ASINs, and bridge IDs are sent to Wikidata. Everything hydrated lives locally.

### 3.11 — Configuration Architecture Standard

**Plain English:** All Engine settings live in a structured `config/` directory as individual JSON files, grouped by concern. This replaces the legacy single-file `tuvima_master.json` approach. The old file is automatically migrated on first run and renamed to `.migrated`.

**Directory layout:**
```
config/
  library.json                    ← Core: paths, schema version, org template
  libraries.json                  ← Library folders: category, media types, source path,
                                     intake mode (watch/import), library root (§3.1)
  scoring.json                    ← Scoring: thresholds, decay
  field_priorities.json           ← Per-field provider priority overrides (§3.2):
                                     which provider wins for description, cover, etc.
  maintenance.json                ← Maintenance: retention, vacuum, sync
  hydration.json                  ← Hydration pipeline: stage timeouts, concurrency,
                                     disambiguation/confidence thresholds (§3.13)
  providers/
    local_filesystem.json         ← Per-provider: weight, enabled, endpoints,
    apple_api.json                   field_weights, throttle_ms, max_concurrency,
                                       hydration_stages, media-type-scoped strategies (§3.13)
    open_library.json
    google_books.json
    wikidata.json
    wikidata_reconciliation.json  ← Reconciliation + Data Extension config
  universe/
    wikidata.json                 ← Universe knowledge model: full property map,
                                     bridge priority, value transforms, scope exclusions
```

**Key distinction:**
- `config/providers/wikidata.json` = how the **scoring engine** treats Wikidata (weight, field weights, throttle, enabled)
- `config/universe/wikidata.json` = the **knowledge model** — how to interpret Wikidata's data (property map, bridge lookup order, value transforms, which P-codes are entity-valued, which scopes exclude which properties)

**Architectural rules:**
1. **One concern per file** — each config file contains a single, cohesive group of related settings.
2. **Minimum grouping threshold** — if a proposed configuration section has fewer than 3 fields, Claude must ask the Product Owner how to handle it: combine with another section, give it its own file anyway, or defer until more settings accumulate.
3. **Provider files are self-contained** — each provider carries its own endpoints, weights, capability tags, throttle delay, and concurrency limit in `config/providers/{name}.json`.
4. **Universe files hold the knowledge model** — property mappings, bridge definitions, value transforms, and scope exclusions live in `config/universe/{provider}.json`, separate from provider scoring config.
5. **Universe replaceability** — the schema is generic enough that a different universe provider could use the same model shape.
6. **Example files committed, live files gitignored** — `config.example/` is in git; `config/` is gitignored.
7. **Migration contract** — the `ConfigurationDirectoryLoader` auto-migrates from the legacy single-file format on first run. The legacy file is renamed to `.migrated`.
8. **Fallback resilience** — compiled defaults in `WikidataPropertyMap.DefaultMap` serve as fallback if config files are missing or corrupt.
9. **Transform registry in code, transform assignment in config** — transform functions are behaviour (live in `ValueTransformRegistry.cs`); which property uses which transform is data (lives in `config/universe/wikidata.json`).
10. **Config directory path** — specified in `appsettings.json` as `MediaEngine:ConfigDirectory` (default: `"config"`). Legacy `MediaEngine:ManifestPath` is still checked as fallback for backward compatibility.

**Key types:**
- `IConfigurationLoader` (`MediaEngine.Storage.Contracts`) — granular config access contract: `LoadCore()`, `LoadScoring()`, `LoadMaintenance()`, `LoadFieldPriorities()`, `LoadProvider(name)`, `LoadAllProviders()`, generic `LoadConfig<T>(subdirectory, name)`.
- `ConfigurationDirectoryLoader` (`MediaEngine.Storage`) — implements both `IConfigurationLoader` (new granular access) and `IStorageManifest` (backward compat). Auto-migrates legacy files; `.bak` rotation on every save.
- `CoreConfiguration`, `ProviderConfiguration`, `FieldPriorityConfiguration` (`MediaEngine.Storage.Models`) — settings models.
- `UniverseConfiguration`, `WikidataPropertyConfig`, `BridgeLookupEntry` (`MediaEngine.Providers.Models`) — universe knowledge model.
- `ValueTransformRegistry` (`MediaEngine.Providers.Models`) — named transform function registry.

**Why this matters to the business:**
- **Extensibility** — Adding a new Wikidata property, reordering bridge lookups, or disabling a transform is a JSON edit. Zero code changes.
- **Maintenance** — Each config file is small, focused, and independently editable. A provider misconfiguration does not corrupt scoring settings.
- **Reliability** — Compiled defaults still serve as fallback if config files are missing or corrupt. Migration from the old single-file format is automatic.

### 3.12 — UI Configuration & Device Profiles

**Plain English:** The Dashboard adapts its visual layout, available features, and page structure based on what device is viewing it. A phone gets a compact layout, a TV gets oversized buttons, a car gets audio-only tiles. All of this is controlled by configuration files — not hard-coded CSS breakpoints.

**Three-Tier Cascade:** Global → Device → Profile. Settings merge in order: Global provides app-wide defaults, Device overlays structural constraints and overrides for each device class, Profile adds user preferences. Device **constraints** (disabled features, disabled pages, forced dark mode) are hard limits — no profile can re-enable them.

**Config files (following §3.11 pattern):**
```
config/ui/
  global.json                    ← App-wide UI defaults
  devices/
    web.json                     ← Desktop browser (unconstrained)
    mobile.json                  ← Phone-sized screens
    television.json              ← TV displays (remote-navigable)
    automotive.json              ← Car displays (audio-focused)
  profiles/
    {profile-id}.json            ← Per-user overrides
```

Example files in `config.example/ui/`. Live files in `config/ui/` gitignored.

**DB Cache:** Table `ui_settings_cache` (migration M-011) caches resolved JSON per scope. On startup: scan `config/ui/`, populate cache. API reads from cache.

**Four device classes:**

| Class | Constraints | Key Overrides |
|---|---|---|
| **web** | None | Full layout, all features |
| **mobile** | view_toggle disabled, 48px touch targets | Compact appbar, icon-only logo, stacked layouts, 1-col grid |
| **television** | 8 features disabled, server_settings page disabled, no text input, 64px targets | Oversized appbar/dock, focus-navigable tabs, 2-col large tiles |
| **automotive** | Same as TV + forced dark mode, 80px targets | Minimal appbar, 2 dock items (Hubs+Listen), audio-only tiles |

**Dashboard device detection:** JS interop checks URL param `?device=`, then `localStorage`, then auto-detects (viewport ≤768px + touch = mobile, else web). Television/automotive require explicit selection.

**Key types:**
- `UIGlobalSettings`, `UIFeatureFlags`, `UIShellSettings`, `UIPageSettings` + 3 nested, `UIDeviceProfile` + `UIDeviceConstraints`, `UIProfileSettings`, `ResolvedUISettings` (`MediaEngine.Storage.Models`)
- `UISettingsCascadeResolver` (`MediaEngine.Storage`) — merges Global+Device+Profile
- `UISettingsCacheRepository` (`MediaEngine.Storage`) — SQLite cache CRUD
- `UISettingsEndpoints` (`MediaEngine.Api.Endpoints`) — 7 API endpoints including `GET /settings/ui/resolved`
- `DeviceContextService` (`MediaEngine.Web.Services.Theming`) — per-circuit scoped; replaced `AutomotiveModeService`
- `ResolvedUISettingsViewModel` + DTOs (`MediaEngine.Web.Models.ViewDTOs`) — Dashboard view model

**Adapted components:** MainLayout, TopBar (4 variants), LeftDock, MobileNavDrawer, SettingsTabBar (5 layout modes), GeneralTab (conditional sections), Home, HubHero (cinematic banner), PosterSwimlane + PosterCard (responsive card widths), PendingFilesAlert (expandable/badge/hidden), ServerSettings (redirect guard), Preferences (conditional links).

**Why this matters to the business:**
- **Extensibility** — Adding a new device class is one JSON file + one entry in the cascade resolver. No code changes in the Dashboard.
- **Maintenance** — All layout rules live in configuration, not scattered CSS breakpoints. A single file controls what an automotive dashboard looks like.
- **Privacy** — Device detection runs client-side. No telemetry or fingerprinting.
- **Reliability** — If the Engine is offline, the Dashboard falls back to compiled web defaults. No blank screens.

### 3.13 — Two-Stage Hydration Pipeline & Review Queue

**Philosophy:** Wikidata is the sole identity authority (see §3.6). Every media item is identified by its Wikidata Q-identifier; all other providers supply media assets only. The two-stage pipeline runs retail providers first to gather cover art and deposit bridge IDs, then uses those bridge IDs for precise Wikidata identity resolution. Person and human data is always sourced from Wikidata. Secondary retail providers exist to fill gaps — new releases, and copyright-safe cover art that Wikimedia cannot host.

**Plain English:** When a file arrives in the library, the Engine runs a two-stage enrichment pipeline. Stage 1 (RetailIdentification) runs retail providers to gather cover art, descriptions, ratings, and bridge IDs. Stage 2 (WikidataBridge) uses those bridge IDs for precise Wikidata QID resolution and fetches core properties. Ambiguous matches are surfaced via a dedicated review queue.

**The two stages:**

| Stage | Name | What it does | Who runs |
|---|---|---|---|
| **Stage 1** | RetailIdentification | Retail providers run in waterfall order from `config/slots.json`. Use file metadata (title, author) to search, score results, and deposit cover art, descriptions, ratings, and bridge IDs (ISBN, ASIN, TMDB ID). | Apple API, Open Library, Google Books, TMDB, MusicBrainz, Comic Vine — any provider declaring `hydration_stages: [1]` |
| **Stage 2** | WikidataBridge | Wikidata Reconciliation runs second. Uses bridge IDs from Stage 1 for edition-first QID resolution (work fallback). Single QID → Data Extension for core + bridge properties. Wikipedia descriptions fetched via `GetWikipediaSummariesAsync`. Hub Intelligence + Person Enrichment. On failure: `AuthorityMatchFailed` review item created, pipeline continues if `continue_pipeline_on_authority_failure` is true. | ReconciliationAdapter — any provider declaring `hydration_stages: [2]` |

**Post-pipeline confidence check:** After both stages complete, the pipeline reloads canonical values and computes overall confidence. If below `auto_review_confidence_threshold` (default: 0.60), a review queue entry is created.

**Provider stage assignment:**

Each provider config carries a `hydration_stages` array declaring which stages it participates in:
```json
{
  "name": "open_library",
  "hydration_stages": [2],
  ...
}
```

All retail REST providers (Apple API, Open Library, Google Books, TMDB, etc.) declare `[1]` (RetailIdentification). Wikidata Reconciliation declares `[2]` (WikidataBridge).

**Review Queue:**

The `review_queue` table stores items that need human attention:
- **AuthorityMatchFailed** — Stage 2 (Wikidata) failed to resolve a QID for the work
- **LowConfidence** — pipeline completed but overall confidence is below threshold
- **MultipleQidMatches** — Stage 2 found multiple Wikidata QID candidates; user must pick one
- **UserFixMatch** — user-triggered re-review
- **ArbiterNeedsReview** — the Hub Arbiter flagged an uncertain assignment

Each review item carries: entity reference, trigger reason, confidence score, optional disambiguation candidates (JSON array of `{ qid, label, description }`), and a detail string.

**Review resolution flow:**
1. User opens Needs Review in Settings → Metadata section
2. Selects an item → sees current metadata vs proposed match
3. For disambiguation: picks a QID candidate from a card grid
4. Clicks Resolve → `POST /review/{id}/resolve` fires
5. Engine creates user-locked claims for any field overrides
6. If a QID was selected → `RunSynchronousAsync` with `PreResolvedQid` triggers Stage 2 (WikidataBridge)
7. Activity ledger records `ReviewItemResolved`
8. SignalR broadcasts `ReviewItemResolved` → badge count decrements

**Image hash validation (cross-cutting):**

Cover art and provider thumbnails are tracked by content hash (SHA-256) in the `image_cache` table to prevent redundant re-downloads. When the same image URL appears across multiple entities, the hash is checked first; if found, the cached file path is reused.

**Pipeline configuration (`config/hydration.json`):**
```json
{
  "stage_concurrency": 3,
  "stage1_timeout_seconds": 45,
  "stage2_timeout_seconds": 30,
  "disambiguation_threshold": 0.7,
  "auto_review_confidence_threshold": 0.60,
  "max_qid_candidates": 5,
  "continue_pipeline_on_authority_failure": true,
  "universe_title_search_auto_accept": 0.80,
  "stage2_waterfall_confidence_threshold": 0.65
}
```

**Dual-path architecture:** The existing `MetadataHarvestingService` is preserved for `Person`-type requests from `RecursiveIdentityService`. The new `HydrationPipelineService` handles `MediaAsset`-type hydration using retail providers (Stage 1) followed by `ReconciliationAdapter` (Stage 2). Both paths are safe to run concurrently — person creation is idempotent.

**`ScoringHelper`:** The duplicated claim-persist-score-upsert pattern (previously inlined in `MetadataHarvestingService` and `MetadataEndpoints`) is extracted into a shared static helper used by both services.

**API endpoints:**

| Method | Route | Purpose |
|---|---|---|
| GET | `/review/pending?limit=50` | List pending review items |
| GET | `/review/{id}` | Single review item with full detail |
| GET | `/review/count` | Pending count (for sidebar badge + avatar badge) |
| POST | `/review/{id}/resolve` | Resolve: select QID or confirm field overrides |
| POST | `/review/{id}/dismiss` | Dismiss: mark as irrelevant |
| GET | `/settings/hydration` | Load pipeline configuration |
| PUT | `/settings/hydration` | Save pipeline configuration |

**SignalR events:**
- `ReviewItemCreated` — new review queue item (badge increment)
- `ReviewItemResolved` — item resolved/dismissed (badge decrement)
- `HydrationStageCompleted` — stage-by-stage progress for live display

**Dashboard integration:**

The Settings sidebar is restructured into three groups:

| Group | Items |
|---|---|
| **Preferences** | General, Playback, Navigation |
| **Metadata** | Connection Vault, Needs Review, Activity |
| **Server** | Library, Connectivity, API Keys, Conflicts, Users, Maintenance |

Three Settings tabs in the Metadata group:
- **Connection Vault** — unified provider slot assignment via drag-drop. Media-type tabs (Books, Audiobooks, Comics, Movies, TV Shows) each show three priority slots (Primary — Automatic Match, Secondary — Manual Fallback 1, Tertiary — Manual Fallback 2). **All providers** appear in every media-type tab regardless of their `can_handle.media_types` — copy semantics means dragging a provider to a slot creates a copy; the provider stays in the available pool. Users can mix and match providers across media types as they choose. Provider cards show health status, accent colour, and a config flyout for endpoint/timeout/API key editing.
- **Needs Review** — review queue table with trigger chips, confidence gauges, resolve/dismiss actions, live SignalR updates
- **Activity** — date-grouped timeline of all Engine actions (ingestion, metadata hydration, review resolution, pruning). Configurable retention period (default 60 days) at the top. Load-more pagination. Entries show action icon, label, hub name chip, entity type chip, detail text, and formatted time. Replaces the activity section formerly in MaintenanceTab.

Absorbed/removed tabs:
- **Property Mapper** — removed (file deleted). Deep-link URLs redirect to Connection Vault via `Settings.razor` switch.
- **Matching Pipeline** — removed (file deleted). Pipeline config absorbed into Connection Vault's Advanced section. Deep-link URLs redirect to Connection Vault.

**Profile avatar notification badge:** The profile avatar (top-right in the AppBar) shows a `MudBadge` with the pending review count, kept current via SignalR. This ensures the user sees pending reviews globally.

**Mobile constraints:** `property_mapper`, `matching_pipeline`, and `universe_schema_editing` are added to `features_disabled` on mobile devices. Only Needs Review and Connection Vault (status view) are visible in the Metadata group on mobile.

**Key types:**
- `HydrationStage` (`MediaEngine.Domain.Enums`) — `RetailIdentification = 1, WikidataBridge = 2`
- `ReviewTrigger`, `ReviewStatus` (`MediaEngine.Domain.Enums`) — trigger/status enums
- `ReviewQueueEntry` (`MediaEngine.Domain.Entities`) — domain entity
- `HydrationResult` (`MediaEngine.Domain.Models`) — pipeline result with per-stage claim counts
- `IHydrationPipelineService` (`MediaEngine.Domain.Contracts`) — `EnqueueAsync` + `RunSynchronousAsync`
- `IReviewQueueRepository` (`MediaEngine.Domain.Contracts`) — CRUD for review queue
- `IImageCacheRepository` (`MediaEngine.Domain.Contracts`) — content-hash image cache
- `HydrationPipelineService` (`MediaEngine.Providers.Services`) — two-stage orchestrator
- `ScoringHelper` (`MediaEngine.Providers.Services`) — shared claim-persist-score helper
- `ReviewQueueRepository`, `ImageCacheRepository` (`MediaEngine.Storage`) — SQLite implementations
- `HydrationSettings` (`MediaEngine.Storage.Models`) — pipeline config model
- `ReviewEndpoints` (`MediaEngine.Api.Endpoints`) — review queue API
- `ConnectionVaultTab`, `NeedsReviewTab` (`MediaEngine.Web.Components.Settings`) — Dashboard settings tabs (PropertyMapperTab and MatchingPipelineTab removed — absorbed into ConnectionVault)
- `ReviewItemViewModel`, `ReviewResolveRequestDto`, `HydrationSettingsDto` (`MediaEngine.Web.Models.ViewDTOs`) — Dashboard DTOs

**Why this matters to the business:**
- **Reliability** — Ambiguous matches are surfaced to the user instead of being silently dropped. The review queue ensures no metadata decision is made without confidence.
- **Extensibility** — Adding a new provider to any stage is a one-line JSON change (`hydration_stages: [1, 2]`). The pipeline handles routing automatically.
- **Performance** — Stage 1 retail providers run concurrently. The bounded channel queue (500 items) prevents memory pressure. Image hash caching eliminates redundant downloads.
- **Maintenance** — Pipeline configuration (timeouts, thresholds, concurrency) lives in `config/hydration.json` — zero code changes to tune behaviour. The dual-path architecture preserves backward compatibility with the existing person enrichment flow.
- **Privacy** — Only titles, ISBNs, ASINs, and bridge IDs leave the machine. All review decisions and hydrated data live locally.

### 3.14 — Media Type Disambiguation During Ingestion

**Plain English:** Some file formats map to multiple possible media types — an MP3 could be an audiobook, music, or a podcast; an MP4 could be a movie or a TV show. Magic bytes detect the container format but not the content type. This system uses heuristic signals (duration, genre tags, chapter markers, filename patterns, folder context) to vote on the most likely media type, routing ambiguous cases to the review queue for user resolution.

**How it works:**

Media type is treated as a **voted claim** — consistent with the Weighted Voter architecture. Multiple heuristic signals emit competing `media_type` candidates with varying confidence. The scoring engine resolves the winner.

| Signal source | Confidence range | Examples |
|---|---|---|
| Magic bytes (unambiguous) | 0.95–1.0 | EPUB → Books, CBZ → Comics, M4B → Audiobooks |
| Processor heuristics | 0.30–0.80 | Duration, bitrate, chapter markers, genre tag |
| Filename/path patterns | 0.25–0.65 | "S01E01" → TV, path contains "audiobooks" |
| User lock | 1.0 | Manual override (always wins) |

**Confidence thresholds:**
- **≥ 0.70** (`auto_assign_threshold`) — accept automatically, proceed normally
- **0.40–0.70** (`review_threshold`) — accept provisionally, create `AmbiguousMediaType` review queue entry
- **< 0.40** — assign `MediaType.Unknown`, block auto-organize, create review entry

**AudioProcessor (new):**
- Priority: **95** (above VideoProcessor at 90)
- Unambiguous: M4B → Audiobooks (0.98), FLAC/OGG/WAV → Music (0.95)
- Ambiguous (MP3, M4A): heuristic signals — duration bands, chapter markers, genre tags, album/track metadata, bitrate, path keywords, file size
- Base score 0.25 per type (Audiobook, Music, Podcast), signals additive, normalized to [0.0, 1.0]

**VideoProcessor (enhanced):**
- Heuristic signals: TV filename patterns (`SxxExx`, `NxNN`), duration bands, path keywords, file size, sibling file count
- Base score 0.35 per type (Movie, TV), signals additive, normalized to [0.20, 0.90]

**Pipeline integration (Step 6a):**
Inserted between Step 6 (Process) and canonical value persistence. When `ProcessorResult.MediaTypeCandidates` is non-empty, the top candidate's confidence is checked against thresholds. The auto-organize gate blocks organization when `mediaTypeNeedsReview` is true.

**Review resolution:**
- `POST /metadata/{entityId}/reclassify` — creates user-locked `media_type` claim at 1.0, upserts canonical value, resolves review item, re-triggers hydration
- NeedsReviewTab shows candidate cards with confidence bars for `AmbiguousMediaType` items
- Post-hydration auto-resolve: if Stage 1 returns ≥ 3 claims, auto-resolves pending `AmbiguousMediaType` review items

**Configuration (`config.example/disambiguation.json`):**
Thresholds and all heuristic parameters (duration bands, bitrate thresholds, path keywords, genre tags, TV filename patterns) are configurable. Thresholds are surfaced to `IngestionOptions` via `PostConfigure` (no new project references to Ingestion).

**Key types:**
- `MediaTypeCandidate` (`MediaEngine.Domain.Models`) — candidate with Type, Confidence, Reason
- `AudioProcessor` (`MediaEngine.Processors.Processors`) — audio format detection + heuristic disambiguation
- `DisambiguationSettings` (`MediaEngine.Storage.Models`) — typed config model
- `ProcessorResult.MediaTypeCandidates` — list of candidates emitted by processors
- `ReviewTrigger.AmbiguousMediaType` — review queue trigger constant

**Why this matters to the business:**
- **Reliability** — Ambiguous files are surfaced for user decision instead of being silently misclassified. Unambiguous formats (EPUB, CBZ, M4B) continue to work exactly as before.
- **Extensibility** — Adding new heuristic signals or adjusting weights requires only a config edit. The candidate voting pattern extends naturally to future media types (Music, Podcasts).
- **Maintenance** — All thresholds and heuristic parameters live in `config/disambiguation.json`. Zero code changes to tune behaviour.
- **Performance** — Heuristic analysis runs in-process during ingestion with no external calls. No impact on ingestion speed.

### 3.15 — Playback & Streaming Architecture (Target State)

> **Note:** Sections 3.15–3.20 were renumbered when §3.14 (Media Type Disambiguation) was inserted.

> **Status:** Not yet implemented. This section describes the target architecture for in-browser media consumption.

**Plain English:** Tuvima Library becomes a full media server by embedding high-quality players for every media type directly in the browser. Users can read books, watch movies, listen to audiobooks, and browse comics without leaving the Dashboard.

**Four built-in players:**

| Player | Media types | Route | Key features |
|---|---|---|---|
| **EPUB Reader** | Ebooks | `/read/{assetId}` | Paginated CSS multi-column view, chapter sidebar (TOC from EPUB), font size/family adjustment, dark/light reading mode, resume from last position, keyboard navigation, mobile swipe gestures |
| **Comic Viewer** | CBZ, CBR | `/read/{assetId}` (comic type) | Full-page image display, page-turn navigation (click/swipe/keyboard), page thumbnails sidebar, zoom/pan for high-res panels, LTR/RTL toggle for manga, prefetch next 2-3 pages |
| **Audiobook Player** | M4B, MP3, M4A | Persistent bottom bar | Play/pause, skip ±30s, playback speed (0.5x-3x), chapter list with navigation, sleep timer, progress bar with chapter markers, background audio (persists during page navigation) |
| **Video Player** | MP4, MKV, WebM, AVI | `/watch/{assetId}` | HTML5 video + HLS.js for adaptive streaming, subtitle track selection (SRT/VTT/ASS with on-the-fly WebVTT conversion), chapter markers, playback speed, PiP, keyboard shortcuts (Space/F/M/arrows), "mark as watched" at 90% threshold |

**Progress tracking contract:**
- `PUT /progress/{assetId}` — upserts `UserState` with progress percentage, last-accessed timestamp, and media-specific extended properties (page number for books, chapter index for audiobooks, timestamp for video)
- `GET /progress/{assetId}` — retrieves current position for resume
- All players update progress at configurable intervals (default: every 30 seconds or on chapter/page change)

**Content serving endpoints:**
- `GET /read/{assetId}/chapter/{index}` — serves EPUB chapter HTML/XHTML with embedded images and CSS
- `GET /comic/{assetId}/page/{pageNum}` — extracts individual images from CBZ/CBR archives
- `GET /stream/{assetId}/subtitles/{trackIndex}` — extracts embedded subtitles from MKV, converts to WebVTT
- `GET /stream/{assetId}/chapters` — chapter metadata for MKV/M4B files

**Audiobook player persistence:** The audiobook player component lives in `MainLayout.razor` (not inside a routed page) so it persists across navigation. A `PlaybackStateService` (scoped per circuit) manages the active audio session and exposes play/pause/seek to any component.

**Dashboard components (Feature-Sliced):**

| Component | Slice | Purpose |
|---|---|---|
| `EpubReader.razor` | `Components/Playback/` | Paginated ebook reader |
| `ComicViewer.razor` | `Components/Playback/` | Full-page comic viewer |
| `AudiobookPlayer.razor` | `Components/Playback/` | Persistent bottom-bar audio player |
| `VideoPlayer.razor` | `Components/Playback/` | Full-screen video player with HLS |
| `PlaybackStateService.cs` | `Services/Playback/` | Active session management, progress sync |

**Why this matters to the business:**
- **Reliability** — Every player tracks progress independently. Resume works across devices via the Engine API.
- **Extensibility** — The progress API is media-type-agnostic. Any future player (PDF viewer, music player) uses the same contract.
- **Privacy** — All playback happens locally. No telemetry, no cloud sync.
- **Performance** — Prefetching (comics), segmented serving (EPUB chapters), and byte-range streaming (audio/video) ensure smooth playback even for large files.

### 3.16 — Authentication & Multi-User (Target State)

> **Status:** Not yet implemented. Profiles exist with roles but no login system.

**Plain English:** Each household member gets their own login with separate progress tracking, reading history, and personalised settings. A simple PIN or password protects each profile.

**Phase 1 — Local authentication (target):**
- Login page: profile selection grid (avatar + name) → PIN/password entry
- Session management via secure HTTP-only cookie (or JWT for API access)
- Profile ↔ session binding: all API calls scoped to the authenticated profile
- "Remember me" cookie with configurable expiry (default: 30 days)
- PIN: minimum 4 digits. Password: minimum 8 characters.
- PIN/password management in the Users settings tab

**Profile-scoped data:** UserState (progress), reading/watching history, navigation config, Virtual Library config, UI theme preferences — all scoped to `profileId`.

**Phase 2 — OIDC (future):** Google, Facebook, and custom OIDC provider support. Deferred until after local auth is stable.

**Shared Journey Inference:** The Engine compares `UserState.LastAccessed` timestamps between profiles. If 2+ profiles access the same asset within a 5-minute window, the viewing session is tagged as a "Shared Journey" (e.g. family movie night). Solo sessions are tagged "Solo Journey". The Dashboard shows which journeys were shared.

**Parental controls:** Content rating tags from TMDB (P1657 via Wikidata) or manual assignment. Profile-level maturity filter (Kids, Teen, Adult). PIN-protected access to mature content.

**Why this matters to the business:**
- **Privacy** — Each household member's reading habits are private to their profile.
- **Reliability** — Session-based auth ensures progress is always attributed to the correct person.
- **Extensibility** — OIDC support can be layered on later without changing the profile model.

### 3.17 — Transcoding Pipeline (Target State)

> **Status:** Not yet implemented. `IVideoMetadataExtractor` is a stub.

**Plain English:** Tuvima Library uses FFmpeg to convert video files into formats that every device can play smoothly. It can transcode on-the-fly when you press play, or pre-create mobile-friendly copies overnight so streaming is instant.

**FFmpeg integration (`FFmpegService`):**
- Auto-detect FFmpeg/FFprobe installation paths
- Hardware capability detection: NVENC (NVIDIA), QuickSync (Intel), VAAPI (Linux AMD/Intel)
- Replace `StubVideoMetadataExtractor` with real metadata extraction (resolution, duration, codec, frame rate)
- Extract embedded subtitles (SRT, ASS, VTT)
- Extract chapter information from MKV/MP4

**On-the-fly transcoding:**
- `GET /stream/{assetId}/transcode` — HLS output (segmented .m3u8 + .ts chunks)
- Client sends codec capabilities via query params; Engine selects appropriate transcode profile
- Quality profiles: Original, 1080p, 720p, 480p
- Hardware-accelerated encoding when available; software fallback
- Session management: active sessions tracked per user, temp segments cleaned up after session ends

**Shadow Transcoder (scheduled background jobs):**
- `TranscodeJob` domain model: source asset, target quality profile, status, progress percentage
- Priority: user-requested > scheduled > background
- Background service runs on configurable schedule (default: daily at 3:00 AM)
- Scans library for video assets without mobile-optimized copies
- Creates lower-bitrate variants (720p H.264 for mobile) stored at `{LibraryRoot}/.tuvima-shadow/{assetId}/{quality}.mp4`
- Respects hardware limits (1 concurrent transcode by default, configurable)
- Progress reporting via SignalR (`TranscodeProgress` event)
- Automatic cleanup when source asset is deleted

**Configuration:** `config/transcoding.json`
```json
{
  "quality_profiles": [
    { "name": "mobile", "resolution": "720p", "codec": "h264", "bitrate": "2M" },
    { "name": "tablet", "resolution": "1080p", "codec": "h264", "bitrate": "5M" }
  ],
  "schedule": { "enabled": true, "cron": "0 3 * * *" },
  "hardware_preference": "auto",
  "max_concurrent": 1,
  "shadow_storage_limit_gb": 500
}
```

**Why this matters to the business:**
- **Performance** — Shadow copies eliminate transcoding delay during playback. Mobile users get instant streaming.
- **Reliability** — Hardware-accelerated encoding reduces CPU load. Software fallback ensures it works everywhere.
- **Extensibility** — Quality profiles are configurable. New profiles can be added without code changes.
- **Maintenance** — Storage limits prevent shadow copies from consuming the entire drive.

### 3.18 — Music Domain Model (Target State)

> **Status:** Not yet implemented. No music media type exists.

**Plain English:** Music becomes a first-class citizen. Albums are Hubs, tracks are Works, and artists are Persons. MusicBrainz provides authoritative metadata.

**Domain model extension:**
- New `MediaType.Music` enum value
- Album maps to Hub (the "story" is the album)
- Track maps to Work (individual title within the album)
- Artist maps to Person with role "Artist"
- Track number → `Work.SequenceIndex` (album ordering)
- Disc number → stored as canonical value `disc_number`

**MusicProcessor:**
- Reads ID3v2 tags (MP3), Vorbis comments (FLAC/OGG), and MP4 atoms (M4A/AAC)
- Extracts: title, artist, album, track number, disc number, genre, year, album art
- Embedded album art extraction (same pattern as EPUB cover extraction)
- Magic bytes detection: MP3 (`FF FB` / `49 44 33`), FLAC (`66 4C 61 43`), OGG (`4F 67 67 53`), M4A (ftyp)

**Providers:**
- **MusicBrainz** (config-driven, zero-key): search by artist + album or MBID. Field weights: artist 0.9, album 0.85, year 0.9, genre 0.7
- **Spotify** (config-driven, requires API key): artist headshots, album art, genre, popularity. Metadata only — no streaming.

**Music player UI:** Album view with track list, play queue, gapless playback (stretch goal), crossfade (stretch goal). Shares the persistent bottom-bar player with audiobooks (same `AudiobookPlayer.razor` component, generalised to `AudioPlayer.razor`).

**Why this matters to the business:**
- **Extensibility** — Music uses the exact same Hub/Work/Edition/MediaAsset hierarchy as all other media types. No special casing.
- **Maintenance** — MusicBrainz is a zero-key provider. Spotify requires a free API key but brings high-quality artwork.
- **Privacy** — Only artist names and album titles are sent to external services.

### 3.19 — Interoperability & Ecosystem (Target State)

> **Status:** Not yet implemented.

**Plain English:** Tuvima Library speaks the languages that other apps already understand. Ebook readers can browse your library via OPDS. Audiobook apps can connect via an Audiobookshelf-compatible interface. External tools get notified via webhooks when new content arrives.

**OPDS 1.2 catalog (`/opds/`):**
- Atom XML feeds: root, search (OpenSearch), categories (by media type, author, series), recently added
- OPDS Page Streaming Extension for comic viewers
- Compatible with: Moon Reader, KOReader, Calibre, Thorium Reader
- Authentication via API key in URL parameter or HTTP Basic (mapped to API key)

**Audiobookshelf-compatible API (subset):**
- `/api/libraries` — list available audiobook libraries
- `/api/items/{id}` — item detail with chapters and progress
- `/api/me/listening-sessions` — progress sync
- Allows existing Audiobookshelf mobile apps to connect without modification

**Webhook system:**
- Configuration: `config/webhooks.json` — list of URL + events + HMAC secret
- Events: `FileIngested`, `MetadataHydrated`, `TranscodeCompleted`, `PersonEnriched`
- Delivery: HTTP POST with `X-Tuvima-Signature` HMAC-SHA256 header
- Use cases: Discord/Telegram notifications for new content, automation triggers

**Import wizard:**
- Plex: read Plex SQLite DB (`com.plexapp.plugins.library.db`), map sections → Library Hubs, import watched status
- Calibre: read `metadata.db`, import books with existing metadata
- Jellyfin: read NFO sidecar files alongside media, map to Library claims

**PWA capabilities:**
- Web app manifest + service worker for installable experience
- Offline cached shell (app loads without network; content requires Engine)
- Push notifications for new content (via Intercom bridge to Push API)

**Why this matters to the business:**
- **Extensibility** — OPDS is a universal standard. Any ebook reader in the world can connect. This is a major differentiator over Plex/Jellyfin.
- **Reliability** — The import wizard reduces migration friction. Users don't lose their existing watched/read progress.
- **Privacy** — Webhooks are outbound-only and user-configured. No telemetry.

### 3.20 — Browse & Discovery Pages (Target State)

> **Status:** Not yet implemented. Only the Home grid and Settings page exist.

**Plain English:** Users can drill into any Hub to see its full contents, view rich metadata for any Work or Edition, browse creator profiles with social links, and always see what's new or in progress.

**New pages:**

| Page | Route | Content |
|---|---|---|
| `HubDetail.razor` | `/hub/{id}` | Hero artwork + dominant color, Hub metadata (name, year, franchise, QID link), Works list grouped by media type, Person credits with headshots, Social Pivot links, "Hydrate from Wikidata" button |
| `WorkDetail.razor` | `/work/{id}` | Work metadata (title, author, year, series position), Edition list with format labels and file sizes, Claim history panel, Cover art, Play/Read/Listen button |
| `PersonDetail.razor` | `/person/{id}` | Headshot, biography, occupation, Social links (Instagram, TikTok, Mastodon, website), Works grouped by role (author, narrator, director). The "Human Hub" from the spec |

**New Home page sections (top to bottom):**
1. **Continue Journey** — most recently accessed, incomplete items (queries `UserState`)
2. **Recently Added** — horizontal scroll of newest Hubs (new endpoint: `GET /hubs/recent?limit=20`)
3. **Smart Collections** — "In Progress", "New This Week", "Unread" (auto-generated from metadata)
4. **Your Universes** (existing) — Poster swimlane grid of all Hubs

**Navigation additions:**
- Breadcrumb trail: Home → Hub → Work
- Click-through from UniverseStack tiles → HubDetail
- Click-through from search results → WorkDetail
- "Next in series" navigation on WorkDetail (uses `SequenceIndex`)
- Faceted filtering on Home: by year range, media type, author

**API additions:**
- `GET /hubs/recent?limit=20` — recently added Hubs
- `GET /journey/continue?profileId={id}&limit=10` — continue watching/reading
- `GET /hubs/{id}/works` — Works for a Hub with full canonical values
- `GET /works/{id}/editions` — Editions for a Work with file metadata
- `GET /persons/{id}` — Person detail with social links and linked works
- `GET /collections` — Smart and user-created collections
- `GET /collections/{id}/items` — Items in a collection

**Why this matters to the business:**
- **Reliability** — Every piece of data the Engine knows is now accessible in the Dashboard. No hidden metadata.
- **Extensibility** — The page routing pattern (`/hub/{id}`, `/work/{id}`, `/person/{id}`) is consistent and predictable.
- **Performance** — Smart Collections are pre-computed from existing canonical values. No expensive queries at render time.

### 3.21 — Cross-Media Metadata Strategy & Provider Response Caching

**Plain English:** The two-stage hydration pipeline (§3.13) works identically for all seven media types — Books, Audiobooks, Movies, TV Shows, Comics, Music, and Podcasts. Stage 1 runs retail providers to gather cover art and bridge IDs; Stage 2 runs Wikidata Reconciliation using those bridge IDs. The pipeline architecture itself is media-type-agnostic. A new response cache eliminates redundant API calls when bulk-importing large collections.

**Stage 1 retail provider slot assignments per media type:**

Wikidata Reconciliation (Stage 2) is the universal authority source for all media types. The table below shows Stage 1 retail provider assignments — these gather cover art, ratings, and bridge IDs that Wikidata uses for precise identity resolution.

| Media Type | Retail Primary | Retail Secondary | Retail Tertiary | Bridge to Wikidata |
|-----------|-------------------|-----------|----------|-------------------|
| **Books** | Apple API (zero-key) | Google Books (key) | Open Library (zero-key) | ISBN (P212), Apple Books ID (P6395) |
| **Audiobooks** | Apple API (zero-key) | Google Books (key) | — | ASIN, Apple Books ID (P6395) |
| **Movies** | TMDB (free key) | — | — | TMDB ID (P4947), IMDb ID (P345) |
| **TV Shows** | TMDB (free key) | — | — | TMDB TV ID (P4983), IMDb ID (P345) |
| **Comics** | Comic Vine (free key) | — | — | Comic Vine ID (P5905) |
| **Music** | MusicBrainz (zero-key) | — | — | MusicBrainz ID (P434/P436) |
| **Podcasts** | Apple Podcasts (zero-key) | Podcast Index (free key) | — | Apple Podcasts ID (P5842) |

Slot assignments are configured in `config/slots.json`. TMDB and Comic Vine require free API keys — a first-run setup wizard guides key registration.

**New providers added:**

- **Apple Podcasts** (`config/providers/apple_podcasts.json`) — uses the same iTunes Search API as Apple API (`https://itunes.apple.com`) with `entity=podcast` and `entity=podcastEpisode`. Zero-key. Same 9999 artwork trick for up to 3000×3000 cover art. Provider ID: `b9000009-0000-4000-8000-000000000010`.
- **Podcast Index** (`config/providers/podcast_index.json`) — secondary podcast provider from podcastindex.org. Requires a free API key. Returns show metadata, episode lists, and podcast GUIDs. Provider ID: `ba00000a-0000-4000-8000-000000000011`.

**Artwork quality strategy:**

| Media Type | Primary Art Source | Resolution | Notes |
|-----------|-------------------|-----------|-------|
| Books & Audiobooks | Apple API | Up to 3000×3000 | 9999 trick already in config |
| Movies & TV | TMDB | Up to 2000×3000 (w500 default) | Backdrop also available at w1280 |
| Comics | Comic Vine | ~900px (super_url) | Upgraded from medium_url |
| Music | Cover Art Archive | 500px (front-500) | Upgraded from front-250 |
| Podcasts | Apple Podcasts | Up to 3000×3000 | Same 9999 trick as Apple API |

**Provider response caching:**

A new `provider_response_cache` table (migration M-021) stores raw JSON responses from metadata provider API calls, eliminating redundant requests when multiple files share the same entity (TV episodes from one show, album tracks, comic issues from one volume).

Schema:
```sql
CREATE TABLE provider_response_cache (
    cache_key     TEXT NOT NULL PRIMARY KEY,
    provider_id   TEXT NOT NULL,
    query_hash    TEXT NOT NULL,
    response_json TEXT NOT NULL,
    etag          TEXT,
    fetched_at    TEXT NOT NULL,
    expires_at    TEXT NOT NULL
);
```

How it works:
1. Before making an HTTP call, `ConfigDrivenAdapter` hashes the full request URL
2. Checks `provider_response_cache` for a non-expired entry → cache HIT skips HTTP entirely
3. If expired but has ETag → sends `If-None-Match` header; 304 Not Modified → reuses cached response
4. If miss → makes HTTP call, caches response with per-provider TTL from `cache_ttl_hours` config

Per-provider TTL defaults: Apple API 168h (7d), TMDB 168h, Open Library 336h (14d), Google Books 168h, MusicBrainz 336h, Comic Vine 720h (30d — strict rate limits).

**File-first invariant:** The response cache is a **performance optimization only**. On a fresh install or database rebuild, the cache starts empty. Canonical values are rebuilt via file re-ingestion and batch Reconciliation API. The cache repopulates naturally during re-hydration.

**Rate limit awareness:**

| Provider | Rate Limit | Time for 10,000 files (uncached) | With Cache |
|----------|-----------|----------------------------------|-----------|
| Apple API/Podcasts | ~20 req/sec | ~33 min | ~5 min (series/episodes share) |
| TMDB | 50 req/sec | ~42 min | ~5 min (TV series episodes share) |
| MusicBrainz | 1 req/sec | ~3 hours | ~15 min (album tracks share) |
| Comic Vine | 200 req/hour | ~14 hours | ~2 hours (volume issues share) |
| Wikidata Reconciliation | ~5 req/sec | ~83 min | ~20 min |

**Key types:**
- `IProviderResponseCacheRepository` (`MediaEngine.Domain.Contracts`) — cache contract: `FindAsync`, `UpsertAsync`, `FindExpiredEtagAsync`, `RefreshExpiryAsync`, `PurgeExpiredAsync`, `ClearAllAsync`, `GetStatsAsync`
- `ProviderResponseCacheRepository` (`MediaEngine.Storage`) — SQLite implementation
- `CachedResponse` (`MediaEngine.Domain.Contracts`) — record: `ResponseJson`, `Etag`
- `CacheStats` (`MediaEngine.Domain.Contracts`) — record: `TotalEntries`, `ActiveEntries`, `OldestEntryAt`
- `ProviderConfiguration.CacheTtlHours` (`MediaEngine.Storage.Models`) — per-provider TTL config field

**Config changes:**
- All provider configs gained `cache_ttl_hours` field
- `config/providers/tmdb.json` v1.1 — strategies scoped by `media_types` (Movies/TV), backdrop field mapping added
- `config/providers/comic_vine.json` v1.1 — cover URL upgraded to `image.super_url`, `comicvine_id` bridge mapping added
- `config/providers/musicbrainz.json` — Cover Art Archive URL upgraded to `front-500`
- `config/universe/wikidata.json` — added P4983 (TMDB TV), P5842 (Apple Podcasts), expanded P434 scope to Both, bridge lookup priority expanded
- `config/slots.json` — all 7 media types with default primary/secondary/tertiary providers

**Ingestion Hinting — sibling-aware metadata priming:**

When multiple files arrive in the same source folder (e.g. a TV season with 22 episodes, or an album with 12 tracks), the Engine uses **Ingestion Hinting** to avoid redundant external lookups. After the first file in a folder is fully hydrated, its resolved metadata becomes a "hint prior" for sibling files in the same directory.

How it works:
1. **First file in folder** — processed normally through the full two-stage pipeline. On successful hydration, the Engine caches a `FolderHint` record keyed by the source folder path, containing: resolved Hub ID, QID, series name, author/artist, and all bridge IDs deposited by Stage 1 (retail providers) and resolved by Stage 2 (Wikidata).
2. **Subsequent siblings** — when the next file from the same folder enters ingestion, the Engine checks for an existing `FolderHint`. If found, the hint's bridge IDs and Hub ID are injected as high-confidence priors (0.80) into the scoring pipeline *before* Stage 2 runs. This allows:
   - **Stage 2 skip for bridge-matched siblings:** If the sibling's embedded metadata (e.g. same series name, sequential episode number) is consistent with the hint, Stage 2 can resolve the QID instantly from the cached bridge IDs instead of running a fresh Reconciliation lookup.
   - **Hub pre-assignment:** The sibling is tentatively assigned to the same Hub, skipping the Arbiter's full matching cycle. The Arbiter still validates the assignment (and rejects if metadata diverges significantly), but the common case — 22 episodes of the same show — resolves in milliseconds.
3. **Hint expiry** — `FolderHint` records expire after 24 hours or when the source folder is no longer being actively monitored. They are stored in-memory (`ConcurrentDictionary<string, FolderHint>`) and not persisted to the database — they are a performance optimisation, not a source of truth.
4. **Divergence detection** — if a sibling file's embedded metadata disagrees with the hint (different series name, different author), the hint is ignored for that file and it proceeds through the full pipeline. This prevents a single misplaced file from corrupting an entire folder's metadata.

**Performance impact:** For a 22-episode TV season, Ingestion Hinting reduces Stage 2 Reconciliation calls from 22 to 1 (95% reduction). For a 12-track album, MusicBrainz lookups drop from 12 to 1. The provider response cache (above) handles Stage 1 deduplication; Ingestion Hinting handles Stage 2 + Hub assignment deduplication.

**Why this matters to the business:**
- **Performance** — Response caching cuts API calls by 3–5x for bulk imports of related content. A 5,000-episode TV library takes ~5 minutes instead of ~42 minutes. Ingestion Hinting further reduces Stage 2 Wikidata lookups by up to 95% for folder-grouped content.
- **Reliability** — Each provider's rate limits are respected. Cache prevents quota exhaustion during large imports. Hinting ensures sibling files land in the correct Hub consistently.
- **Extensibility** — Adding a new media type requires only a provider config file and a slot assignment — zero code changes. Podcasts were added this way.
- **Maintenance** — All provider configs, slot assignments, and cache TTLs are JSON-editable. No recompilation needed.
- **Privacy** — Only titles, ISBNs, and bridge IDs leave the machine. The response cache lives locally in SQLite. Ingestion Hints are in-memory only.

### 3.22 — Universe Graph Data Capture

**Plain English:** The Library builds a visual relationship graph connecting characters, locations, factions, and works across all media. When a work is enriched via Wikidata, the Engine discovers its fictional universe, extracts characters, locations, and organizations, enriches each entity with its own Data Extension query, and populates graph edges (father, mother, member_of, residence, etc.). The entire graph is served as Cytoscape.js-ready JSON and stored in SQLite.

**Key components:**

| Component | Purpose |
|---|---|
| **Narrative Root Resolver** | Determines which fictional universe a work belongs to (P1434 → P8345 → P179 → Hub DisplayName). Stores in `narrative_roots` table. |
| **RecursiveFictionalEntityService** | Find-or-create entities by QID, link to work, enqueue enrichment if not yet enriched. Mirrors `RecursiveIdentityService`. |
| **RelationshipPopulationService** | After entity enrichment, reads `_qid` claims and creates graph edges (father, spouse, member_of, residence, etc.). Depth limit: 1. |
| **UniverseGraphWriterService** | No-op — universe.xml sidecar writing removed. Database is the authoritative data store. |
| **UniverseGraphQueryService** | dotNetRDF in-memory SPARQL graph. Loads entities + relationships from SQLite, caches per universe. Pathfinding, family trees, cross-media queries. |
| **UniverseGraphEndpoints** | `GET /universes`, `GET /universe/{qid}`, `GET /universe/{qid}/graph` with type/work/ego-network filters. |

**Database tables (M-024 to M-027):** `fictional_entities` (UNIQUE on wikidata_qid), `fictional_entity_work_links` (junction), `entity_relationships` (graph edges with UNIQUE constraint), `narrative_roots` (QID PK with hierarchy).

**Entity types:** `FictionalEntityType.Character`, `Location`, `Organization`, `Event` — all stored in a single `fictional_entities` table with a CHECK constraint on `entity_sub_type`. Event represents narrative events (e.g. Battle of Helm's Deep, Clone Wars).

**Relationship types (22):** father, mother, spouse, sibling, child, opponent, student_of, partner, member_of, allegiance, educated_at, residence, creator, located_in, part_of, head_of, parent_organization, has_parts, position_held, conflict, significant_person, affiliation. Performer links stored separately in `character_performer_links` table, merged at query time.

**Sidecar:** Removed. All universe graph data stored exclusively in SQLite (fictional_entities, entity_relationships, narrative_roots). dotNetRDF loads from SQLite into in-memory graph for SPARQL queries. Actor headshots resolved from `.people/{QID}/headshot.jpg` via performer Person QID.

**SignalR events:** `FictionalEntityEnrichedEvent`, `RelationshipDiscoveredEvent`, `UniverseGraphUpdatedEvent`.

**Great Inhale extension:** Deprecated. Universe data recovery via scheduled SQLite backups or Wikidata re-fetch. `ScanUniversesAsync` is a no-op.

**Query efficiency:** Three layers prevent redundant Reconciliation calls — skip-if-enriched (entity level), provider response cache (HTTP level), universe-level deduplication (known QID).

### 3.23 — Person Infrastructure v1.1

**Plain English:** Person records now carry full biographical data (birth/death dates, birthplace, nationality), pseudonym links, and character-performer links. Person folders use `Name (QID)` naming for guaranteed uniqueness. Person.xml v1.1 includes biographical details, characters played, and pseudonym relationships for complete filesystem-first recovery.

**Database migrations (M-028 to M-030):**
- M-028: 6 biographical columns on `persons` (date_of_birth, date_of_death, place_of_birth, place_of_death, nationality, is_pseudonym)
- M-029: `character_performer_links` (person_id, fictional_entity_id, work_qid PK) — links actors to fictional characters
- M-030: `person_aliases` (pseudonym_person_id, real_person_id PK) — bidirectional pseudonym links

**Pseudonym resolution:** After Wikidata enrichment, P1773 (attributed_to) links pen names to real people, P742 (pseudonym) links real people to their pen names. Both directions stored in `person_aliases` table.

**Wikipedia for Persons:** After Wikidata enrichment, the `ReconciliationAdapter` fetches rich Wikipedia descriptions via `_reconciler.GetWikipediaSummariesAsync` (QID→Wikidata sitelink→Wikipedia REST API). Language-aware: reads `CoreConfiguration.Language`. Confidence 0.90. Runs at Stage 2 (WikidataBridge).

**Actor-character mapping:** `HydrationPipelineService.ResolveActorCharacterMappingsAsync` uses `WikibaseApiService.GetClaimsAsync(workQid, "P161")` to fetch cast member statements with P453 (character) qualifiers. For each (actor QID, character QID) pair: creates Person record for the actor, finds FictionalEntity for the character, links via `character_performer_links`. Actor is a real Person (e.g. Timothée Chalamet) with standard Wikipedia headshot — not a character-specific image.

**Person folder naming:** `.people/{person_qid}/` format. QID-based path avoids rename issues. Contains `headshot.jpg` (from Wikimedia Commons P18) and `characters/{character_qid}.jpg` (user-uploaded, future feature).

**Person.xml:** Removed. Person data stored exclusively in the database. Person folders (`.people/{QID}/`) contain images only — headshots and future user-uploaded character images. No XML sidecar files.

**New Wikidata properties (Person-scoped):** P19 (place_of_birth), P20 (place_of_death), P27 (country_of_citizenship), P742 (pseudonym), P1773 (attributed_to), P1813 (short_name).

**IPersonRepository additions:** `UpdateBiographicalFieldsAsync`, `LinkAliasAsync`, `FindAliasesAsync`, `LinkToCharacterAsync`, `GetCharacterLinksAsync`.

**Great Inhale v1.1 support:** `ScanPeopleAsync` reads v1.1 person.xml fields (biographical details, characters, pseudonym links), parses `Name (QID)` folder pattern for QID extraction, and rebuilds `person_aliases` and `character_performer_links` tables from sidecar data.

### 3.24 — Two-Pass Enrichment Architecture

**Plain English:** When a file arrives, the Engine runs a quick first pass to get it looking good on the Dashboard immediately — title, author, cover art, basic person photos. The deeper universe lookup — character relationships, cross-media narrative links, fictional entities, actor-character connections — runs later in the background when the system is idle, or on a nightly schedule.

**Pass 1 — Quick Match (immediate, during ingestion):**

Pass 1 runs as part of the normal two-stage hydration pipeline (§3.13) but fetches only core properties:

- **Stage 1 (RetailIdentification — core subset):** Retail providers run to gather cover art, ratings, and bridge IDs (ISBN, ASIN, TMDB ID). Falls back to title search when no bridge IDs are available.
- **Stage 2 (WikidataBridge — core subset):** Uses bridge IDs from Stage 1 to resolve QID. Fetch **core properties only**: title, author/artist, year, genre, series, series_position. Skip the full 50+ property Data Extension deep hydration. Wikipedia descriptions fetched via `GetWikipediaSummariesAsync`. Confidence 0.90.
- **Basic person creation:** Find/create Person records for author, narrator, director. Fetch name + headshot + occupation from Wikidata. Skip deep social links and biographical enrichment.

Result: the file appears on the Dashboard within seconds with title, author, cover art, and author photo.

**Pass 2 — Universe Lookup (deferred, background):**

Pass 2 handles everything that makes the library *intelligent* — the deep connections between media, people, and fictional worlds:

- **Full Data Extension deep hydration** — all 50+ properties from `WikidataPropertyMap`
- **Hub Intelligence** — franchise resolution, narrative root assignment (P1434, P8345, P179)
- **Fictional entity discovery** — characters, locations, organisations from Wikidata
- **Relationship population** — father, spouse, member_of, performer links (depth limit 1)
- **Deep person enrichment** — social links (Instagram, TikTok, Mastodon), biographical details (birth/death dates, nationality), pseudonym resolution (P1773/P742)
- **Character-performer links** — actor who played the character from the book
- **Universe graph population** — fictional entities, relationships, and narrative roots written to SQLite (sidecar writing removed)

**Recursive person enrichment in Pass 2:** When Pass 2 discovers a link — an author's pen name, an actor who played a character from a book, a director's other works — it enriches those people too. This recursive chain only runs in Pass 2 to avoid heavy load during initial ingestion. Pass 1 creates the person records; Pass 2 follows the web of connections.

**Scheduling: Priority Queue + Nightly Sweep (hybrid):**

1. **Primary mechanism: Priority queue.** Pass 2 requests go onto a low-priority background channel. When the ingestion pipeline is idle (no Pass 1 work pending, no files being processed), the service picks up Pass 2 requests and processes them with respectful rate limiting (e.g., 1 Reconciliation request every 2 seconds). If a new file arrives, Pass 2 processing pauses and Pass 1 takes priority.

2. **Safety net: Nightly sweep.** A configurable cron job (default: daily at 2:00 AM) scans for any Pass 2 requests older than N hours that the priority queue has not yet processed (e.g., due to sustained heavy ingestion). Processes in batches with configurable size and inter-batch delay.

3. **User-triggered override.** The existing "Hydrate" button in the Dashboard runs both passes synchronously via `RunSynchronousAsync`, bypassing the queue entirely.

4. **Dynamic fallback (on UI click).** `POST /universe/entity/{qid}/deep-enrich` — when a user clicks on an un-enriched entity in the Chronicle Explorer, triggers on-demand enrichment for the entity and its 1-hop neighbors. Enqueues via `IMetadataHarvestingService`. Depth capped at 3 to prevent runaway traversal. Returns within 2-3 seconds via existing harvest queue.

**Configuration** (`config/hydration.json` additions):
```json
{
  "two_pass_enabled": true,
  "pass1_core_properties_only": true,
  "pass2_idle_delay_seconds": 10,
  "pass2_rate_limit_ms": 2000,
  "pass2_nightly_cron": "0 2 * * *",
  "pass2_stale_threshold_hours": 24,
  "pass2_batch_size": 50
}
```

**Key types:**
- `DeferredEnrichmentRequest` (`MediaEngine.Domain.Models`) — Pass 2 queue item with entity ID, QID, and creation timestamp
- `IDeferredEnrichmentService` (`MediaEngine.Domain.Contracts`) — enqueue + process contract
- `DeferredEnrichmentService` (`MediaEngine.Providers.Services`) — priority queue reader + nightly sweep BackgroundService
- `HydrationPass` (`MediaEngine.Domain.Enums`) — `Quick = 1, Universe = 2`

**Why this matters to the business:**
- **Performance** — Files appear on the Dashboard in seconds, not minutes. The heavy universe work happens when the system is idle.
- **Reliability** — The nightly sweep ensures nothing is permanently starved during sustained imports. The user override provides immediate results when needed.
- **Extensibility** — The pass boundary is configurable. Properties can be moved between passes via `config/hydration.json`.
- **Maintenance** — All scheduling parameters (idle delay, rate limit, cron, batch size) are JSON-configurable. Zero code changes to tune behaviour.

### 3.25 — Supported Library Types

| Library Type | Includes | Notes |
|---|---|---|
| **Books** | Ebooks (EPUB, PDF) + Audiobooks (M4B, MP3) | Unified library; format subfolder distinguishes reading and listening formats |
| **TV** | Episodic television, web series | Season/episode folder structure |
| **Movies** | Feature films, short films | Single-work structure |
| **Music** | Albums, singles, tracks | Album = Hub, Track = Work |
| **Comics** | CBZ, CBR, PDF comics, manga | Sequential art |
| **Podcasts** | Podcast series and episodes | Series = Hub, Episode = Work |

**Future Library Types:**

**Other** — YouTube videos, lectures, personal recordings, and any media that doesn't fit the six primary library types. This category would require manual tagging since metadata inference is limited for unstructured content. The Engine would store files and allow user-provided metadata, but automated enrichment would be minimal. Planned for a future release.

**Photos** — Photo collections, albums, and galleries. This domain has unique challenges that set it apart from the other media types: EXIF/XMP metadata extraction, GPS geolocation, face detection and grouping, event-based organisation, and timeline views. The scope is large enough that it could be its own product built on the same base Engine — reusing the Hub concept, Weighted Voter, and filesystem-first architecture, but with a specialised processing pipeline and Dashboard experience. Planned for a future release pending further design exploration.

### 3.26 — Chronicle Engine (Temporal Knowledge Graph)

**Plain English:** The Chronicle Engine extends the Universe Graph (§3.22) with time-awareness. Relationships now carry temporal qualifiers — *when* a character was married, *when* an actor played a role, *when* a faction existed. The Engine detects when Wikidata learns something new about your library's universe (Lore Delta), resolves era-correct actors for characters across different adaptations, and serves a spoiler-safe timeline that can be scrubbed to any year. A Chronicle Explorer page in the Dashboard renders the full relationship graph using Cytoscape.js.

**Temporal qualifiers on relationships:**

`EntityRelationship` carries `StartTime` and `EndTime` (nullable ISO 8601 strings) sourced from Wikidata P580/P582 temporal qualifiers. These are stored in the `entity_relationships` table (migration M-037) and surfaced in the graph API, universe.xml v1.1 sidecars, and the Chronicle Explorer timeline slider.

**2-hop lineage depth:**

`RelationshipPopulationService` now accepts `currentDepth` and `maxDepth` parameters. When a stub entity is created at depth N < maxDepth, it is enqueued for enrichment via `IMetadataHarvestingService`, discovering its own relationships (e.g., grandparents, sub-organizations). Default depth increased from 1 to 2 via `HydrationSettings.LineageDepth`.

**Social web relationships:**

Two new relationship types: `significant_person` (P3342 — ally, rival, mentor) and `affiliation` (P1416 — group membership). Four new Wikidata properties: P3342, P1416, P103 (native_language), P1281 (avatar_image).

**Batch Data Extension queries:**

`WikidataPropertyMap.BuildBatchEntityRequest` generates Data Extension API requests for fetching properties of up to 50 entities per request. `ReconciliationAdapter.FetchEntitiesBatchAsync` provides the implementation.

**Reconciliation response caching:**

`ReconciliationAdapter` caches API responses using the existing `IProviderResponseCacheRepository`. SHA-256 hash of the request serves as the cache key. Supports ETag-based revalidation (If-None-Match → 304 Not Modified). Default TTL: 168 hours (7 days).

**Lore Delta change detection:**

`ILoreDeltaService.CheckForUpdatesAsync` batch-fetches current Wikidata revision IDs via `wbgetentities?props=info` and compares against `FictionalEntity.WikidataRevisionId`. Changed entities are reported as `LoreDeltaResult` records. API endpoint: `GET /universe/{qid}/lore-delta`.

**Canon discrepancy detection:**

`ICanonDiscrepancyService.DetectAsync` compares an edition's canonical values against its master work (P629 edition_or_translation_of) for 6 core fields (title, author, year, genre, series, series_position). API endpoint: `GET /metadata/{entityId}/canon-discrepancies`.

**Era-correct actor resolution:**

`IEraActorResolverService.ResolveActorForEraAsync` queries performer edges for a character, filters by temporal range against a timeline year, and returns the matching actor's headshot URL. Falls back to most recent performer when no temporal match exists.

**Timeline-filtered graph API:**

`GET /universe/{qid}/graph?timeline_year={year}` filters edges to exclude relationships starting after the given year and populates character node images with era-correct actor headshots.

**Chronicle Explorer Dashboard page:**

Route: `/universe/{Qid}/explore`. Features:
- Universe header with entity/edge count chips
- Lore Delta amber alert banner when Wikidata changes are detected
- Timeline slider (when edges have temporal data)
- Type filter toggle chips (Character/Location/Organization)
- Layout selector (force-directed/concentric/grid)
- Cytoscape.js graph panel (60%) with node click → detail drawer
- Searchable entity list panel (40%)
- "Explore Universe" button on HubDetail page sidebar (when fictional universe QID is available)

**Device constraints:** Chronicle Explorer is disabled on mobile. Timeline slider is disabled on television.

**Cytoscape.js** (MIT license) is vendored at `wwwroot/lib/cytoscape/cytoscape.min.js`. JS interop module at `wwwroot/js/cytoscape-interop.js` exposes `initGraph`, `updateGraph`, `filterByTimelineYear`, `focusNode`, `setLayout`, `destroy`.

**SignalR event:** `LoreDeltaDiscoveredEvent(UniverseQid, ChangedCount)` — broadcast when Lore Delta check discovers updated entities.

**Configuration** (`config/hydration.json` additions):
- `fetch_temporal_qualifiers` (default: true) — enable qualified statement fetching via Data Extension API
- `batch_query_size` (default: 50) — max entities per batch Data Extension request
- `lineage_depth` (default: 2) — maximum depth for relationship traversal
- `lore_delta_check_on_explorer_open` (default: true) — auto-check on page load
- `canon_discrepancy_detection` (default: true) — enable canon checking
- `era_actor_resolution` (default: true) — enable temporal actor resolution

**Key types:**
- `LoreDeltaResult`, `CanonDiscrepancy`, `ActorResolution` (`MediaEngine.Domain.Models`) — result records
- `ILoreDeltaService`, `ICanonDiscrepancyService`, `IEraActorResolverService` (`MediaEngine.Domain.Contracts`) — service contracts
- `LoreDeltaService`, `CanonDiscrepancyService`, `EraActorResolverService` (`MediaEngine.Providers.Services`) — implementations
- `CanonEndpoints` (`MediaEngine.Api.Endpoints`) — canon discrepancy API
- `ChronicleExplorerDtos` (`MediaEngine.Web.Models.ViewDTOs`) — DTOs for graph, nodes, edges, lore delta
- `ChronicleExplorer.razor` (`MediaEngine.Web.Components.Pages`) — Chronicle Explorer page

**Why this matters to the business:**
- **Extensibility** — Temporal data enables future features: spoiler gates, era-filtered cast panels, temporal search.
- **Reliability** — Lore Delta detects stale data automatically. Canon discrepancy prevents edition/master work confusion.
- **Performance** — Reconciliation response caching and batch queries reduce API calls by 3-5x. 2-hop depth is configurable.
- **Maintenance** — All thresholds and feature flags live in `config/hydration.json`. Zero code changes to tune.

### 3.27 — Description Signal Extraction Pipeline

**Plain English:** When a file's metadata doesn't include the narrator, translator, or illustrator — or when Wikidata resolves to the work level instead of a specific edition — the Engine mines retail provider descriptions for person names. "Read by Scott Brick" in an Apple API description is extracted via regex, verified against Wikidata (is this a real person who works as a narrator?), and linked to the work with a headshot and biography.

**Two purposes:**

1. **Candidate ranking improvement** — during Stage 1 retail search, person names are extracted from each candidate's description and compared name-to-name against file metadata hints. If the file says `narrator: "Scott Brick"` but a candidate's description says "Read by Stephen Fry", that mismatch penalises the candidate. A matching candidate gets boosted. This is more precise than fuzzy-matching the name against the full description paragraph.

2. **Person record creation** — after Stage 1 selects a winning candidate, the Engine extracts all person names from the description, validates them (min 2 words, uppercase start, not in stop list), and records them as pending signals. A background worker batch-verifies all pending signals against Wikidata: searches for each unique name, fetches P31 (is human?) + P106 (occupation), and confirms the person works in the right field for the extracted role.

**Config-driven extraction rules** (`config/signal_extraction.json`):

Each media type has extraction rules with regex patterns, role assignments, and Wikidata occupation classes for verification:

| Media Type | Extracted Roles | Example Patterns |
|-----------|----------------|-----------------|
| Audiobooks | Narrator | "Read by", "Narrated by", "Performed by" |
| Books | Translator, Editor, Illustrator, Author (foreword) | "Translated by", "Edited by", "Illustrated by", "Foreword by" |
| Movies | Director, Cast Member, Producer | "Directed by", "Starring", "Produced by" |
| TV | Director, Cast Member | "Directed by", "Starring" |
| Comics | Author, Illustrator | "Written by", "Art by", "Pencils by" |
| Podcasts | Host | "Hosted by", "Presented by" |
| Music | Producer, Featured Artist | "Produced by", "feat." |

**Role-based occupation verification:**

Each extraction rule carries Wikidata occupation class Q-identifiers. After finding a person on Wikidata, the Engine checks P106 (occupation) for overlap:

| Role | Wikidata Occupation Classes |
|------|---------------------------|
| Narrator | Q1622272 (narrator), Q33999 (actor), Q2405480 (voice actor) |
| Translator | Q333634 (translator), Q14467526 (literary translator) |
| Director | Q2526255 (film director), Q3455803 (television director) |
| Illustrator | Q644687 (illustrator), Q1028181 (painter) |

**Confidence tiers:**

| Verification result | Confidence |
|--------------------|-----------|
| Extracted from description, unverified | 0.60 |
| Extracted from file metadata, unverified | 0.75 |
| QID found + occupation matches role | 0.85 |
| QID found + human but no matching occupation | 0.65 |
| QID found but not human, or no match | Discarded |

**Batch processing architecture:**

Inline extraction runs during hydration with zero API calls — pure regex + validation. All Wikidata verification is deferred to a background worker (`PersonSignalVerificationWorker`) that polls every 5 minutes, deduplicates names across entities, and batch-verifies in a single `wbgetentities` call. For 500 audiobooks sharing 30 unique narrators: 30 search calls + 1 batch properties call.

**Enhanced candidate ranking** (`extract_then_compare` match type):

`DescriptionMatchService` gains a new match type that extracts person names from candidate descriptions via regex before comparing name-to-name. This replaces the previous `partial_ratio` fuzzy match for person-role fields (narrator, translator, director, cast, writer, host) in `config/description_matching.json`.

**Database migration M-057:**
- `pending_person_signals` table (id, entity_id, name, role, source, pattern, media_type, created_at)
- `persons` table role CHECK expanded: added `Translator`, `Editor`, `Host`, `Producer`

**Key types:**
- `IDescriptionSignalExtractor` (`MediaEngine.Domain.Contracts`) — inline extraction contract
- `IPersonSignalVerificationService` (`MediaEngine.Domain.Contracts`) — batch verification contract
- `IPendingPersonSignalRepository` (`MediaEngine.Domain.Contracts`) — pending signals CRUD
- `ExtractedPersonSignal`, `PendingPersonSignal` (`MediaEngine.Domain.Models`) — domain models
- `SignalExtractionSettings` (`MediaEngine.Storage.Models`) — typed config model
- `DescriptionSignalExtractor` (`MediaEngine.Providers.Services`) — inline regex extraction
- `PersonSignalVerificationService` (`MediaEngine.Providers.Services`) — batch Wikidata verification
- `PersonSignalVerificationWorker` (`MediaEngine.Api.Services`) — background polling service

**Why this matters to the business:**
- **Reliability** — Recovers edition-level people lost when Wikidata resolves to the work level. Role-based occupation verification sidesteps the edition-matching problem entirely.
- **Extensibility** — All patterns live in `config/signal_extraction.json`. Adding new phrases or roles is a config edit, zero code changes.
- **Performance** — Inline extraction is pure regex (no API calls). Batch verification deduplicates names and uses a single `wbgetentities` call. Provider response cache prevents re-verification of known names.
- **Privacy** — Only extracted names are sent to Wikidata for verification. No new external services.
- **Maintenance** — Zero new NuGet dependencies. All thresholds, patterns, and occupation classes are config-driven.

### 3.28 — Local AI Intelligence Layer (The Library Vault)

**Plain English:** The Library has an AI brain that runs entirely on your machine. It downloads two types of models on first run: text models (for understanding filenames, resolving Wikidata matches, generating mood tags, and powering natural language search) and an audio model (for speech-to-text, language detection, and audio-text synchronization). All inference is local — no cloud, no subscription, no data leaving the NAS.

**AI is a core function of Tuvima Library, not an add-on.** It replaces the brittle regex and heuristic code that previously handled filename cleaning, media type disambiguation, and metadata scoring. The Engine requires AI models to be present and will not begin ingestion until they are downloaded.

**New project: `MediaEngine.AI`** — sits alongside Providers in the dependency chain. References Domain (for contracts) and Storage (for configuration). All AI implementations live here.

**NuGet dependencies:**
- `LLamaSharp` + `LLamaSharp.Backend.Cpu` (MIT) — .NET native llama.cpp binding for text inference with GBNF grammar constraints
- `Whisper.net` + `Whisper.net.Runtime` (MIT) — .NET native whisper.cpp binding for audio transcription

**Model roles (configured in `config/ai.json`):**

| Role | Default Model | RAM Loaded | Purpose |
|------|--------------|-----------|---------|
| `text_fast` | Llama 3.2 1B Q4_K_M | ~750MB | On-demand tasks: search parsing, TL;DR, recommendation explanations |
| `text_quality` | Llama 3.2 3B Q4_K_M | ~2GB | Batch tasks: ingestion manifest, vibe tags, QID disambiguation |
| `audio` | Whisper Medium | ~1.5GB | Audio tasks: transcription, language detection, sync maps |

Only one model is loaded at a time (mutual exclusion via `SemaphoreSlim`). Auto-unloads after configurable idle timeout. Models download automatically on first run to `/models` Docker volume.

**Structured output reliability:** All LLM calls use GBNF grammar constraints — llama.cpp forces the model to produce valid JSON at the token level. This is model-agnostic (works with Llama, Mistral, Phi, Gemma, Qwen). JSON schema validation + retry logic as safety net.

**16 AI features across 7 categories:**

| Category | Features | Model | Trigger |
|----------|----------|-------|---------|
| **Ingestion** | Smart Labeling, Media Type Classification, Batch Manifest | text_quality | Automatic |
| **Alignment** | QID Disambiguation, Series Alignment, Watching Order | text_quality / text_fast | Automatic / On-demand |
| **Enrichment** | Vibe Tags (per-category vocabulary), TL;DR, Cover Art Validation, Audio Similarity (Chromaprint) | text_quality / text_fast | Background / On-demand |
| **Syncing** | Immersive Bake, Subtitle Sync, Cross-Media Scene Mapping | audio + text_quality | Scheduled / On-demand |
| **Personalization** | Local Taste Profiling, "Why" Factor | text_quality / text_fast | Background / On-demand |
| **Discovery** | Intent Search (NL → structured filters) | text_fast | User input |
| **Advanced** | User-Assisted URL Paste | text_quality | Manual |

**Code replaced by AI:**
- `TitleNormalizer.cs` — deleted, replaced by SmartLabeler
- AudioProcessor/VideoProcessor disambiguation heuristics — deleted, replaced by MediaTypeAdvisor
- `IngestionHintCache` — superseded by BatchManifestBuilder
- Priority Cascade Engine Tiers B-D — replaced by LLM per-field confidence scores

**Configuration:** `config/ai.json` — model definitions by role, per-feature enable flags, per-category vibe vocabularies, cron schedules for background services.

**API endpoints:** `/ai/status`, `/ai/models`, `/ai/models/{role}/download|load|unload`, `/ai/config`, `/ai/enrich/tldr/{entityId}`, `/ai/enrich/vibes/{entityId}`, `/ai/enrich/search/intent`, `/ai/enrich/extract-url`.

**Background services:** VibeBatchService (4 AM daily), SeriesAlignmentBackgroundService (3 AM daily), TasteProfileBackgroundService (Sunday 5 AM), ModelAutoDownloadService (startup).

**Database migrations:** M-058 (`user_taste_profiles`), M-059 (`audio_fingerprints`).

**Docker:** `/models` volume mount for AI model storage. `TUVIMA_MODELS_DIR` environment variable.

**Key types:**
- `LlamaInferenceService` (`MediaEngine.AI.Llama`) — core LLM wrapper with GBNF grammar, timeout, JSON parsing
- `WhisperInferenceService` (`MediaEngine.AI.Whisper`) — Whisper.net wrapper for transcription and language detection
- `AudioPreprocessor` (`MediaEngine.AI.Whisper`) — FFmpeg 16kHz mono WAV conversion
- `ModelDownloadManager` (`MediaEngine.AI.Infrastructure`) — HTTP download with SHA-256 validation and SignalR progress
- `ModelLifecycleManager` (`MediaEngine.AI.Infrastructure`) — mutual exclusion, idle auto-unload, memory profiling
- `SmartLabeler`, `MediaTypeAdvisor`, `BatchManifestBuilder`, `QidDisambiguator`, `SeriesAligner`, `VibeTagger`, `TldrGenerator`, `IntentSearchParser`, `UrlMetadataExtractor` (`MediaEngine.AI.Features`) — feature implementations
- `AiSettings` (`MediaEngine.AI.Configuration`) — typed config model for `config/ai.json`

**Why this matters to the business:**
- **Reliability** — AI replaces brittle heuristic code, dramatically reducing review queue items and misclassifications.
- **Performance** — Batch manifest analysis reduces retail API calls by 80-95% for bulk imports. Models load on demand and unload when idle.
- **Extensibility** — Model swapping is a config change (any GGUF model works). New features use the same LlamaInferenceService + GBNF pattern.
- **Privacy** — All inference runs locally. Models and data never leave the NAS.
- **Maintenance** — All AI config lives in `config/ai.json`. Feature flags, vibe vocabularies, cron schedules — zero code changes to tune.

---

## 4. Product Owner Communication Rules

> Claude must apply these rules in every single message to the Product Owner. There are no exceptions.

### 4.1 — Mandatory vocabulary

Always use the plain-English term. Never use the technical term in conversation.

| ❌ Never say | ✅ Always say |
|---|---|
| Backend / Frontend | Engine / Dashboard |
| API / Endpoint | Engine connection point / Action |
| Database / Schema | Data store / Data structure |
| Deploy / Ship | Publish / Release |
| Refactor | Reorganise / Clean up |
| Repository / Commit | Code history / Save point |
| Dependency / Package | Tool / Library |
| DI container / Service registration | App's ingredient list |
| Null reference / Exception | Missing value / Unexpected error |
| Compile / Build | Assemble / Verify |
| Pull request / Branch | Proposed change / Parallel work stream |
| csproj / props file | Project configuration file |
| namespace / class | Code module / Blueprint |

### 4.2 — Always explain the "Why" in business terms

Every technical choice must be justified using one or more of these business goals:

| Goal | Meaning |
|---|---|
| **Maintenance** | Makes the product easier and cheaper to change in the future |
| **Extensibility** | Allows new features or external tools to be added without breaking existing ones |
| **Privacy** | Keeps user data on the user's machine; nothing leaves without explicit action |
| **Reliability** | Reduces the chance of errors or data loss |
| **Performance** | Makes the product faster or more responsive for the user |

*Example:* Instead of "We're separating the Engine and Dashboard for loose coupling," say "We're separating the Engine and the Dashboard so that future tools like a mobile app can connect to the Engine directly — **Extensibility**."

### 4.3 — Plan before coding

Before writing any code, Claude must present a plain-English plan using this exact format:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 PLAN: [Feature name]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 What I'm going to build:
   [1–3 plain-English sentences]

 Why this serves your goals:
   [Cite Maintenance / Extensibility / Privacy / Reliability / Performance]

 What I'll create:
   [plain list of new items]

 What I'll change:
   [plain list of modified items]

 New tools needed:
   [name + license] or "None"

 Trade-offs to know about:
   [any limitations or risks] or "None"
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Do not proceed until the Product Owner says "go ahead" or equivalent.**

### 4.4 — Error reporting

When the build fails or a quality check breaks, Claude must:
1. Say **what went wrong** in plain English (no error codes as the primary explanation).
2. Say **what will be done** to fix it.
3. Fix it immediately — errors during an approved task do not need a second sign-off.

### 4.5 — Honest about uncertainty

Never guess silently. If Claude is unsure about an approach, it must say so:
> *"I'm not certain which approach is best here — I see two options. Here's the trade-off: [explain]. Which matters more to you?"*

### 4.6 — Model Adherence & Delegation

This project uses a two-tier model strategy to balance quality with speed:

**Opus** handles:
- Planning, architectural decisions, and task decomposition
- Code review and quality assurance
- Resolving ambiguity or scope questions escalated by Sonnet agents
- Writing and updating project documentation (`CLAUDE.md`, `MEMORY.md`, `.agent/` files)

**Sonnet** handles:
- Implementation and coding tasks delegated by Opus
- File modifications with clear, scoped instructions
- Build verification (`dotnet build`) after each unit of work

**Delegation rules:**
1. When a task involves both planning and coding, Opus must break down the work first into self-contained units, then dispatch coding subtasks to Sonnet agents with clear, scoped instructions — including all necessary context (file paths, exact changes, integration points, verification steps).
2. Sonnet agents must not make architectural decisions autonomously. If the scope is ambiguous, requirements are unclear, or an unexpected build failure suggests a design problem, the agent must escalate back to Opus rather than improvising a fix.
3. Independent units should be dispatched to Sonnet agents in parallel whenever possible. Dependent units must wait for their prerequisites to complete.
4. Each Sonnet agent receives a complete handoff spec: files to modify, exact entries to add, patterns to follow, and a verification command. The agent should not need to explore the codebase to understand its task.
5. If a Sonnet agent encounters a build failure it cannot resolve within its scoped instructions, it escalates to Opus with the full error context rather than attempting architectural changes.

---

## 5. Compliance & Workflow

### 5.1 — License: AGPLv3

> **⚠️ This project is licensed under the GNU Affero General Public License v3.0 (AGPLv3). This cannot be changed without the Product Owner's explicit decision.**

**What this means in practice:**
- AGPLv3 is a "share-alike" license. If Tuvima Library is ever run as a network service (even privately), any modifications must also be released under AGPLv3.
- **Every new tool added to the project must have a compatible license.** Claude must check the license before adding anything.

**Compatibility guide:**

| License | Compatible? | Notes |
|---|---|---|
| MIT | ✅ Safe | Most common open-source license |
| Apache 2.0 | ✅ Safe | Used by many Microsoft packages |
| BSD 2/3-clause | ✅ Safe | Permissive |
| LGPL v2.1 / v3 | ✅ Safe | Common for libraries |
| GPL v3 / AGPL v3 | ✅ Safe | Same family |
| GPL v2 (no "or later") | ⚠️ Check | May be incompatible — ask before adding |
| SSPL | ❌ Block | Not OSI-approved; incompatible |
| Commons Clause | ❌ Block | Restricts commercial use |
| Proprietary / commercial | ❌ Block | Incompatible by definition |

**If Claude is unsure about a tool's license**, it must stop, say so, and ask the Product Owner before proceeding.

**Current approved tools and their licenses:**

| Tool | License | Added in |
|---|---|---|
| Microsoft.Data.Sqlite | MIT | Phase 4 (Storage) |
| Microsoft.Extensions.* | MIT | Phase 4 |
| Microsoft.AspNetCore.* | MIT | Phase 4 |
| Microsoft.AspNetCore.SignalR.Client | MIT | Deliverable 2 (UI) |
| MudBlazor 9 | MIT | Deliverable 1 (UI) |
| VersOne.Epub | MIT | Phase 5 (Processors) |
| Swashbuckle.AspNetCore | MIT | Phase 4 (API) |
| xUnit 2 | Apache 2.0 | Phase 4 (Tests) |
| coverlet | MIT | Phase 4 (Tests) |
| Microsoft.Extensions.Http | MIT | Phase 9 (Providers) — `IHttpClientFactory` named HTTP clients |
| TagLibSharp | LGPL-2.1 | Sprint 6 (Ingestion) — Audio + video metadata tag writing (ID3v2, MP4 atoms, Vorbis, MKV) |
| SkiaSharp | MIT | Hero Banner Pipeline — Cross-platform image processing for cinematic hero banner generation (blur, vignette, grain) |
| SkiaSharp.NativeAssets.Linux | MIT | Hero Banner Pipeline — Native SkiaSharp binaries for Linux deployment |
| Cytoscape.js | MIT | Chronicle Engine — Graph visualization for the Chronicle Explorer Dashboard (vendored, no NuGet) |
| dotNetRDF | MIT | Stage 4c (Universe Graph) — Local in-memory SPARQL graph querying over SQLite data |
| FuzzySharp | MIT | Fuzzy string matching — candidate re-ranking, title verification, narrator disambiguation, sequel-safe composite scoring |
| Tuvima.WikidataReconciliation | MIT | Unified Wikidata/Wikipedia API client — reconciliation, entity fetching, property extraction, Wikipedia summaries, image URLs. Replaces WikibaseApiService and WikipediaAdapter. |
| Tuvima.WikidataReconciliation.AspNetCore | MIT | DI registration extension for WikidataReconciler |
| Serilog.AspNetCore | Apache 2.0 | Structured logging with rolling file output for headless Engine operation |
| Serilog.Sinks.File | Apache 2.0 | Rolling file sink for Serilog — auto-deletes after configurable retention |
| Microsoft.Extensions.Http.Resilience | MIT | Standard retry, circuit-breaker, and timeout policies for all external HTTP calls (Polly-based) |
| Cronos | MIT | Lightweight cron expression parser for configurable background task scheduling |
| Dapper | Apache 2.0 | Micro-ORM for type-safe SQLite data access with named column mapping |
| LLamaSharp | MIT | Local LLM inference — .NET native llama.cpp binding with GBNF grammar constraints for structured JSON output |
| LLamaSharp.Backend.Cpu | MIT | CPU backend for LLamaSharp — cross-platform inference. Future: LLamaSharp.Backend.Vulkan for GPU |
| Whisper.net | MIT | Local audio inference — .NET native whisper.cpp binding for speech-to-text and language detection |
| Whisper.net.Runtime | MIT | CPU runtime for Whisper.net — cross-platform. Future: Whisper.net.Runtime.Vulkan for GPU |

### 5.2 — Mandatory Workflow

Claude must follow every step of this workflow for every piece of work, without exception.

---

**Step 1 — Read before touching anything**

Read `CLAUDE.md` (this file), `README.md`, and every file relevant to the task before proposing changes. Never assume the current state of the code — always verify.

---

**Step 2 — Present the plan and wait for sign-off**

Use the plan format in Section 4.3. Do not write a single line of code until the Product Owner approves.

**Always ask detailed questions before finalising the plan.** Do not assume intent — probe for specifics: which media types are affected, what the user expects to see on-screen, edge cases, whether existing behaviour should change, and how the feature interacts with other parts of the system. Multiple rounds of clarification are expected and encouraged. A plan built on assumptions will be rejected; a plan built on answers will be approved.

---

**Step 3 — Assemble and verify**

After writing code, run:
```bash
dotnet build
```
The result must be **0 errors, 0 warnings** before moving to the next step.
Warnings are not acceptable — they indicate future problems and must be fixed immediately.

**Process cleanup:** After all parallel agents in a batch have completed, kill stale `dotnet.exe` processes to prevent accumulation:
```bash
taskkill //F //IM dotnet.exe
```
This ensures multiple agent runs do not leave orphaned build/server processes consuming resources. Do not kill between individual agents within a parallel batch — only after the entire batch finishes.

---

**Step 4 — Update the project memory**

After every completed feature, update documentation:

| Document | Update when… |
|---|---|
| `README.md` (repo root) | The feature changes how the product is installed, configured, or used |
| `CLAUDE.md` §3 (Tool inventory) | A new tool or library was added |
| `CLAUDE.md` §5.1 (License table) | A new dependency was approved |
| `MEMORY.md` (`.claude/projects/.../memory/`) | A new architectural decision was made that future sessions need to know |

---

**Step 5 — Commit to GitHub automatically**

After documentation is updated:

```bash
# Stage only specific, relevant files — never use git add -A or git add .
git add <file1> <file2> ...

# Write a clear, useful commit message
git commit -m "Short imperative summary (≤72 chars)

Optional body: explain the WHY, not the WHAT.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"

git push
```

**Files that must never be committed:**

| File / Pattern | Reason |
|---|---|
| `tuvima_master.json` | Contains local paths and secrets — gitignored |
| `*.db` | Local database file — user data, not code |
| `bin/`, `obj/` | Assembled output — regenerated automatically |
| `.vs/`, `.idea/` | Editor preferences — personal, not shared |
| `appsettings.*.json` containing real keys | Contains secrets — only example files go in git |

### 5.3 — Cross-Agent Synchronization

> **Two AI assistants work on this repository.** This file (`CLAUDE.md`) is the **canonical source of truth** for all project knowledge. The `.agent/` directory contains supplementary files read by **Antigravity (Gemini)**. Both must stay in sync.

**Sync mapping — CLAUDE.md → `.agent/` files:**

| CLAUDE.md section | `.agent/` file | Content |
|---|---|---|
| §3.1 (Watch Folder) | `features/INGESTION-PIPELINE.md` | Ingestion flow, settle/lock/fingerprint/scan/identify/move |
| §3.2 (Weighted Voter) | `features/METADATA-MANAGEMENT.md`, `skills/METADATA-SCORING.md` | Claim system, trust weights, per-field voting |
| §3.3 (Security) | `features/API-SECURITY.md`, `features/ROLE-ACCESS-MODEL.md`, `skills/API-SECURITY.md` | Auth, roles, rate limiting, path traversal |
| §3.4 (Dashboard UI) | `features/LIBRARY-DASHBOARD.md`, `skills/DASHBOARD-UI.md` | Poster swimlanes, cinematic hero, theming, state management |
| §3.6 (Metadata Adapters) | `features/METADATA-MANAGEMENT.md`, `features/METADATA-PRIORITY.md` | Provider config, harvest pipeline, person enrichment |
| §3.8 (Activity Ledger) | `features/SETTINGS-OVERVIEW.md` | Maintenance tab, activity timeline |
| §3.12 (Device Profiles) | `features/SETTINGS-OVERVIEW.md` | UI config cascade, device detection |
| §3.13 (Hydration Pipeline) | `features/INGESTION-PIPELINE.md` | Three-stage pipeline, review queue |
| §3.14 (Media Type Disambiguation) | `features/INGESTION-PIPELINE.md` | Heuristic disambiguation, confidence thresholds, review queue |
| §3.21 (Cross-Media Strategy) | `features/METADATA-MANAGEMENT.md` | Provider slots, response caching, artwork strategy, rate limits |
| §3.24 (Two-Pass Enrichment) | `features/INGESTION-PIPELINE.md`, `features/METADATA-MANAGEMENT.md` | Quick match vs universe lookup, priority queue, nightly sweep |
| §3.26 (Chronicle Engine) | `features/METADATA-MANAGEMENT.md` | Temporal qualifiers, Lore Delta, era actors, Chronicle Explorer |
| §3.25 (Supported Library Types) | `features/INGESTION-PIPELINE.md` | Library types, future types (Other, Photos) |
| §6 (Dashboard Layout) | `skills/DASHBOARD-UI.md` | Feature-sliced file locations |
| FIX-PLAN tiers | `FIX-PLAN.md` | Systematic fix plan, tier-based issue tracking |

**When to sync:**
- After updating any section of `CLAUDE.md` listed above, update the corresponding `.agent/` file(s) to reflect the same information.
- After creating a new feature or skill in `.agent/`, ensure `CLAUDE.md` has a matching section.
- The `.agent/SYNC-MAP.md` file contains the reverse mapping for Antigravity's reference.

**Step 4 addition:** Add `.agent/` files to the documentation update table when architectural decisions affect feature or skill docs.

---

## 6. Structure Reference — Feature-Sliced Dashboard Layout

All Dashboard code in `src/MediaEngine.Web/` follows the **Feature-Sliced** pattern. Every new piece of UI code must go into the correct slice. Claude must respect this layout and never mix responsibilities between slices.

```
src/MediaEngine.Web/
│
├── Services/
│   ├── Integration/          ← ALL communication with the Engine lives here
│   │   ├── ILibraryApiClient.cs         Contract for HTTP calls to the Engine
│   │   ├── LibraryApiClient.cs          Implementation: makes HTTP calls, maps raw JSON
│   │   ├── UniverseStateContainer.cs   Per-session cache: hubs, universe view, progress
│   │   ├── UIOrchestratorService.cs    Orchestrator: bridges HTTP + SignalR + state cache
│   │   ├── UniverseMapper.cs           Maps Engine data → flat Dashboard view model
│   │   └── IntercomEvents.cs           SignalR event shapes (MediaAdded, IngestionProgress, MetadataHarvested, PersonEnriched)
│   │
│   ├── Playback/             ← (TARGET STATE) Playback session management
│   │   └── PlaybackStateService.cs     Active session management, progress sync, audio persistence
│   │
│   └── Theming/              ← ALL visual configuration lives here
│       ├── ThemeService.cs             Dark-mode-only theme, colour palette, corner radii
│       └── DeviceContextService.cs     Per-circuit device class + resolved UI settings
│
├── Components/
│   ├── Universe/             ← Hub-related visual components
│   │   ├── HubHero.razor               Cinematic hero: blurred cover art backdrop + vignette + metadata badges
│   │   ├── MetadataChips.razor          Renders multi-valued fields (genres, tags) as MudChip elements with optional QID styling
│   │   ├── PosterCard.razor            Poster art tile: cover image, title, metadata badges
│   │   ├── PosterSwimlane.razor        Horizontal scrolling row of PosterCards with hidden scrollbar
│   │   └── ProgressIndicator.razor     Reusable progress card (icon + bar + label)
│   │
│   ├── Bento/                ← Legacy grid wrappers (primary layout is Poster Swimlanes in Universe/)
│   │   ├── BentoGrid.razor             Legacy CSS grid container
│   │   └── BentoItem.razor             Legacy glassmorphic tile wrapper
│   │
│   ├── Navigation/           ← Navigation and search components
│   │   ├── CommandPalette.razor        Ctrl+K global search and navigation
│   │   ├── TopBar.razor                Horizontal top bar: logo, search, bell, profile (4 variants)
│   │   ├── LeftDock.razor              Icon-only left dock: 52px narrow / 200px hover-expand
│   │   ├── MobileNavDrawer.razor       Slide-out navigation drawer for mobile
│   │   └── AppLogo.razor               Inline SVG wordmark logo
│   │
│   ├── Settings/             ← Settings page tab components (3 groups: Preferences, Metadata, Server)
│   │   ├── SettingsSidebar.razor        Sidebar navigation with search, badges, collapsible sections (defines SettingsSection enum)
│   │   ├── GeneralTab.razor             [Preferences] Profile: display name, avatar colour swatches
│   │   ├── NavigationTab.razor          [Preferences] Navigation config: Action Cluster toggles + Tray Libraries
│   │   ├── ConnectionVaultTab.razor     [Metadata] Unified vault: Wikidata pinned, media-type field priorities, provider connections, pipeline config
│   │   ├── UniverseSettingsTab.razor    [Metadata] Wikidata universe provider: bridge identifiers + property map (read-only, orphaned)
│   │   ├── NeedsReviewTab.razor         [Metadata] Review queue: trigger chips, confidence gauges, resolve/dismiss
│   │   ├── ActivityTab.razor            [Metadata] Date-grouped activity timeline, configurable retention, prune
│   │   ├── LibrariesTab.razor           [Server] Watch Folder + Library Folder configuration
│   │   ├── ConnectivityTab.razor        [Server] Engine connectivity status
│   │   ├── ApiKeysTab.razor             [Server] Guest API Keys: generate, revoke, copy-to-clipboard
│   │   ├── ConflictsTab.razor           [Server] Unresolved metadata conflicts
│   │   ├── UsersTab.razor               [Server] User profile management
│   │   ├── MaintenanceTab.razor         [Server] Activity ledger, retention, prune
│   │   ├── ProviderEditPanel.razor      Reusable provider editing: endpoint test, property picker, trust sliders
│   │   └── ProvidersSettingsTab.razor   Legacy (redirects to ConnectionVault)
│   │
│   ├── Playback/             ← (TARGET STATE) In-browser media players
│   │   ├── EpubReader.razor             Paginated EPUB reader with chapter nav, font controls
│   │   ├── ComicViewer.razor            Full-page comic viewer with page-turn, zoom, RTL toggle
│   │   ├── AudioPlayer.razor            Persistent bottom-bar player for audiobooks + music
│   │   └── VideoPlayer.razor            Full-screen video player with HLS, subtitles, chapters
│   │
│   └── Pages/                ← Full-page views (routed)
│       ├── Home.razor                  Library overview: Cinematic Hero + Poster Swimlanes (Continue Journey, Recently Added, media-type groups)
│       ├── MediaLanePage.razor         Content-type lane pages: /books, /video, /music, /podcasts, /comics with hero, format toggle, swimlanes
│       ├── HubDetail.razor             (TARGET STATE) Hub detail: artwork, works, persons, social pivot
│       ├── WorkDetail.razor            (TARGET STATE) Work detail: editions, metadata, play button
│       ├── PersonDetail.razor          (TARGET STATE) Person detail: headshot, bio, social links, works
│       ├── Statistics.razor            (TARGET STATE) Library + personal stats, charts
│       ├── Login.razor                 (TARGET STATE) Profile selection + PIN/password login
│       ├── Settings.razor              Unified settings: sidebar + content, 16 tabs in 3 groups (Preferences/Metadata/Server)
│       ├── ChronicleExplorer.razor      Chronicle Engine: universe graph explorer with Cytoscape.js, timeline slider, Lore Delta banner
│       └── NotFound.razor              404 page
│
├── Models/
│   └── ViewDTOs/             ← Data shapes used ONLY by the Dashboard (never shared with Engine)
│       ├── HubViewModel.cs             Hub for display: DisplayName, WorkCount, MediaTypes, CoverUrl, HeroUrl, Year, PrimaryMediaType
│       ├── LaneDefinition.cs           Content lane record: Key, Label, Icon, MediaTypes[], SubItems[]
│       ├── ContentLanes.cs             Static registry of 7 fixed content-type lanes (Home, Search, Books, Video, Music, Podcasts, Comics)
│       ├── WorkViewModel.cs            Work for display: Title, Author, Year helpers
│       ├── UniverseViewModel.cs        Flattened cross-media library view + DominantHexColor
│       ├── SystemStatusViewModel.cs    Engine health probe result
│       ├── ScanResultViewModel.cs      Dry-run scan result (pending file operations)
│       ├── ProviderManagementDtos.cs   Provider test/sample/config DTOs for settings UI
│       ├── ReviewQueueDtos.cs         Review queue + hydration settings DTOs (§3.13)
│       ├── LabelResolveViewModel.cs   QID label resolution DTO (Label, Description, EntityType)
│       ├── ChronicleExplorerDtos.cs     Chronicle Engine DTOs: UniverseGraphResponse, GraphNodeDto, GraphEdgeDto, LoreDeltaResultDto
│       └── ResolvedUISettingsViewModel.cs  Device-resolved UI configuration (8 DTO classes)
│
└── Shared/                   ← Top-level layout shell (used by every page)
    ├── MainLayout.razor                App chrome: TopBar + LeftDock + MobileNavDrawer, review badge on profile avatar
    ├── NavMenu.razor                   Deprecated stub (replaced by LeftDock + Command Palette)
    └── _Imports.razor                  Namespace imports for all Shared components
```

**Rules for adding new code to the Dashboard:**

| New code type | Where it goes |
|---|---|
| A new call to the Engine | `Services/Integration/LibraryApiClient.cs` + `ILibraryApiClient.cs` |
| A new data shape for the Dashboard | `Models/ViewDTOs/` |
| A new reusable visual component | `Components/<FeatureName>/` |
| A new full page | `Components/Pages/` |
| A new media player component | `Components/Playback/` |
| A new playback service | `Services/Playback/` |
| A new layout wrapper | `Shared/` |
| A new visual theme setting | `Services/Theming/ThemeService.cs` |
| A new device-context feature flag | `Services/Theming/DeviceContextService.cs` + `Models/ViewDTOs/ResolvedUISettingsViewModel.cs` |

---

## 7. Project Contacts

| Role | Detail |
|---|---|
| Product Owner | Shaya |
| Repository | [github.com/shyfaruqi/tuvima-library](https://github.com/shyfaruqi/tuvima-library) |
| License | AGPLv3 |
| Engine base URL (local dev) | `http://localhost:61495` |
| Dashboard URL (local dev) | `http://localhost:5016` |
