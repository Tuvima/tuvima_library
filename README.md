<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**The Private Universe Discovery & Media Engine.**

*Tuvima Library automatically unifies your Ebooks, Audiobooks, Comics, Music, TV Shows, and Movies into single intelligent Hubs — with a cinematic dark dashboard, live metadata enrichment, and a local-first engine that never touches the cloud.*

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20macOS%20%7C%20Windows-lightgrey.svg)]()
[![Arr Compatible](https://img.shields.io/badge/Arr--Compatible-Radarr%20%7C%20Sonarr-orange.svg)]()

</div>

---

## What is Tuvima Library?

You have a book. Then you find the movie adaptation. Then you grab the audiobook for the commute. Three files. Three folders. Three separate apps. Zero connection between them.

**Tuvima Library presents them as one.**

Drop your files into a Watch Folder, and the Intelligence Engine automatically reads the metadata inside each file, scores it for reliability, and groups everything that belongs to the same story into a single **Hub**. The Hub for *Dune* becomes the single, unified presentation of that story in your collection — your EPUB, your 4K video, your audiobook, and your comic all brought forward together into one visual tile. You navigate by story, not by file type or folder.

Everything runs on your own machine. No account. No subscription. No data sent anywhere.

---

## Key Features

### Cinematic Dark Dashboard

The Dashboard is dark-mode only — background `#060A16`, designed to let cover art and hero imagery breathe. Navigation uses two persistent elements:

- **TopBar** — a 64px glassmorphic bar fixed at the top with the Tuvima wordmark, search, notification bell, and profile avatar
- **LeftDock** — a floating 64px icon panel with seven fixed content lanes (Home, Search, Books, Video, Music, Podcasts, Comics), Settings, profile avatar, and a live clock. Active lane shown with a 3px amber accent bar

**Cinematic Hero Banners** — when a file is ingested, `HeroBannerGenerator` (SkiaSharp) creates a 1920×600 pre-rendered hero image from the cover art: gaussian blur σ40, radial vignette, film grain at 6% opacity. Hero images are served from disk — no runtime image processing during browsing.

**Poster Swimlanes** — the library overview uses horizontal-scrolling poster rows (2:3 aspect ratio, touch-friendly scroll-snap). Rows include "Continue your Journey", "Recently Added", and one row per media type.

**Command Palette** (`Ctrl+K`) — global deep-navigation. Search Hubs, Works, and system pages from anywhere without leaving the current view.

> *Every change is pushed live to your browser via the Intercom channel the moment a new file is detected. The Dashboard is always a real-time reflection of what the Engine knows.*

### Hub Detail & Person Detail Pages

**Hub Detail** (`/hub/{id}`) — a full cinematic page for each story. Breadcrumb navigation (Home / Lane / Author), a full-bleed hero backdrop, 400×600px cover, genre chips, and an action bar (Read Now, Add To, Share, Edit, ⋯ menu). Related works swim below in a horizontal row.

**Person Detail** (`/person/{id}`) — the "Human Hub" for every creator. Shows Wikidata-enriched headshot, biography, occupation, social links (Instagram, TikTok, Mastodon, website), and works grouped by role (Author, Narrator, Director).

### The Intelligence Engine (Field-Specific Weighted Voter)

The Engine never asks you to manually enter a title, year, or author. Instead it uses a **Field-Specific Weighted Voter**:

- Every piece of metadata from every source (embedded file tags, filenames, external providers) is recorded as a **Claim**
- Each Claim carries a **per-field trust weight** — Audnexus is authoritative for audiobook narrators (0.9), Wikidata is definitive for franchise identifiers (1.0), Open Library excels at ISBN (0.9)
- The Voter tallies Claims for each field independently and elects a **Canonical Value**
- If the vote is too close, the conflict is surfaced in the **Needs Review** queue for a single human decision — the only time you ever need to intervene
- **User-Locked Claims** — when you set a value manually, it is locked at confidence 1.0. No automated provider can override it on any future re-score

### Three-Stage Hydration Pipeline

After ingestion, the Engine enriches every file through three background stages:

| Stage | Name | What happens |
|---|---|---|
| **1** | Retail Match | ALL matching providers run concurrently (Apple Books, Audnexus, Open Library, Google Books). Claims from each are persisted and scored independently |
| **2** | Universal Bridge | Wikidata QID resolution via bridge IDs (ISBN, ASIN, Apple Books ID). Single SPARQL query fetches 50+ properties for the matched work |
| **3** | Human Hub | Person enrichment: authors and narrators get Wikidata headshots, biographies, and social links via `RecursiveIdentityService` |

If Stage 2 finds multiple QID candidates, a **MultipleQidMatches** review item is created — the user picks the correct Wikidata entry from a card grid. Ambiguous media type detections (MP3 that could be audiobook, music, or podcast) also create **AmbiguousMediaType** review items.

### Media Type Disambiguation

Some containers hold multiple media types — an MP3 could be an audiobook, a music track, or a podcast. Magic bytes detect the format; heuristic signals vote on the content type:

| Signal | Example | Confidence |
|---|---|---|
| Magic bytes (unambiguous) | EPUB → Books, M4B → Audiobooks | 0.95–1.0 |
| Processor heuristics | Duration, chapter markers, genre tag, bitrate | 0.30–0.80 |
| Filename / path patterns | `S01E01` → TV, path contains `audiobooks` | 0.25–0.65 |
| User lock | Manual override | 1.0 (always wins) |

Files scoring ≥ 0.70 auto-assign. Between 0.40–0.70 they are provisionally assigned and surfaced in the review queue. Below 0.40 they remain `MediaType.Unknown` and block auto-organize.

### Needs Review Queue

Every ambiguous decision the Engine cannot resolve confidently is queued for human attention:

- **LowConfidence** — overall score below threshold after all three pipeline stages
- **MultipleQidMatches** — Stage 2 found multiple Wikidata candidates; user picks one
- **AmbiguousMediaType** — media type could not be confidently determined
- **MetadataConflict** — two strong claims for the same field that are too close to separate

The profile avatar badge shows the pending count at all times. Resolving a conflict re-runs the hydration pipeline with the chosen answer.

### Privacy-First by Design

- **Local SQLite database** — your entire library catalogue lives in a single file on your own hard drive. No cloud sync, no telemetry
- **Secret Store** — API keys for external metadata providers are encrypted at rest using your OS's built-in protection layer. Never stored as plain text
- **Guest Key system** — any external tool that connects to the Engine must present a named, revocable API key. You control exactly who has access
- **Filesystem-First** — the database is a cache of the filesystem, not the master copy. `library.xml` sidecars carry the authoritative record alongside each file on disk

---

## Screenshots

> *Screenshots will be added once the final UI polish is complete.*
>
> **Current dashboard state:**
> - Cinematic hero banner (pre-rendered SkiaSharp, blur + vignette + grain)
> - TopBar + floating LeftDock navigation with 7 content lanes
> - Horizontal-scrolling poster swimlanes (Continue Journey, Recently Added, per-type rows)
> - Hub Detail page with 400×600 cover, genre chips, action bar, related content row
> - Person Detail page with Wikidata headshot, bio, and social links
> - Needs Review tab with confidence gauges and disambiguation cards

---

## Quick Start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
# 1. Clone the repository
git clone https://github.com/Tuvima/tuvima_library.git
cd tuvima-library

# 2. Create your local configuration
cp -r config.example config
```

Open `config/core.json` and set your paths:

```json
{
  "database_path": "/your/path/library.db",
  "data_root":     "/your/media/library"
}
```

```bash
# 3. Start the Intelligence Engine
dotnet run --project src/MediaEngine.Api
# Engine: http://localhost:61495
# Swagger: http://localhost:61495/swagger

# 4. Start the Dashboard (optional)
dotnet run --project src/MediaEngine.Web
# Dashboard: http://localhost:5016

# 5. Run the full test suite
dotnet test
```

### Configuration Reference

Settings live in `config/` as individual JSON files:

| File | What it controls |
|---|---|
| `config/core.json` | Database path, data root, library root, schema version |
| `config/scoring.json` | Auto-link threshold (0.85), conflict threshold, stale claim decay |
| `config/maintenance.json` | Retention days (60), vacuum on startup, sync interval |
| `config/hydration.json` | Stage timeouts, concurrency, confidence thresholds |
| `config/providers/*.json` | Per-provider endpoints, trust weights, throttle, cache TTL |
| `config/universe/*.json` | Wikidata property map, bridge lookup order, value transforms |
| `config/disambiguation.json` | Media type heuristic thresholds and signal weights |
| `config/ui/` | Global and per-device UI settings cascade |

### Docker

```bash
docker build -t tuvima-library .
docker run -p 5016:5016 -p 61495:61495 \
  -v /your/library:/data/library \
  tuvima-library
```

---

## Architecture

Tuvima Library is built on a **headless Engine + visual Dashboard** split. The two parts are completely independent.

```
┌─────────────────────────────────────────┐
│             MediaEngine.Web             │  ← Cinematic Dashboard (Blazor Server)
│  TopBar + LeftDock + Poster Swimlanes   │    Connects via HTTP + SignalR
└────────────────┬────────────────────────┘
                 │  HTTP / SignalR
┌────────────────▼────────────────────────┐
│             MediaEngine.Api             │  ← Intelligence Engine (Headless API)
│    all logic, data, file operations     │    Runs independently; no UI required
└────────┬────────────────────────────────┘
         │
   ┌─────▼──────┐   ┌───────────────┐   ┌──────────────────┐
   │  Storage   │   │  Intelligence │   │    Ingestion     │
   │  (SQLite)  │   │ (Voter/Scorer)│   │  (Watch Folder)  │
   └────────────┘   └───────────────┘   └──────────────────┘
         │                                       │
   ┌─────▼──────┐                       ┌────────▼─────────┐
   │  Providers │                       │   Processors     │
   │ (6 built-in│                       │ EPUB/Video/Comic │
   │  + podcasts│                       │  /Audio/Generic  │
   └────────────┘                       └──────────────────┘
```

**Why the split matters:**
- The Engine runs silently as a background service — no browser, no interface, no overhead
- Any app that speaks HTTP can connect to the Engine — see [Arr Compatibility](#arr-compatibility-radarrsonarr) below
- The Dashboard can be redesigned or replaced without touching the Engine or database

**Internal Engine layers:**

```
MediaEngine.Domain          ← Business rules and data shapes (zero dependencies)
  └─ MediaEngine.Storage    ← Database reads and writes (raw SQL, no ORM)
      └─ MediaEngine.Intelligence  ← Weighted Voter, Conflict Resolver, Hub Arbiter
          └─ MediaEngine.Processors  ← EPUB, Video, Comic, Audio, Generic file readers
              └─ MediaEngine.Providers  ← 6 built-in metadata providers + config-driven adapter
                  └─ MediaEngine.Ingestion  ← Watch folder, debounce queue, 3-stage pipeline
                      └─ MediaEngine.Api    ← HTTP endpoints and SignalR hub
```

---

## Filesystem-First Philosophy

**The database is a cache of the filesystem, not the other way around.**

Every organised file carries a `library.xml` sidecar written directly alongside it on disk. If the database is ever wiped or migrated, the library can be fully reconstructed from those XML files alone via the **Great Inhale** (`POST /ingestion/library-scan`).

### Hub-First Folder Structure

When a file scores ≥ 0.85 confidence (or has a user-locked value), the Engine auto-organises it:

```
{Library Root}/
  Books/
    The Hobbit (1937)/
      library.xml                    ← Hub sidecar (display name, year, Wikidata QID, bridges)
      Epub - Standard/
        The Hobbit.epub
        library.xml                  ← Edition sidecar (content hash, title, author, user locks)
        cover.jpg                    ← Cover art (always on disk, never in the database)
        hero.jpg                     ← Pre-rendered cinematic hero banner (SkiaSharp)
```

### The library.xml Sidecar

Two schemas, both human-readable and backward-compatible:

- **Hub-level** (`<library-hub>`) — display name, year, Wikidata QID, franchise, and a `<bridges>` section with every external platform ID harvested from Wikidata (TMDB, IMDb, Goodreads, Apple Books, etc.)
- **Edition-level** (`<library-edition>`) — title, author, media type, ISBN, ASIN, content hash, cover path, and any user-locked claims with their lock timestamps

### Activity Ledger

Every significant Engine action is permanently recorded in the `system_activity` table: file ingested, metadata hydrated, review resolved, orphan cleaned, reconciliation run. Configurable retention (default 60 days). Visible in the **Activity** tab in Dashboard Settings.

---

## Supported Metadata Providers

Eight built-in zero-key providers plus a config-driven adapter for adding any REST+JSON source with zero code changes.

### Built-in (Zero-Key)

| Provider | Media types | What it contributes |
|---|---|---|
| **Apple Books** | Ebooks, Audiobooks | Cover art (up to 3000×3000 via 9999 trick), description, rating, title |
| **Apple Podcasts** | Podcasts | Cover art, show description, episode metadata |
| **Audnexus** | Audiobooks | Narrator, series, series position, cover art, author |
| **Open Library** | Ebooks | Title, author, year, cover art, ISBN, series |
| **Google Books** | Ebooks | Title, author, year, cover art, ISBN, description, page count |
| **MusicBrainz** | Music | Artist, album, year, genre, MBID, Cover Art Archive |
| **Wikidata** | All media + people | 50+ SPARQL properties — franchise, series, characters, bridge IDs to every major platform, person headshots and biographies |

### Free API Key Required

| Provider | Media type | What it contributes |
|---|---|---|
| **TMDB** | Movies, TV | High-res posters, backdrop images, cast/crew, episode details, TMDB/IMDb bridge |
| **Comic Vine** | Comics | Character arcs, universe lore, issue details, Comic Vine ID bridge |
| **Podcast Index** | Podcasts | Secondary podcast metadata, GUIDs, episode lists |

### Provider Response Cache

A `provider_response_cache` table (with per-provider TTL) eliminates redundant API calls when bulk-importing related content — TV episodes from the same series, album tracks, comic issues from one volume. At 1 req/sec MusicBrainz rate, caching reduces a 10,000-track library scan from ~3 hours to ~15 minutes.

### Recursive Person Enrichment

Each author/narrator/director found in a file's embedded tags gets a `Person` record linked to the asset. Unenriched persons are queued for Wikidata lookup. When the headshot, biography, and social links arrive, a `PersonEnriched` SignalR event updates the Dashboard in real time.

---

## Arr Compatibility (Radarr / Sonarr)

The Engine uses the same `X-Api-Key` authentication pattern as the \*Arr ecosystem.

**To connect an external app:**

1. Open the Swagger UI at `http://localhost:61495/swagger`
2. Use `POST /admin/api-keys` to create a named key (e.g. `"Radarr integration"`)
3. Add the key as an `X-Api-Key` header in your external app's connection settings
4. Revoke any key individually with `DELETE /admin/api-keys/{id}` — other apps are unaffected

---

## Project Roadmap

### Completed — Intelligence & Infrastructure

| Phase | What was built |
|---|---|
| **Phases 1–3** | Architecture, domain model (Hub → Work → Edition → MediaAsset), metadata contracts |
| **Phase 4** | SQLite storage — ORM-less raw SQL, WAL mode, embedded schema, 20+ tables |
| **Phase 5** | Media processors — EPUB (word count), Video, Comic (CBZ/CBR), Audio (MP3/M4A/M4B), Generic fallback |
| **Phase 6** | Intelligence Engine — Weighted Voter, Conflict Resolver, Identity Matcher, Hub Arbiter |
| **Phase 7** | Ingestion Engine — Watch Folder, debounce queue, SHA-256 hasher, background worker |
| **Phase 8** | Field-Level Arbitration — User-Locked Claims, per-field provider trust matrix |
| **Phase 9** | External Metadata — Apple Books, Audnexus, Open Library, Google Books, Wikidata SPARQL; Recursive Person Enrichment; Config-driven universal adapter |
| **Phase A** | Wikidata property map (50+ P-codes), person social links schema, sidecar XML bridge identifiers |
| **Phase B** | SPARQL deep-hydration engine, three-step QID resolution (bridge IDs → title search → SPARQL) |
| **Library Org** | Hub-first folder structure, `library.xml` sidecars, Great Inhale rebuild, AutoOrganize (≥0.85 gate) |
| **Security** | API keys (generate/revoke), role-based auth (Admin/Curator/Consumer), rate limiting, path traversal protection, SignalR auth |
| **Config** | Directory-based config (`config/`), per-provider JSON files, universe knowledge model, auto-migration from legacy |
| **Activity Ledger** | System activity table, configurable retention, daily pruning, `ActivityTab` in Dashboard |
| **Profiles** | User profiles with roles, avatar colours, 4-device adaptive layouts (Web/Mobile/TV/Automotive) |
| **Disambiguation** | `AudioProcessor` + `VideoProcessor` heuristic media type voting; confidence-gated auto-assign |
| **3-Stage Pipeline** | Concurrent Stage 1 providers, Wikidata bridge Stage 2, Person enrichment Stage 3 |
| **Review Queue** | LowConfidence, MultipleQidMatches, AmbiguousMediaType, MetadataConflict — NeedsReview tab + avatar badge |
| **Response Cache** | `provider_response_cache` table with per-provider TTL; ETag conditional requests |
| **Hero Banners** | SkiaSharp cinematic hero generation (blur + vignette + grain) during ingestion and post-hydration |
| **Reconciliation** | `LibraryReconciliationService` — daily orphan cleanup, `POST /ingestion/reconcile` |

### Completed — Dashboard

| Feature | What was built |
|---|---|
| **Cinematic Shell** | Dark-mode only (`#060A16`), TopBar + floating LeftDock (7 content lanes), amber accent (#C9922E) |
| **Poster Swimlanes** | Horizontal scroll rows (2:3 aspect ratio, scroll-snap, hidden scrollbar), per-media-type grouping |
| **Hero Banners** | Pre-rendered `hero.jpg` served from disk; CSS blur fallback; full-bleed 1920×600 display |
| **Hub Detail** | Cinematic page — breadcrumb, cover, genre chips, action bar, description, related content row |
| **Person Detail** | Human Hub — Wikidata headshot, bio, social links (Instagram/TikTok/Mastodon/website), works by role |
| **Real-time** | SignalR Intercom (12 events), live progress bars, review badge, PersonEnriched card updates |
| **Content Lanes** | 7 fixed lanes (Home/Search/Books/Video/Music/Podcasts/Comics), `MediaLanePage`, Books format toggle |
| **Command Palette** | `Ctrl+K` global search and navigation |
| **Settings** | 16 tabs in 3 groups — Preferences, Metadata (Connection Vault, Needs Review, Activity), Server |
| **Device Profiles** | Web, Mobile, TV, Automotive — 3-tier cascade (Global → Device → Profile) |
| **Mobile Nav** | `MobileNavDrawer` slide-out, hamburger in TopBar, responsive cover sizing |

### Planned — The Road to v1.0

| Sprint | Theme | Key Deliverables |
|---|---|---|
| **1–2** | In-Browser Playback | EPUB reader (paginated, chapter nav, font controls), Comic viewer (page-turn, zoom, RTL manga toggle) |
| **3–4** | Audio & Music | Persistent audiobook player (chapters, sleep timer, speed), Music media type, album → Hub mapping |
| **5–6** | Video & FFmpeg | Video player (HLS, subtitles, chapters, PiP), FFmpeg integration, hardware-accelerated transcoding |
| **7–8** | Shadow Transcoder & PWA | Scheduled background transcoding (NVENC/QuickSync/VAAPI), offline-capable PWA |
| **9–10** | Authentication & Multi-User | Local PIN/password login, profile-scoped progress, Shared Journey Inference, parental controls |
| **11–12** | Interoperability | OPDS catalog (`/opds/`), Audiobookshelf-compatible API, webhooks, Plex/Calibre/Jellyfin import |
| **13–14** | Statistics & Collections | Library/personal stats, Smart Collections, Recently Added endpoint, Continue Journey |
| **15–16** | Polish & Accessibility | Loading skeletons, keyboard accessibility, performance audit, DLNA/Chromecast casting |

### Future Horizon

- Native iOS/Android apps (MAUI or React Native)
- OIDC authentication (Google, Facebook)
- Plugin / extension system
- Multi-language / i18n
- TV apps (Android TV, Apple TV)

---

## Tech Stack

| What it does | Technology |
|---|---|
| Language & runtime | C# / .NET 10 |
| Database | SQLite via `Microsoft.Data.Sqlite` — raw SQL, no ORM |
| Engine API | ASP.NET Core minimal APIs |
| Real-time events | SignalR (`/hubs/intercom`) |
| Dashboard | Blazor Server |
| UI components | MudBlazor 9 |
| SignalR client | `Microsoft.AspNetCore.SignalR.Client` |
| EPUB parsing | VersOne.Epub |
| Image generation | SkiaSharp (hero banners — blur, vignette, grain) |
| Audio/video tags | TagLibSharp (ID3v2, MP4 atoms, Vorbis, MKV) |
| HTTP client lifecycle | `Microsoft.Extensions.Http` (`IHttpClientFactory`, named clients) |
| API docs | Swashbuckle (`/swagger`) |
| Tests | xUnit 2, coverlet (286 tests, 0 failures) |

---

## License

Tuvima Library is free and open-source software, licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.

> This means you are free to use, modify, and distribute Tuvima Library — but if you deploy a modified version as a network service, you must also make your modifications available under the same license.

See the [`LICENSE`](LICENSE) file for the full license text.

All dependencies are MIT, Apache 2.0, or LGPL licensed and are compatible with AGPLv3.

---

<div align="center">

*Built with care for people who take their media library seriously.*

[Report a Bug](https://github.com/Tuvima/tuvima_library/issues) · [Request a Feature](https://github.com/Tuvima/tuvima_library/issues) · [View the Engine API](http://localhost:61495/swagger)

</div>
