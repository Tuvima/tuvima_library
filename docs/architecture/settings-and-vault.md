# Settings Architecture & Library Vault

## Settings Design Principles

Every setting falls into one of four categories that determine how it is exposed:

1. **Breaks things when changed** — config file only. Not surfaced in the GUI. Changing these requires a deliberate edit to the relevant JSON file and a restart.
2. **Set once** — captured during the First-Run Wizard. Accessible again via Server > Setup if a re-run is needed.
3. **Actively managed** — surfaced in the Settings GUI for ongoing use.
4. **Group by user thinking, not technical subsystem** — settings are grouped by what the user is trying to do, not by which internal component owns the data.

---

## First-Run Wizard

Runs automatically on first launch. Re-runnable from **Server > Setup**.

**5 steps:**

1. **Welcome / Identity** — display name, locale, date/time format
2. **Library Folders** — configure source paths, library root, category, media types, and intake mode (watch or import) for each folder
3. **Provider API Keys** — enter optional free API keys for providers that require registration (TMDB, Comic Vine, Podcast Index, Google Books)
4. **AI Models** — select which text and audio models to download; shows RAM requirements; triggers background download
5. **Ready** — summary of configuration choices; launches the Engine and opens the Dashboard

---

## Settings Groups

### Preferences (All Users)

Available to every user regardless of role.

**Profile** — Display name, avatar color selection, date and time format.

**Playback** _(planned)_ — Playback speed defaults, skip interval, subtitle preferences.

---

### Providers (Administrator)

**Provider Connections** — One card per configured provider. Each card shows: provider name, health status indicator, enabled toggle, and an expand button for detailed configuration (endpoint URL, API key entry, timeout, per-field trust weights, test connection button).

**Provider Priority per Media Type** — Drag-and-drop slot assignment sourced from `config/slots.json`. Three priority slots per media type (Primary, Secondary, Tertiary). All providers appear in every media-type tab — dragging creates a copy of the provider into the slot; the provider remains in the available pool. Users can mix providers freely across media types.

**Wikidata Configuration** — Controls for Stage 2 (identity resolution) and Stage 3 (universe data) behavior. Includes enable/disable toggles and confidence thresholds for automated acceptance.

---

### Intelligence (Administrator)

**Models** — One row per AI model role (text_fast, text_quality, audio). Shows model name, current status (not downloaded / downloaded / loaded), RAM usage when loaded, and action buttons: Download, Load, Unload, Delete.

**AI Features** — 16 toggle switches grouped by trigger type (Ingestion, Alignment, Enrichment, Syncing, Personalization, Discovery, Advanced). Allows individual features to be disabled without affecting others.

**Vibe Vocabulary** — Per-media-type editable tag lists used by the Vibe Tagger feature. Each media type has its own vocabulary that can be extended or pruned.

**AI Schedule** — Cron expression editor for background AI services (Vibe batch job at 4 AM daily, Series Alignment at 3 AM daily, Taste Profile on Sunday at 5 AM). Each job can be individually enabled, disabled, or rescheduled.

---

### Library (Administrator)

**Library Folders** — The same configuration as Wizard step 2, available for ongoing management: add new folders, change intake mode, update source paths, change the library root. Per-folder status shows file counts and last scan time.

Note: staging management, review queue, and quarantine are handled by the Vault page, not here.

---

### Server (Administrator)

**Status Dashboard** — Live Engine health: uptime, database size, active background jobs, memory usage, SignalR connection count.

**Security & API Keys** — Generate new guest keys with role assignment and label. View all active keys with creation date and last-used timestamp. Revoke individual keys.

**Users** _(planned)_ — Per-profile management: create profiles, assign PINs or passwords, set maturity filters.

**Activity Log** — Date-grouped timeline of all Engine actions (ingestion, metadata hydration, review resolution, pruning). Configurable retention period. Load-more pagination.

**Maintenance** — Manual triggers for database vacuum, cache purge, shadow storage cleanup, and expired provider response cache eviction.

**Setup Wizard** — Re-runs the First-Run Wizard from step 1.

---

## Config-File-Only Settings

The following settings are intentionally not exposed in the GUI. They are edited directly in their respective JSON files and take effect on restart.

