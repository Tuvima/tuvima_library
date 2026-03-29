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
| Wikidata/Wikipedia API client | Tuvima.WikidataReconciliation | Unified client for Wikidata reconciliation, entity fetching, Wikipedia summaries, image URLs |
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

You configure Library Folders — each tells the Engine where to look and what media to expect. Watch mode monitors for new files and auto-moves them. Import mode scans existing collections. Files go through: Settle → Lock Check → Fingerprint (SHA-256) → Scan (processor reads metadata) → Identify (scoring) → Stage in `.staging/` → Hydrate → Promote to organized library. All files land in `.staging/` first; only hydrated files with sufficient confidence (≥0.85) get promoted. Ambiguous media types (MP3, MP4) are classified via AI and heuristic signals. Config: `config/libraries.json`, `config/disambiguation.json`.

### 3.2 — Priority Cascade Engine
**Detail:** [`docs/architecture/scoring-and-cascade.md`](docs/architecture/scoring-and-cascade.md)

When multiple sources disagree about metadata, the Priority Cascade resolves disputes. Each piece of metadata is a Claim with a source and confidence weight. Four tiers evaluated in order: **Tier A** (user locks always win) → **Tier B** (per-field provider priority from config) → **Tier C** (Wikidata authority) → **Tier D** (highest confidence wins). AI improves matching quality (SmartLabeler, QidDisambiguator) but does NOT replace the cascade — Wikidata remains the authority for canonical data. All claims are append-only; history is never lost. Config: `config/scoring.json`.

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

### 3.6 — Hydration Pipeline & Providers
**Detail:** [`docs/architecture/hydration-and-providers.md`](docs/architecture/hydration-and-providers.md)

Two-stage enrichment runs after ingestion. **Stage 1 (RetailIdentification):** retail providers gather cover art, descriptions, ratings, and bridge IDs (ISBN, ASIN, TMDB ID) in waterfall order. Retail providers are a rich data source for matching — descriptions, narrator data via AI extraction, ratings — all used to rank candidates against file metadata. **Stage 2 (WikidataBridge):** Wikidata uses bridge IDs for precise QID resolution and fetches canonical properties — including person properties P50 (author), P57 (director), P58 (screenwriter), P86 (composer), P110 (illustrator), and P161 (cast member, capped at 20). Wikipedia descriptions included. Wikidata is the authority for canonical data; retail providers supply matching data. **Standalone Person Reconciliation:** After Stage 2, any person name from file metadata or structured properties that lacks a companion QID is searched independently on Wikidata — filtered to Q5 (human), scored by occupation match and notable work, auto-accepted at ≥0.80 confidence (0.80 for file metadata, 0.75 for AI-extracted). Person freshness check: existing persons enriched within 30 days skip re-fetch; stale persons check `last_revision_id` before full property fetch. Two-pass enrichment: Pass 1 (quick, immediate) gets files on Dashboard fast; Pass 2 (universe, background/scheduled) does deep enrichment. Provider response caching eliminates redundant API calls. Config: `config/hydration.json`, `config/providers/*.json`, `config/slots.json`.

### 3.7 — Configuration Architecture
**Detail:** [`docs/architecture/hydration-and-providers.md`](docs/architecture/hydration-and-providers.md) (provider config section)

All settings live in `config/` directory as individual JSON files grouped by concern. One concern per file. Provider files are self-contained. Universe files hold the knowledge model. Example files committed in `config.example/`; live files gitignored. Adding a new REST+JSON provider is a zero-code operation: drop a config file, restart.

### 3.8 — Universe Graph & Chronicle Engine
**Detail:** [`docs/architecture/universe-graph.md`](docs/architecture/universe-graph.md)

Builds a relationship graph connecting characters, locations, factions, and works across all media. Entities and relationships stored in SQLite; dotNetRDF provides in-memory SPARQL queries. Person infrastructure includes biographical data, social links (Actionable URI Schemes), pseudonym resolution, and character-performer links. Chronicle Engine adds temporal qualifiers, Lore Delta detection, era-correct actors, and canon discrepancy detection. Chronicle Explorer page at `/universe/{Qid}/explore` with Cytoscape.js graph visualization.

