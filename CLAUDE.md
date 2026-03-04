# CLAUDE.md — Tanaste Project Memory

> **Who reads this file?**
> Every Claude session working on this repository reads this file automatically before doing anything else.
> It is the single, authoritative source of truth for what Tanaste is, how it is built, and how to work on it.
> It bridges the Product Owner's business goals with the technical team's execution.

---

## 1. Project Overview

### What is Tanaste?

#### Name & Vision

The name **Tanaste** is derived from Tolkien's Quenya (High-Elvish) language, where *tanas* carries the meaning of **"Presentation"** — the act of bringing something forward and making it whole.

This is the project's core philosophy: Tanaste does not create a library. It **presents** one. The stories already exist on the hard drive, fragmented across formats and folders. Tanaste's job is to find them, understand them, unify them, and surface the result as something coherent and beautiful — as if it always belonged together.

Every feature exists in service of that word:
- The **Intelligence Engine** works invisibly so the library is already whole when you look at it.
- The **Hub** is the act of presentation made structural — the book, film, and audiobook of the same story brought forward as one.
- The **Bento Dashboard** is the presentation layer made visible — the interface where the Engine's understanding reaches the screen.

> **All future sessions must preserve this creative context.** When writing copy, naming features, or explaining the product, the Presentation philosophy should be the frame.

#### What it does

**Tanaste** is a **unified media intelligence platform** that runs entirely on your own machine — no cloud account, no subscription, no data leaving your home.

Its core job is to bring order to a large, messy media collection spread across folders. You point it at your hard drive, and it automatically:

1. **Watches** your folders for new files — books, audiobooks, comics, TV episodes, and movies.
2. **Fingerprints** each file with a unique identifier (like a barcode), so it can track files even if you rename or move them.
3. **Reads the embedded information** inside each file — title, author, year, cover art, series name — and uses a *Weighted Voter* system to determine the most trustworthy version of each piece of information.
4. **Groups everything into Hubs** — a single, intelligent home for all versions of the same story.
5. **Serves a visual dashboard** in your browser for browsing, searching, and managing the library.
6. **Broadcasts instant updates** to your dashboard the moment a new file is detected, with no page refresh.

### The Hub Concept

The central idea in Tanaste is the **Hub**.

A Hub is the single digital home for a *story* — not just one file format, but every version of that story in your collection.

> *Example: "The Lord of the Rings" Hub might contain:*
> - *The eBook (EPUB)*
> - *The movie trilogy (MP4 videos)*
> - *The audiobook narration*
> - *The graphic novel adaptation (CBZ comic)*

All four live under one Hub. Tanaste links them together automatically by comparing their metadata. You browse by story, not by file type.

The Hub hierarchy works like this:

```
Universe  (your entire library — all Hubs)
  └── Hub  (one story — e.g. "Dune")
        └── Work  (one title in that story — e.g. "Dune Part One")
              └── Edition  (one physical version — e.g. "4K HDR Blu-ray Remux")
                    └── Media Asset  (one file on disk)
```

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
| Automated quality checks | xUnit + coverlet | Runs a set of automated tests after every change to catch mistakes early |
| Version control | Git + GitHub | Tracks every change made to the code, with full history |

### Headless Design — Engine and Dashboard are Separate

**This is a critical architectural decision.** The Tanaste *Engine* (the intelligence and data layer) is completely separate from the *Dashboard* (the visual interface).

**Why this matters to the business:**
- **Extensibility:** Other applications — like Radarr, Sonarr, or a custom mobile app — can connect directly to the Engine without needing the Dashboard. The Engine speaks a universal language (HTTP/JSON) that any app can understand.
- **Maintenance:** If the Dashboard needs to be redesigned, the Engine keeps running untouched. Each part can be updated independently.
- **Privacy:** The Engine can run as a silent background service with no interface at all, while the Dashboard is opened only when needed.

| Part | Technical project | Role |
|---|---|---|
| The Engine | `Tanaste.Api` | Handles all intelligence, data, and file operations. Exposes an API that any app can talk to. |
| The Dashboard | `Tanaste.Web` | The browser interface. Purely visual — it asks the Engine for data and displays it. |

### Source Code Layout (Plain English)

| Folder | What it is | Plain-English role |
|---|---|---|
| `src/Tanaste.Domain` | The Rulebook | Defines what a Hub, Work, and Edition *are*. No external tools — pure business logic. |
| `src/Tanaste.Storage` | The Filing Clerk | Reads and writes the SQLite database. Keeps all library data safe. |
| `src/Tanaste.Intelligence` | The Analyst | Runs the Weighted Voter system. Scores metadata Claims. Detects duplicates. |
| `src/Tanaste.Processors` | The Scanner | Opens each file type (EPUB, video, comic) and extracts its embedded information. |
| `src/Tanaste.Providers` | The Research Team | Fetches enriched metadata from external sources (Apple Books, Audnexus, Wikidata). Runs non-blocking on a background channel. |
| `src/Tanaste.Ingestion` | The Mail Room | Watches the Watch Folder. Queues new files. Manages the safe move of files into the organised library. |
| `src/Tanaste.Api` | The Reception Desk | The Engine's public interface. Exposes all features over HTTP. Hosts the real-time intercom. |
| `src/Tanaste.Web` | The Showroom | The browser Dashboard. Uses the Feature-Sliced layout (see Section 6). |
| `tests/` | The Quality Inspector | Automated checks for every module. |

### Dashboard Internal Layout (Feature-Sliced Design)

Inside `src/Tanaste.Web`, the code is organised by *what it does*, not by *what type of code it is*. This matches industry best practice for maintainable interfaces.

