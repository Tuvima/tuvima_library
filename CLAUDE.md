# CLAUDE.md — Tuvima Library Project Memory

> **Who reads this file?**
> Every Claude session working on this repository reads this file automatically before doing anything else.
> It is the single, authoritative source of truth for what Tuvima Library is, how it is built, and how to work on it.
> It bridges the Product Owner's business goals with the technical team's execution.

> **Architecture details** live in `docs/architecture/*.md`. This file contains summaries — read the relevant detail doc when working on a subsystem.

---

## 1. Project Overview

### What is Tuvima Library?

#### Name & Vision

**Tuvima Library** is the product name. **Tuvima** is the company. Code namespaces use `MediaEngine.*` intentionally — decoupled from branding for future resilience.

The project's core philosophy is **Presentation** — the act of bringing something forward and making it whole.

Tuvima Library does not create a library. It **presents** one. The stories already exist on the hard drive, fragmented across formats and folders. The Library's job is to find them, understand them, unify them, and surface the result as something coherent and beautiful — as if it always belonged together.

Every feature exists in service of that word:
- The **Intelligence Engine** works invisibly so the library is already whole when you look at it.
- The **Universe** is the act of presentation made structural — the book, film, and audiobook of the same story brought forward as one.
- The **Cinematic Dashboard** is the presentation layer made visible — the interface where the Engine's understanding reaches the screen.

> **All future sessions must preserve this creative context.** When writing copy, naming features, or explaining the product, the Presentation philosophy should be the frame.

#### What it does

**Tuvima Library** is a **unified media intelligence platform** that runs entirely on your own machine — no cloud account, no subscription, no data leaving your home.

Its core job is to bring order to a large, messy media collection spread across folders. You point it at your hard drive, and it automatically:

1. **Watches** your folders for new files — books, audiobooks, comics, TV shows, movies, music, and podcasts.
2. **Fingerprints** each file with a unique identifier (like a barcode), so it can track files even if you rename or move them.
3. **Reads the embedded information** inside each file — title, author, year, cover art, series name — and uses a *Priority Cascade* to determine the most trustworthy version of each piece of information.
4. **Groups everything into Universes and Series** — intelligent groupings that link all versions of the same story across media types.
5. **Serves a visual dashboard** in your browser for browsing, searching, and managing the library.
6. **Broadcasts instant updates** to your dashboard the moment a new file is detected, with no page refresh.

### The Grouping Model — Universes and Series

Tuvima Library organises media into **Universes** and **Series** — virtual containers that link multiple media forms through shared contextual metadata.

A **Universe** is a creative world — the books, films, audiobooks, comics, and podcasts that belong together because their metadata says so. A **Series** is a sub-grouping within a Universe — a specific sequence or collection of related works.

The matching is automatic. When the Engine discovers that a novel, its film adaptation, and a podcast discussion share the same author, franchise identifiers, or Wikidata Q-identifier, it groups them into the same Universe. You browse by creative world, not by file type.

This can be as simple as grouping books by the same author, or as rich as following a story from an ebook into its movie adaptation, or linking a podcast that covers the same topic.

> *Example: The "Dune" Universe might contain:*
> - *The "Dune Novels" Series — Frank Herbert's novels (EPUB ebooks)*
> - *The "Dune Films" Series — Denis Villeneuve film adaptations (MP4 videos)*
> - *The audiobook narrations (M4B)*
> - *The graphic novel adaptations (CBZ comics)*
> - *A related podcast series discussing the Dune universe*
>
> *These are linked not because they share a filename, but because their metadata — author, franchise QID, series identifiers — connects them to the same creative world.*

**A Series is not limited to a numbered sequence.** While a Series often represents a book series or film franchise, it is a flexible virtual container for *any* creative grouping — film adaptations of a novel, spin-off works, thematic collections, or cross-media narrative links. What defines a Series is shared contextual metadata, not a shared format or filesystem location.

#### Terminology — User-Facing vs Internal

| Level | User-facing name | Internal code name | Example |
|---|---|---|---|
| Entire library | **Library** | Library | Everything you own |
| Franchise grouping | **Universe** | ParentHub | Dune, Marvel, Tolkien |
| Series / collection | **Series** | Hub | Dune Novels, Dune Films |
| Single title | **Work** | Work | Dune Part One |
| Specific version | **Edition** | Edition | 4K HDR Blu-ray Remux |
| File on disk | **Media Asset** | MediaAsset | the .mkv file |

> **Rule:** Anything the user sees — UI labels, column headers, tab names, documentation prose — uses the **user-facing name**. Internal code names (ParentHub, Hub) stay in the domain/engine layer only.

The hierarchy:

```
Library   (your entire collection)
  └── Universe   (franchise/creative world — e.g. "Dune")
        └── Series   (sub-grouping — e.g. "Dune Novels" or "Dune Films")
              └── Work   (one title — e.g. "Dune Part One")
                    └── Edition   (one physical version — e.g. "4K HDR Blu-ray Remux")
                          └── Media Asset   (one file on disk)
```

**Universes** are optional. A Series that belongs to no larger franchise sits directly under the Library. When the Engine discovers franchise-level relationships (Wikidata P8345 franchise, P179 series, or shared narrative roots), it can promote a group of related Series under a common Universe.

**Important:** Both Universes and Series are resolved at metadata-scoring time by the Intelligence Engine. They have no presence on the filesystem — files are organised by category and title, not by Universe or Series.

### Who is it for?

A single power user who wants complete, private control over a large media collection — without depending on services like Plex, Jellyfin, or any subscription platform.

---

## 2. Technical Stack

> **Note to Claude:** When speaking to the Product Owner, always use the plain-English column, not the technical column.

### Core Tools

| Plain-English name | Technical name | Purpose |
|---|---|---|
| Programming language | C# / .NET 10 | Everything is written in this language |
| Local database | SQLite | A single file on disk that stores the entire library catalogue |
| Visual interface library | MudBlazor 9 | Pre-built visual building blocks for the browser dashboard |
| Real-time intercom | SignalR | Pushes live updates to the dashboard without a page refresh |
| Book file reader | VersOne.Epub | Reads information embedded inside EPUB book files |
| Engine API documentation | Swashbuckle | Auto-generates an interactive menu of Engine capabilities at `/swagger` |
| Structured logging | Serilog | Writes rolling log files for Engine action review |
| Resilient HTTP calls | Polly (Microsoft.Extensions.Http.Resilience) | Auto-retries failed external API calls with backoff |
| Wikidata/Wikipedia API client | Tuvima.Wikidata | Unified client for Wikidata reconciliation, entity fetching, Wikipedia summaries, image URLs, and in-memory graph engine |
| Cron scheduling | Cronos | Runs background tasks at specific times |
| Data access layer | Dapper | Maps database rows to C# objects |
| AI text inference | LLamaSharp | Local LLM inference with GBNF grammar constraints |
| AI audio inference | Whisper.net | Local speech-to-text and language detection |
| Automated quality checks | xUnit + coverlet | Automated tests after every change |
| Version control | Git + GitHub | Tracks every code change |

### Headless Design — Engine and Dashboard are Separate

**This is a critical architectural decision.** The Engine (intelligence and data) is completely separate from the Dashboard (visual interface). They communicate via HTTP + SignalR.

| Part | Technical project | Role |
|---|---|---|
| The Engine | `MediaEngine.Api` | Handles all intelligence, data, and file operations. Exposes an API. |
| The Dashboard | `MediaEngine.Web` | The browser interface. Asks the Engine for data and displays it. |

### Source Code Layout

| Folder | What it is | Role |
|---|---|---|
| `src/MediaEngine.Domain` | The Rulebook | Defines what a Hub, Work, and Edition *are*. Pure business logic. |
| `src/MediaEngine.Storage` | The Filing Clerk | Reads and writes the SQLite database. |
| `src/MediaEngine.Intelligence` | The Analyst | Runs the Priority Cascade. Scores metadata Claims. |
| `src/MediaEngine.Processors` | The Scanner | Opens each file type and extracts embedded information. |
| `src/MediaEngine.Providers` | The Research Team | Fetches metadata from external sources. |
| `src/MediaEngine.Ingestion` | The Mail Room | Monitors folders. Queues new files. Manages file organization. |
| `src/MediaEngine.AI` | The Brain | Local AI inference. Smart Labeling, classification, search, vibe tags. |
| `src/MediaEngine.Api` | The Reception Desk | The Engine's public interface. HTTP + SignalR. |
| `src/MediaEngine.Web` | The Showroom | The browser Dashboard. |
| `tests/` | The Quality Inspector | Automated checks for every module. |

---

## 3. Architecture Summary

