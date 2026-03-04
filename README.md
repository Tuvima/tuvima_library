<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**The Private Universe Discovery & Media Engine.**

*Tuvima Library automatically unifies your Ebooks, Audiobooks, Comics, Music, TV Shows, and Movies into single intelligent Hubs — with built-in streaming, reading, and listening — powered by a local-first engine that never touches the cloud.*

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20macOS%20%7C%20Windows-lightgrey.svg)]()
[![Arr Compatible](https://img.shields.io/badge/Arr--Compatible-Radarr%20%7C%20Sonarr-orange.svg)]()

</div>

---

## 🧠 What is Tuvima Library?

You have a book. Then you find the movie adaptation. Then you grab the audiobook for the commute. Three files. Three folders. Three separate apps. Zero connection between them.

**Tuvima Library presents them as one.**

Drop your files into a Watch Folder, and the Intelligence Engine automatically reads the metadata inside each file, scores it for reliability, and groups everything that belongs to the same story into a single **Hub**. The Hub for *Dune* becomes the single, unified presentation of that story in your collection — your EPUB, your 4K video, your audiobook, and your comic all brought forward together into one visual tile. You navigate by story, not by file type or folder.

The Bento Dashboard is where this act of presentation reaches the screen: a glassmorphic, asymmetric grid of Hub tiles that reflects what the Intelligence Engine already knows about your library. The interface is the presentation layer. Everything behind it is inference and order.

Everything runs on your own machine. No account. No subscription. No data sent anywhere.

---

## ✨ Key Features

### 📊 The Spatial Bento Dashboard
The Dashboard is built around a **Universe-First** navigation philosophy. Instead of browsing files by type (books in one folder, movies in another), you browse by *story* — each Hub is a Universe that holds every version of that story in your collection.

**The "Last Journey" Hero Tile** — the most recently accessed Hub is displayed as a large, two-column hero card at the top of the page. It shows the Hub's artwork, title, and three progress indicators (Watch %, Read Page, Listen Time) so you can resume exactly where you left off. Click any other Hub tile to promote it to the hero position.

**The Floating Intent Dock** — a glassmorphic bar fixed at the bottom of the screen with four intent-based filters: **Hubs** (all), **Watch** (video), **Read** (books/comics), and **Listen** (audio). This replaces the traditional sidebar — navigation by media intent, not by page.

**The Command Palette** (`Ctrl+K`) — the primary deep-navigation tool. Search across all Hubs, Works, and system pages from anywhere in the Dashboard without leaving the current view.

All cards and panels use **glassmorphic styling** with 32px corner radii, `backdrop-filter: blur(20px)`, and colour-matched glow shadows drawn from each Hub's dominant media colour. A global **Command Palette** (activated with `Ctrl+K`) lets you navigate the entire library by name without touching the mouse.

> *Live updates are pushed directly to your browser via the Intercom channel the moment a new file is detected — no page refresh, no manual sync. The Dashboard is always a real-time reflection of what the Engine knows.*

### 🤖 The Intelligence Engine (Field-Specific Weighted Voter)
The Engine never asks you to manually enter a title, year, or author. Instead, it uses a **Field-Specific Weighted Voter** system:

- Every piece of metadata from every source (embedded file tags, filenames, external providers) is recorded as a **Claim**
- Each Claim carries a **per-field trust weight** based on how reliable its source is *for that specific kind of data* — for example, Audnexus is authoritative for audiobook narrators (weight 0.9), while Open Library excels at series data (weight 0.9) but is not a dedicated cover-art source
- The Voter tallies all Claims for each metadata field independently and elects a winner — the **Canonical Value** — using only that field's weights
- If the vote is too close to call, the conflict is surfaced in the dashboard for a single human decision — the only time you ever need to intervene
- **User-Locked Claims** — when you manually set a metadata value, that claim is locked. The engine gives it a weight of 1.0 and cannot override it on any future re-score

Provider trust levels are **never hard-coded**. Every weight lives in the provider config files (`config/providers/`) so you can tune them at any time without touching code.

All original Claims are preserved forever. Nothing is overwritten. Full audit history, always.

### 🔒 Privacy-First by Design
- **Local SQLite database** — your entire library catalogue lives in a single file on your own hard drive. No cloud sync, no telemetry
- **Secret Store** — API keys for external metadata providers (e.g. TMDB, MusicBrainz) are encrypted at rest using your OS's built-in protection layer. Never stored as plain text
- **Guest Key system** — any external tool that connects to the Engine must present a named, revocable API key. You control exactly who has access and can revoke a key in seconds without affecting others

### 🚗 Automotive Mode *(Planned)*
A dedicated high-contrast display mode with oversized buttons and enlarged text — designed for safe, glanceable use on a media room TV or a tablet mounted in a vehicle. One toggle switches the entire dashboard into this mode; one toggle switches it back.

---

## 📸 Screenshots

> *Bento Grid dashboard screenshots will be added here once the full UI is complete.*
>
> **Coming in a future update:**
> - Universe overview (Bento Grid with Hub cards)
> - Hub detail page with Works list
> - Ingestion progress live feed
> - Command Palette overlay

---

## 🚀 Quick Start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
# 1. Clone the repository
git clone https://github.com/Tuvima/tuvima_library.git
cd tuvima_library

# 2. Create your local configuration
cp -r config.example config
```

Open `config/core.json` and set these values:

```json
{
  "database_path": "/your/path/library.db",
  "data_root":     "/your/media/library"
}
```

```bash
# 3. Start the Intelligence Engine (headless, API-only mode)
dotnet run --project src/MediaEngine.Api

# Engine is now running at:
#   http://localhost:61495
#   Swagger UI: http://localhost:61495/swagger

# 4. (Optional) Start the visual Dashboard
dotnet run --project src/MediaEngine.Web

# Dashboard is now running at:
#   http://localhost:5016

# 5. Run the automated test suite
dotnet test
```

### Configuration Reference

Settings are stored in `config/` as individual JSON files grouped by concern:

| File | What it controls |
|---|---|
| `config/core.json` | Database path, data root, library root, schema version |
| `config/scoring.json` | Auto-link threshold, conflict threshold, stale claim decay |
| `config/maintenance.json` | Vacuum on startup, retention days, sync interval |
| `config/hydration.json` | Pipeline stage timeouts, concurrency, confidence thresholds |
| `config/providers/*.json` | Per-provider endpoints, trust weights, throttle, enabled state |
| `config/universe/*.json` | Wikidata property map, bridge lookups, value transforms |

---

## 🏗️ Architecture

Tuvima Library is built on a **headless Engine + visual Dashboard** split. The two parts are completely independent.

```
┌─────────────────────────────────────────┐
│             MediaEngine.Web             │  ← Visual Dashboard (Blazor Server)
│         browser dashboard               │    Connects via HTTP + SignalR
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
```

**Why the split matters:**
- The Engine can run silently as a background service — no browser, no interface, no overhead
- Any app that speaks HTTP can connect to the Engine directly — see [Arr Compatibility](#-arr-compatibility-radarrsonarr) below
- The Dashboard can be redesigned or replaced without touching the Engine or the database

**Internal Engine layers** (each depends only on the one above it):

```
MediaEngine.Domain          ← Business rules and data shapes (zero dependencies)
  └─ MediaEngine.Storage    ← Database reads and writes
      └─ MediaEngine.Intelligence  ← Scoring, deduplication, conflict resolution
          └─ MediaEngine.Processors  ← File-type readers (EPUB, video, comic)
              └─ MediaEngine.Ingestion  ← Watch folder, file queue, background worker
                  └─ MediaEngine.Api    ← HTTP endpoints and SignalR hub
```

---

## 🗂️ Filesystem-First Philosophy

**The database is a cache of the filesystem, not the other way around.**

Every file that the Engine organises carries its own self-describing manifest — a `library.xml` sidecar written directly alongside it on disk. If the database is ever wiped or migrated, the library can be fully reconstructed from those XML files alone. Nothing is ever lost that cannot be recovered from disk.

### Hub-First Folder Structure

When AutoOrganize is enabled and a file scores above the confidence threshold (≥ 0.85) or has a user-locked metadata value, the Engine moves it into a clean, human-readable hierarchy:

```
{Library Root}/
  Books/
    The Hobbit (1937)/
      library.xml                    ← Hub sidecar — human-readable identity + metadata
      Epub - Standard/
        The Hobbit.epub
        library.xml                  ← Edition sidecar — content hash, title, author, locks
        cover.jpg                    ← Cover art (always on disk, never stored in DB)
```

The top-level category (`Books`, `Comics`, `Videos`, `Audio`) is derived from the file's detected media type. The Hub name is the scored title canonical value. The format folder identifies the media type and edition. The file is then placed inside, alongside its XML sidecar and cover image.

### The library.xml Sidecar

Each sidecar is a small XML file with two schemas:

- **Hub-level** (`<library-hub>`) — stores the Hub's display name, year, Wikidata identifier, and franchise. Written once per Hub folder; idempotent on repeat ingestion of the same Hub.
- **Edition-level** (`<library-edition>`) — stores all metadata canonical values (title, author, media type, ISBN, ASIN), the content hash (permanent file identity), the cover-art path, and a list of any user-locked claims with their lock timestamps.

### The Great Inhale — Rebuilding from Disk

If the database is deleted or corrupted, call `POST /ingestion/library-scan` from the Engine API (or via the Dashboard). The Engine will:

1. Recursively walk every folder under the Library Root looking for `library.xml` files.
2. For each **Hub sidecar**: create or update the corresponding Hub record. XML always wins on conflict.
3. For each **Edition sidecar**: find the existing MediaAsset by its content hash, then restore all canonical values and any user-locked claims. Files not yet present in the database are skipped — a normal ingestion pass is needed to create them.

The Great Inhale is a **read-only XML scan** — it reads XML only, performs no file hashing and no metadata extraction, and completes in seconds even for large libraries.

> **The design constraint:** Cover art is never stored in the database. `cover.jpg` is always read from disk. The `library.xml` sidecar is the single portable source of truth.

---

## 🌐 Supported Metadata Providers

Tuvima Library ships with six built-in zero-key providers and supports additional providers that require a free API key. All config-driven — adding a new REST+JSON provider is a zero-code operation.

### Built-in (Zero-Key)

| Provider | Media type | What it contributes | Throttle |
|---|---|---|---|
| **Apple Books** (ebook) | EPUB / ebooks | Cover art (600 × 600), description, rating, title | 1 req / 300 ms |
| **Apple Books** (audiobook) | Audiobooks | Cover art (600 × 600), description, rating, title | shared |
| **Audnexus** | Audiobooks | Narrator, series, series position, cover art, author | none |
| **Open Library** | Books | Title, author, year, cover art, ISBN, series | 1 req / 1 s |
| **Google Books** | Books | Title, author, year, cover art, ISBN, description, page count | 1 req / 500 ms |
| **Wikidata** | All media + people | 50+ properties via SPARQL, person headshots, biography, social links, bridge IDs to every major platform | 1 req / 1.1 s |

### Planned (Free API Key Required)

| Provider | Media type | What it contributes | Status |
|---|---|---|---|
| **TMDB** | Movies, TV | High-res posters (2000px), backdrop images, cast/crew, episode details | Planned (Sprint 13) |
| **Comic Vine** | Comics | Character arcs, universe lore, issue details | Planned (Sprint 13) |
| **Grand Comics Database** | Comics | Illustrator, colorist, penciller credits | Planned (Sprint 13) |
| **MusicBrainz** | Music | Artist, album, year, genre, MBID (zero-key) | Planned (Sprint 6) |
| **Spotify** | Music | Artist headshots, album art, genre, popularity | Planned (Sprint 13) |

All network calls run on a **background channel** (`Channel<HarvestRequest>`, bounded 500 items, DropOldest). File ingestion never blocks waiting for network.

**Recursive Person Enrichment** — each author and narrator found in a file's embedded tags gets a `Person` record linked to the asset. Unenriched persons are automatically queued for a Wikidata lookup. When the headshot and biography arrive, a `PersonEnriched` SignalR event pops the data into the Dashboard card in real time.

**To enable providers**, edit the individual provider config files in `config/providers/`:

```json
{
  "name": "audnexus",
  "enabled": true,
  "endpoints": {
    "base_url": "https://api.audnexus.com"
  }
}
```

All URLs and settings live in per-provider config files — changing a provider's base address requires only a config edit, never a recompile.

---

## 🔌 Arr Compatibility (Radarr / Sonarr)

The Engine exposes a standard HTTP API secured by an **`X-Api-Key` header** — the same authentication pattern used by Radarr, Sonarr, Lidarr, and the broader \*Arr ecosystem.

**To connect an external app:**

1. Open the Swagger UI at `http://localhost:61495/swagger`
2. Use `POST /admin/api-keys` to create a named key for your app (e.g. `"Radarr integration"`)
3. Add the key as an `X-Api-Key` header in your external app's connection settings
4. Revoke it any time with `DELETE /admin/api-keys/{id}` — other apps are unaffected

External apps can query Hubs, trigger library scans, and resolve metadata conflicts via the Engine's full REST API without ever opening the Dashboard.

---

## 🗺️ Project Roadmap

### ✅ Completed — Intelligence & Infrastructure

| Phase | What was built |
|---|---|
| **Phase 1–3** | Architecture, domain model (Hub → Work → Edition → MediaAsset), metadata contracts |
| **Phase 4** | SQLite storage — ORM-less raw SQL, WAL mode, embedded schema, 13 tables |
| **Phase 5** | Media processors — EPUB, Video (stub), Comic (CBZ/CBR), Generic fallback |
| **Phase 6** | Intelligence Engine — Weighted Voter, Conflict Resolver, Identity Matcher, Hub Arbiter |
| **Phase 7** | Ingestion Engine — Watch Folder, debounce queue, SHA-256 hasher, background worker |
| **Phase 8** | Field-Level Arbitration — User-Locked Claims, per-field provider trust matrix |
| **Phase 9** | External Metadata — Apple Books, Audnexus, Open Library, Google Books, Wikidata SPARQL; Recursive Person Enrichment; Config-driven universal adapter |
| **Phase A** | Wikidata property map (50+ P-codes), person social links, sidecar XML bridges |
| **Phase B** | SPARQL deep-hydration engine, Two-Stage Handshake (bridge IDs → title → SPARQL) |
| **Library Org** | Hub-first folder structure, library.xml sidecars, Great Inhale rebuild, AutoOrganize |
| **Security** | API keys (generate/revoke), role-based auth (Admin/Curator/Consumer), rate limiting, path traversal protection, SignalR auth |
| **Config** | Directory-based config (`config/`), auto-migration from legacy, per-provider files, universe knowledge model |
| **Activity** | System activity ledger, configurable retention, daily pruning |
| **Profiles** | User profiles with roles, avatar colours, 4-device adaptive layouts |

### ✅ Completed — Dashboard

| Deliverable | What was built |
|---|---|
| **Dashboard Shell** | MudBlazor 9, dark/light mode, glassmorphic Bento Grid, Hub cards, Command Palette (Ctrl+K) |
| **Real-time** | SignalR Intercom (7 events), live progress bars, auto-reconnect |
| **Navigation** | Floating Intent Dock (Hubs/Watch/Read/Listen), Virtual Libraries, content-aware visibility |
| **Settings** | 11 tabs: General, Playback, Navigation, Libraries, Universe, Providers, Connectivity, API Keys, Conflicts, Users, Maintenance |
| **Device Profiles** | Web, mobile, TV, automotive — 3-tier cascade (Global → Device → Profile) |

### 🔄 Planned — The Road to v1.0

| Sprint | Theme | Key Deliverables |
|---|---|---|
| **1–2** | Browsing & Detail Pages | Hub detail, Work detail, Person detail ("Human Hub"), Recently Added, Continue Journey, Smart Collections |
| **3–4** | Ebook & Comic Playback | In-browser EPUB reader (paginated, chapter nav, font controls), Comic viewer (page-turn, zoom, RTL manga) |
| **5–6** | Audiobook & Music | Persistent audiobook player (chapters, sleep timer, speed control), Music media type, MusicBrainz provider |
| **7–8** | Video & FFmpeg | Video player (subtitles, chapters, PiP), FFmpeg integration, on-the-fly HLS transcoding |
| **9–10** | Shadow Transcoder & PWA | Scheduled background transcoding (NVENC/QuickSync), offline-capable PWA, download for offline |
| **11–12** | Authentication & Multi-User | Local PIN/password login, profile-scoped progress, Shared Journey Inference, parental controls |
| **13–14** | Providers & Ecosystem | TMDB, Comic Vine, GCD, Spotify providers; OPDS catalog; Audiobookshelf API; webhooks; Plex/Calibre/Jellyfin import |
| **15–16** | Statistics & Polish | Library/personal stats, Smart Collections engine, keyboard accessibility, loading skeletons, performance audit |

### 🔮 Future Horizon

- Native iOS/Android apps (MAUI or React Native)
- DLNA/Chromecast/AirPlay casting
- OIDC authentication (Google, Facebook)
- Plugin/extension system
- Multi-language/i18n
- TV apps (Android TV, Apple TV)

---

## 🛠️ Tech Stack (Full Reference)

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
| HTTP client lifecycle | `Microsoft.Extensions.Http` (IHttpClientFactory, named clients) |
| API docs | Swashbuckle (`/swagger`) |
| Tests | xUnit 2, coverlet |

---

## 📄 License

Tuvima Library is free and open-source software, licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.

> This means you are free to use, modify, and distribute Tuvima Library — but if you deploy a modified version as a network service, you must also make your modifications available under the same license.

See the [`LICENSE`](LICENSE) file for the full license text.

All dependencies are MIT or Apache 2.0 licensed and are compatible with AGPLv3.

---

<div align="center">

*Built with care for people who take their media library seriously.*

[Report a Bug](https://github.com/Tuvima/tuvima_library/issues) · [Request a Feature](https://github.com/Tuvima/tuvima_library/issues) · [View the Engine API](http://localhost:61495/swagger)

</div>
