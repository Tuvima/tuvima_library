# CLAUDE.md — Tuvima Library Project Memory

> **Who reads this file?**
> Every Claude session working on this repository reads this file automatically before doing anything else.
> It is the single, authoritative source of truth for what Tuvima Library is, how it is built, and how to work on it.
> It bridges the Product Owner's business goals with the technical team's execution.

> **Companion documents.** [`AGENTS.md`](AGENTS.md) is a concise, developer-first code tour — read it for a fast repository map. `docs/architecture/*.md` holds the deep dives per subsystem. This file is a summary that points out to both.

---

## 1. Project Overview

### What is Tuvima Library?

**Tuvima Library** is the product name; **Tuvima** is the company. Code namespaces use `MediaEngine.*` intentionally — decoupled from branding for future resilience. In this repo the two names refer to the same product.

The core philosophy is **Presentation** — the act of bringing something forward and making it whole. Tuvima does not create a library. It presents one. The stories already exist on the hard drive, fragmented across formats and folders; the Library's job is to find them, understand them, unify them, and surface the result as something coherent and beautiful.

Every feature exists in service of that word:
- The **Intelligence Engine** works invisibly so the library is already whole when you look at it.
- The **Universe** is the act of presentation made structural — the book, film, and audiobook of the same story brought forward as one.
- The **Cinematic Dashboard** is the presentation layer made visible.

> **All future sessions must preserve this creative context.** When writing copy, naming features, or explaining the product, the Presentation philosophy is the frame.

### Quality Gates and Regression Rules

- Do not recreate the old Vault workflow. No new `/vault` routes, `LibraryPage`, `LibrarySurfacePreset`, Vault navigation labels, Vault docs as current product behavior, or all-in-one media correction workbenches.
- Normal media fixes belong inline on the media surface where the issue appears. Use `MediaEditorLauncherService` and `SharedMediaEditorShell` for Normal, Review, and Batch editing modes.
- Review Queue is the exception workflow for blocked, uncertain, low-confidence, or unresolved items. Settings/Admin is for configuration and operational state, not a normal media correction workspace.
- Use `IDatabaseConnection.CreateConnection()` for normal repository, read-service, endpoint, background-job, and request-path database work. Dispose each short-lived connection with `using`.
- `IDatabaseConnection.Open()` is startup/schema/integrity-only. New uses outside `DatabaseConnection`, Engine startup, or explicitly documented test fixtures should fail guardrail tests.
- Avoid silent `catch { }` blocks. Best-effort failures need a justification comment or an explicit guardrail allowlist entry; user-visible failures need logging and degraded/error UI.
- Domain must stay independent of Web, API, Storage, Providers, Ingestion, Processors, AI, and UI packages. UI should consume view models/contracts/typed clients, not storage implementation models.
- Razor components must not contain direct SQL. API endpoints should move SQL-heavy behavior into repositories/read services when touched.
- When product concepts, navigation, editing flows, database lifecycle, Docker startup, or CI checks change, update README/docs/AGENTS/CLAUDE and relevant `.agent` guidance in the same change.
- Before finalizing code changes, run at minimum `dotnet restore MediaEngine.slnx`, `dotnet build MediaEngine.slnx --no-restore`, and `dotnet test MediaEngine.slnx --no-build`. Run docs, Docker, format, and dependency checks when those areas are touched.

### What it does

Tuvima is a unified media intelligence platform that runs entirely on the user's machine — no cloud account, no subscription, no data leaving the home. Point it at a hard drive, and it automatically:

1. **Watches** folders for new files (books, audiobooks, comics, TV shows, movies, music).
2. **Fingerprints** each file so it can be tracked across moves and renames.
3. **Reads embedded metadata** (title, author, year, cover art, series) and applies a Priority Cascade to pick the most trustworthy version of each field.
4. **Groups files into Universes and Series** that link all versions of the same story across media types.
5. **Serves a Blazor dashboard** for browsing, search, playback, and management.
6. **Broadcasts instant updates** to the dashboard via SignalR.

### The Grouping Model — Universes and Series

A **Universe** is a creative world — the books, films, audiobooks, comics, and music that belong together because their metadata says so. A **Series** is a sub-grouping within a Universe — a specific sequence or collection of related works.

Matching is automatic: when the Engine discovers that a novel, its film adaptation, and an audiobook share the same author, franchise identifiers, or Wikidata Q-identifier, it groups them into the same Universe. Users browse by creative world, not by file type.

> *Example: The "Dune" Universe might contain:*
> - *The "Dune Novels" Series — Frank Herbert's novels (EPUB)*
> - *The "Dune Films" Series — Denis Villeneuve adaptations (MP4)*
> - *The audiobook narrations (M4B)*
> - *The graphic novels (CBZ)*
>
> *Linked by shared author, franchise QID, and series identifiers — not shared filename.*

A Series is not limited to a numbered sequence. While it often represents a book series or film franchise, it is a flexible virtual container for *any* creative grouping — spin-off works, thematic collections, or cross-media narrative links.

### Terminology — User-Facing vs Internal

| Level | User-facing | Internal code | Example |
|---|---|---|---|
| Entire library | **Library** | Library | Everything you own |
| Franchise grouping | **Universe** | ParentCollection | Dune, Marvel, Tolkien |
| Series / collection | **Series** | Collection | Dune Novels, Dune Films |
| Single title | **Work** | Work | Dune Part One |
| Specific version | **Edition** | Edition | 4K HDR Blu-ray Remux |
| File on disk | **Media Asset** | MediaAsset | the .mkv file |