### 3.9 — Local AI Intelligence Layer
**Detail:** [`docs/architecture/ai-integration.md`](docs/architecture/ai-integration.md)

AI is a core function, not an add-on. Three model roles: text_fast (1B, on-demand), text_quality (3B, batch), audio (Whisper, transcription). 16 features across 7 categories: Ingestion (Smart Labeling, Media Type Classification, Batch Manifest), Alignment (QID Disambiguation, Series Alignment), Enrichment (Vibe Tags, TL;DR, Audio Similarity), Syncing (Immersive Bake, Subtitle Sync), Personalization (Taste Profiling, "Why" Factor), Discovery (Intent Search), Advanced (URL Paste). GBNF grammar constraints force valid JSON output. AI improves matching; Priority Cascade determines canonical values. **Genre vs Vibe distinction:** genres (from Wikidata/retail) describe *what something is*; vibes (AI-generated, 25–30 per media type) describe *how it feels*. Intent Search combines both for natural language discovery ("something scary set in space" → genre:horror + vibe:tense). Config: `config/ai.json`.

### 3.10 — Settings Architecture & Screen Hierarchy
**Detail:** [`docs/architecture/settings-and-vault.md`](docs/architecture/settings-and-vault.md)

Settings are organised by what the user is thinking about. Three design rules: breaks things → config file only; set once → First-Run Wizard; actively managed → GUI. Five groups: **Preferences** (Profile, Playback), **Providers** (Connections, Priority per media type, Wikidata Stage 2/3 config), **Intelligence** (Models, AI Features, Vibe Vocabulary, Schedule), **Library** (Folders), **Server** (Status, Security, Users, Activity, Maintenance, Setup). 16 settings screens + 5-step wizard + Vault page.

### 3.11 — Library Vault (`/vault`)
**Detail:** [`docs/architecture/settings-and-vault.md`](docs/architecture/settings-and-vault.md)

The command centre for managing everything in the library. Four tabs: Media, People, Universes, Hubs. **Sidebar-driven layout (Option C):** pinned header (tabs + media type chips + toolbar) at top, collapsible sidebar (200px, icon-only at 48px) on left with context-aware views, scrollable content area on right. Sidebar views change per media type — Books: All/Series/Authors, Music: All/Albums/Artists/Genres, TV: All Shows (seasons expand inline), etc. "Recently Added" and "Needs Review" always pinned at top of sidebar. **Configurable columns** per media type with column picker (toggle/reorder, saved to localStorage). **Batch selection** with Shift+click range, Ctrl+click individual, group-header select-all, floating action bar (Delete, Sync Now). **Music album grid** view with cover art cards and size slider. **Mobile:** sidebar becomes slide-out drawer triggered by filter icon. Alert banners for items needing review (amber) or quarantined (red). Toolbar with search/sort/group/filters; media type count bar doubles as filters. **Media list view:** each row shows thumbnail, title+creator, Universe link, pipeline dots (mouseover for detail), status pill (Verified/Provisional/Needs Review/Quarantined/Pending). Problem rows get an extra line explaining why. **Detail drawer** slides in from right with pinned header (cover, title, status, Universe link), scrollable collapsible sections (Sync, Enrichment, Pipeline, File, Claims), and pinned action bar (Identify, Sync Now, Purge). Pipeline section includes inline resolution panels for both Retail and Wikidata stages — pick candidates, search manually, or Add Provisional (pre-populated from file metadata, user corrects fields). **Provisional status** for items the engine couldn't match — file metadata is the authority, user corrections improve future Identify runs. **30-day refresh cycle** re-runs enrichment to catch updated provider/Wikidata data. **Sync writeback** (on by default) writes resolved metadata back to file tags. **People tab:** always Wikidata-sourced, no status column needed. List shows photo, name+description, role chips, library presence counts. Detail drawer: Library Presence (works grouped by role/media type), Linked Identities (pseudonym merges), and Assets. No manual actions — enrichment via 30-day refresh cycle. Auto-cleaned when associated media removed. **Universes tab:** franchise-level groupings, always Wikidata-sourced. Stats bar shows Universe count and total Series. List shows name+description, Series count, media breakdown, people count. Detail drawer: Series list (clickable → filters Media tab), People, and Assets. No manual actions — 30-day refresh, auto-cleaned when child Series empty. **Shared Assets section** across all three tabs: five uniform asset types (Cover Art, Headshot, Banner, Logo, Backdrop) available on every entity for uniformity — same slots everywhere, providers fill what they can, user uploads the rest. TMDB auto-categorises images by type; book/audio providers return Cover Art only; Wikidata provides Headshots for People. Grouped by type, with preferred selection, user upload, and source labelling. Embedded original artwork always preserved. AI artwork matching uses embedded cover art as a visual signal during retail identification. Live SignalR updates throughout.