| Config file | What it controls |
|-------------|-----------------|
| `config/scoring.json` | Confidence thresholds, field count scaling multiplier, auto-link threshold |
| `config/field_normalization.json` | ISBN checksum validation rules, identifier alias mappings |
| `config/description_matching.json` | Candidate re-ranking match types, field weights, extract-then-compare patterns |
| `config/disambiguation.json` | Media type heuristic weights, duration bands, bitrate thresholds, path keywords, TV filename patterns, auto-assign and review thresholds |
| Provider config deep fields | Per-provider: throttle delay, max concurrency, cache TTL, individual field weights beyond the UI sliders |
| `config/universe/wikidata.json` | Full Wikidata property map, bridge lookup priority order, value transform assignments, scope exclusions, instance_of class mappings |
| `config/writeback.json` | Embedded metadata writeback behavior: which fields are written back to file tags, and in which format |
| `config/signal_extraction.json` | Description signal extraction regex patterns, role assignments, Wikidata occupation class identifiers for verification |
| `config/ui/devices/{class}.json` | Device profile constraints — modifying these changes what entire device classes can see and do |
| UI global deep fields | Feature flag overrides, shell dimension constants (dock width, topbar height), scroll behavior |
| `config/transcoding.json` | Quality profiles, hardware preference, max concurrent transcode jobs, shadow storage size limit |
| Organization templates | Per-media-type folder structure templates (e.g. TV season/episode path format, Music artist/album nesting) |

---

## Screen Inventory

16 settings screens in total, plus the First-Run Wizard and the Vault page:

| Group | Screen |
|-------|--------|
| Preferences | Profile |
| Preferences | Playback _(planned)_ |
| Providers | Provider Connections |
| Providers | Provider Priority |
| Providers | Wikidata Configuration |
| Intelligence | Models |
| Intelligence | AI Features |
| Intelligence | Vibe Vocabulary |
| Intelligence | AI Schedule |
| Library | Library Folders |
| Server | Status Dashboard |
| Server | Security & API Keys |
| Server | Users _(planned)_ |
| Server | Activity Log |
| Server | Maintenance |
| Server | Setup Wizard |
| — | First-Run Wizard (5 steps) |
| — | Library Vault (`/vault`) |

---

## Library Vault (`/vault`)

The Vault is the command centre for managing every file the Engine has ever seen — staged, verified, quarantined, or under review. It is separate from Settings and operates as a full-page management interface.

### Three Tabs

**Media** — All media assets in the pipeline and library.

**People** — All Person records: authors, narrators, directors, cast members, and their enrichment status.

**Universes** — All fictional universes, their entity counts, and universe graph population status.

### Pipeline Header

Persistent progress bar across the top of the Vault showing the current state of the ingestion and hydration pipeline: files in staging, files currently being hydrated, and the queue depth for each stage.

### Alert Banners

- **Amber** — items in the review queue requiring attention (low confidence, ambiguous media type, multiple QID candidates)
- **Red** — quarantined items (failed hydration, unresolvable identity, file errors)

### Toolbar

Search bar, sort selector, group-by selector, view mode toggle (table / grid), and filter chips (by status, by media type, by stage gate state).

### Media Table / Grid

Each item in the Vault displays:

**Confidence segments** — a 5-bar visual indicator showing the overall confidence score broken into segments (very low / low / medium / high / verified).

**VaultStatus badge** — one of four states:
- `Verified` — QID confirmed, all three stage gates passed
- `NeedsReview` — in the review queue
- `Failed` — hydration failed; no QID could be resolved
- `Quarantined` — file error or unresolvable state; blocked from further processing

**Stage gate indicators** — three icon indicators showing which pipeline stages have completed for this item:
- `Retail` — Stage 1 (RetailIdentification) completed
- `Wikidata` — Stage 2 (WikidataBridge) completed; QID confirmed
- `Universe` — Stage 3 (universe graph population) completed

### Overlays

**VaultResolutionOverlay** — opens when the user clicks to resolve a NeedsReview item. Shows current metadata vs. proposed Wikidata match. For disambiguation items, presents a card grid of QID candidates with labels and descriptions. The user picks a candidate and clicks Resolve, or dismisses the item.

**VaultDeleteConfirm** — confirmation dialog before any destructive action. Shows the file path, current status, and a plain-English description of what will be deleted.

### Live Updates

All Vault state is kept current via SignalR. When the Engine ingests a file, advances a stage gate, moves an item to the review queue, or quarantines a file, the Vault updates instantly without a page refresh.