> **Rule:** Anything the user sees uses the **user-facing name**. Internal code names stay in the domain/engine layer only.

The hierarchy:

```
Library (your entire collection)
  └── Universe (franchise/creative world — e.g. "Dune")
        └── Series (sub-grouping — e.g. "Dune Novels")
              └── Work (one title — e.g. "Dune Part One")
                    └── Edition (one physical version — e.g. "4K HDR Blu-ray Remux")
                          └── Media Asset (one file on disk)
```

Universes are optional. A Series that belongs to no larger franchise sits directly under the Library. Both Universes and Series are resolved at metadata-scoring time by the Intelligence Engine — they have no presence on the filesystem.

### Who is it for?

A single power user who wants complete, private control over a large media collection — without depending on services like Plex, Jellyfin, or any subscription platform.

---

## 2. Technical Stack

> When speaking to the Product Owner, always use the plain-English column.

### Core Tools

| Plain-English name | Technical name | Purpose |
|---|---|---|
| Programming language | C# / .NET 10 | All Engine and Dashboard code |
| Local database | SQLite | Single file storing the library catalogue |
| Data access layer | Dapper | Maps database rows to C# objects |
| Visual interface library | MudBlazor 9 | Pre-built UI building blocks |
| Real-time intercom | SignalR | Pushes live updates to the dashboard |
| Book file reader | VersOne.Epub | Reads EPUB contents |
| Audio/video tagging | TagLibSharp | Reads/writes ID3, MP4, FLAC, Vorbis tags |
| Transcoding / FFmpeg wrapper | Xabe.FFmpeg | Video/audio stream inspection and extraction |
| Image rendering | SkiaSharp | Cover art thumbnails, hero banners |
| Engine API documentation | Swashbuckle | Interactive API menu at `/swagger` |
| Structured logging | Serilog | Rolling log files |
| Resilient HTTP calls | Polly (Microsoft.Extensions.Http.Resilience) | Retries with backoff |
| Wikidata client | Tuvima.Wikidata | Reconciliation, entity fetching, Wikipedia, in-memory graph |
| Cron scheduling | Cronos | Background task timing |
| AI text inference | LLamaSharp | Local LLM with GBNF grammar constraints |
| AI audio inference | Whisper.net | Local speech-to-text and language detection |
| Automated quality checks | xUnit + coverlet | Tests |
| Version control | Git + GitHub | Code history |

See `Directory.Packages.props` for the authoritative dependency list.

### Headless Design — Engine and Dashboard are Separate

The Engine and Dashboard are two independent apps that communicate over HTTP + SignalR.

| Part | Technical project | Role |
|---|---|---|
| The Engine | `MediaEngine.Api` | Intelligence, data, file operations, API surface. Main runtime composition root. |
| The Dashboard | `MediaEngine.Web` | Blazor Server browser interface, client of the Engine |
| Standalone worker | `MediaEngine.Ingestion` | A worker host that can run the ingestion pipeline on its own; not the main runtime path today |

### Source Code Layout

| Folder | What it is | Role |
|---|---|---|
| `src/MediaEngine.Domain` | The Rulebook | Aggregates, entities, enums, constants, contracts. Pure business language. |
| `src/MediaEngine.Storage` | The Filing Clerk | SQLite repositories, embedded schema bootstrap, startup migrations, config loading. |
| `src/MediaEngine.Intelligence` | The Analyst | Priority Cascade, scoring, fuzzy matching, identity strategies. |
| `src/MediaEngine.Processors` | The Scanner | Reads EPUB, audio, video, comic, and generic files for embedded metadata. |
| `src/MediaEngine.Providers` | The Research Team | External metadata providers, hydration pipeline workers, reconciliation. |
| `src/MediaEngine.Ingestion` | The Mail Room | Folder watchers, debounce, hashing, dedup, file organization. |
| `src/MediaEngine.AI` | The Brain | Local model lifecycle, hardware benchmarking, AI features. |
| `src/MediaEngine.Identity` | The Front Desk | Profiles, roles, multi-user rules. Small but important. |
| `src/MediaEngine.Api` | The Reception Desk | Composition root: API endpoints, hosted services, SignalR, health checks. |
| `src/MediaEngine.Web` | The Showroom | Blazor Server dashboard. |
| `tests/` | The Quality Inspector | One test project per source project. |

---

## 3. Architecture Summary

> **Detail docs** live in `docs/architecture/*.md`. Each subsection below is a short summary — read the linked detail doc when working on a subsystem.

### 3.1 — Ingestion Pipeline
**Detail:** [`docs/architecture/ingestion-pipeline.md`](docs/architecture/ingestion-pipeline.md)

Configured Library Folders tell the Engine where to look and what media to expect. Files go through Settle → Lock Check → Fingerprint → Scan → Identify → Stage 1 retail match → Move to organised library (if a title is found) → Stage 2 Wikidata enrichment. Items surface in the UI with a `RetailMatched` status as soon as Stage 1 produces a title — a QID is not required. The database tracks every status transition; rejected files land under `.data/staging/rejected/`. Work-level deduplication ensures duplicate files create new Editions under an existing Work instead of creating new Works. Config: `config/libraries.json`, `config/disambiguation.json`.

### 3.2 — Priority Cascade Engine
**Detail:** [`docs/architecture/scoring-and-cascade.md`](docs/architecture/scoring-and-cascade.md)