### 3.12 — Hubs & Playlists
**Detail:** [`docs/architecture/hubs-and-playlists.md`](docs/architecture/hubs-and-playlists.md)

Four hub types: **Smart Hubs** (auto-generated from library data — by genre, vibe, author, director, narrator, decade, plus Recently Added, Highest Rated, Unrated), **System Lists** (per-user, pre-created — Reading List, Watchlist, Currently Watching, Listening Queue, Favorites), **Personalised Mixes** (AI-generated per-user — Continue, Heavy Rotation, Discovery Queue, New For You, Because You Liked, Taste Mix, On Repeat, Rediscover), **Playlists** (user-created, any media type — two sub-types: Manual and Smart). Smart hubs are library-scoped and read-only (disable/enable, feature, adjust thresholds in Vault). System lists, mixes, and playlists are per-user. All share the same visual pattern but differ in creation, ownership, and editability. **Smart Playlists** use a rule builder (field + operator + value) with AND/OR logic, nested groups, optional limits, and sort controls — items auto-populate from rules. Fields span metadata (title, creator, genre, vibe, series, universe, format), engagement (user rating, provider rating, play count, completion status, list membership), dates (added, published, last played, completed), file properties (size, duration, quality), and AI-derived signals (TL;DR text, vibe similarity, taste match percentage). Live preview shows matching items as rules are built. "Add to..." interaction: primary action adds to default list for media type (Reading List for books, Watchlist for movies, etc.), heart icon toggles Favorites, secondary action opens picker for other lists/playlists. Hub artwork auto-composed from items; SkiaSharp auto-generates banners; user can upload overrides. Managed via Vault Hubs tab (fourth tab — oversight and configuration). My Library page (/my-library) for personal lists and playlist CRUD (including smart playlist rule builder). Home page surfaces personalised mixes. Media lane pages surface smart hubs filtered by media type.

### 3.13 — Localization & Multi-Language Support