> **Detailed documentation** lives in `docs/architecture/*.md`. Each summary below links to its detail doc.
> Read the detail doc when working on that subsystem.

### 3.1 — Ingestion Pipeline
**Detail:** [`docs/architecture/ingestion-pipeline.md`](docs/architecture/ingestion-pipeline.md)

You configure Library Folders — each tells the Engine where to look and what media to expect. Watch mode monitors for new files and auto-moves them. Import mode scans existing collections. Files go through: Settle → Lock Check → Fingerprint (SHA-256) → Scan (processor reads metadata) → Identify (scoring) → Stage in `.data/staging/` → Hydrate → Promote to organized library. All files land in `.data/staging/` first in a flat structure (`{assetId12}/` subfolders only, plus `rejected/` for explicitly rejected files — no `pending/`, `low-confidence/`, or `unidentifiable/` subcategories; database tracks status); only hydrated files with sufficient confidence (≥0.85) get promoted. Ambiguous media types (MP3, MP4) are classified via AI and heuristic signals. **Work-level deduplication:** `MediaEntityChainFactory` checks for existing Works (by title+author+media type via `IWorkRepository`) before creating new ones — duplicate files create new Editions under the existing Work instead of duplicating it. **Dedup fallback:** When `canonical_values` are not yet populated (race condition during batch ingestion), `WorkRepository.FindByTitleAuthorAsync` falls back to matching raw `metadata_claims` from file processors to prevent duplicate Works. **Filesystem conventions:** Audiobooks get their own top-level folder. TV follows Plex convention (`Show/Season XX/SxxExx - Title`). Music follows Plex convention (`Artist/Album/Track`). The `{Format}` token has been removed from all templates to prevent duplicate subfolder nesting. **TV claim keys:** VideoProcessor emits `series`, `season`, `episode`, and `episode_title` claims alongside `show_name`, `season_number`, `episode_number` for FileOrganizer compatibility. **Leading SxxExx handling:** When a filename starts with `S01E01` (no show name prefix), VideoProcessor infers the show name from the parent folder hierarchy (skipping "Season XX" folders) via `InferShowNameFromPath`. Episode titles are extracted from text following the SxxExx pattern across all TV filename formats. **FileOrganizer fallback chains:** Token lookup falls back from `series` → `show_name`, `season` → `season_number`, `episode` → `episode_number`. **Duplicate file prevention:** FileOrganizer checks file size before creating collision copies — if destination exists with identical size, the move is skipped. **ContentGroup hub creation disabled:** `MediaEntityChainFactory.EnsureContentGroupAsync` is a no-op; Vault container views are now driven by System view hubs seeded at startup. **Staging visibility:** Items with files still in `.data/staging/` are excluded from Vault media tabs (filtered in `RegistryRepository.asset_data` CTE) but remain visible in the Action Center for review. The visibility filter in `full_data` CTE allows review items (`rd.review_id IS NOT NULL`) without requiring a non-staging asset — the staging asset gate was removed to prevent the Action Center list from appearing empty while the count showed items. **Staging lifecycle logging:** All stage transitions (staging, review creation, promotion) are logged at Information level for pipeline tracing. Config: `config/libraries.json`, `config/disambiguation.json`.

### 3.2 — Priority Cascade Engine
**Detail:** [`docs/architecture/scoring-and-cascade.md`](docs/architecture/scoring-and-cascade.md)

When multiple sources disagree about metadata, the Priority Cascade resolves disputes. Each piece of metadata is a Claim with a source and confidence weight. Four tiers evaluated in order: **Tier A** (user locks always win) → **Tier B** (per-field provider priority from config) → **Tier C** (Wikidata authority) → **Tier D** (highest confidence wins). **Author confidence override in Tier C:** For the `author` field only, when a non-Wikidata claim has strictly higher confidence than the best Wikidata claim, the higher-confidence claim wins. This honours the deliberately reduced P50 author confidence (0.75) so that embedded pen names (0.95) survive the cascade when the safety nets in `ReconciliationAdapter` don't fully re-key the P50 claims. For all other fields, Wikidata wins unconditionally in Tier C. AI improves matching quality (SmartLabeler, QidDisambiguator) but does NOT replace the cascade — Wikidata remains the authority for canonical data. All claims are append-only; history is never lost. **Unknown media type:** When `MediaType.Unknown`, reconciliation is blocked entirely — the item is routed to the review queue for manual classification. AI MediaTypeAdvisor is consulted first; only after LLM classification fails does the item go to review. **Unified retail match scoring:** `RetailMatchScoringService` is the single scorer used by both the automated pipeline (Stage 1 confidence gate) and manual search (Vault detail drawer). Field weights: title 45%, author 35%, year 10%, format 10%, plus cross-field boosts. Missing metadata (no author, no year) scores 0.0 (not neutral 0.5). Placeholder titles ("Unknown", "Untitled", "Track XX") return zero-score and route to review. **Creator field fallback:** `fileAuthor` checks author → artist → director → writer to cover all media types. **Weight redistribution:** When BOTH file and candidate lack author data (common for Movies, TV, Comics), the 35% author weight is redistributed proportionally to title/year/format so scoring is driven entirely by available fields. Composite score is clamped to [0.0, 1.0] and rounded to 4 decimal places to prevent floating-point drift at threshold boundaries. **Multi-author matching:** When full-string author comparison < 0.70, both file and candidate authors are split on "&", "and", "," and matched proportionally (matched/total). **Wikidata author validation:** When the file has an author but the candidate's P50/P175 doesn't match (similarity < 0.3), a -35 penalty is applied. When the candidate has no author properties at all, a -40 penalty applies. Score blending: 85% composite / 15% API score. **Pipeline enforcement:** No retail match = no Wikidata. The text-only Wikidata fallback is removed; `ResolveBridgeAsync` blocks sentinel-only calls (only `_title`/`_author` with no real bridge IDs). Config: `config/scoring.json`.

### 3.3 — Security
**Detail:** [`docs/architecture/security.md`](docs/architecture/security.md)

Every endpoint requires authentication except `/system/status` and localhost (when bypass is enabled). Three roles: Administrator (full access), Curator (browse + metadata), Consumer (browse only). API keys are generated with roles, labeled, and individually revocable. Rate limiting: key generation 5/min, streaming 100/min, general 60/min. Path traversal protection on folder endpoints. SignalR Hub requires auth.

### 3.4 — Dashboard UI
**Detail:** [`docs/architecture/dashboard-ui.md`](docs/architecture/dashboard-ui.md)