When sources disagree, a four-tier cascade resolves the dispute: **Tier A** (user locks) → **Tier B** (per-field provider priority) → **Tier C** (Wikidata authority) → **Tier D** (highest confidence). AI improves matching quality (SmartLabeler, QidDisambiguator) but Wikidata remains the canonical authority. All claims are append-only; history is never lost. `IdentityDecisionService` (in `Intelligence/Services`) centralises accept/review/retry decisions using a `ConfidenceBand` (Exact / Strong / Provisional / Ambiguous / Insufficient) and a `ReviewRootCause`. Media-type-specific identity logic lives in `Intelligence/Strategies/` as `IMediaTypeIdentityStrategy` implementations. Config: `config/scoring.json`, `config/field_priorities.json`.

### 3.3 — Security
**Detail:** [`docs/architecture/security.md`](docs/architecture/security.md)

Every endpoint requires authentication except `/system/status` and localhost (when bypass is enabled). Three roles: Administrator (full), Curator (browse + metadata), Consumer (browse only). API keys are labelled and individually revocable. Rate-limiting policies (`key_generation`, `streaming`, `general`) are configured in `config/core.json`. Path traversal protection guards folder endpoints; the SignalR hub requires auth.

### 3.4 — Dashboard UI
**Detail:** [`docs/architecture/dashboard-ui.md`](docs/architecture/dashboard-ui.md)

Dark-mode-only cinematic design with an ambient gradient background. The Dashboard is a set of dedicated surfaces rather than one command page:

- `/` — **LibraryBrowsePage** (home / discovery landing)
- `/read`, `/read/{Tab}` — ReadPage (books + audiobooks)
- `/watch`, `/watch/{Tab}`, `/watch/movie/{WorkId}`, `/watch/tv/show/{CollectionId}/...`, `/watch/player/{AssetId}` — Watch surfaces
- `/listen`, `/listen/music/...`, `/listen/audiobooks`, `/listen/audiobook/{WorkId}` — Listen surfaces
- `/collections`, `/collection/{Id}` — Collections browse + detail
- `/book/{Id}`, `/person/{Id}` — detail pages
- `/universe/{Qid}/explore` — Chronicle Explorer (Cytoscape graph)
- `/search` — global search
- `/settings`, `/settings/{Section}` — Settings shell (review queue lives at `/settings/review`)

Real-time SignalR updates push pipeline progress into every surface. Theming is fixed dark with a golden amber accent (`#C9922E`).

### 3.5 — Brand Assets

Three official SVG logo files. **Never replace logo placements with hand-written text.**

| File | Location | Use when… |
|---|---|---|
| `tuvima-logo.svg` | `wwwroot/images/`, `assets/images/` | Full horizontal logo — mark + "TUVIMA" wordmark |
| `tuvima-icon.svg` | `wwwroot/images/`, `wwwroot/favicon.svg`, `assets/images/` | Square icon mark only — favicon, app icon |
| `tuvima-hero.svg` | `assets/images/` | Mark + wordmark + subtitle — README hero and marketing |

All SVGs are designed for dark backgrounds. Source files live outside the repo at `C:\Users\shaya\OneDrive\Documents\Projects\Tuvima\Graphics\`.

### 3.6 — Centralized Data Directory (`.data/`)

All Engine-managed artefacts live under a single `.data/` directory at the library root:

```
{LibraryRoot}/.data/
  database/library.db
  images/
    {QID}/                 ← cover.jpg, hero.jpg, cover-thumb.jpg
    _pending/{GUID12}/     ← items awaiting QID
    people/{QID}/          ← headshot.jpg + thumb (lazy-created)
  staging/
    {assetId12}/           ← in-flight assets
    rejected/              ← explicitly rejected files