Six language concerns, addressed in phases: (1) UI language, (2) metadata display language, (3) content language, (4) provider query language, (5) AI working language, (6) search language. **Phase 1 (implemented):** `CoreConfiguration.Language` is now a structured `LanguagePreferences` object with `Display` (UI), `Metadata` (provider queries), `Additional` (accepted content languages), and `AcceptAny` (default true — accepts files in any language). Backward compatible: deserialises from both `"language": "en"` (legacy) and `"language": { "display": "en", ... }` (new). `LanguageMismatch` review trigger is now **informational** (amber banner in Vault) — does NOT block Stage 2 Wikidata enrichment. `ReconciliationAdapter.ReconcileMultiLanguageAsync` searches Wikidata in both the file's detected language and the metadata language, deduplicating by QID. `ProviderLookupRequest.FileLanguage` propagates file language through the hydration pipeline. Settings UI: ProfileTab has Language Preferences section (display language, metadata language, additional languages, accept-any toggle) wired to `GET/PUT /settings/server-general`. **Phase 2 (implemented):** `original_title` metadata field — when file language differs from metadata language, `ReconciliationAdapter.FetchWorkAsync` fetches the Wikidata label in the file's language and emits it as `original_title`. Display-language title is primary; original-language title shown as smaller subtitle in VaultMediaTable. FTS5 full-text search index with 6 columns (`entity_id` UNINDEXED, `title`, `original_title`, `alternate_titles`, `author`, `description`) using `unicode61` tokenizer — migration M-061. `SearchIndexRepository` rewritten for the expanded schema. `ISearchIndexRepository.UpsertByEntityIdAsync` extended with `originalTitle`, `alternateTitles`, `description` parameters. **Phase 3 (implemented):** English remains the AI working language. `IntentSearchParser` detects non-English display language and prepends a translation hint to the LLM prompt for cross-language keyword extraction. Whisper audio model default changed from `"language": "en"` to `"language": "auto"` for automatic language detection. `DescriptionIntelligenceService` and `VibeTagger` documented to note descriptions may arrive in non-English languages. **Phase 4 (implemented):** Full UI localization via standard Blazor `IStringLocalizer<SharedStrings>` pattern. ~465 keys extracted from 35 .razor files across Navigation, Vault, Settings, and Pages. `SharedStrings.resx` (English) + `SharedStrings.fr.resx` (French) + `SharedStrings.de.resx` (German) + `SharedStrings.es.resx` (Spanish). `_Imports.razor` includes `Microsoft.Extensions.Localization` and `MediaEngine.Web.Resources`. Culture synced from `CoreConfiguration.Language.Display` on circuit start via `MainLayout.SyncCultureAsync()` — checks server display language against current UI culture, redirects to `/culture/set` cookie endpoint if mismatched. ProfileTab triggers culture redirect on display language change. English fallback for untranslated strings. **Phase 5 (implemented):** CJK support via `trigram` FTS5 tokenizer (migration M-062) — replaces `unicode61`, handles CJK characters that lack word boundaries. Short queries (&lt;3 chars) fall back to LIKE scan. Wikidata aliases emitted as `alternate_title` claims at 0.85 confidence in `ReconciliationAdapter.FetchWorkAsync` — romanized titles (e.g. "Sen to Chihiro no Kamikakushi") are indexed for search. Qwen 2.5 3B Instruct added as `text_cjk` model role (`AiModelRole.TextCjk`) — auto-downloads only when user has CJK languages in preferences (ja/ko/zh/zh-TW). Available on High and Medium hardware tiers. **Phase 6 (implemented):** Per-provider `language_strategy` config field with three modes: `source` (always English — Google Books, Open Library, MusicBrainz, Metron, Podcast Index), `localized` (user's metadata language — TMDB, Apple API, Apple Podcasts), `both` (query twice, merge — Wikidata). `LanguageStrategy` enum in Domain. `ProviderConfiguration.LanguageStrategyRaw` JSON field parsed to enum. `ConfigDrivenAdapter` resolves effective language before URL building; "both" mode retries in English on empty results and tags claims with `SourceLanguage = "en"`. `ProviderClaim` extended with optional `SourceLanguage` parameter. Silent English fallback — no user-facing error. ProviderPriorityTab drawer has Language Strategy dropdown per provider. SetupTab shows language strategy label per configured provider. All 10 provider config files updated with defaults. Localization keys in all 4 .resx files. Full plan at `.claude/plans/wild-wondering-tide.md`.

### 3.14 — Target State Features
**Detail:** [`docs/architecture/target-state.md`](docs/architecture/target-state.md)

**Not yet implemented:** Playback (EPUB Reader, Comic Viewer, Audiobook Player, Video Player with HLS), Authentication & Multi-User (profiles, PIN/password, parental controls, User Preferences API — column/view preferences stored per-user in database), Transcoding Pipeline (FFmpeg, Shadow Transcoder), Music Domain Model (MusicBrainz, MusicProcessor), Interoperability (OPDS 1.2, Audiobookshelf API, webhooks, import wizard, PWA), Browse & Discovery Pages (UniverseDetail, WorkDetail, PersonDetail, Statistics).

### 3.15 — Supported Library Types

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
| dotNetRDF | MIT |
| FuzzySharp | MIT |
| Tuvima.WikidataReconciliation | MIT |
| Tuvima.WikidataReconciliation.AspNetCore | MIT |
| Serilog.AspNetCore | Apache 2.0 |
| Serilog.Sinks.File | Apache 2.0 |
| Microsoft.Extensions.Http.Resilience | MIT |
| Cronos | MIT |
| Dapper | Apache 2.0 |
| LLamaSharp + Backend.Cpu | MIT |
| Whisper.net + Runtime | MIT |

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

**Step 5 — Commit and push**
```bash
git add <specific files>
git commit -m "Short summary

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
git push
```

**Never commit:** `tuvima_master.json`, `*.db`, `bin/`, `obj/`, `.vs/`, `.idea/`, `appsettings.*.json` with real keys.

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
| `hubs-and-playlists.md` | (new — create matching `.agent/` file) |

After updating any architecture doc, update the corresponding `.agent/` file(s). The `.agent/SYNC-MAP.md` file contains the reverse mapping.

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
│   │   ├── VaultPage.razor              Main page: header, tabs, toolbar, content, overlays
│   │   ├── VaultToolbar.razor           Search, sort, group, view mode, filters
│   │   ├── VaultMediaTable.razor        List view with grouping
│   │   ├── VaultMediaGrid.razor         Grid/card view
│   │   ├── VaultMediaCard.razor         Individual media card
│   │   ├── VaultPeopleTable.razor       People tab
│   │   ├── VaultUniversesTable.razor    Universes tab
│   │   ├── VaultHubsTable.razor        Hubs tab (smart hubs, lists, mixes, playlists)
│   │   ├── VaultResolutionOverlay.razor Resolution panel for review items
│   │   ├── VaultDeleteConfirm.razor     Delete confirmation
│   │   ├── VaultMobileDetail.razor      Mobile detail view
│   │   ├── VaultSidebar.razor            Collapsible sidebar with context-aware views
│   │   ├── VaultConfigurableTable.razor  Shared data-driven table from column defs
│   │   ├── VaultColumnDefinitions.cs    Column configs per media type
│   │   ├── VaultColumnPicker.razor      Column toggle + drag reorder panel
│   │   ├── VaultBatchBar.razor          Floating batch action bar (Delete, Sync)
│   │   ├── VaultMusicGrid.razor         Album card grid view for Music
│   │   ├── VaultMobileSidebarDrawer.razor Mobile slide-out sidebar drawer
│   │   ├── StageGate.razor              Pipeline stage indicator
│   │   ├── ConfidenceBar.razor          5-segment confidence indicator
│   │   ├── StatusPill.razor             Status badge
│   │   └── VaultHelpers.cs              Sort/utility functions
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
│   ├── Hubs/                 ← Hub browsing and display components
│   │   ├── HubCard.razor               Hub tile for swimlanes and grids
│   │   ├── HubDetail.razor             Hub detail page (shared by all hub types)
│   │   └── AddToPicker.razor           "Add to..." list/playlist picker overlay
│   │
│   └── Pages/                ← Full-page views (routed)
│       ├── Home.razor                  Personalised dashboard: mixes + system list shortcuts
│       ├── MediaLanePage.razor         Content-type lanes: /books, /video, /music, etc.
│       ├── MyLibrary.razor             Personal space: system lists + playlists + creation
│       ├── Vault.razor                 Library Vault: unified management (4 tabs)
│       ├── Settings.razor              Unified settings: 16 screens in 5 groups
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
| Hub browsing component | `Components/Hubs/` |
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
