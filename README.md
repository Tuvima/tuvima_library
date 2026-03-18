<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**Make your media collection discoverable.**

*Tuvima Library is the first media platform that organizes by story, not by file type — unifying your books, audiobooks, movies, TV shows, music, comics, and podcasts into intelligent Hubs, with a cinematic dark dashboard, a Universe Explorer that maps the connections between your stories, and a local-first engine that never touches the cloud.*

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20macOS%20%7C%20Windows-lightgrey.svg)]()
[![Arr Compatible](https://img.shields.io/badge/Arr--Compatible-Radarr%20%7C%20Sonarr-orange.svg)]()

</div>

---

## Why Tuvima?

*Tuvima* means "discoverable" — and that's exactly what it does.

Plex and Jellyfin organize your videos. Calibre organizes your books. Audiobookshelf organizes your audiobooks. But none of them talk to each other. Your Dune ebook lives in one app. Your Dune audiobook lives in another. The Villeneuve film lives in a third. Three apps, three databases, three interfaces — for the same story.

**Tuvima Library is the first media platform that organizes by story, not by file type.**

Drop your files into a watch folder. Within seconds, Tuvima reads each file, identifies it using Wikidata (the knowledge database behind Wikipedia), and files it into a clean, organized library on your hard drive. Books, audiobooks, movies, TV shows, music, comics, and podcasts — all in one place, all managed by one intelligence engine.

### The Hub: your story, every format, one click

This is the idea that no other media manager has. When Tuvima discovers that your Dune ebook, Dune audiobook, and Dune film all share the same creative universe, it groups them into a single "Hub." One entry in your library. Click it, and you see every version of the story you own — read, listen, or watch. No switching apps. No remembering where you put things.

Plex shows you movies. Calibre shows you books. Tuvima shows you *stories*.

### The Universe Explorer

This is where Tuvima lives up to its name. The Universe Explorer makes the hidden relationships in your library *discoverable* — building an interactive map of the fictional worlds you own. Characters, locations, factions, and the connections between them, all visualized as an explorable graph.

Open the Dune universe and see Paul Atreides linked to House Atreides, connected to Arrakis, opposed by the Harkonnens. See which actors played which characters across different film adaptations. Scrub a timeline slider to see relationships change across eras. Discover connections between works you never knew existed.

No other media manager builds a knowledge graph of your collection.

### How Tuvima compares

| | Plex | Jellyfin | Calibre | Audiobookshelf | **Tuvima** |
|---|---|---|---|---|---|
| Video | Yes | Yes | No | No | **Yes** |
| Books | No | No | Yes | No | **Yes** |
| Audiobooks | No | No | Partial | Yes | **Yes** |
| Music | Yes | Yes | No | No | **Yes** |
| Comics | No | No | Partial | No | **Yes** |
| Podcasts | No | No | No | No | **Yes** |
| Cross-media linking | No | No | No | No | **Yes (Hubs)** |
| Universe explorer | No | No | No | No | **Yes** |
| Fully local (no cloud) | No | Yes | Yes | Yes | **Yes** |
| Open source | No | Yes (GPL) | Yes (GPL) | Yes (GPL) | **Yes (AGPLv3)** |

---

## Key Features

### Cinematic Dark Dashboard

Dark-mode only, poster-art swimlanes, cinematic hero banners with blurred backdrops — the kind of interface you'd expect from Netflix or Apple TV+, but running entirely on your machine. The dashboard adapts to any screen: full desktop, compact mobile, oversized TV, or minimal in-car audio mode.

- **TopBar** — glassmorphic bar with wordmark, search, notification bell, and profile avatar
- **LeftDock** — floating icon panel with seven content lanes (Home, Search, Books, Video, Music, Podcasts, Comics)
- **Cinematic Hero Banners** — pre-rendered via SkiaSharp (gaussian blur, radial vignette, film grain). Served from disk — no runtime processing
- **Poster Swimlanes** — horizontal-scrolling poster rows (2:3 aspect ratio, touch-friendly scroll-snap)
- **Command Palette** (`Ctrl+K`) — global search across Hubs, Works, and system pages
- **Real-time updates** — every change pushed live via SignalR the moment a file is detected

### Hub Detail & Person Detail

**Hub Detail** (`/hub/{id}`) — full cinematic page for each story. Hero backdrop, cover art, genre chips, action bar with contextual progress (`Continue Reading · 12%`), Wikipedia description, and related works.

**Person Detail** (`/person/{id}`) — the "Human Hub" for every creator. Wikidata-enriched headshot, biography, occupation, social links (Instagram, TikTok, Mastodon, website), and works grouped by role.

### In-Browser EPUB Reader

A full paginated EPUB reader built directly into the Dashboard. CSS multi-column pagination, chapter sidebar, font controls, reading themes (Light/Sepia/Dark), full-text search, and automatic progress tracking (saved every 30 seconds, syncs to Hub Detail in real time).

### The Priority Cascade Engine

The Engine never asks you to manually enter metadata. Instead it uses a four-tier **Priority Cascade**:

| Tier | Rule | Effect |
|---|---|---|
| **A** | User-Locked Claims | Your manual edits always win (confidence 1.0). Never overridden. |
| **B** | Per-Field Provider Priority | Configurable in `config/field_priorities.json` — e.g., Apple API for cover art, Wikipedia for descriptions |
| **C** | Wikidata Authority | When present, Wikidata claims win unconditionally |
| **D** | Confidence Cascade | Highest-confidence claim wins |

Every piece of metadata from every source (embedded file tags, filenames, external providers) is recorded as an append-only **Claim**. History is never lost.

### Two-Stage Hydration Pipeline

After ingestion, the Engine enriches every file through two background stages:

| Stage | Name | What happens |
|---|---|---|
| **1** | Reconciliation | Wikidata QID resolution via the Reconciliation API. Bridge ID cross-reference (ISBN, ASIN, TMDB ID), title search with media type filtering, Data Extension API for 50+ properties |
| **2** | Enrichment | Retail providers (Apple API, TMDB, Open Library, Google Books) fill cover art and rating gaps using bridge IDs from Stage 1 |

Person enrichment runs in parallel: authors, narrators, and directors get Wikidata headshots, biographies, and social links via `RecursiveIdentityService`.

### Review Queue

Every ambiguous decision is surfaced for human attention — never guessed silently:

- **AuthorityMatchFailed** — Wikidata could not resolve a QID
- **MultipleQidMatches** — multiple Wikidata candidates; user picks one from a card grid
- **AmbiguousMediaType** — media type could not be confidently determined (MP3: audiobook or music?)
- **LowConfidence** — overall score below threshold after hydration

### Privacy-First

- **Local SQLite database** — your entire library in a single file on your hard drive. No cloud, no telemetry
- **Database is the authoritative data store** — user edits written back to file embedded metadata (EPUB OPF, ID3 tags) for portability
- **Secret Store** — API keys encrypted at rest using OS-level protection
- **Guest Key system** — named, revocable API keys for any external tool that connects

---

## Screenshots

> *Screenshots from the current development build — subject to change as the UI is polished toward v1.0.*

### Hub Detail Page

Full cinematic page for a story — breadcrumb navigation, hero backdrop, cover art, star rating, metadata badges, Wikipedia description, action bar, and author section with Wikidata headshot.

![Hub Detail — Abaddon's Gate by James S.A. Corey](assets/screenshots/hub-detail.png)

### In-Browser EPUB Reader

Paginated reading view with chapter navigation, search, bookmark, and persistent close button. Progress bar and reading stats at the bottom. Dark reading theme shown.

![EPUB Reader — Chapter Thirty-Five: Anna](assets/screenshots/epub-reader.png)

### Books Lane

Content-type lane page with cinematic hero banner, format toggle (Book / Audiobook), sort and filter controls, and poster swimlanes.

![Books Lane — Smart Sections view](assets/screenshots/books-lane.png)

---

## Quick Start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
# 1. Clone the repository
git clone https://github.com/shyfaruqi/tuvima-library.git
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
   ┌─────▼──────┐   ┌────────────────┐   ┌──────────────────┐
   │  Storage   │   │  Intelligence  │   │    Ingestion     │
   │  (SQLite   │   │  (Priority     │   │  (Watch Folder)  │
   │  + Dapper) │   │   Cascade)     │   │                  │
   └────────────┘   └────────────────┘   └──────────────────┘
         │                                       │
   ┌─────▼──────┐                       ┌────────▼─────────┐
   │  Providers │                       │   Processors     │
   │ (9 built-in│                       │ EPUB/Audio/Video │
   │ + config-  │                       │  /Comic/Generic  │
   │  driven)   │                       └──────────────────┘
   └────────────┘
```

**Why the split matters:**
- The Engine runs silently as a background service — no browser, no interface, no overhead
- Any app that speaks HTTP can connect to the Engine
- The Dashboard can be redesigned or replaced without touching the Engine or database

**Internal Engine layers:**

```
MediaEngine.Domain          ← Business rules and data shapes (zero dependencies)
  └─ MediaEngine.Storage    ← SQLite via Dapper — 39 tables, 23 repositories
      └─ MediaEngine.Intelligence  ← Priority Cascade Engine, Fuzzy Matcher, Hub Arbiter
          └─ MediaEngine.Processors  ← EPUB, Audio, Video, Comic, Generic file readers
              └─ MediaEngine.Providers  ← 9 metadata providers + config-driven adapter
                  └─ MediaEngine.Ingestion  ← Watch folder, debounce queue, 2-stage pipeline
                      └─ MediaEngine.Api    ← 21 endpoint groups, SignalR hub, 7 background services
```

---

## Database-First Architecture

**The database is the authoritative data store.** User metadata edits are written back to file embedded metadata (EPUB OPF, ID3 tags, MP4 atoms) for portability. Wikidata properties are re-fetchable via batch Reconciliation API as a recovery fallback.

### Library Folder Structure

When a file scores ≥ 0.85 confidence (or has a user-locked value), the Engine auto-organises it:

```
{Library Root}/
  Books/
    Dune - Q190159/
      Epub/
        Dune.epub
      Audiobook/
        Dune.m4b
      cover.jpg                    ← Cover art (always on disk, never in the database)
      hero.jpg                     ← Pre-rendered cinematic hero banner (SkiaSharp)
  Movies/
    Dune Part Two - Q104686073/
      Dune Part Two.mkv
      cover.jpg
      hero.jpg
```

Files below the confidence threshold land in `.staging/` (pending, low-confidence, or unidentifiable) until hydration improves their score or a user resolves them manually.

### Activity Ledger

Every significant Engine action is permanently recorded: file ingested, metadata hydrated, review resolved, orphan cleaned, reconciliation run. Configurable retention (default 60 days). Visible in the **Activity** tab.

---

## Supported Metadata Providers

Nine built-in providers plus a config-driven adapter for adding any REST+JSON source with zero code changes.

### Zero-Key Providers

| Provider | Media types | What it contributes |
|---|---|---|
| **Wikidata Reconciliation** | All media + people | QID resolution via Reconciliation API, 50+ properties via Data Extension API, franchise/series/character relationships, person headshots and biographies |
| **Wikipedia** | All media + people | Rich 2-3 paragraph descriptions via REST API with QID-to-sitelink resolution |
| **Apple API** | Ebooks, Audiobooks, Podcasts | Cover art (up to 3000×3000), description, rating, title |
| **Open Library** | Ebooks | Title, author, year, cover art, ISBN, series |
| **Google Books** | Ebooks | Title, author, year, cover art, ISBN, description, page count |
| **MusicBrainz** | Music | Artist, album, year, genre, MBID, Cover Art Archive |

### Free API Key Required

| Provider | Media type | What it contributes |
|---|---|---|
| **TMDB** | Movies, TV | High-res posters, backdrop images, cast/crew, TMDB/IMDb bridge |
| **Comic Vine** | Comics | Comic covers, issue details, Comic Vine ID bridge |
| **Podcast Index** | Podcasts | Secondary podcast metadata, GUIDs, episode lists |

### Provider Response Cache

A `provider_response_cache` table (per-provider TTL) eliminates redundant API calls when bulk-importing related content. At 1 req/sec MusicBrainz rate, caching reduces a 10,000-track library scan from ~3 hours to ~15 minutes.

---

## Arr Compatibility (Radarr / Sonarr)

The Engine uses the same `X-Api-Key` authentication pattern as the \*Arr ecosystem.

1. Open the Swagger UI at `http://localhost:61495/swagger`
2. Use `POST /admin/api-keys` to create a named key (e.g. `"Radarr integration"`)
3. Add the key as an `X-Api-Key` header in your external app's connection settings
4. Revoke any key individually with `DELETE /admin/api-keys/{id}`

---

## Tech Stack

| What it does | Technology |
|---|---|
| Language & runtime | C# / .NET 10 |
| Database | SQLite via Dapper (micro-ORM) |
| Engine API | ASP.NET Core minimal APIs (21 endpoint groups) |
| Real-time events | SignalR (`/hubs/intercom`, 14 event types) |
| HTTP resilience | Polly (retry, circuit-breaker, timeout) |
| Dashboard | Blazor Server + MudBlazor 9 |
| EPUB parsing | VersOne.Epub |
| Image generation | SkiaSharp (hero banners — blur, vignette, grain) |
| Audio/video tags | TagLibSharp (ID3v2, MP4 atoms, Vorbis, MKV) |
| Video probing | Xabe.FFmpeg |
| String matching | FuzzySharp |
| Graph queries | dotNetRDF (in-memory SPARQL) |
| Scheduling | Cronos (cron expressions) |
| Logging | Serilog (rolling files, 14-day retention) |
| API docs | Swashbuckle (`/swagger`) |
| Tests | xUnit + coverlet |

---

## License

Tuvima Library is free and open-source software, licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.

> This means you are free to use, modify, and distribute Tuvima Library — but if you deploy a modified version as a network service, you must also make your modifications available under the same license.

See the [`LICENSE`](LICENSE) file for the full license text. All dependencies are MIT, Apache 2.0, or LGPL licensed and compatible with AGPLv3.

No premium tiers. No feature gates. No "Pro" version.

---

<div align="center">

**You already own the stories. Tuvima makes them discoverable.**

Your collection isn't a pile of files. It's stories that moved you — novels you stayed up too late reading, films that changed how you see the world, audiobooks that made long drives disappear. They deserve more than scattered folders and five different apps.

No subscriptions. No cloud. No compromises. Just your stories, finally discoverable.

[Report a Bug](https://github.com/shyfaruqi/tuvima-library/issues) · [Request a Feature](https://github.com/shyfaruqi/tuvima-library/issues) · [View the Engine API](http://localhost:61495/swagger)

</div>