```

`ImagePathService` (Domain layer, singleton) is the single source of truth for image paths. Thumbnails are SkiaSharp-generated JPEGs (200px wide, quality 75). User-uploaded images are flagged and protected from orphan sweeps.

### 3.7 — Hydration Pipeline & Providers
**Detail:** [`docs/architecture/hydration-and-providers.md`](docs/architecture/hydration-and-providers.md)

A durable, two-stage enrichment pipeline runs after ingestion. `identity_jobs` rows (SQLite) replace any in-memory queue. Three pipeline workers poll for jobs:

- **`RetailMatchWorker`** — Stage 1. Retail providers score candidates through `RetailMatchScoringService`. Scores ≥0.85 auto-accept, 0.50–0.85 accept-with-review, below 0.50 routes to review.
- **`WikidataBridgeWorker`** — Stage 2. Uses retail bridge IDs (ISBN, ASIN, TMDB ID, etc.) to resolve a QID. A batch gate holds Stage 2 until Stage 1 finishes for the run.
- **`QuickHydrationWorker`** — Populates canonical values, hero images, collection assignment.

`SynchronousIdentityPipelineService` provides an inline implementation for synchronous callers.

Enrichment is modular. `EnrichmentService` dispatches to dedicated workers: `CoverArtWorker`, `PersonEnrichmentWorker`, `ChildEntityWorker`, `FictionalEntityWorker`, `DescriptionEnrichmentWorker`, plus `ImageEnrichmentService` for Fanart.tv imagery. `PostPipelineService` auto-resolves stale review items when confidence improves.

Every retail candidate and Wikidata candidate is persisted (`retail_match_candidates`, `wikidata_bridge_candidates`) so the Review drawer can show full score breakdowns. Provider behaviour is driven by JSON config — adding a REST+JSON provider is a zero-code operation. See [`docs/reference/providers.md`](docs/reference/providers.md).

### 3.8 — Configuration Architecture

All settings live in `config/` as individual JSON files grouped by concern. Provider secrets (API keys) go in `config/secrets/` (gitignored).

| File / folder | What it controls |
|---|---|
| `core.json` | Core paths, rate limits, language preferences, batch gate, maintenance schedules |
| `libraries.json` | Watch folders, media-type hints, organization templates |
| `providers/*.json` | One file per provider; includes language strategy |
| `providers/wikidata_reconciliation.json` | All Wikidata config — reconciliation, edition pivot, data extension, child entity discovery |
| `ai.json` | Local models, feature toggles, cron schedules |
| `pipelines.json` | Ranked Stage 1 provider pipelines per media type (Waterfall / Cascade / Sequential) |
| `scoring.json`, `field_priorities.json` | Metadata priority |
| `ui/palette.json`, `ui/global.json`, `ui/devices/*`, `ui/profiles/*` | UI theming and device profiles |
| `writeback.json`, `writeback-fields.json` | Tag write-back rules |
| `transcoding.json`, `media_types.json`, `narration/phrases.json` | Additional runtime settings |

Before changing C# for behaviour that looks configurable, check `config/` first.

### 3.9 — Universe Graph & Chronicle Engine
**Detail:** [`docs/architecture/universe-graph.md`](docs/architecture/universe-graph.md)

Builds a relationship graph connecting characters, locations, factions, and works. Entities and relationships live in SQLite; `Tuvima.Wikidata.Graph` provides in-memory graph queries. Person infrastructure includes biographical data, social links, pseudonym resolution, and character-performer links. Roles: Actor, Voice Actor, Performer, Artist, Composer, Author, Director, Narrator. The Chronicle Engine adds temporal qualifiers, Lore Delta detection, and era-correct actor detection. The Chronicle Explorer at `/universe/{Qid}/explore` visualises the graph with Cytoscape.js.

### 3.10 — Local AI Intelligence Layer
**Detail:** [`docs/architecture/ai-integration.md`](docs/architecture/ai-integration.md)

AI is a core function, not an add-on. Model roles: **text_fast** (on-demand, small), **text_quality** (batch, larger), **audio** (Whisper transcription + language detection), and **text_cjk** (loaded when the user has CJK languages in their preferences). Features span Ingestion (Smart Labeling, Media Type Classification), Alignment (QID Disambiguation, Series Alignment), Enrichment (Vibe Tags, TL;DR, Audio Similarity), Syncing (Immersive Bake, Subtitle Sync), Personalization (Taste Profiling, "Why" Factor), and Discovery (Intent Search). GBNF grammar constraints force valid JSON output. AI improves matching; the Priority Cascade determines canonical values. Config: `config/ai.json`.

### 3.11 — Settings

Settings at `/settings/{Section}` is the Dashboard's operational command centre. It's a two-row tab shell: a primary group-tabs row and a secondary section-tabs row. `SettingsNav` resolves routes and filters visibility by role. Current sections:

| Group | Sections |
|---|---|
| **Overview** | Overview, Review |
| **Preferences** | Profile, Playback |
| **Library** | Folders |
| **Providers** | Providers, Wikidata |
| **Intelligence** | Models, Features, Vocabulary, Schedule |
| **Server** | System, Security, Users, Activity, Maintenance, Setup |

Navigation is URL-driven: `/settings/review` deep-links straight into the review queue.

### 3.12 — Review Queue (inside Settings)

The Review Queue is the Engine's safety net for uncertain matches. It lives at `/settings/review` and is rendered by `SettingsReviewQueueTab`. It opens the shared media editor in review mode for blocked or uncertain items.

The queue surfaces items that need human attention: failed retail matches, ambiguous Wikidata candidates, low-confidence matches, missing titles, and items that fell through during enrichment. Review rows can launch the shared editor in review mode, dismiss an item, skip universe matching where supported, or retry/resolve according to existing Engine rules. `PostPipelineService` auto-resolves queue items when a later enrichment pass pushes confidence above threshold.

> **Historical note:** An earlier `/vault` page concept combined browsing, management, and review into one 9-tab surface. That page has been **deprecated**. Browsing now lives on the media surfaces (`/`, `/read`, `/watch`, `/listen`, `/collections`) and review lives inside Settings. Do not add new Vault routes, components, docs, or navigation. Normal media correction belongs inline on media pages and details, using `MediaEditorLauncherService` and `SharedMediaEditorShell`; Review uses the same editor in review mode.

### 3.13 — Universal Parameterized Collection System
**Detail:** [`docs/architecture/collections.md`](docs/architecture/collections.md)

Every collection is a parameterised query container. Normalised filter predicates are stored as JSON arrays of `{field, op, value}` objects in the `rule_json` column; `CollectionRuleEvaluator` (Storage) translates predicates to SQL. Six collection types as presentation hints:

- **ContentGroup** — drillable containers (albums, TV shows, book series)
- **Smart** — auto-generated from library data (by genre, author, director, decade, etc.)
- **System** — per-user, pre-created (Reading List, Watchlist, Favorites, etc.)
- **Mix** — AI-generated per-user (Continue, Heavy Rotation, Discovery Queue, etc.)
- **Playlist** — user-created, materialised
- **Custom** — user-created, query-resolved via the collection builder

Resolution is hybrid: query-resolved collections evaluate predicates at display time; materialised collections track membership in `collection_works`. System view collections (seeded at startup) drive container views for media types. `CollectionAssignmentService` (called by `QuickHydrationWorker`) reads Wikidata series / franchise / universe QIDs and assigns works to a ContentGroup collection via the `collection_id` FK. `collection_placements` maps collections to UI locations. The Collections page at `/collections` lets the user browse, create, and manage all collection types.

### 3.14 — Localization & Multi-Language Support

Six language concerns addressed across six phases (all implemented): UI language, metadata display language, content language, provider query language, AI working language, search language. `CoreConfiguration.Language` is a structured `LanguagePreferences` object (Display / Metadata / Additional / AcceptAny). UI localisation uses `IStringLocalizer<SharedStrings>` with .resx files for English, French, German, Spanish. Wikidata searches run in both the file's detected language and the metadata language, deduplicating by QID. FTS5 search uses a `trigram` tokenizer for CJK support. Provider adapters support per-provider `language_strategy` (`source` / `localized` / `both`). See [`docs/guides/language-setup.md`](docs/guides/language-setup.md).

### 3.15 — Target State Features
**Detail:** [`docs/architecture/target-state.md`](docs/architecture/target-state.md)

Not yet implemented: full Authentication & Multi-User (PIN/password, parental controls), a full Transcoding Pipeline (Shadow Transcoder), a deeper Music Domain Model (MusicBrainz, richer `MusicProcessor`), full Interoperability (OPDS 1.2, Audiobookshelf API, webhooks, import wizard, PWA), and advanced Browse & Discovery pages (UniverseDetail, Statistics). Local profiles exist, and the Dashboard persists an active browser profile selection for role-aware navigation.

### 3.16 — Supported Library Types

| Library Type | Includes |
|---|---|
| **Books** | Ebooks (EPUB, PDF) + Audiobooks (M4B, MP3) |
| **TV** | Episodic television, web series |
| **Movies** | Feature films, short films |
| **Music** | Albums, singles, tracks |
| **Comics** | CBZ, CBR, PDF comics, manga |

Future: **Other** (YouTube, lectures — manual tagging), **Photos** (separate product scope).

---

## 4. Product Owner Communication Rules

> Claude must apply these rules in every single message to the Product Owner. There are no exceptions.

### 4.1 — Mandatory vocabulary

Always use the plain-English term. Never use the technical term in conversation.

| Never say | Always say |
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
| Collection (internal) | Series |
| ParentCollection (internal) | Universe |

### 4.2 — Always explain the "Why" in business terms

Every technical choice must be justified using one or more of these business goals:

| Goal | Meaning |
|---|---|
| **Maintenance** | Makes the product easier and cheaper to change in the future |
| **Extensibility** | Allows new features or external tools to be added without breaking existing ones |
| **Privacy** | Keeps user data on the user's machine; nothing leaves without explicit action |
| **Reliability** | Reduces the chance of errors or data loss |
| **Performance** | Makes the product faster or more responsive for the user |

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

 Plain English Summary:
   [2–4 sentences a non-technical person can read to understand
    what will change and why, written as if explaining to someone
    who has never seen the code]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Do not proceed until the Product Owner says "go ahead" or equivalent.**

**Always ask detailed questions before finalising the plan.** Do not assume intent — probe for specifics: which media types are affected, what the user expects to see on-screen, edge cases, whether existing behaviour should change, and how the feature interacts with other parts of the system. A plan built on assumptions will be rejected; a plan built on answers will be approved.

**Plain English Summary is mandatory** — every plan and every completed task must end with a plain English summary that a non-technical person can understand. This is not optional.

### 4.4 — Error reporting

When the build fails or a quality check breaks, Claude must:
1. Say **what went wrong** in plain English (no error codes as the primary explanation).
2. Say **what will be done** to fix it.
3. Fix it immediately — errors during an approved task do not need a second sign-off.

### 4.5 — Honest about uncertainty

Never guess silently. If Claude is unsure about an approach, it must say so:
> *"I'm not certain which approach is best here — I see two options. Here's the trade-off: [explain]. Which matters more to you?"*

### 4.6 — Model Adherence & Delegation

This project uses a two-tier model strategy to balance quality with speed.

**Opus** handles planning, architectural decisions, task decomposition, code review, resolving ambiguity escalated by Sonnet agents, and project documentation (`CLAUDE.md`, `AGENTS.md`, `MEMORY.md`, `.agent/`).

**Sonnet** handles implementation and coding tasks delegated by Opus, file modifications with clear scoped instructions, and build verification (`dotnet build`) after each unit of work.

**Delegation rules:**
1. When a task involves both planning and coding, Opus must break down the work first, then dispatch coding subtasks to Sonnet agents with clear, scoped instructions — including all necessary context (file paths, exact changes, integration points, verification steps).
2. Sonnet agents must not make architectural decisions autonomously. If scope is ambiguous or requirements unclear, escalate to Opus.
3. Independent units should be dispatched in parallel. Dependent units must wait.
4. Each Sonnet agent receives a complete handoff spec. The agent should not need to explore the codebase.
5. If a Sonnet agent encounters a build failure it cannot resolve, it escalates to Opus with full error context.

---

## 5. Compliance & Workflow

### 5.1 — License: AGPLv3

> **This project is licensed under AGPLv3. This cannot be changed without the Product Owner's explicit decision.**

**Every new tool added must have a compatible license.** Claude must check before adding anything.

| License | Compatible? | Notes |
|---|---|---|
| MIT | Safe | Most common |
| Apache 2.0 | Safe | Used by many Microsoft packages |
| BSD 2/3-clause | Safe | Permissive |
| LGPL v2.1 / v3 | Safe | Common for libraries |
| GPL v3 / AGPL v3 | Safe | Same family |
| GPL v2 (no "or later") | Check | May be incompatible — ask first |
| SSPL | Block | Not OSI-approved |
| Commons Clause | Block | Restricts commercial use |
| Proprietary | Block | Incompatible |

**Current approved tools (selected):** Microsoft.Data.Sqlite (MIT), MudBlazor 9 (MIT), VersOne.Epub (MIT), TagLibSharp (LGPL-2.1), Xabe.FFmpeg (MIT), SkiaSharp (MIT), Swashbuckle (MIT), xUnit 2 (Apache 2.0), coverlet (MIT), Serilog (Apache 2.0), Polly / Microsoft.Extensions.Http.Resilience (MIT), Dapper (Apache 2.0), LLamaSharp (MIT), Whisper.net (MIT), Cronos (MIT), FuzzySharp (MIT), Cytoscape.js (MIT, vendored), Tuvima.Wikidata (MIT), MkDocs (BSD-2-Clause), Material for MkDocs (MIT).

See `Directory.Packages.props` for the authoritative dependency list.

### 5.2 — Mandatory Workflow

**Step 1 — Read before touching anything**
Read `CLAUDE.md`, `AGENTS.md`, `README.md`, and every file relevant to the task. Never assume current state.

**Step 2 — Present the plan and wait for sign-off**
Use the plan format in §4.3 (including Plain English Summary). Do not code until approved.

**Step 3 — Assemble and verify**
```bash
dotnet build
```
Result must be **0 errors, 0 warnings**. After all parallel agents complete:
```bash
taskkill //F //IM dotnet.exe
```

**Step 4 — Update documentation**

| Document | Update when… |
|---|---|
| `README.md` | Feature changes install/config/usage |
| `CLAUDE.md` §3 or `docs/architecture/*.md` | Architecture changes |
| `AGENTS.md` | Repository map / startup / high-level code tour changes |
| `CLAUDE.md` §5.1 | New dependency approved |
| `MEMORY.md` | New architectural decision |
| `docs/**/*.md` | Feature, config, API, schema, or UI changes |

**Step 5 — Commit and push**
```bash
git add <specific files>
git commit -m "Short summary

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
git push
```

**Never commit:** `tuvima_master.json`, `*.db`, `bin/`, `obj/`, `.vs/`, `.idea/`, `appsettings.*.json` with real keys, `.codex/`, `site/`.

### 5.3 — Cross-Agent Synchronization

Two AI assistants work on this repository:
- **Claude Code** — reads `CLAUDE.md` (this file) as its canonical source of truth.
- **Antigravity (Gemini)** — reads files under `.agent/`.
- Both should be able to start from `AGENTS.md` for a quick code tour.

`CLAUDE.md` is the canonical source. The `.agent/` directory contains supplementary files that must stay in sync. `.agent/SYNC-MAP.md` contains the reverse mapping from `.agent/` files to `CLAUDE.md` sections.

> **Naming note:** Older `.agent/` notes may still mention the retired Library Vault concept as historical context. Current work uses the surface-per-concern layout: `LibraryBrowsePage`, `ReadPage`, `WatchPage`, `ListenPage`, `Collections`, and Settings (with the review queue at `/settings/review`). Do not add new Vault routes, components, docs, or navigation.

### 5.4 — Documentation Upkeep (Diátaxis)

> **All project documentation follows the [Diátaxis framework](https://diataxis.fr/).** The `docs/` directory is organised into Tutorials, How-to Guides, Reference, and Explanation. The landing page is `docs/index.md`.

Documentation must be updated when:

| Trigger | What to update |
|---|---|
| New feature added | Relevant Explanation page + Reference entries + How-to Guide if user-facing |
| New config field added | `docs/reference/configuration.md` |
| New API endpoint added | `docs/reference/api-endpoints.md` |
| New database table or column | `docs/reference/database-schema.md` |
| New file format supported | `docs/reference/media-types.md` |
| New provider added | `docs/reference/configuration.md` + `docs/reference/providers.md` + `docs/guides/configuring-providers.md` |
| New processor added | `docs/reference/media-types.md` + `docs/guides/writing-a-processor.md` |
| Terminology change | `docs/reference/glossary.md` |
| Architecture change | `docs/architecture/*.md` + corresponding Explanation page |
| New UI surface or Settings section | relevant Explanation page |

Rules:
1. Documentation updates are part of Step 4 in the Mandatory Workflow (§5.2). They are not optional.
2. User-facing docs use plain English per §4.1 vocabulary rules. Developer docs may use technical terms.
3. Cross-link between related docs: Explanation pages link to Architecture deep-dives; How-to Guides link to Reference pages.
4. Every new Explanation page must be linked from `docs/index.md`.
5. Every published docs page must include front matter with `title`, `summary`, `audience`, `category`, and `product_area`. Use `status: target-state` for future-facing pages.
6. `.codex/` is a gitignored, derived local context layer. Regenerate it with `scripts/docs/refresh-codex-context.ps1`; never edit it by hand.

---

## 6. Structure Reference — Feature-Sliced Dashboard Layout

All Dashboard code in `src/MediaEngine.Web/` follows the **Feature-Sliced** pattern. Every new piece of UI code must go into the correct slice.

### 6.1 — Services (`src/MediaEngine.Web/Services/`)

Non-UI logic the Dashboard needs, organised by concern.

| Subfolder | Key files | Purpose |
|---|---|---|
| `Discovery/` | `DiscoveryComposerService.cs` | Builds the home-page discovery feed |
| `Editing/` | `MediaEditorLauncherService.cs`, `CollectionEditorLauncherService.cs`, `*Models.cs` | Editor open/close state and DTOs |
| `Integration/` | `EngineApiClient.cs` + `IEngineApiClient.cs`, `UIOrchestratorService.cs`, `UniverseStateContainer.cs`, `UniverseMapper.cs`, `ProviderCatalogueService.cs`, `IntercomEvents.cs` | All HTTP + SignalR communication with the Engine |
| `Narration/` | `PhraseTemplateService.cs` + interface | Narrated-copy phrase templates |
| `Navigation/` | `MediaNavigation.cs`, `ListenNavigation.cs` | Route-building helpers |
| `Playback/` | `ListenPlaybackService.cs`, `ReadingProgressService.cs`, `ReaderSettingsService.cs`, `MediaReactionService.cs`, `WatchlistService.cs` | Playback session state |
| `Theming/` | `ThemeService.cs`, `DeviceContextService.cs`, `PaletteProvider.cs`, `SocialUriHelper.cs` | Dark-mode theme, device cascade, palette, Actionable-URI helpers |

### 6.2 — Components (`src/MediaEngine.Web/Components/`)

Reusable visual components, organised by feature slice.

| Subfolder | What lives here |
|---|---|
| `Bento/` | `BentoGrid`, `BentoItem` — legacy glass-tile wrappers |
| `Browse/` | `MediaBrowseShell`, `MediaBrowseHero`, `BrowseQueryBuilder`, `BrowseState`, `BrowseArtworkRules` — shared shell and extracted query/state/artwork helpers used by Read / Watch / Listen |
| `Collections/` | `CollectionsPage`, `CollectionEditorShell` |
| `Discovery/` | `DiscoveryHero`, `DiscoveryShelf`, `DiscoveryCard`, `DiscoveryHubStrip`, `AddToCollectionDialog` |
| `Layout/` | `MainLayout`, `NavMenu`, `ReconnectModal` — the routed app shell |
| `Library/` | Reusable legacy-named library helpers still used by current browse/list surfaces, such as configurable tables, column definitions, batch bars, status pills, and group drill-down components. Do not add Vault workflow components here. |
| `Listen/` | `ListenNowPlayingBar`, `ListenTrackDataGrid` |
| `MediaEditor/` | `SharedMediaEditorShell`, `SharedMediaBatchConfirmDialog` |
| `Navigation/` | `TopBar`, `AppLogo`, `AppTabs`, `AppSelectorNav`, `CommandPalette` (Ctrl+K), `ProfileDropdown`, `MobileFilterBar` |
| `Pages/` | All routed pages — see 6.4 |
| `Playback/` | Reader controls: `ReaderTopBar`, `ReaderBottomBar`, `ReaderTocDrawer`, `ReaderBookmarksPanel`, `ReaderHighlightsPanel`, `ReaderSettingsPanel`, `ReaderStatsOverlay`, `ReaderSearchPanel`, `ReaderContextMenu` |
| `LibraryItems/` | Internal building blocks used by Library + Universe: `LibraryItemInspector`, `LibraryItemCard`, `LibraryItemGrid`, `Inspector*Section` panels, `LibraryItemActionsBar`, `LibraryItemBulkBar`, `LibraryItemBatchList`, `LibraryItemFilterBar`, `LibraryItemHelpers`, `ProvisionalFormPanel`, `ActivityItemCard`, `ReportProblemDialog` |
| `Settings/` | Settings shell tabs — see §3.11 for the section list. Includes `OverviewTab`, `ProfileTab`, `PlaybackTab`, `LibrariesTab`, `ProviderPriorityTab`, `WikidataConfigTab`, `ModelsTab`, `AiFeaturesTab`, `VibeVocabularyTab`, `AiScheduleTab`, `SystemTab`, `SecurityTab`, `ApiKeysTab`, `UsersTab`, `ActivityTab`, `MaintenanceTab`, `SetupTab`, `GeneralTab`, `ServerGeneralTab`, `StatusDashboardTab`, `ConnectivityTab`, `UniverseSettingsTab`, `ProviderCard`, `WikidataConnectionPanel`, `CuratorsDrawer`, `MetadataEditDialog`, `MediaItemEditor`, `CollectionEditCoverCompare`, `FolderBrowserDialog`, `SettingsPlaceholder` |
| `Shared/` | `AppIcon`, `AppIconCatalog`, `AppPageHeader`, `AppSurfaceCard`, `FuzzySearchField` — cross-cutting primitives |
| `Universe/` | Hero, swimlane, and card components: `CollectionHero`, `CompactHero`, `HeroCarousel`, `PosterSwimlane`, `SwimlaneSection`, `LandscapeCard`, `SquareCard`, `WideCard`, `WorkCard`, `LibraryCard`, `PersonCard`, `PersonSwimlaneHeader`, `TrackRow`, `MetadataChips`, `ProgressIndicator`, `AmbientBackground`, `GlobalBackground`, `GreetingBar`, `AdaptationTree` + node, `FamilyTreeView`, `AlphabeticalGrid`, `CastComparison`, `BookDetailContent`, `CollectionShell`, `CollectionToolbar`, `LaneFilterBar`, `ManualEntryForm`, `MediaSearchPanel`, `MissingUniverseChip`, `PathFinderPanel`, `PendingFilesAlert`, `UniverseGuide` |
| `Watch/` | `TvBrowsePage`, `WatchPlaybackSpecs`, `WatchTvStyles` |

### 6.3 — Other top-level folders under `src/MediaEngine.Web/`

| Folder | Purpose |
|---|---|
| `Models/ViewDTOs/` | Data shapes used ONLY by the Dashboard (`ProviderAccentMap.cs` sits at `Models/` root) |
| `Resources/` | `SharedStrings.resx` (+ `.fr` / `.de` / `.es`) plus generated `SharedStrings.cs` |
| `Shared/` | Blazor app-host layout wrappers: `MainLayout`, `NavMenu`, `PopupLayout`, `ReaderLayout`, `DateFormatHelper`, `_Imports` |
| `wwwroot/` | Static assets: images, CSS, JS (`cytoscape-interop.js`, `epub-reader.js`, `cover-popup.js`, `app.js`) |

### 6.4 — Routed Pages (`Components/Pages/`)

| Route | Page | Purpose |
|---|---|---|
| `/` | `LibraryBrowsePage.razor` | Home — discovery landing |
| `/home-legacy` | `Home.razor` | Legacy home (retained for reference) |
| `/read`, `/read/{Tab}` | `ReadPage.razor` | Books + audiobooks browse |
| `/read/{AssetId:guid}` | `EpubReader.razor` | In-browser EPUB reader |
| `/watch`, `/watch/{Tab}` | `WatchPage.razor` | Movies + TV browse |
| `/watch/movie/{WorkId:guid}` | `WatchMoviePage.razor` | Movie detail |
| `/watch/tv/show/{CollectionId:guid}` | `WatchTvShowPage.razor` | TV show detail |
| `/watch/tv/show/{CollectionId:guid}/episode/{WorkId:guid}` | `WatchTvEpisodePage.razor` | TV episode detail |
| `/watch/player/{AssetId:guid}` | `WatchPlayerPage.razor` | Video player |
| `/listen`, `/listen/music`, `/listen/music/{Section}`, `/listen/music/albums/{CollectionId:guid}`, `/listen/music/artists/{ArtistKey}`, `/listen/music/playlists/{CollectionId:guid}`, `/listen/music/playlists/system/{PlaylistKey}`, `/listen/audiobooks`, `/listen/audiobook/{WorkId:guid}` | `ListenPage.razor` | Music + audiobooks browse |
| `/listen/player-popup` | `ListenPlayerPopupPage.razor` | Detached listen window |
| `/collections` | `Collections.razor` | Browse / create / manage collections |
| `/collection/{Id:guid}` | `CollectionDetail.razor` | Collection detail |
| `/book/{Id:guid}` | `BookDetail.razor` | Book detail |
| `/person/{Id:guid}` | `PersonDetail.razor` | Person detail |
| `/universe/{Qid}/explore` | `ChronicleExplorer.razor` | Universe graph explorer |
| `/search` | `SearchPage.razor` | Global search |
| `/settings`, `/settings/{Section}` | `Settings.razor` | Settings shell (review queue at `/settings/review`) |
| `/preferences` | `Preferences.razor` | User preferences shortcut |
| `/server-settings` | `ServerSettings.razor` | Server controls shortcut |
| `/review` | `ReviewRedirect.razor` | 301 redirect → `/settings/review` |
| `/provider-tester`, `/tester` | `ProviderTester.razor`, `EnrichmentTester.razor` | Dev-only testers |
| `/not-found`, `/Error` | `NotFound.razor`, `Error.razor` | Error pages |

### 6.5 — Rules for adding new code

| New code type | Where it goes |
|---|---|
| Engine HTTP call | `Services/Integration/EngineApiClient.cs` + `IEngineApiClient` |
| Dashboard data shape | `Models/ViewDTOs/` |
| Reusable visual component | `Components/<FeatureSlice>/` |
| Full routable page | `Components/Pages/` |
| Settings section | `Components/Settings/<Name>Tab.razor` + register in `SettingsNav` |
| Review-queue surface component | `Components/Library/` |
| Inspector / cards reused by Library or Universe | `Components/LibraryItems/` |
| Media-player component | `Components/Playback/` |
| Media-playback session service | `Services/Playback/` |
| Route-building helper | `Services/Navigation/` |
| Editor launcher or state | `Services/Editing/` |
| Theme or device setting | `Services/Theming/` |
| Cross-cutting primitive | `Components/Shared/` |
| Blazor host layout wrapper | `Shared/` |

---

## 7. Project Contacts

| Role | Detail |
|---|---|
| Product Owner | Shaya |
| Repository | [github.com/shyfaruqi/tuvima-library](https://github.com/shyfaruqi/tuvima-library) |
| License | AGPLv3 |
| Engine base URL (local dev) | `http://localhost:61495` (HTTPS on `61494`) |
| Dashboard URL (local dev) | `http://localhost:5016` (HTTPS on `7062`) |

