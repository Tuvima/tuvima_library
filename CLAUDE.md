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

The key upgrade over a simple weighted vote: each provider carries a **per-field trust weight** that reflects how reliable it is *for that specific kind of data*. Audnexus is the gold standard for audiobook narrators (weight 0.9) and series data (0.9) but is only moderately trusted for cover art. Wikidata is the definitive authority for franchise identifiers (weight 1.0). Apple Books splits into two provider entries — one for ebooks, one for audiobooks — because its field-weight profile differs between the two domains.

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

**Plain English:** After a file lands in the library, the Engine quietly reaches out to three free online sources — Apple Books, Audnexus, and Wikidata — to fetch better cover art, descriptions, narrator credits, and author portraits. This happens entirely in the background; the file appears on the Dashboard immediately, and the richer information pops in moments later without any page refresh.

**The three zero-key providers (no accounts, no API keys required):**

| Provider | What it contributes | Trust weights |
|---|---|---|
| **Apple Books (Ebook)** | Cover art (600×600), description, rating, title | cover 0.85, description 0.85, rating 0.8, title 0.7 |
| **Apple Books (Audiobook)** | Cover art (600×600), description, rating, title | same profile, separate provider ID |
| **Audnexus** | Narrator, series, series position, cover art, author | narrator/series/cover/series_pos 0.9, author 0.75 |
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

**Key architectural rules for this subsystem:**
- `Tanaste.Ingestion` has **zero new project references**. All interfaces (`IMetadataHarvestingService`, `IRecursiveIdentityService`, `IMetadataClaimRepository`, `ICanonicalValueRepository`) live in `Tanaste.Domain.Contracts` — which Ingestion already references.
- All provider base URLs live in `provider_endpoints` in `tanaste_master.json`. Changing a URL requires only a config edit, never a recompile.
- All trust weights live in the `field_weights` section per provider in `tanaste_master.json`. The engine picks them up automatically.
- Provider GUIDs are stable hardcoded constants in each adapter class (not looked up from the DB at runtime) so new `MetadataClaim` rows can be written without a DB round-trip.
- Throttle rules: Apple Books — 300ms between calls; Wikidata — 1100ms between calls (Wikidata's 1 req/sec policy). Audnexus has no throttle.
- Audnexus requires an ASIN. If none is present in the file's metadata, the adapter short-circuits immediately (no HTTP call made) and returns an empty claim list.

**Why this matters to the business:**
- **Reliability** — Providers are never in the critical path. A failed network call returns an empty list; the file remains in the library with its local metadata intact.
- **Performance** — The harvest queue is non-blocking. File ingestion completes in milliseconds regardless of network conditions.
- **Maintenance** — Adding a new provider is one new class implementing `IExternalMetadataProvider` plus one JSON entry. No existing code changes.
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
│   └── Theming/              ← ALL visual configuration lives here
│       └── ThemeService.cs             Dark/light mode, colour palette, corner radii
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
│   │   └── IntentDock.razor            Floating bottom dock: Hubs / Watch / Read / Listen
│   │
│   ├── Settings/             ← Settings page tab components
│   │   ├── SettingsTabBar.razor       Icon-based tab navigation strip (defines SettingsTab enum)
│   │   ├── GeneralTab.razor           Appearance: dark/light toggle + accent colour swatches
│   │   ├── FoldersTab.razor           Watch Folder + Library Folder configuration
│   │   ├── ProvidersTab.razor         Enriched provider cards: domain, tags, weights, reachability
│   │   └── SecurityTab.razor          Guest API Keys: generate, revoke, copy-to-clipboard
│   │
│   └── Pages/                ← Full-page views (routed)
│       ├── Home.razor                  Library overview page
│       ├── Settings.razor              Tab-shell: delegates to Settings/* components
│       └── NotFound.razor              404 page
│
├── Models/
│   └── ViewDTOs/             ← Data shapes used ONLY by the Dashboard (never shared with Engine)
│       ├── HubViewModel.cs             Hub for display: DisplayName, WorkCount, MediaTypes
│       ├── WorkViewModel.cs            Work for display: Title, Author, Year helpers
│       ├── UniverseViewModel.cs        Flattened cross-media library view + DominantHexColor
│       ├── SystemStatusViewModel.cs    Engine health probe result
│       └── ScanResultViewModel.cs      Dry-run scan result (pending file operations)
│
└── Shared/                   ← Top-level layout shell (used by every page)
    ├── MainLayout.razor                App chrome: glassmorphic AppBar, Intent Dock, dark-mode toggle
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
| A new layout wrapper | `Shared/` |
| A new visual theme setting | `Services/Theming/ThemeService.cs` |

---

## 7. Project Contacts

| Role | Detail |
|---|---|
| Product Owner | Shaya |
| Repository | [github.com/shyfaruqi/tanaste](https://github.com/shyfaruqi/tanaste) |
| License | AGPLv3 |
| Engine base URL (local dev) | `http://localhost:61495` |
| Dashboard URL (local dev) | `http://localhost:5016` |