Dark-mode-only cinematic design with ambient gradient background and film-grain texture. Dual navigation: TopBar (4 variants: desktop/mobile/TV/automotive) + LeftDock (icon-only, desktop). Poster swimlanes with 2:3 cover art cards. Cinematic hero banners (SkiaSharp pre-rendered or CSS fallback). Real-time SignalR updates. Four device profiles (web/mobile/television/automotive) with config-driven constraints. Fixed golden amber accent (#C9922E).

### 3.5 — Brand Assets

Three official SVG logo files. **Never replace logo placements with hand-written text.**

| File | Location | Use when… |
|---|---|---|
| `tuvima-logo.svg` | `wwwroot/images/` and `assets/images/` | Full horizontal logo — mark + "TUVIMA" wordmark |
| `tuvima-icon.svg` | `wwwroot/images/`, `wwwroot/favicon.svg`, `assets/images/` | Square icon mark only — favicon, app icon |
| `tuvima-hero.svg` | `assets/images/` | Mark + wordmark + subtitle — README hero and marketing |

All SVGs use white + black fills, designed for dark backgrounds. Source files in `C:\Users\shaya\OneDrive\Documents\Projects\Tuvima\Graphics\` (outside repo).

### 3.6 — Centralized Data Directory (`.data/`)

All internal data lives under a single `.data/` directory at the library root. This keeps every Engine-managed artefact out of the media tree and in one predictable place.

**Directory layout:**

```
{LibraryRoot}/
  .data/
    database/
      library.db              ← SQLite database (path set in config: database_path)
    images/
      {QID}/
        cover.jpg
        hero.jpg
        cover-thumb.jpg       ← 200px-wide SkiaSharp JPEG (quality 75)
      _pending/
        {GUID12}/             ← items awaiting Wikidata resolution
      people/
        {QID}/                ← only created when a headshot image actually exists
          headshot.jpg
          headshot-thumb.jpg
    staging/
      {assetId12}/            ← one subfolder per in-flight asset
      rejected/               ← explicitly rejected files
```

`ImagePathService` (Domain layer, registered as singleton) is the single source of truth for all image paths. When a QID is assigned, `PromoteToQid()` renames the pending directory under `images/`. Migration from `_provisional` to `_pending` is handled automatically. People image directories under `images/people/{QID}/` are created lazily — only when a headshot image actually exists, never eagerly. Thumbnails are 200px-wide SkiaSharp-generated JPEGs (quality 75) served via `/stream/{assetId}/cover-thumb`. Image cleanup runs on media delete (checks QID siblings before removing), and an orphan sweep endpoint (`POST /maintenance/sweep-orphan-images`) validates `images/` directories against the database. User-uploaded images (flagged `user_override` in `image_cache`) are protected from orphan sweeps.

### 3.7 — Hydration Pipeline & Providers
**Detail:** [`docs/architecture/hydration-and-providers.md`](docs/architecture/hydration-and-providers.md)

Two-stage enrichment runs after ingestion. Cover art from providers is written to `.data/images/` via `ImagePathService`; thumbnails auto-generated on save. **Cover art download fix:** `ImagePathService` is properly injected into `HydrationPipelineService` via DI; an existing-file check prevents redundant downloads. **Stage 1 (RetailIdentification):** retail providers gather cover art, descriptions, ratings, and bridge IDs (ISBN, ASIN, TMDB ID) in waterfall order. Retail providers are a rich data source for matching — descriptions, narrator data via AI extraction, ratings — all used to rank candidates against file metadata. **Retail confidence gate:** after each provider returns, `RetailMatchScoringService` scores the candidate (same scorer used by manual search); below 0.50 = discard, 0.50–0.85 = accept with review flag, ≥0.85 = auto-accept. **No retail = no Wikidata:** the text-only Wikidata fallback is removed; Stage 1 failure routes directly to review. **Stage 2 (WikidataBridge):** Wikidata uses bridge IDs for precise QID resolution and fetches canonical properties — including person properties P50 (author), P57 (director), P58 (screenwriter), P86 (composer), P110 (illustrator), P161 (cast member, capped at 20), and P449 (original broadcaster/network, TV only). Wikipedia descriptions included. Description heading markup (`== Plot ==`) is stripped before storage. Wikidata is the authority for canonical data; retail providers supply matching data. **Adapter slimdown (Tuvima.Wikidata v2.4.1):** the public `ReconciliationAdapter.ResolveAsync` / `ResolveBatchAsync` are now thin pass-throughs to the library's `Stage2Service` sub-service. The hand-rolled `ResolveBridgeAsync` (~370 lines), `ResolveMusicAlbumAsync` (~120 lines), `ResolveByTextAsync`, `AutoDetectStrategy`, batch group-key helpers, and the `BridgeResolutionResult` DTO have been deleted. The library natively groups requests by natural key (one round-trip per unique ISBN / album / text signature), handles edition pivoting via `EditionPivotRule`, and supports a discriminated `IStage2Request` hierarchy (`BridgeStage2Request` / `MusicStage2Request` / `TextStage2Request`) so illegal combinations are not representable. The adapter follows up each successful resolution with a Data Extension call (`ExtendAsync` + `ExtensionToClaims`) to populate `WikidataResolveResult.Claims` and `CollectedBridgeIds`, since the library's `Stage2Result` deliberately does not carry property claims. A small inline text-fallback pass mirrors the legacy `ResolveBridgeAsync`'s "if bridge IDs fail, try title+author" behaviour for parity. See `.claude/plans/adapter-slimdown-remediation.md` and the parity baseline at `tests/fixtures/stage2-baseline-v2.json`. **Child entity discovery:** After Stage 2 QID resolution, `DiscoverChildEntitiesAsync` delegates to `_reconciler.Children.GetChildEntitiesAsync(ChildEntityRequest)` with `ChildEntityKind.TvSeasonsAndEpisodes` / `MusicTracks` / `ComicIssues`. The library walks the manifest in a single call (TV: 1 call instead of N+1), exposes typed `ChildEntityRef` fields (`Duration as TimeSpan?`, `ReleaseDate as DateOnly?`, `Creators` map), and handles the reverse-P179 comics traversal via `Direction.Incoming`. Results stored as claims: `season_count`, `episode_count`/`track_count`/`issue_count`, and `child_entities_json` (the adapter re-shapes the library's flat `Children` list back into the nested `{seasons:[{episodes:[]}]}` shape the Vault parser expects). Capped at 20 seasons and 500 total children. Wrapped in try/catch — never fails the main pipeline. **Multi-author reconciliation constraints:** `PropertyConstraint.Values` passes all split authors to the reconciliation API for proportional matching (e.g., "Good Omens" with P50 constraint ["Neil Gaiman", "Terry Pratchett"]). Config: `config/providers/wikidata_reconciliation.json`. **Tuvima.Wikidata v2.4.1 features used:** the v2.0 facade exposes nine focused sub-services — `Reconcile`, `Entities`, `Wikipedia`, `Editions`, `Children`, `Authors`, `Labels`, `Persons`, `Stage2`. The adapter's `Program.cs` registration adds each sub-service as a singleton so callers can inject narrow slices. Specific primitives consumed: `Stage2.ResolveBatchAsync` (replaces `ResolveBridgeAsync` + `ResolveMusicAlbumAsync` + `ResolveByTextAsync`); `Children.GetChildEntitiesAsync(ChildEntityRequest)` with built-in `MaxPrimary`/`MaxTotal` caps and `IncludeCreatorProperties`; `Persons.SearchAsync(PersonSearchRequest)` with role → P106 occupation mapping, year hints, work hints, companion-name hints, and musical-group defaulting; `Authors.ResolveAsync(AuthorResolutionRequest)` for the three pseudonym patterns (Pattern 1: reverse P742 — "Richard Bachman" → Stephen King; Pattern 2: P742 enumeration — Stephen King → ["Richard Bachman"]; Pattern 3: collective pseudonym P527 walk — "James S.A. Corey" → Daniel Abraham + Ty Franck); `Labels.GetAsync` / `GetBatchAsync` with malformed-QID pre-filter; `EntityGraph` for in-memory graph queries (replaces dotNetRDF). The v2.3 P106-on-musical-groups penalty fix is live, so Daft Punk / Radiohead pass at the documented 0.80 `AcceptThreshold` (the 0.5 workaround threshold has been removed). The v2.4.1 multi-author parallelism fix means multi-author book resolutions fan out across the library's bounded `ConcurrencyLimiter`. **CirrusSearch type filter consolidation:** The per-media-type P31 allow-lists for CirrusSearch text fallback are now read from `instance_of_classes` in `config/providers/wikidata_reconciliation.json` — the same source used by Stage 1 reconciliation. The former `config/cirrus-type-filters.json` (extracted during the adapter slimdown) was a stale snapshot that had drifted from the authoritative list, causing manga (Q21198342) and other types to be missing from text fallback. `CirrusTypeFilterConfiguration` model and `LoadCirrusTypeFilters()` on `IConfigurationLoader` are removed; `GetCirrusTypesForMediaType()` in `ReconciliationAdapter` now reads directly from `_config.InstanceOfClasses`. **Tuvima.Wikidata features used (carried from earlier versions):** CirrusSearch type filtering (`ReconciliationRequest.Types`), concurrent multi-language search (`Languages` parameter), `DiacriticInsensitive` flag and `QueryCleaners` pipeline, `GetEditionsAsync` for audiobook P747 edition discovery, `resolveEntityLabels: true` on `GetEntitiesAsync` for batch display label resolution, `MatchedLabel` for alias/sitelink-aware title emission, `EntityLabel` on `WikidataValue` for pre-resolved companion QID labels, `GetRevisionIdsAsync` for lightweight entity staleness detection. **Standalone Person Reconciliation:** After Stage 2, any person name from file metadata or structured properties that lacks a companion QID is searched independently via `_reconciler.Persons.SearchAsync` (the adapter's `PersonReconciliationService` is now a 175-line thin wrapper, down from 445 lines). The library handles role → occupation mapping, musical-group defaulting (Performer / Artist roles include Q215380 + Q5741069), and notable-work boost. Auto-accepted at ≥0.80 confidence (0.80 for file metadata, 0.75 for AI-extracted). Person freshness check: existing persons enriched within 30 days skip re-fetch; stale persons check `last_revision_id` via `GetRevisionIdsAsync` before full property fetch. **Musical Group Support:** Person reconciliation accepts Q215380 (musical group) and Q5741069 (musical ensemble) alongside Q5 (human) for music-specific roles (Performer, Artist, Composer). P527 (has part) resolves group → members; P463 (member of) resolves individual → groups. Group membership stored in `person_group_members` junction table with optional start/end dates from Wikidata P580/P582 qualifiers. `IsGroup` flag on Person entity determined by P31 at enrichment time. New person roles: Performer, Artist. **Performer role mapping:** The `performer` claim key is mapped to media-type-aware roles: Music → "Performer", Audiobooks → "Narrator", TV/Movies → "Actor". This prevents musicians from being classified as narrators. Two-pass enrichment: Pass 1 (quick, immediate) gets files on Dashboard fast; Pass 2 (universe, background/scheduled) does deep enrichment. Provider response caching eliminates redundant API calls. **Diacritics normalization:** `ConfigDrivenAdapter.ComputeWordOverlap` strips Unicode diacritics (NormalizationForm.FormD decomposition) before word matching, so "Shogun" matches "Shōgun". For TV-specific strategies, `BuildUrl` prefers `ShowName` over `Title` to avoid searching by episode title. **Subtitle stripping:** `ReconciliationAdapter.FetchWorkAsync` strips "; or," subtitle patterns (e.g. "Frankenstein; or, The Modern Prometheus" → "Frankenstein") before Wikidata CirrusSearch. Config: `config/hydration.json`, `config/providers/*.json`, `config/slots.json`.
**Ranked Pipeline System (replaces fixed 3-slot waterfall):** Stage 1 now supports unlimited ranked providers per media type with three execution strategies configured in `config/pipelines.json` (falls back to `slots.json` auto-conversion): **Waterfall** (first match wins — Movies, TV, Comics), **Cascade** (all run independently, claims merge — Books, Podcasts), **Sequential** (chained, each feeds the next via bridge IDs — Audiobooks, Music). `PipelineConfiguration` in Storage/Models; `ProviderStrategy` enum in Domain/Enums. `HydrationPipelineService` resolves providers by rank and executes per strategy. In Sequential mode, `PriorProviderBridgeIds` on `ProviderLookupRequest` passes bridge IDs from Provider A to Provider B. `PriorityCascadeEngine` Tier B checks `pipelines.json` per-media-type field priorities before global `field_priorities.json`. `MediaTypeFieldRegistry` (Domain/Constants) centralises fields, search display configs, and searchable fields per media type. Settings UI: `ProviderPriorityTab` has strategy picker per media type. API: `GET/PUT /settings/pipelines`.
**Music Pipeline (Apple API sole provider):** Music now uses Apple API as the sole Stage 1 provider (Waterfall strategy). MusicBrainz is disabled for music (config `enabled: false`). Apple provides: track metadata (title, artist, album, genre, year), high-quality scalable cover art, and Apple Music identifiers (trackId → `apple_music_id`, collectionId → `apple_music_collection_id`, artistId → `apple_artist_id`). Stage 2 bridge resolution uses three-tier fallback: `apple_music_id` → P4857, ISRC → P1243 (if file has it), then CirrusSearch text reconciliation with Q105543609 (musical work/composition — tested against 5 ambiguous songs with 100% accuracy, eliminating false matches against movies/albums with the same title) type filtering. **Album-first lookup:** when a track is matched, the full album is fetched via `/lookup?id={collectionId}&entity=song`, creating the Hub (album) and all Works (tracks) — subsequent tracks from the same album slot in without additional API calls (same pattern as TV series). Sentinel keys `_title` and `_author` in the bridge ID dictionary signal the text reconciliation fallback path.

**Durable Identity Pipeline:** The durable job model has replaced the in-memory `BoundedChannel` queue. Ingestion creates `identity_jobs` rows (SQLite, migration M-080) instead of enqueuing in-memory `HarvestRequest` objects. `SynchronousIdentityPipelineService` implements `IHydrationPipelineService` by running the three workers inline for synchronous callers. Three pipeline workers poll for jobs: `RetailMatchWorker` (Stage 1, leases `Queued` → `RetailMatched`/`RetailNoMatch`), `WikidataBridgeWorker` (Stage 2, leases `RetailMatched` → `QidResolved`/`QidNoMatch`, strict retail gate), `QuickHydrationWorker` (leases `QidResolved` → `Completed`). Every candidate is persisted: `retail_match_candidates` and `wikidata_bridge_candidates` tables store full score breakdowns for the Action Center detail drawer. `EnrichmentService` is a thin dispatcher routing to modular workers: `CoverArtWorker`, `PersonEnrichmentWorker`, `ChildEntityWorker`, `FictionalEntityWorker`, `DescriptionEnrichmentWorker`. **Image enrichment (Fanart.tv):** `ImageEnrichmentService` runs during the Universe Pass and fetches rich imagery — backdrops, logos, banners, and character art — from Fanart.tv via direct HTTP (not ConfigDrivenAdapter) for full control over multi-image array responses. Bridge IDs (tmdb_id, tvdb_id, musicbrainz_id) select the correct Fanart.tv endpoint (/movies, /tv, /music). Images are downloaded with SHA-256 hash dedup via `IImageCacheRepository`, stored to disk via `ImagePathService` (new methods: `GetWorkBackdropPath`, `GetWorkLogoPath`, `GetWorkBannerPath`), and tracked as `EntityAsset` records. Backdrop downloads trigger hero banner regeneration via `IHeroBannerGenerator` (higher quality than cover-derived heroes). Character art is fuzzy-matched (≥0.70 threshold via `IFuzzyMatchingService`) against fictional entity labels linked to the work, with matched images stored as `CharacterPortrait` records. Config: `config/providers/fanart_tv.json` (provider_id: `bc00000c-0000-4000-8000-000000000013`, was previously colliding with AiProvider GUID). `WellKnownProviders.FanartTv` constant added. `BridgeIdKeys.TvdbId` constant added. `IPersonRepository.GetCharacterLinksByWorkAsync` and `IFictionalEntityRepository.GetByWorkQidAsync` added for character art matching queries. Helpers extracted from the monolith: `BridgeIdHelper` (P-code mapping), `StageOutcomeFactory` (review item creation), `TimelineRecorder` (event recording), `BatchProgressService` (counter + SignalR), `PersonReferenceExtractor` (person ref extraction). The legacy `HydrationPipelineService` monolith has been removed. See plan: `.claude/plans/modular-launching-wall.md`. **RetailMatchWorker lookup fields:** `ProviderLookupRequest` is fully populated with all media-specific fields — Narrator, ShowName (with Series fallback), Album, Artist, Director, SeasonNumber, EpisodeNumber, TrackNumber, Series, Genre, Isbn, Asin — so provider URL templates (e.g. Metron `{series}`) can resolve correctly across all media types. **Batch counter increments:** `IngestionEngine` calls `IncrementCounterAsync` (atomic SQL `UPDATE SET col = col + 1`) for FilesProcessed after identity job creation and FilesFailed after lock-probe or quarantine failures. `BatchCounterColumn` enum in Domain/Enums. **PostPipelineService auto-resolve:** Expanded trigger set includes RetailMatchAmbiguous, WikidataBridgeFailed, and MissingQid alongside the original RetailMatchFailed, AuthorityMatchFailed, ContentMatchFailed, and LowConfidence — prevents stale review items from blocking organization when confidence has improved. **QID-based hub assignment:** `HubAssignmentService` runs during QuickHydrationWorker after enrichment populates canonical values. It reads `series_qid` (P179), `franchise_qid` (P8345), and `fictional_universe_qid` (P1434) — most specific first — to find or create a ContentGroup Hub backed by a Wikidata QID. Works are assigned via `AssignWorkToHubAsync` (FK on `works.hub_id`). Idempotent — skips works that already have a hub. Standalone works (no series/franchise/universe in Wikidata) remain at `hub_id = NULL`. This replaces the previous display-time-only grouping with real FK-backed containers for book series, TV shows, music albums (when Wikidata has the relationship), and franchises.

### 3.8 — Configuration Architecture
**Detail:** [`docs/architecture/hydration-and-providers.md`](docs/architecture/hydration-and-providers.md) (provider config section)

All settings live in `config/` directory as individual JSON files grouped by concern. One concern per file. Provider files are self-contained. Universe files hold the knowledge model. All config files committed directly in `config/`. Provider secrets (API keys, passwords) go in `config/secrets/` (gitignored). Adding a new REST+JSON provider is a zero-code operation: drop a config file, restart.

**Provider Catalogue Centralisation (implemented):** Provider UI metadata (display names, icons, accent colours, supported media types, language strategy) is centralised in provider config JSON files and exposed via `GET /providers/catalogue`. `ProviderCatalogueService` in the Dashboard caches the catalogue at startup. `ProviderAccentMap` provides static fallbacks during boot. See [`docs/reference/providers.md`](docs/reference/providers.md) for the complete provider reference.

**Configuration Centralisation (implemented):** All scattered configuration has been consolidated into single sources of truth:
- **`WellKnownProviders.cs`** (Domain) — all 16 provider GUIDs, replacing ~15 files of copy-pasted literals
- **`ClaimConfidence.cs`** (Domain) — 22 named constants for the metadata confidence hierarchy (0.70–1.00)
- **`AppRoles.cs`** (Domain) — authorisation role constants bridging `ProfileRole` enum to string comparisons
- **`BridgeIdKeys.cs`** (Domain) — 18 external identifier key constants (isbn, tmdb_id, etc., plus 3 Apple Music constants: AppleMusicId, AppleMusicCollectionId, AppleArtistId), replacing 218 raw strings
- **`MetadataFieldConstants.cs`** (Domain) — extended with 34 single-valued claim key constants (title, author, year, etc.), replacing 500+ raw strings
- **`SignalREvents.cs`** (Domain) — hub path + 16 event name constants, shared between Engine publishers and Dashboard subscribers
- **`MediaTypeClassifier.cs`** (Domain/Services) — single classifier replacing 4 divergent format-to-type implementations
- **`PaletteConfiguration`** (Domain/Models) — ~80 hex colours loaded from `config/ui/palette.json`, enabling seasonal themes
- **`PaletteProvider`** (Web/Theming) — static accessor for palette colours used by VaultHelpers, RegistryHelpers, ThemeService, UniverseMapper
- **`ScoringConfiguration`** — now registered as DI singleton; `IngestionEngine`, `OrganizationGate` read thresholds from config instead of hardcoding
- **`MaintenanceSettings.Schedules`** — all 11 cron expressions (5 background services + hydration pass2 + 5 AI schedules) consolidated from 3 config files into one `schedules` section in `maintenance.json`
- **`RateLimitingSettings`** — rate limiting parameters (key_generation, streaming, general) moved from hardcoded `Program.cs` to `core.json`
- **Hub rules** — normalized JSON predicate arrays (`{field, op, value}` objects) stored in the `rule_json` column on hubs, evaluated by `HubRuleEvaluator`

### 3.9 — Universe Graph & Chronicle Engine
**Detail:** [`docs/architecture/universe-graph.md`](docs/architecture/universe-graph.md)

Builds a relationship graph connecting characters, locations, factions, and works across all media. Entities and relationships stored in SQLite; Tuvima.Wikidata.Graph provides in-memory graph queries via EntityGraph. Person infrastructure includes biographical data, social links (Actionable URI Schemes), pseudonym resolution, and character-performer links. **Roles:** Actor, Voice Actor, Performer, Artist, Composer, Author, Director, Narrator. Illustrator and Screenwriter removed. "Cast Member" renamed to "Actor" in database and code. Performer role is assigned from P175 for music items. Band member expansion (P527) disabled — musical groups stored as single Person with `IsGroup = true`. **People tab filters:** All, Authors (Author+Narrator), Directors, Actors (Actor+Voice Actor), Musicians (Artist+Composer+Performer). Chronicle Engine adds temporal qualifiers, Lore Delta detection, era-correct actors, and canon discrepancy detection. Chronicle Explorer page at `/universe/{Qid}/explore` with Cytoscape.js graph visualization.

### 3.10 — Local AI Intelligence Layer
**Detail:** [`docs/architecture/ai-integration.md`](docs/architecture/ai-integration.md)

AI is a core function, not an add-on. Three model roles: text_fast (1B, on-demand), text_quality (3B, batch), audio (Whisper, transcription). 16 features across 7 categories: Ingestion (Smart Labeling, Media Type Classification, Batch Manifest), Alignment (QID Disambiguation, Series Alignment), Enrichment (Vibe Tags, TL;DR, Audio Similarity), Syncing (Immersive Bake, Subtitle Sync), Personalization (Taste Profiling, "Why" Factor), Discovery (Intent Search), Advanced (URL Paste). GBNF grammar constraints force valid JSON output. AI improves matching; Priority Cascade determines canonical values. **Genre vs Vibe distinction:** genres (from Wikidata/retail) describe *what something is*; vibes (AI-generated, 25–30 per media type) describe *how it feels*. Intent Search combines both for natural language discovery ("something scary set in space" → genre:horror + vibe:tense). Config: `config/ai.json`.

### 3.11 — Settings Architecture & Screen Hierarchy
**Detail:** [`docs/architecture/settings-and-vault.md`](docs/architecture/settings-and-vault.md)

Settings are organised by what the user is thinking about. Three design rules: breaks things → config file only; set once → First-Run Wizard; actively managed → GUI. Five groups: **Preferences** (Profile, Playback), **Providers** (Connections, Priority per media type, Wikidata Stage 2/3 config), **Intelligence** (Models, AI Features, Vibe Vocabulary, Schedule), **Library** (Folders), **Server** (Status, Security, Users, Activity, Maintenance, Setup). 16 settings screens + 5-step wizard + Vault page.

### 3.12 — Library Vault (`/vault`)
**Detail:** [`docs/architecture/settings-and-vault.md`](docs/architecture/settings-and-vault.md)

The command centre for managing everything in the library. **Two-row tab navigation:** Row 1 contains the top-level groups: Media | People | Universes | Action Center (right-aligned). Row 2 is context-aware: Media shows New/Movies/TV/Music/Books/Audiobooks/Podcasts/Comics; People shows All/Authors/Directors/Actors/Musicians; Universes shows All (stub). **Action Center is a tab** (right-aligned in Row 1), amber-colored when items exist, neutral when empty. Navigating to `/vault` routes directly to Action Center when it has items. **Per-media-type tab layout:** New, Movies, TV, Music, Books, Audiobooks, Podcasts, Comics under the Media group. Each media type gets its own tab with metadata-focused columns (no pipeline/status — those live in the detail drawer only). Tab badges show item counts. No sidebar — full-width content area. Hub management has moved to the dedicated Hubs page (`/hubs`). **View modes per tab:** Each media tab supports a flat list view (all individual items) and a container view (shows, albums, series). Default view modes: Movies=flat, TV=shows, Music=artists, Books=flat, Audiobooks=flat, Podcasts=shows, Comics=series. View mode switcher in toolbar. Music has three modes: By Artist (artist→album accordion→tracks), By Album (album containers), All Songs (flat). View mode preferences saved via `GET/PUT /settings/ui/vault-preferences` (`VaultPreferencesSettings` in Storage/Models). **Container data:** Containers are driven by System view hubs (seeded at startup). Clicking a container navigates to `MediaGroupPage.razor` (URL change, back button). Container columns defined in `VaultColumnDefinitions.GetColumnsByTab(tabId, viewMode)` with V3 container column sets using `ColumnRenderType.ContainerCell`. **Action Center** (`VaultActionCenter.razor`): shows "X Items Require Review" (Needs Review + Quarantined + Waiting for Provider). **Registry CTE fix:** `review_data` CTE aggregates at work level (through `media_assets → editions → works` join chain) so reviews match regardless of which asset they target. Status CASE prioritises `InReview` before `Identified`/`Confirmed` to prevent review items from being hidden. Checkboxes for bulk selection with "Delete Selected" bar. No Edit button — title click opens detail drawer. No retry button. Amber-themed for warnings, rose-themed when quarantined items exist. **New tab:** shows recently added items across all media types (last 30 days) with Type badge, Added date, Parent Context (show/series/album), Specs column, and Size column. **Contextual toolbar** per tab: view mode switcher, search, per-tab filter dropdown (quality for movies, format for books, etc.), sort, and column picker trigger. No group-by dropdown. Back button appears during drill-down. **Configurable columns** per media type via `VaultColumnDefinitions.GetColumnsByTab(tabId)` with column picker (toggle/reorder, saved to localStorage). Column sets are metadata-focused: Movies (Director, Year, Specs, Size), TV (Show, Season, Episode, Quality), Music (Artist, Album, Bitrate, Size), Books (Author, Series, Year, Format), Audiobooks (Author, Narrator, Length, Format), Podcasts (Publisher, Episodes, Duration, Genre), Comics (Writer, Series, Year, Format). Specs and Size are separate sortable columns; Size sorts on raw bytes. **Column headers are pinned** — table header is sticky, body scrolls. **Column sorting:** clickable column headers toggle ascending/descending. **Title click opens detail drawer.** No Edit button in rows. **Delete sends items to quarantine** (soft delete). **Multiple authors** are split on "and", "&", and "," and rendered as separate clickable links. `ColumnRenderType.ClickableText` reuses this pattern for any text column (e.g. Artist in Music tab). **TV tab** is always by-show, sorted by recently updated. **Batch selection** with Shift+click range, Ctrl+click individual, floating action bar (Delete, Sync Now). **Drill-down:** Container rows navigate to `MediaGroupPage.razor` with cover art header, metadata, and season/album accordion. TV: season accordion with episodes (titles resolved from `episode_title` canonical value with `title` fallback). Music artists: album accordion with tracks. Other types: flat list. Child rows open detail drawer on title click. **Owned/unowned toggle** in MediaGroupPage header: shows unowned items (episodes/tracks not in library) in gray. Enabled by default, saved to vault preferences. **Music album grid** view with cover art cards and size slider. **Mobile:** tab bar scrolls horizontally. **Detail drawer** slides in from right with pinned header (cover, title, status, Universe link), scrollable collapsible sections (Pipeline with File/Retail/Wikidata tabs, History, Assets), and pinned action bar (Purge). Pipeline section includes inline resolution panels for both Retail and Wikidata stages. **Provisional status** for items the engine couldn't match — file metadata is the authority, user corrections improve future Identify runs. **30-day refresh cycle** re-runs enrichment. **Sync writeback** (on by default) writes resolved metadata back to file tags. **People tab:** always Wikidata-sourced. List shows photo, name, primary role (color-coded), media count, associated types. Sub-tabs: All, Authors (Author+Narrator), Directors, Actors (Actor+Voice Actor), Musicians (Artist+Composer+Performer). Detail drawer: Library Presence, Linked Identities, Members/Member of, Character Roles, Assets. **Universes tab:** franchise-level groupings, always Wikidata-sourced. List shows name+description, Series count, media breakdown, people count. Detail drawer: health score, characters, content, people, assets. **Shared Assets section** across all tabs: five uniform asset types (Cover Art, Headshot, Banner, Logo, Backdrop). Live SignalR updates throughout. **Deprecated components** (scheduled for removal): `VaultSidebar.razor`, `VaultStatsBar.razor`, `VaultMobileSidebarDrawer.razor`, `VaultMediaTable.razor`, `VaultHubsTable.razor`.
**Detail drawer restructured:** Pipeline section now uses three tabs (File / Retail / Wikidata) instead of the old stage panel. File tab shows current resolved metadata + collapsed original file metadata. Retail tab shows match status with always-visible search. Wikidata tab shows QID status with always-visible search. History moved to its own drawer section. `StageGate` supports a `Running` state with pulse animation.

### 3.13 — Universal Parameterized Hub System
**Detail:** [`docs/architecture/hubs-and-playlists.md`](docs/architecture/hubs-and-playlists.md)

Every collection is a **hub** — a parameterized query container with normalized filter predicates stored as JSON arrays of `{field, op, value}` objects. Six hub types as presentation hints: **ContentGroup** (drillable containers — albums, TV shows, book series), **Smart** (auto-generated from library data — by genre, vibe, author, director, narrator, decade, plus Recently Added, Highest Rated, Unrated), **System** (per-user, pre-created — Reading List, Watchlist, Currently Watching, Listening Queue, Favorites), **Mix** (AI-generated per-user — Continue, Heavy Rotation, Discovery Queue, New For You, Because You Liked, Taste Mix, On Repeat, Rediscover), **Playlist** (user-created, materialized), **Custom** (user-created query-resolved collections via hub builder). **Hybrid resolution:** query-resolved hubs (Smart, Custom) evaluate predicates against the data store at display time; materialized hubs (ContentGroup, System, Playlist, Mix) track membership explicitly in `hub_works`. **ContentGroup hub creation disabled:** `MediaEntityChainFactory.EnsureContentGroupAsync` is now a no-op. **System view hubs** (23 hubs, seeded at startup) drive all Vault container views. System view hubs have `resolution = "query"`, a `group_by_field`, and are evaluated at display time via `HubRuleEvaluator`. Full list: Recently Added, All Movies, TV by Show, All TV, Music by Artist, Music by Album, All Songs, All Books, Books by Series, Books by Author, All Audiobooks, Audiobooks by Series, Audiobooks by Author, Audiobooks by Narrator, Podcasts by Show, All Podcasts, All Comics, Comics by Series, Comics by Writer, Movies by Director, Movies by Genre, TV by Genre, Music by Genre. **ContentGroup hub assignment:** `HubAssignmentService` (called by `QuickHydrationWorker` after enrichment) reads Wikidata relationship properties from canonical values — priority: series_qid (P179) > franchise_qid (P8345) > fictional_universe_qid (P1434) — and creates or finds a ContentGroup hub for the parent entity, assigning the work via `hub_id` FK. Canonical values are keyed by MediaAsset entity ID (not Work ID). **Hub seeding guard:** When deciding whether to seed Smart/Mix/Playlist hubs, auto-generated hub types (System, ContentGroup, Universe) are excluded from the count. New Engine action: `GET /hubs/system-views?mediaType=&groupField=` — resolves system hubs and returns grouped results. **Hub placements:** `hub_placements` table maps hubs to UI locations (home, media lanes, vault, hubs page) with display limits and position — location is a display constraint, not hardcoded logic. **Hubs page (`/hubs`):** dedicated page for browsing, creating, and managing all hub types — replaces the former Vault Hubs tab and the former "Collections" name. Hub builder (Apple Music Smart Playlist-inspired) with field+operator+value filter rows, ALL/ANY match mode, nested groups, live preview via `POST /hubs/preview`, limits, and sort controls. **Rule evaluation:** `HubRuleEvaluator` (Storage) translates predicates to SQL with operator support for eq, neq, contains, gt, lt, between, in, like. Fields span identity (title, author, genre, series), Wikidata (QID, director, composer), engagement (rating, play count, completion), temporal (added, published, last played), file (size, duration, quality), and AI (vibe, TL;DR, taste match). **Engine actions:** `GET /hubs/resolve/{id}`, `POST /hubs/preview`, `GET /hubs/by-location/{location}`, `GET /hubs/field-values/{field}`, `GET /hubs/system-views`, full CRUD (`POST/PUT/DELETE /hubs`), `GET/PUT /hubs/{id}/placements`. Migration M-070: hub columns (resolution, rule_json, rule_hash, group_by_field, match_mode, sort_field, sort_direction, live_updating) + `hub_placements` table + backfill. **"Add to..." interaction** unchanged: primary action adds to default list for media type, heart icon toggles Favorites, secondary action opens picker. Hub artwork auto-composed from items; SkiaSharp auto-generates banners; user can upload overrides. **Group-level 30-day refresh:** when any work in a TV series or music album triggers a stale refresh, all sibling works in the same hub are included in the batch for consistency. My Library page (`/my-library`) for personal lists and playlist CRUD. Home page surfaces personalised mixes. Media lane pages surface smart hubs filtered by media type.

### 3.14 — Localization & Multi-Language Support

Six language concerns, addressed in phases: (1) UI language, (2) metadata display language, (3) content language, (4) provider query language, (5) AI working language, (6) search language. **Phase 1 (implemented):** `CoreConfiguration.Language` is now a structured `LanguagePreferences` object with `Display` (UI), `Metadata` (provider queries), `Additional` (accepted content languages), and `AcceptAny` (default true — accepts files in any language). Backward compatible: deserialises from both `"language": "en"` (legacy) and `"language": { "display": "en", ... }` (new). `LanguageMismatch` review trigger is now **informational** (amber banner in Vault) — does NOT block Stage 2 Wikidata enrichment. `ReconciliationAdapter.ReconcileMultiLanguageAsync` searches Wikidata in both the file's detected language and the metadata language, deduplicating by QID. `ProviderLookupRequest.FileLanguage` propagates file language through the hydration pipeline. Settings UI: ProfileTab has Language Preferences section (display language, metadata language, additional languages, accept-any toggle) wired to `GET/PUT /settings/server-general`. **Phase 2 (implemented):** `original_title` metadata field — when file language differs from metadata language, `ReconciliationAdapter.FetchWorkAsync` fetches the Wikidata label in the file's language and emits it as `original_title`. Display-language title is primary; original-language title shown as smaller subtitle in VaultMediaTable. FTS5 full-text search index with 6 columns (`entity_id` UNINDEXED, `title`, `original_title`, `alternate_titles`, `author`, `description`) using `unicode61` tokenizer — migration M-061. `SearchIndexRepository` rewritten for the expanded schema. `ISearchIndexRepository.UpsertByEntityIdAsync` extended with `originalTitle`, `alternateTitles`, `description` parameters. **Phase 3 (implemented):** English remains the AI working language. `IntentSearchParser` detects non-English display language and prepends a translation hint to the LLM prompt for cross-language keyword extraction. Whisper audio model default changed from `"language": "en"` to `"language": "auto"` for automatic language detection. `DescriptionIntelligenceService` and `VibeTagger` documented to note descriptions may arrive in non-English languages. **Phase 4 (implemented):** Full UI localization via standard Blazor `IStringLocalizer<SharedStrings>` pattern. ~465 keys extracted from 35 .razor files across Navigation, Vault, Settings, and Pages. `SharedStrings.resx` (English) + `SharedStrings.fr.resx` (French) + `SharedStrings.de.resx` (German) + `SharedStrings.es.resx` (Spanish). `_Imports.razor` includes `Microsoft.Extensions.Localization` and `MediaEngine.Web.Resources`. Culture synced from `CoreConfiguration.Language.Display` on circuit start via `MainLayout.SyncCultureAsync()` — checks server display language against current UI culture, redirects to `/culture/set` cookie endpoint if mismatched. ProfileTab triggers culture redirect on display language change. English fallback for untranslated strings. **Phase 5 (implemented):** CJK support via `trigram` FTS5 tokenizer (migration M-062) — replaces `unicode61`, handles CJK characters that lack word boundaries. Short queries (&lt;3 chars) fall back to LIKE scan. Wikidata aliases emitted as `alternate_title` claims at 0.85 confidence in `ReconciliationAdapter.FetchWorkAsync` — romanized titles (e.g. "Sen to Chihiro no Kamikakushi") are indexed for search. Qwen 2.5 3B Instruct added as `text_cjk` model role (`AiModelRole.TextCjk`) — auto-downloads only when user has CJK languages in preferences (ja/ko/zh/zh-TW). Available on High and Medium hardware tiers. **Phase 6 (implemented):** Per-provider `language_strategy` config field with three modes: `source` (always English — Google Books, Open Library, MusicBrainz, Metron, Podcast Index), `localized` (user's metadata language — TMDB, Apple API, Apple Podcasts), `both` (query twice, merge — Wikidata). `LanguageStrategy` enum in Domain. `ProviderConfiguration.LanguageStrategyRaw` JSON field parsed to enum. `ConfigDrivenAdapter` resolves effective language before URL building; "both" mode retries in English on empty results and tags claims with `SourceLanguage = "en"`. `ProviderClaim` extended with optional `SourceLanguage` parameter. Silent English fallback — no user-facing error. ProviderPriorityTab drawer has Language Strategy dropdown per provider. SetupTab shows language strategy label per configured provider. All 10 provider config files updated with defaults. Localization keys in all 4 .resx files. Full plan at `.claude/plans/wild-wondering-tide.md`.

### 3.15 — Target State Features
**Detail:** [`docs/architecture/target-state.md`](docs/architecture/target-state.md)

**Not yet implemented:** Playback (EPUB Reader, Comic Viewer, Audiobook Player, Video Player with HLS), Authentication & Multi-User (profiles, PIN/password, parental controls, User Preferences API — column/view preferences stored per-user in database), Transcoding Pipeline (FFmpeg, Shadow Transcoder), Music Domain Model (MusicBrainz, MusicProcessor), Interoperability (OPDS 1.2, Audiobookshelf API, webhooks, import wizard, PWA), Browse & Discovery Pages (UniverseDetail, WorkDetail, PersonDetail, Statistics).

### 3.16 — Supported Library Types

| Library Type | Includes |
|---|---|
| **Books** | Ebooks (EPUB, PDF) + Audiobooks (M4B, MP3) |
| **TV** | Episodic television, web series |
| **Movies** | Feature films, short films |
| **Music** | Albums, singles, tracks |
| **Comics** | CBZ, CBR, PDF comics, manga |
| **Podcasts** | Podcast series and episodes |

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
| Hub (internal) | Series |
| ParentHub (internal) | Universe |

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

**Current approved tools:**

| Tool | License |
|---|---|
| Microsoft.Data.Sqlite | MIT |
| Microsoft.Extensions.* | MIT |
| Microsoft.AspNetCore.* | MIT |
| Microsoft.AspNetCore.SignalR.Client | MIT |
| MudBlazor 9 | MIT |
| VersOne.Epub | MIT |
| Swashbuckle.AspNetCore | MIT |
| xUnit 2 | Apache 2.0 |
| coverlet | MIT |
| Microsoft.Extensions.Http | MIT |
| TagLibSharp | LGPL-2.1 |
| SkiaSharp + NativeAssets.Linux | MIT |
| Cytoscape.js | MIT (vendored) |
| FuzzySharp | MIT |
| Tuvima.Wikidata | MIT |
| Tuvima.Wikidata.AspNetCore | MIT |
| Serilog.AspNetCore | Apache 2.0 |
| Serilog.Sinks.File | Apache 2.0 |
| Microsoft.Extensions.Http.Resilience | MIT |
| Cronos | MIT |
| Dapper | Apache 2.0 |
| LLamaSharp + Backend.Cpu | MIT |
| Whisper.net + Runtime | MIT |
| MkDocs | BSD-2-Clause |
| Material for MkDocs | MIT |

### 5.2 — Mandatory Workflow

**Step 1 — Read before touching anything**
Read `CLAUDE.md`, `README.md`, and every file relevant to the task. Never assume current state.

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

> **Two AI assistants work on this repository.** `CLAUDE.md` is the canonical source of truth. The `.agent/` directory contains supplementary files read by **Antigravity (Gemini)**. Both must stay in sync.

**Sync mapping — `docs/architecture/` → `.agent/` files:**

| Architecture doc | `.agent/` file(s) |
|---|---|
| `ingestion-pipeline.md` | `features/INGESTION-PIPELINE.md` |
| `scoring-and-cascade.md` | `features/METADATA-MANAGEMENT.md`, `skills/METADATA-SCORING.md` |
| `security.md` | `features/API-SECURITY.md`, `features/ROLE-ACCESS-MODEL.md` |
| `dashboard-ui.md` | `features/LIBRARY-DASHBOARD.md`, `skills/DASHBOARD-UI.md` |
| `hydration-and-providers.md` | `features/METADATA-MANAGEMENT.md`, `features/METADATA-PRIORITY.md` |
| `universe-graph.md` | `features/METADATA-MANAGEMENT.md` |
| `settings-and-vault.md` | `features/SETTINGS-OVERVIEW.md` |
| `ai-integration.md` | (new — create matching `.agent/` file) |
| `hubs-and-playlists.md` | `features/HUB-SYSTEM.md` |

After updating any architecture doc, update the corresponding `.agent/` file(s). The `.agent/SYNC-MAP.md` file contains the reverse mapping.

### 5.4 — Documentation Upkeep (Diátaxis)

> **All project documentation follows the [Diátaxis framework](https://diataxis.fr/).** The `docs/` directory is organised into four categories: **Tutorials** (learning-oriented), **How-to Guides** (task-oriented), **Reference** (information-oriented), and **Explanation** (understanding-oriented). The landing page is `docs/index.md`.

**Documentation must be updated when:**

| Trigger | What to update |
|---|---|
| New feature added | Relevant Explanation page + Reference entries + How-to Guide if user-facing |
| New config field added | `docs/reference/configuration.md` |
| New API endpoint added | `docs/reference/api-endpoints.md` |
| New database table or column | `docs/reference/database-schema.md` |
| New file format supported | `docs/reference/media-types.md` |
| New provider added | `docs/reference/configuration.md` (provider section) + `docs/guides/configuring-providers.md` |
| New processor added | `docs/reference/media-types.md` + `docs/guides/writing-a-processor.md` (if pattern changes) |
| Terminology change | `docs/reference/glossary.md` |
| Architecture change | `docs/architecture/*.md` (existing) + corresponding Explanation page |
| New UI screen or Settings tab | `docs/explanation/how-the-vault-works.md` or relevant Explanation page |

**Rules:**
1. Documentation updates are part of Step 4 in the Mandatory Workflow (§5.2). They are not optional.
2. User-facing docs use plain English per §4.1 vocabulary rules. Developer docs may use technical terms.
3. The `docs/index.md` landing page must be updated when new pages are added.
4. Cross-link between related docs: Explanation pages link to Architecture deep-dives. How-to Guides link to relevant Reference pages.
5. Every new Explanation page must be linked from `docs/index.md`.
6. Every published docs page must include front matter with `title`, `summary`, `audience`, `category`, and `product_area`. Use `status: target-state` for future-facing pages.
7. `.codex/` is a gitignored, derived local context layer. Regenerate it with `scripts/docs/refresh-codex-context.ps1`; never edit it by hand or treat it as canonical.

---

## 6. Structure Reference — Feature-Sliced Dashboard Layout

All Dashboard code in `src/MediaEngine.Web/` follows the **Feature-Sliced** pattern. Every new piece of UI code must go into the correct slice.

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
│   │   └── IntercomEvents.cs           SignalR event shapes
│   │
│   ├── Playback/             ← (TARGET STATE) Playback session management
│   │   └── PlaybackStateService.cs     Active session management, progress sync
│   │
│   └── Theming/              ← ALL visual configuration lives here
│       ├── ThemeService.cs             Dark-mode-only theme, colour palette, corner radii
│       └── DeviceContextService.cs     Per-circuit device class + resolved UI settings
│
├── Components/
│   ├── Universe/             ← Universe/Series-related visual components
│   │   ├── HubHero.razor               Cinematic hero: blurred cover art + vignette + badges
│   │   ├── CompactHero.razor           Shared spotlight strip (Portrait/Landscape/Square) used by Read/Watch/Listen
│   │   ├── TrackRow.razor              Spotify-style compact row for Listen's "All Songs" view
│   │   ├── MetadataChips.razor          Multi-valued fields as MudChip elements
│   │   ├── PosterCard.razor            Poster art tile: cover, title, badges
│   │   ├── PosterSwimlane.razor        Horizontal scrolling row of PosterCards
│   │   └── ProgressIndicator.razor     Reusable progress card
│   │
│   ├── Bento/                ← Legacy grid wrappers
│   │   ├── BentoGrid.razor             Legacy CSS grid container
│   │   └── BentoItem.razor             Legacy glassmorphic tile wrapper
│   │
│   ├── Navigation/           ← Navigation and search components
│   │   ├── CommandPalette.razor        Ctrl+K global search
│   │   ├── TopBar.razor                Horizontal top bar (4 variants)
│   │   ├── LeftDock.razor              Icon-only left dock (desktop)
│   │   ├── MobileNavDrawer.razor       Slide-out drawer (mobile)
│   │   └── AppLogo.razor               Inline SVG wordmark logo
│   │
│   ├── Vault/                ← Library Vault page — see docs/architecture/settings-and-vault.md
│   │   ├── VaultPage.razor              Main page: 10-tab layout, Action Center, full-width content
│   │   ├── VaultActionCenter.razor      Collapsible conflict banner (replaces alert badges + stats bar)
│   │   ├── VaultToolbar.razor           Contextual search, per-tab filter/sort, column picker (no group-by)
│   │   ├── VaultConfigurableTable.razor  Shared data-driven table with TypeBadge + drill-down; title click opens drawer, no Edit button in ManageActions
│   │   ├── VaultColumnDefinitions.cs    V2 column configs per tab + GetColumnsByTab dispatcher
│   │   ├── VaultPeopleTable.razor       People tab (restyled, consistent formatting)
│   │   ├── VaultUniversesTable.razor    Universes tab (restyled, consistent formatting)
│   │   ├── VaultHubsTable.razor        [DEPRECATED] — replaced by /hubs Hubs page
│   │   ├── VaultColumnPicker.razor      Column toggle + drag reorder panel
│   │   ├── VaultBatchBar.razor          Floating batch action bar (Delete, Sync)
│   │   ├── MediaGroupPage.razor         Hierarchical sub-page (TV show, album, series)
│   │   ├── VaultMusicGrid.razor         Album card grid view for Music
│   │   ├── VaultDeleteConfirm.razor     Delete confirmation
│   │   ├── PersonBiographyDrawer.razor  People tab detail drawer (biography, presence, assets)
│   │   ├── StageGate.razor              Pipeline stage indicator
│   │   ├── ConfidenceBar.razor          5-segment confidence indicator
│   │   ├── StatusPill.razor             Status badge
│   │   ├── VaultHelpers.cs              Sort/utility functions
│   │   ├── VaultSidebar.razor           [DEPRECATED] — replaced by per-tab navigation
│   │   ├── VaultStatsBar.razor          [DEPRECATED] — replaced by Action Center + tab badges
│   │   ├── VaultMediaTable.razor        [DEPRECATED] — replaced by VaultConfigurableTable
│   │   └── VaultMobileSidebarDrawer.razor [DEPRECATED] — replaced by scrolling tab bar
│   │
│   ├── Settings/             ← Settings tabs (5 groups) — see docs/architecture/settings-and-vault.md
│   │   ├── SettingsSidebar.razor        Sidebar navigation
│   │   ├── ProfileTab.razor             [Preferences] Profile
│   │   ├── PlaybackTab.razor            [Preferences] Playback (TARGET STATE)
│   │   ├── ProvidersTab.razor           [Providers] Connections + priority + Wikidata
│   │   ├── ProviderEditPanel.razor      Reusable provider editing
│   │   ├── ModelsTab.razor              [Intelligence] AI models
│   │   ├── AiFeaturesTab.razor          [Intelligence] AI feature toggles
│   │   ├── VibeVocabularyTab.razor      [Intelligence] Vibe tag lists
│   │   ├── AiScheduleTab.razor          [Intelligence] Cron schedule
│   │   ├── LibrariesTab.razor           [Library] Library Folders
│   │   ├── StatusTab.razor              [Server] Status dashboard
│   │   ├── SecurityTab.razor            [Server] API Keys + security
│   │   ├── UsersTab.razor               [Server] User profiles (TARGET STATE)
│   │   ├── ActivityTab.razor            [Server] Activity log
│   │   ├── MaintenanceTab.razor         [Server] Maintenance
│   │   ├── SetupTab.razor               [Server] Re-run wizard
│   │   └── FirstRunWizard.razor         5-step guided setup
│   │
│   ├── Playback/             ← (TARGET STATE) In-browser media players
│   │   ├── EpubReader.razor, ComicViewer.razor, AudioPlayer.razor, VideoPlayer.razor
│   │
│   ├── Hubs/                 ← Hub browsing, creation, and management
│   │   ├── HubsPage.razor              Hubs page (/hubs): browse/create/manage all hubs (renamed from "Collections")
│   │   ├── HubBuilder.razor            Smart Playlist-style filter builder for custom collections
│   │   ├── HubCard.razor               Hub tile for swimlanes and grids
│   │   ├── HubDetail.razor             Hub detail page (shared by all hub types)
│   │   └── AddToPicker.razor           "Add to..." list/playlist picker overlay
│   │
│   └── Pages/                ← Full-page views (routed)
│       ├── Home.razor                  Personalised dashboard: mixes + system list shortcuts
│       ├── MediaLanePage.razor         Content-type lanes: /books, /video, /music, etc.
│       ├── ReadPage.razor              Browse page /read — Kindle-inspired, Portrait spotlight, green accent
│       ├── WatchPage.razor             Browse page /watch — Netflix-inspired, Landscape spotlight, blue accent
│       ├── ListenPage.razor            Browse page /listen — Spotify-inspired, Square spotlight, purple accent, TrackRow all-songs view
│       ├── MyLibrary.razor             Personal space: system lists + playlists + creation
│       ├── Vault.razor                 Library Vault: unified management (10 tabs)
│       ├── Settings.razor              Unified settings: 16 screens in 5 groups
│       ├── Hubs.razor                  Hubs page: /hubs — browse/create/manage all hubs (renamed from "Collections")
│       ├── ChronicleExplorer.razor     Universe graph explorer
│       ├── UniverseDetail.razor         (TARGET STATE) Universe detail
│       ├── WorkDetail.razor            (TARGET STATE) Work detail
│       ├── PersonDetail.razor          (TARGET STATE) Person detail
│       ├── Statistics.razor            (TARGET STATE) Library stats
│       ├── Login.razor                 (TARGET STATE) Profile login
│       └── NotFound.razor              404 page
│
├── Models/ViewDTOs/          ← Data shapes used ONLY by the Dashboard
│
└── Shared/                   ← Top-level layout shell
    ├── MainLayout.razor                App chrome: TopBar + LeftDock + MobileNavDrawer
    ├── NavMenu.razor                   Deprecated stub
    └── _Imports.razor                  Namespace imports
```

**Rules for adding new code:**

| New code type | Where it goes |
|---|---|
| Engine call | `Services/Integration/LibraryApiClient.cs` + interface |
| Dashboard data shape | `Models/ViewDTOs/` |
| Reusable visual component | `Components/<FeatureName>/` |
| Full page | `Components/Pages/` |
| Vault sub-component | `Components/Vault/` |
| Hub browsing/builder component | `Components/Hubs/` |
| Settings tab | `Components/Settings/{GroupName}Tab.razor` |
| Media player | `Components/Playback/` |
| Playback service | `Services/Playback/` |
| Layout wrapper | `Shared/` |
| Theme setting | `Services/Theming/ThemeService.cs` |
| Device feature flag | `Services/Theming/DeviceContextService.cs` |

---

## 7. Project Contacts

| Role | Detail |
|---|---|
| Product Owner | Shaya |
| Repository | [github.com/shyfaruqi/tuvima-library](https://github.com/shyfaruqi/tuvima-library) |
| License | AGPLv3 |
| Engine base URL (local dev) | `http://localhost:61495` |
| Dashboard URL (local dev) | `http://localhost:5016` |