| Folder | Purpose |
|---|---|
| `Services/Integration/` | All communication with the Engine (HTTP calls, SignalR intercom) |
| `Services/Theming/` | Visual appearance — dark/light mode, colour palette, corner radii |
| `Components/Universe/` | Hub grid, hero card, media item cards |
| `Components/Bento/` | The asymmetric card-grid layout system |
| `Components/Navigation/` | Command palette, navigation menu |
| `Models/ViewDTOs/` | The data shapes used by the Dashboard (separate from the Engine's data shapes) |
| `Shared/` | Top-level layout, navigation shell, shared imports |

---

## 3. Technical Strategy & Key Features

### 3.1 — The Watch Folder (Ingestion Engine)

**Plain English:** You designate a folder on your computer as the "inbox". Anything you drop into that folder is automatically processed.

**What happens when a file lands in the inbox:**

1. **Settle** — The Engine waits briefly to make sure the file has finished copying (prevents reading half-written files).
2. **Lock check** — It confirms no other program is currently using the file.
3. **Fingerprint** — It computes a unique hash of the file's contents (like a barcode). This is the file's permanent identity — it survives renaming and moving.
4. **Scan** — The appropriate Scanner (book/video/comic) opens the file and reads all embedded metadata.
5. **Identify** — The Analyst runs the Weighted Voter to assign the file to a Hub (or create a new one).
6. **Move** — The file is moved from the inbox to a clean, organised folder structure in the library.

**Why this matters to the business:** Once configured, the library manages itself. Drop files in; Tanaste does the rest.

### 3.2 — The Field-Specific Weighted Voter (Intelligence Engine)

**Plain English:** Different information sources disagree about a file's title, author, or year. The Weighted Voter holds an election to determine the truth — and each source's authority is judged *separately for each type of data*.

Each piece of metadata (e.g. the title "Dune") is a **Claim**. Claims come from multiple sources:

| Claim source | Example | Trust weight |
|---|---|---|
| File's internal metadata (OPF/ID3) | `title = "Dune"` | High (0.9) |
| Filename | `dune_part1.epub` | Medium (0.5) |
| External metadata provider | `Dune (Frank Herbert, 1965)` | Configurable per field |

The key upgrade over a simple weighted vote: each provider carries a **per-field trust weight** that reflects how reliable it is *for that specific kind of data*. Audnexus is the gold standard for audiobook narrators (weight 0.9) and series data (0.9) but is only moderately trusted for cover art. Wikidata is the definitive authority for franchise identifiers (weight 1.0). Apple Books is a single provider that supports multiple media types (ebooks and audiobooks) through media-type-scoped search strategies and field mappings.

All of these weights live in `tanaste_master.json` and can be changed at any time without touching code.

**User-Locked Claims:** When you manually set a metadata value, that Claim is permanently locked. The engine gives it a confidence of 1.0 and never overrides it on any future re-score — regardless of what any external provider says.

The Voter tallies all Claims for each metadata field independently. The winning Claim becomes the **Canonical Value** — the single trusted answer used by the Dashboard.

If two Claims are too close to pick a clear winner, the field is flagged as **Conflicted** and surfaced to the user for manual resolution.

**Why this matters to the business:**
- **No human help needed** for well-tagged files — the library builds itself.
- **Transparent conflicts** — the user is only bothered when the machine genuinely cannot decide.
- **Provenance preserved** — every Claim is kept forever (append-only). History is never lost.
- **Reliability** — user-set values can never be silently overridden by the scoring engine.
- **Maintenance** — all weights live in `tanaste_master.json`; zero code changes needed to re-tune trust levels.
- **Extensibility** — future providers simply declare their field weights in the JSON; the engine picks them up with no new code.

### 3.3 — Security: Secret Store & Guest Keys

**Plain English:** The Engine protects sensitive information and controls who can talk to it.

**Secret Store**
Private API keys (e.g. for external metadata providers like TMDB or MusicBrainz) are encrypted at rest using the operating system's built-in protection layer. They are never stored as plain text in the configuration file.

**Guest Key System**
Any application that wants to talk to the Engine must present a valid API key. Keys are:
- Generated inside the Engine with an assigned role (Administrator, Curator, or Consumer)
- Labelled (e.g. "Radarr integration", "Mobile app")
- Revocable individually without affecting other keys

**Mandatory Authentication (Phase A Security Foundation)**
Every Engine endpoint requires authentication, with two exceptions:
- `/system/status` — health probe, always open.
- Localhost requests — when `Tanaste:Security:LocalhostBypass` is `true` (the default), requests from the local machine are treated as Administrator without needing a key. This preserves the local development experience.

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

**Bento Grid**
The Hub overview uses an asymmetric card layout inspired by Japanese bento boxes — wider cards for recently accessed Hubs, narrower cards for the rest. This creates a visually rich layout that naturally draws attention to what matters most, without the user having to configure anything.

**Automotive Mode** *(planned)*
A high-contrast, large-button display mode designed for use at a distance (e.g. on a media room TV or tablet mounted in a vehicle). All text is enlarged, all touch targets are oversized, and non-essential interface elements are hidden. Activated by a single toggle.

**Real-time updates (Intercom)**
The Dashboard is connected to the Engine via a persistent live channel (the "Intercom"). When a new file is ingested or processing progresses, the Dashboard updates instantly — no manual refresh, no polling. The progress bar is live.

### 3.5 — Brand Assets

Three official SVG logo files exist. **Never replace logo placements with hand-written text.** Always use the correct file for the context.

| File | Location in repo | Use when… |
|---|---|---|
| `tanaste-logo.svg` | `src/Tanaste.Web/wwwroot/images/` and `assets/images/` | Full horizontal logo — mark + "TANASTE" wordmark. Use in the Dashboard AppBar and anywhere a full branded header is needed. |
| `tanaste-icon.svg` | `src/Tanaste.Web/wwwroot/images/`, `wwwroot/favicon.svg`, and `assets/images/` | Square icon mark only. Use as favicon, drawer icon, app icon, or any small/square slot. |
| `tanaste-hero.svg` | `assets/images/` | Mark + wordmark + subtitle ("The Unified Media Intelligence Kernel"). Use in README hero and marketing contexts only. |

**Color note:** All three SVGs use hardcoded fills: white (`#fff`) for highlight layers and black (default) for the main strokes. They are designed primarily for dark backgrounds (as used in the Dashboard). On light backgrounds, the mark renders as a black design — which is clean and intentional.

**Source files** are in `C:\Users\shaya\OneDrive\Documents\Projects\Tanaste\Graphics\` (outside the repo — designer originals). Do not modify the SVGs in the repo directly; request updated exports from the source files.

### 3.6 — External Metadata Adapters & Recursive Person Enrichment (Phase 9)

**Plain English:** After a file lands in the library, the Engine quietly reaches out to five free online sources — Apple Books, Audnexus, Open Library, Google Books, and Wikidata — to fetch better cover art, descriptions, narrator credits, and author portraits. This happens entirely in the background; the file appears on the Dashboard immediately, and the richer information pops in moments later without any page refresh.

**The five zero-key providers (no accounts, no API keys required):**

| Provider | What it contributes | Trust weights |
|---|---|---|
| **Apple Books** | Cover art (600×600), description, rating, title. Single config supporting both ebooks and audiobooks via media-type-scoped search strategies. | cover 0.85, description 0.85, rating 0.8, title 0.7 |
| **Audnexus** | Narrator, series, series position, cover art, author | narrator/series/cover/series_pos 0.9, author 0.75 |
| **Open Library** | Title, author, year, cover art, ISBN, series | title 0.75, author 0.8, year 0.85, cover 0.7, isbn 0.9 |
| **Google Books** | Title, author, year, cover art, ISBN, description, page count, publisher | title 0.75, author 0.8, year 0.85, cover 0.7, isbn 0.9 |
| **Wikidata** | Person headshot (Wikimedia Commons), biography, Q-identifier | qid/headshot/biography 1.0 |

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

The four REST+JSON providers (Apple Books, Audnexus, Open Library, Google Books) are powered by a single `ConfigDrivenAdapter` class (`src/Tanaste.Providers/Adapters/ConfigDrivenAdapter.cs`) that reads its entire behaviour from the provider's JSON config file in `config/providers/`. No individual adapter classes exist for these providers — they were retired in favour of the universal adapter.

Each config file declares:
- `adapter_type: "config_driven"` — tells the DI registration loop to use the universal adapter
- `provider_id` — stable GUID for `metadata_claims.provider_id` foreign keys
- `can_handle.media_types[]` — which media types this provider supports (e.g. `["Epub", "Audiobook"]`)
- `search_strategies[]` — ordered URL template strategies with `required_fields`, `tolerate_404`, `results_path`, and optional `media_types` scoping
- `field_mappings[]` — JSON path extraction rules with named transforms, confidence values, and optional `media_types` scoping

**Media-type scoping on strategies and field mappings:** A single provider config can support multiple media types by declaring `media_types` arrays on individual strategies and mappings. When a search or fetch request includes a media type, only matching strategies/mappings are used. Strategies and mappings with no `media_types` array are universal (apply to all media types). `MediaType.Unknown` acts as a wildcard, returning all strategies/mappings regardless of scoping.

Adding a new REST+JSON provider is a **zero-code operation**: drop a config file in `config/providers/`, restart, done.

**Wikidata stays as a coded adapter** (`WikidataAdapter`) — its SPARQL-based intelligence cannot be expressed as URL templates + JSON path extraction.

**Key types:**
- `ConfigDrivenAdapter` (`Tanaste.Providers.Adapters`) — universal adapter implementing `IExternalMetadataProvider`
- `JsonPathEvaluator` (`Tanaste.Providers.Models`) — static utility navigating `System.Text.Json.Nodes.JsonNode` with dot-notation, array indexing, and wildcard iteration
- `ValueTransformRegistry` (`Tanaste.Providers.Models`) — named transform functions (to_string, strip_html, url_template, regex_replace, prefer_isbn13, array_join, array_nested_join, first_n_chars, fallback_key)

**Key architectural rules for this subsystem:**
- `Tanaste.Ingestion` has **zero new project references**. All interfaces (`IMetadataHarvestingService`, `IRecursiveIdentityService`, `IMetadataClaimRepository`, `ICanonicalValueRepository`) live in `Tanaste.Domain.Contracts` — which Ingestion already references.
- All provider configuration lives in `config/providers/{name}.json` — endpoints, trust weights, search strategies, field mappings, throttle, concurrency. Changing any of these requires only a config edit, never a recompile.
- Provider GUIDs are stable strings in each config file's `provider_id` field (not looked up from the DB at runtime) so new `MetadataClaim` rows can be written without a DB round-trip.
- Throttle rules and concurrency limits are per-provider in their config files. The `ConfigDrivenAdapter` enforces them via `SemaphoreSlim` + timestamp gap.
- Required-field short-circuits: each search strategy declares `required_fields`. If a required field (e.g. ASIN for Audnexus) is missing, the strategy is skipped immediately — no HTTP call made.

**Why this matters to the business:**
- **Reliability** — Providers are never in the critical path. A failed network call returns an empty list; the file remains in the library with its local metadata intact.
- **Performance** — The harvest queue is non-blocking. File ingestion completes in milliseconds regardless of network conditions.
- **Maintenance** — Adding a new REST+JSON provider is a zero-code operation: one JSON config file. Wikidata is the only provider requiring compiled code.
- **Extensibility** — The config-driven architecture supports any REST+JSON API that returns structured data. URL templates, JSON path extraction, and named transforms cover the common patterns.
- **Privacy** — Only titles, authors, and ASINs are sent to external services — no personal data, no usage telemetry, no library structure.

### 3.7 — Library Organization & Sidecar System (Filesystem-First)

**Plain English:** After a file is ingested and scored with sufficient confidence, Tanaste organises it into a clean, human-readable folder structure and writes an XML companion file alongside it. This XML file is the portable source of truth: if the database is ever wiped, the library can be rebuilt from it in seconds.

**Filesystem-First design invariant:**
- The **database is a cache of the filesystem, not the master copy.** XML always wins on conflict during a Great Inhale.
- Cover art is **never stored in the database.** `cover.jpg` lives alongside the file on disk and is always read from there.

**Hub-First Folder Structure:**
```
{LibraryRoot}/{Category}/{HubName} ({Year})/{Format} - {Edition}/
  {filename}.epub          ← the organised media file
  tanaste.xml              ← Edition-level sidecar (this folder)
  cover.jpg                ← Cover art extracted from the file
{LibraryRoot}/{Category}/{HubName} ({Year})/
  tanaste.xml              ← Hub-level sidecar (parent folder)
```

`{Category}` is derived from `MediaType` enum: `Books` (Epub), `Comics`, `Videos` (Movie), `Audio` (Audiobook), `Other`.

**AutoOrganize confidence gate:**
AutoOrganize is gated on:
```
scored.OverallConfidence >= 0.85  ||  claims.Any(c => c.IsUserLocked)
```
Files that score below 0.85 with no user locks are left in the Watch Folder and not moved — they wait for more data. The threshold reuses `AutoLinkThreshold = 0.85` from `ScoringConfiguration` (single source of truth).

**tanaste.xml schemas:**
- `<tanaste-hub version="1.0">` — `identity/display-name`, `identity/year`, `identity/wikidata-qid`, `identity/franchise`, `last-organized`.
- `<tanaste-edition version="1.0">` — `identity/title`, `identity/author`, `identity/media-type`, `identity/isbn`, `identity/asin`, `content-hash`, `cover-path`, `user-locks` (zero or more `<claim key=".." value=".." locked-at=".."/>`), `last-organized`.

**Key types and services:**
- `ISidecarWriter` / `SidecarWriter` (`Tanaste.Ingestion`) — reads and writes both XML schemas using `System.Xml.Linq` (BCL — no new NuGet dependency). `WriteHubSidecarAsync(hubFolderPath, data)`, `WriteEditionSidecarAsync(editionFolderPath, data)`, `ReadHubSidecarAsync(xmlPath)`, `ReadEditionSidecarAsync(xmlPath)`. Both read methods return `null` on any exception (resilient).
- `ILibraryScanner` / `LibraryScanner` (`Tanaste.Ingestion`) — Great Inhale implementation. Recursively enumerates `tanaste.xml` files; peeks root element name via `XmlReader` (fast, no full load); dispatches to `HydrateHubAsync` or `HydrateEditionAsync`. Stable provider GUID `c9d8e7f6-a5b4-4321-fedc-0102030405c9` for re-inserted user-locked claims.
- `LibraryScanResult` (`Tanaste.Ingestion.Models`) — scan outcome counts: `HubsUpserted`, `EditionsUpserted`, `Errors`, `Elapsed`.
- `HubSidecarData`, `EditionSidecarData`, `UserLockedClaim` (`Tanaste.Ingestion.Models`) — POCOs for XML data exchange between `SidecarWriter` and `LibraryScanner`.
- `IHubRepository.FindByDisplayNameAsync` + `IHubRepository.UpsertAsync` — added to support Hub hydration in Great Inhale.
- `Hub.DisplayName` (`Tanaste.Domain.Aggregates`) — human-readable hub name; populated at organise time and restored from XML.
- Migration **M-004** — `ALTER TABLE hubs ADD COLUMN display_name TEXT;` — applied in `DatabaseConnection.RunStartupChecks()`.

**API endpoint:**
- `POST /ingestion/library-scan` — triggers Great Inhale. Returns `LibraryScanResponse` with hub/edition counts and elapsed time. Requires `LibraryRoot` to be configured; returns 400 if unset or if the directory does not exist.
- Dashboard client method: `TriggerLibraryScanAsync()` on `ITanasteApiClient` / `TanasteApiClient`.

**wikidata_qid canonical key:**
`wikidata_qid` is used as a Work-level canonical value key (produced by the Wikidata adapter in Phase 9). It is written into the Hub-level tanaste.xml as `<identity/wikidata-qid>` for portability.

**Great Inhale scope constraints:**
Edition-level hydration **requires the MediaAsset to already exist in the database** (matched by content hash). It cannot create a `Hub → Work → Edition → MediaAsset` chain from scratch after a complete wipe — that requires a full re-ingestion pass (no `IWorkRepository` or `IEditionRepository` exist yet). Hub-level hydration creates Hub records unconditionally.

**Why this matters to the business:**
- **Reliability** — A complete database wipe is recoverable. Drop the database file; run Great Inhale; the library is back.
- **Maintenance** — The XML schema is human-readable. A user can open `tanaste.xml` in any text editor and understand exactly what Tanaste knows about a file.
- **Privacy** — All data lives on disk under the Library Root. No external dependency, no cloud sync.
- **Performance** — Great Inhale reads XML only (no file hashing, no metadata extraction). A library of thousands of files scans in seconds.

### 3.8 — System Activity Ledger & Maintenance (Phase 1 — Audit Engine)

**Plain English:** Every significant action the Engine takes — ingesting a file, refreshing metadata, pruning old records — is permanently recorded in a system activity ledger. This provides a complete audit trail visible in the Maintenance tab.

**Schema:** A dedicated `system_activity` table (migration M-008) stores entries with: timestamp, action type, optional hub name, entity reference, user attribution, a JSON changes snippet, and a human-readable detail string. Indices on `occurred_at` and `action_type` for fast queries.

**Retention:** Configurable via `activity_retention_days` in `tanaste_master.json` (default: 60 days). A daily `ActivityPruningService` (BackgroundService) runs every 24 hours and deletes entries older than the retention period.

**API Endpoints:**
- `GET /activity/recent?limit=50` — most recent entries (Admin or Curator)
- `POST /activity/prune` — manual prune trigger (Admin only)
- `GET /activity/stats` — total entry count + retention setting (Admin or Curator)

**Dashboard:** The Maintenance tab displays a vertical timeline of recent activity, each entry with an action-type icon, relative timestamp, detail text, and hub-name chip. A Retention Policy section shows the current setting and offers a manual Prune Now button. Stubs for Library Crawl and Weekly Sync sections are ready for future phases.

**Design decision: New table vs extending `transaction_log`.** The existing `transaction_log` has a synchronous `void Log()` interface used throughout the codebase. A new `ISystemActivityRepository` with async-first `Task LogAsync()` is cleaner and avoids breaking existing callers. Both tables coexist.

**Key types:**
- `SystemActivityEntry` (`Tanaste.Domain.Entities`) — domain entity
- `SystemActionType` (`Tanaste.Domain.Enums`) — string constants (FileIngested, MetadataHydrated, HashVerified, PathUpdated, SyncCompleted, CrawlStarted, CrawlFinished, MetadataRefreshed, SidecarUpdated, ActivityPruned)
- `ISystemActivityRepository` (`Tanaste.Domain.Contracts`) — async-first contract
- `SystemActivityRepository` (`Tanaste.Storage`) — SQLite implementation
- `ActivityPruningService` (`Tanaste.Api.Services`) — daily BackgroundService
- `ActivityEndpoints` (`Tanaste.Api.Endpoints`) — minimal API group

**Why this matters to the business:**
- **Reliability** — Complete audit trail of every automated action the Engine performs.
- **Maintenance** — Configurable retention prevents unbounded table growth; manual prune available for immediate cleanup.
- **Extensibility** — Every future phase (Wikidata hydration, library crawl, weekly sync) writes to this ledger, creating a unified activity stream.

### 3.9 — Universal Metadata Hydrator: Foundation (Phase A)

**Plain English:** This phase lays the groundwork for a complete Wikidata-powered enrichment system. Wikidata is treated as the absolute source of truth — it knows the authoritative Q-identifier for every book, film, person, and franchise, plus 50+ structured properties that link to every major external platform. Phase A builds the configurable property map, expands the data store to hold person social links, and teaches the sidecar XML to carry external bridge identifiers.

**Configurable Wikidata Property Map:**

A `WikidataSparqlPropertyMap` contains the Master Authority Table — 50+ Wikidata property entries across 8 categories: Core Identity, People (Work-scoped), People (Person-scoped), Lore & Narrative, Bridges: Books, Bridges: Movies/TV, Bridges: Comics/Anime, Bridges: Music/Audio, and Social Pivot. Each entry maps a Wikidata P-code (e.g. `P179` → `series`) to a Tanaste claim key with a configured confidence and scope (Work, Person, or Both).

The defaults live in code (`WikidataSparqlPropertyMap.DefaultMap`) and are exported to `config/universe/wikidata.json` on first run. Users can override confidence values, remap claim keys, reorder bridge lookups, or disable properties entirely by editing the universe config file — zero code changes needed. The adapter loads the universe config at runtime and falls back to compiled defaults if the file is missing or corrupt. (See §3.11 for the full configuration architecture.)

Static helpers generate SPARQL queries:
- `BuildWorkSparqlQuery(qid)` — fetches all Work-scoped properties in a single SPARQL query
- `BuildPersonSparqlQuery(qid)` — fetches all Person-scoped properties (including P18 headshot — **Person-only**, never for media items)
- `BuildBridgeLookupQuery(pCode, value)` — finds a QID by bridge identifier (ASIN, ISBN, TMDB ID, etc.)

**Copyright constraint — P18 (Image):** Wikidata P18 (Image) is exclusively for Person entities (author/director headshots from Wikimedia Commons — public figures, not copyrighted). Media cover art is sourced exclusively from Apple Books, Audnexus, and TMDB. The `BuildWorkSparqlQuery` deliberately excludes P18. This constraint is enforced at the SPARQL query level.

**Schema migrations:**
- **M-009:** Rebuilds the `persons` table to expand the role CHECK constraint, adding `Illustrator`, `Cast Member`, `Voice Actor`, `Screenwriter`, and `Composer` to the existing `Author`, `Narrator`, `Director` list. Uses the SQLite table-recreation pattern (PRAGMA foreign_keys=OFF → CREATE new → INSERT INTO → DROP old → RENAME).
- **M-010:** Adds six nullable TEXT columns to `persons`: `occupation` (Wikidata P106), `instagram` (P2003), `twitter` (P2002), `tiktok` (P7085), `mastodon` (P4033), `website` (P856). These power the Social Pivot — direct links to official creator feeds.

**Sidecar XML expansion (v1.1):**
Both Hub-level and Edition-level `tanaste.xml` sidecars now carry a `<bridges>` section that records every external bridge identifier harvested from Wikidata SPARQL:
```xml
<bridges>
  <bridge key="tmdb_id" value="438631"/>
  <bridge key="imdb_id" value="tt1160419"/>
  <bridge key="goodreads_id" value="234225"/>
</bridges>
```
This ensures the library can be reconstructed from the filesystem alone — even external IDs survive a database wipe. Backward-compatible: older v1.0 sidecars with no `<bridges>` section are read without error (empty dictionary).

**Provider lookup expansion:**
`ProviderLookupRequest` now carries bridge hint fields (`AppleBooksId`, `AudibleId`, `TmdbId`, `ImdbId`) and `SparqlBaseUrl` — allowing the Wikidata adapter to resolve QIDs from external IDs and run SPARQL deep-hydration queries.

**New activity ledger entries:**
Four new `SystemActionType` constants log hydrator actions: `BridgeSyncUpdated` (external bridge ID synced), `PersonHydrated` (person enriched with social links), `WeeklySyncStarted` (weekly refresh cycle began), `AffiliateGenerated` (affiliate link built from bridge ID).

**tanaste_master.json configuration additions:**
- `wikidata_property_map` — list of property overrides (P-code, claim key, confidence, enabled)
- `maintenance.weekly_sync_interval_days` (default: 7), `weekly_sync_batch_size` (default: 50), `weekly_sync_batch_delay_ms` (default: 2000)
- `affiliate_settings.amazon_affiliate_tag`, `affiliate_settings.show_affiliate_disclosure` (default: true)

**Key types introduced:**
- `WikidataProperty` (`Tanaste.Providers.Models`) — property descriptor record (PCode, ClaimKey, Category, EntityScope, Confidence, IsBridge, Enabled)
- `WikidataSparqlPropertyMap` (`Tanaste.Providers.Models`) — static property map + SPARQL query builders
- `WikidataPropertyMapOverride` (`Tanaste.Storage.Models`) — JSON-configurable override shape
- `AffiliateSettings` (`Tanaste.Storage.Models`) — affiliate tag configuration

**Why this matters to the business:**
- **Extensibility** — Adding a new Wikidata property is one JSON entry in `tanaste_master.json`. Zero code changes. A single SPARQL query replaces dozens of individual API calls.
- **Reliability** — All 50+ property defaults are compiled into the code. If the config file is missing or corrupt, the defaults still work. Sidecar XML preserves all external IDs on disk.
- **Privacy** — Only titles, ISBNs, and ASINs leave the machine during SPARQL queries. Everything hydrated lives on disk.
- **Maintenance** — The property map is editable via settings. Confidence values, claim keys, and enabled flags can all be changed without touching code.

### 3.10 — Universal Metadata Hydrator: Librarian Workflow (Phase B)

**Plain English:** Phase B replaces the placeholder Wikidata work lookup with a full SPARQL deep-hydration engine. You can now click a button on the Dashboard — or call a single Engine action — and Tanaste reaches out to Wikidata, finds the matching creative work by its bridge identifiers, and pulls back every known property: series name, franchise, characters, narrative location, and dozens of external platform links (TMDB, IMDb, Goodreads, Apple Books, etc.). All of this happens in a single SPARQL query. The Wikidata provider is also now pinned at the top of the Metadata tab as the "Universe Provider" — reflecting its unique role as the one source that spans all media types.

**SPARQL deep-hydration algorithm (WikidataAdapter.FetchWorkAsync):**

The adapter uses a three-step strategy to find and hydrate a work:

1. **QID Cross-Reference via Bridge IDs** — If the library already knows an external identifier (ASIN, ISBN, Apple Books ID, Audible ID, TMDB ID, IMDb ID), the adapter runs a lightweight SPARQL query to find the matching Wikidata Q-identifier. Bridge IDs are tried in priority order; first match wins.

2. **Fallback to Title Search** — If no bridge ID matches, the adapter falls back to the existing MediaWiki `wbsearchentities` API with the work's title. This reuses the same search flow already proven for Person enrichment.

3. **SPARQL Deep Ingest** — Once a QID is found, a single SPARQL query (built by `WikidataSparqlPropertyMap.BuildWorkSparqlQuery`) fetches every Work-scoped property in one call. The response is parsed from `application/sparql-results+json` and each binding is transformed into a `ProviderClaim` with the property map's configured confidence value.

**Value transformation rules:**
- **P577 (year):** ISO dates are reduced to 4-digit year strings
- **P1545 (series position):** Numeric portion extracted from ordinal strings
- **Entity-valued properties:** Wikidata entity URIs are stripped to bare QIDs
- **Multi-valued properties** (characters, cast members): Joined with `"; "`
- **wikidata_qid:** Always emitted at confidence 1.0 as a claim

**Copyright constraint reminder:** P18 (Image) is **never emitted for Work entities** — it is Person-only. The `BuildWorkSparqlQuery` deliberately excludes it. Media cover art comes exclusively from Apple Books, Audnexus, and TMDB.

**Hydration Engine action:**

`POST /metadata/hydrate/{entityId}` — a user-triggered, synchronous action available to Administrators and Curators:
1. Loads existing canonical values as lookup hints (title, ISBN, ASIN, bridge IDs)
2. Resolves Wikidata API and SPARQL endpoint URLs from the manifest
3. Calls the WikidataAdapter directly (not through the background queue — immediate response)
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
- `HydrateResultViewModel` (`Tanaste.Web.Models.ViewDTOs`) — Dashboard DTO for hydration results
- `HydrateResponse` (`Tanaste.Api.Models`) — Engine DTO matching the Dashboard shape

**Why this matters to the business:**
- **Extensibility** — A single SPARQL query now replaces what would have been dozens of individual API calls to different platforms. Adding support for a new Wikidata property is still one JSON entry.
- **Reliability** — The three-step QID resolution (bridge IDs → title search → SPARQL ingest) maximises match rate. If no match is found, the file keeps its existing metadata untouched.
- **Performance** — User-triggered hydration bypasses the background queue for immediate results. The background pipeline continues to handle automatic post-ingestion enrichment.
- **Privacy** — Only titles, ISBNs, ASINs, and bridge IDs are sent to Wikidata. Everything hydrated lives locally.

### 3.11 — Configuration Architecture Standard

**Plain English:** All Engine settings live in a structured `config/` directory as individual JSON files, grouped by concern. This replaces the legacy single-file `tanaste_master.json` approach. The old file is automatically migrated on first run and renamed to `.migrated`.

**Directory layout:**
```
config/
  tanaste.json                    ← Core: paths, schema version, org template
  scoring.json                    ← Scoring: thresholds, decay
  maintenance.json                ← Maintenance: retention, vacuum, sync
  hydration.json                  ← Hydration pipeline: stage timeouts, concurrency,
                                     disambiguation/confidence thresholds (§3.13)
  providers/
    local_filesystem.json         ← Per-provider: weight, enabled, endpoints,
    apple_books.json                 field_weights, throttle_ms, max_concurrency,
                                       hydration_stages, media-type-scoped strategies (§3.13)
    audnexus.json
    open_library.json
    google_books.json
    wikidata.json
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
8. **Fallback resilience** — compiled defaults in `WikidataSparqlPropertyMap.DefaultMap` serve as fallback if config files are missing or corrupt.
9. **Transform registry in code, transform assignment in config** — transform functions are behaviour (live in `ValueTransformRegistry.cs`); which property uses which transform is data (lives in `config/universe/wikidata.json`).
10. **Config directory path** — specified in `appsettings.json` as `Tanaste:ConfigDirectory` (default: `"config"`). Legacy `Tanaste:ManifestPath` is still checked as fallback for backward compatibility.

**Key types:**
- `IConfigurationLoader` (`Tanaste.Storage.Contracts`) — granular config access contract: `LoadCore()`, `LoadScoring()`, `LoadMaintenance()`, `LoadProvider(name)`, `LoadAllProviders()`, generic `LoadConfig<T>(subdirectory, name)`.
- `ConfigurationDirectoryLoader` (`Tanaste.Storage`) — implements both `IConfigurationLoader` (new granular access) and `IStorageManifest` (backward compat). Auto-migrates legacy files; `.bak` rotation on every save.
- `CoreConfiguration`, `ProviderConfiguration` (`Tanaste.Storage.Models`) — settings models.
- `UniverseConfiguration`, `WikidataPropertyConfig`, `BridgeLookupEntry` (`Tanaste.Providers.Models`) — universe knowledge model.
- `ValueTransformRegistry` (`Tanaste.Providers.Models`) — named transform function registry.

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
- `UIGlobalSettings`, `UIFeatureFlags`, `UIShellSettings`, `UIPageSettings` + 3 nested, `UIDeviceProfile` + `UIDeviceConstraints`, `UIProfileSettings`, `ResolvedUISettings` (`Tanaste.Storage.Models`)
- `UISettingsCascadeResolver` (`Tanaste.Storage`) — merges Global+Device+Profile
- `UISettingsCacheRepository` (`Tanaste.Storage`) — SQLite cache CRUD
- `UISettingsEndpoints` (`Tanaste.Api.Endpoints`) — 7 API endpoints including `GET /settings/ui/resolved`
- `DeviceContextService` (`Tanaste.Web.Services.Theming`) — per-circuit scoped; replaced `AutomotiveModeService`
- `ResolvedUISettingsViewModel` + DTOs (`Tanaste.Web.Models.ViewDTOs`) — Dashboard view model

**Adapted components:** MainLayout, NavigationTray, SettingsTabBar (5 layout modes), GeneralTab (conditional sections), Home, HubHero (4 layout variants), BentoGrid (dynamic columns), UniverseStack, PendingFilesAlert (expandable/badge/hidden), ServerSettings (redirect guard), Preferences (conditional links).

**Why this matters to the business:**
- **Extensibility** — Adding a new device class is one JSON file + one entry in the cascade resolver. No code changes in the Dashboard.
- **Maintenance** — All layout rules live in configuration, not scattered CSS breakpoints. A single file controls what an automotive dashboard looks like.
- **Privacy** — Device detection runs client-side. No telemetry or fingerprinting.
- **Reliability** — If the Engine is offline, the Dashboard falls back to compiled web defaults. No blank screens.

### 3.13 — Three-Stage Hydration Pipeline & Review Queue

**Plain English:** When a file arrives in the library, the Engine now runs a three-stage enrichment pipeline instead of the old "first provider wins" approach. Each stage builds on the previous one's results, and ambiguous matches are surfaced to the user via a dedicated review queue rather than being silently dropped.

**The three stages:**

| Stage | Name | What it does | Who runs |
|---|---|---|---|
| **Stage 1** | Retail Match | **ALL** matching providers run (not first-wins). Each provider's claims are persisted independently, and the scoring engine resolves field conflicts. Bridge IDs (ISBN, ASIN, Apple Books ID, etc.) deposited here are read by Stage 2. | Apple Books, Audnexus, Open Library, Google Books — any provider declaring `hydration_stages: [1]` |
| **Stage 2** | Universal Bridge | Wikidata QID resolution via bridge IDs from Stage 1. If multiple QID candidates → review queue entry created, pipeline stops. If single QID → SPARQL deep hydration. | WikidataAdapter (locked, not configurable) + any provider declaring stage 2 |
| **Stage 3** | Human Hub | Person enrichment: extracts author/narrator/director references from canonical values, calls `RecursiveIdentityService` for Wikidata person enrichment. Also runs any providers declaring stage 3 (e.g. Audnexus for narrator details). | Wikidata + Audnexus (stage 3) |

**Post-pipeline confidence check:** After all three stages complete, the pipeline reloads canonical values and computes overall confidence. If below `auto_review_confidence_threshold` (default: 0.60), a review queue entry is created.

**Provider stage assignment:**

Each provider config carries a `hydration_stages` array declaring which stages it participates in:
```json
{
  "name": "audnexus",
  "hydration_stages": [1, 3],
  ...
}
```

Audnexus declares `[1, 3]` because it serves both retail metadata (narrator, series) and person enrichment (narrator details). Wikidata declares `[2, 3]`. All other REST providers declare `[1]`.

**Review Queue:**

The `review_queue` table stores items that need human attention:
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
6. If a QID was selected → `RunSynchronousAsync` with `PreResolvedQid` triggers Stage 2+3
7. Activity ledger records `ReviewItemResolved`
8. SignalR broadcasts `ReviewItemResolved` → badge count decrements

**Image hash validation (cross-cutting):**

Cover art and provider thumbnails are tracked by content hash (SHA-256) in the `image_cache` table to prevent redundant re-downloads. When the same image URL appears across multiple entities, the hash is checked first; if found, the cached file path is reused.

**Pipeline configuration (`config/hydration.json`):**
```json
{
  "stage_concurrency": 3,
  "stage1_timeout_seconds": 30,
  "stage2_timeout_seconds": 45,
  "stage3_timeout_seconds": 30,
  "disambiguation_threshold": 0.7,
  "auto_review_confidence_threshold": 0.60,
  "max_qid_candidates": 5,
  "skip_stage2_without_bridge_ids": false
}
```

**Dual-path architecture:** The existing `MetadataHarvestingService` is preserved for `Person`-type requests from `RecursiveIdentityService`. The new `HydrationPipelineService` handles `MediaAsset`-type hydration. Both paths are safe to run concurrently — person creation is idempotent.

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
- `HydrationStage` (`Tanaste.Domain.Enums`) — `RetailMatch = 1, UniversalBridge = 2, HumanHub = 3`
- `ReviewTrigger`, `ReviewStatus` (`Tanaste.Domain.Enums`) — trigger/status enums
- `ReviewQueueEntry` (`Tanaste.Domain.Entities`) — domain entity
- `HydrationResult` (`Tanaste.Domain.Models`) — pipeline result with per-stage claim counts
- `IHydrationPipelineService` (`Tanaste.Domain.Contracts`) — `EnqueueAsync` + `RunSynchronousAsync`
- `IReviewQueueRepository` (`Tanaste.Domain.Contracts`) — CRUD for review queue
- `IImageCacheRepository` (`Tanaste.Domain.Contracts`) — content-hash image cache
- `HydrationPipelineService` (`Tanaste.Providers.Services`) — three-stage orchestrator
- `ScoringHelper` (`Tanaste.Providers.Services`) — shared claim-persist-score helper
- `ReviewQueueRepository`, `ImageCacheRepository` (`Tanaste.Storage`) — SQLite implementations
- `HydrationSettings` (`Tanaste.Storage.Models`) — pipeline config model
- `ReviewEndpoints` (`Tanaste.Api.Endpoints`) — review queue API
- `ConnectionVaultTab`, `NeedsReviewTab` (`Tanaste.Web.Components.Settings`) — Dashboard settings tabs (PropertyMapperTab and MatchingPipelineTab removed — absorbed into ConnectionVault)
- `ReviewItemViewModel`, `ReviewResolveRequestDto`, `HydrationSettingsDto` (`Tanaste.Web.Models.ViewDTOs`) — Dashboard DTOs

**Why this matters to the business:**
- **Reliability** — Ambiguous matches are surfaced to the user instead of being silently dropped. The review queue ensures no metadata decision is made without confidence.
- **Extensibility** — Adding a new provider to any stage is a one-line JSON change (`hydration_stages: [1, 3]`). The pipeline handles routing automatically.
- **Performance** — Stage 1 runs all providers concurrently. The bounded channel queue (500 items) prevents memory pressure. Image hash caching eliminates redundant downloads.
- **Maintenance** — Pipeline configuration (timeouts, thresholds, concurrency) lives in `config/hydration.json` — zero code changes to tune behaviour. The dual-path architecture preserves backward compatibility with the existing person enrichment flow.
- **Privacy** — Only titles, ISBNs, ASINs, and bridge IDs leave the machine. All review decisions and hydrated data live locally.

### 3.14 — Playback & Streaming Architecture (Target State)

> **Note:** Sections 3.14–3.19 were renumbered when §3.13 (Three-Stage Hydration Pipeline) was inserted.

> **Status:** Not yet implemented. This section describes the target architecture for in-browser media consumption.

**Plain English:** Tanaste becomes a full media server by embedding high-quality players for every media type directly in the browser. Users can read books, watch movies, listen to audiobooks, and browse comics without leaving the Dashboard.

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

### 3.15 — Authentication & Multi-User (Target State)

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

### 3.16 — Transcoding Pipeline (Target State)

> **Status:** Not yet implemented. `IVideoMetadataExtractor` is a stub.

**Plain English:** Tanaste uses FFmpeg to convert video files into formats that every device can play smoothly. It can transcode on-the-fly when you press play, or pre-create mobile-friendly copies overnight so streaming is instant.

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
- Creates lower-bitrate variants (720p H.264 for mobile) stored at `{LibraryRoot}/.tanaste-shadow/{assetId}/{quality}.mp4`
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

### 3.17 — Music Domain Model (Target State)

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

### 3.18 — Interoperability & Ecosystem (Target State)

> **Status:** Not yet implemented.

**Plain English:** Tanaste speaks the languages that other apps already understand. Ebook readers can browse your library via OPDS. Audiobook apps can connect via an Audiobookshelf-compatible interface. External tools get notified via webhooks when new content arrives.

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
- Delivery: HTTP POST with `X-Tanaste-Signature` HMAC-SHA256 header
- Use cases: Discord/Telegram notifications for new content, automation triggers

**Import wizard:**
- Plex: read Plex SQLite DB (`com.plexapp.plugins.library.db`), map sections → Tanaste Hubs, import watched status
- Calibre: read `metadata.db`, import books with existing metadata
- Jellyfin: read NFO sidecar files alongside media, map to Tanaste claims

**PWA capabilities:**
- Web app manifest + service worker for installable experience
- Offline cached shell (app loads without network; content requires Engine)
- Push notifications for new content (via Intercom bridge to Push API)

**Why this matters to the business:**
- **Extensibility** — OPDS is a universal standard. Any ebook reader in the world can connect. This is a major differentiator over Plex/Jellyfin.
- **Reliability** — The import wizard reduces migration friction. Users don't lose their existing watched/read progress.
- **Privacy** — Webhooks are outbound-only and user-configured. No telemetry.

### 3.19 — Browse & Discovery Pages (Target State)

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
4. **Your Universes** (existing) — Bento grid of all Hubs

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

---

## 5. Compliance & Workflow

### 5.1 — License: AGPLv3

> **⚠️ This project is licensed under the GNU Affero General Public License v3.0 (AGPLv3). This cannot be changed without the Product Owner's explicit decision.**

**What this means in practice:**
- AGPLv3 is a "share-alike" license. If Tanaste is ever run as a network service (even privately), any modifications must also be released under AGPLv3.
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

### 5.2 — Mandatory Workflow

Claude must follow every step of this workflow for every piece of work, without exception.

---

**Step 1 — Read before touching anything**

Read `CLAUDE.md` (this file), `README.md`, and every file relevant to the task before proposing changes. Never assume the current state of the code — always verify.

---

**Step 2 — Present the plan and wait for sign-off**

Use the plan format in Section 4.3. Do not write a single line of code until the Product Owner approves.

---

**Step 3 — Assemble and verify**

After writing code, run:
```bash
dotnet build
```
The result must be **0 errors, 0 warnings** before moving to the next step.
Warnings are not acceptable — they indicate future problems and must be fixed immediately.

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
| `tanaste_master.json` | Contains local paths and secrets — gitignored |
| `*.db` | Local database file — user data, not code |
| `bin/`, `obj/` | Assembled output — regenerated automatically |
| `.vs/`, `.idea/` | Editor preferences — personal, not shared |
| `appsettings.*.json` containing real keys | Contains secrets — only example files go in git |

---

## 6. Structure Reference — Feature-Sliced Dashboard Layout

All Dashboard code in `src/Tanaste.Web/` follows the **Feature-Sliced** pattern. Every new piece of UI code must go into the correct slice. Claude must respect this layout and never mix responsibilities between slices.

```
src/Tanaste.Web/
│
├── Services/
│   ├── Integration/          ← ALL communication with the Engine lives here
│   │   ├── ITanasteApiClient.cs        Contract for HTTP calls to the Engine
│   │   ├── TanasteApiClient.cs         Implementation: makes HTTP calls, maps raw JSON
│   │   ├── UniverseStateContainer.cs   Per-session cache: hubs, universe view, progress
│   │   ├── UIOrchestratorService.cs    Orchestrator: bridges HTTP + SignalR + state cache
│   │   ├── UniverseMapper.cs           Maps Engine data → flat Dashboard view model
│   │   └── IntercomEvents.cs           SignalR event shapes (MediaAdded, IngestionProgress, MetadataHarvested, PersonEnriched)
│   │
│   ├── Playback/             ← (TARGET STATE) Playback session management
│   │   └── PlaybackStateService.cs     Active session management, progress sync, audio persistence
│   │
│   └── Theming/              ← ALL visual configuration lives here
│       ├── ThemeService.cs             Dark/light mode, colour palette, corner radii
│       └── DeviceContextService.cs     Per-circuit device class + resolved UI settings
│
├── Components/
│   ├── Universe/             ← Hub-related visual components
│   │   ├── HubHero.razor               "Last Journey" hero: artwork + progress indicators
│   │   ├── ProgressIndicator.razor     Reusable progress card (icon + bar + label)
│   │   └── UniverseStack.razor         "Your Universes" Bento grid (all 1×1 tiles)
│   │
│   ├── Bento/                ← The layout grid system (reusable)
│   │   ├── BentoGrid.razor             CSS grid container (3-col desktop, dock clearance)
│   │   └── BentoItem.razor             Glassmorphic tile: 32px radius, blur(20px)
│   │
│   ├── Navigation/           ← Navigation and search components
│   │   ├── CommandPalette.razor        Ctrl+K global search and navigation
│   │   └── NavigationTray.razor        Content-aware bottom tray: Virtual Libraries + Search
│   │
│   ├── Settings/             ← Settings page tab components (3 groups: Preferences, Metadata, Server)
│   │   ├── SettingsSidebar.razor        Sidebar navigation with search, badges, collapsible sections (defines SettingsSection enum)
│   │   ├── GeneralTab.razor             [Preferences] Appearance: dark/light toggle + accent colour swatches
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
│       ├── Home.razor                  Library overview: Continue Journey + Recently Added + Smart Collections + Universe Grid
│       ├── HubDetail.razor             (TARGET STATE) Hub detail: artwork, works, persons, social pivot
│       ├── WorkDetail.razor            (TARGET STATE) Work detail: editions, metadata, play button
│       ├── PersonDetail.razor          (TARGET STATE) Person detail: headshot, bio, social links, works
│       ├── Statistics.razor            (TARGET STATE) Library + personal stats, charts
│       ├── Login.razor                 (TARGET STATE) Profile selection + PIN/password login
│       ├── Settings.razor              Unified settings: sidebar + content, 16 tabs in 3 groups (Preferences/Metadata/Server)
│       └── NotFound.razor              404 page
│
├── Models/
│   └── ViewDTOs/             ← Data shapes used ONLY by the Dashboard (never shared with Engine)
│       ├── HubViewModel.cs             Hub for display: DisplayName, WorkCount, MediaTypes
│       ├── WorkViewModel.cs            Work for display: Title, Author, Year helpers
│       ├── UniverseViewModel.cs        Flattened cross-media library view + DominantHexColor
│       ├── SystemStatusViewModel.cs    Engine health probe result
│       ├── NavigationConfigViewModel.cs Navigation config models, defaults, JSON helpers
│       ├── ScanResultViewModel.cs      Dry-run scan result (pending file operations)
│       ├── ProviderManagementDtos.cs   Provider test/sample/config DTOs for settings UI
│       ├── ReviewQueueDtos.cs         Review queue + hydration settings DTOs (§3.13)
│       └── ResolvedUISettingsViewModel.cs  Device-resolved UI configuration (8 DTO classes)
│
└── Shared/                   ← Top-level layout shell (used by every page)
    ├── MainLayout.razor                App chrome: glassmorphic AppBar, Intent Dock, dark-mode toggle, review badge on profile avatar
    ├── NavMenu.razor                   Deprecated stub (replaced by Intent Dock + Command Palette)
    └── _Imports.razor                  Namespace imports for all Shared components
```

**Rules for adding new code to the Dashboard:**

| New code type | Where it goes |
|---|---|
| A new call to the Engine | `Services/Integration/TanasteApiClient.cs` + `ITanasteApiClient.cs` |
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
| Repository | [github.com/shyfaruqi/tanaste](https://github.com/shyfaruqi/tanaste) |
| License | AGPLv3 |
| Engine base URL (local dev) | `http://localhost:61495` |
| Dashboard URL (local dev) | `http://localhost:5016` |
