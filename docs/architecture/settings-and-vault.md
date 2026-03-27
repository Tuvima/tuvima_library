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

The Vault is the command centre for managing every file the Engine has ever seen — staged, verified, provisional, quarantined, or under review. It is separate from Settings and operates as a full-page management interface.

### Four Tabs

**Media** — All media assets in the pipeline and library.

**People** — All Person records: authors, narrators, directors, cast members. People are always Wikidata-sourced and exist as a byproduct of media — they are created when the engine identifies a person in your library's metadata. People are automatically cleaned up when all their associated media falls out of the library.

**Universes** — All fictional universes (user-facing name for franchise-level groupings), their entity counts, and universe graph population status.

**Hubs** — All smart hubs, system lists, personalised mixes, and playlists. Oversight and configuration of the presentation layer. See `docs/architecture/hubs-and-playlists.md` for full specification.

### Pipeline Header

Persistent progress bar across the top of the Vault showing the current state of the ingestion and hydration pipeline: files in staging, files currently being hydrated, and the queue depth for each stage.

### Alert Banners

- **Amber** — items in the review queue requiring attention (ambiguous match, multiple candidates, classification uncertainty)
- **Red** — quarantined items (failed ingestion, file integrity errors, unresolvable state)

### Toolbar

Search bar, sort selector, group-by selector, and filter chips (by status, by media type, by stage gate state). The media type count bar at the top of the page doubles as a filter — tapping a media type filters the list.

### Add Media

The "+ Add Media" button allows manual file addition for edge cases not covered by watched folder auto-ingestion.

---

### Media List View

Each row in the media list is designed for **scanning and triaging** — answering "what is this, and does it need attention?"

#### List Columns

| Column | Content | Width |
|--------|---------|-------|
| Thumbnail | Cover art with media type icon overlay. Missing cover shows placeholder. Format chip visible on poster. | Narrow, fixed |
| Item | Title (bold) + Creator (uppercase, smaller). Problem items get a third line with the reason. | Flex, fills available space |
| Universe | Clickable link to the Universe detail page, or empty for standalone items. "Unlinked" for items not yet grouped. | Medium |
| Pipeline | Three dot indicators (Retail · Wikidata · Universe). Colour shows stage state. Mouseover reveals stage name and detail. | Narrow, fixed |
| Status | Single pill badge, colour-coded by state. | Narrow, fixed |

#### Status Taxonomy

| Status | Colour | Meaning | User action |
|--------|--------|---------|-------------|
| **Verified** | Green | Fully enriched, all stages complete | None — it's done |
| **Provisional** | Blue | User-provided or file-embedded metadata, not engine-verified | Optional — run Identify when ready |
| **Needs Review** | Amber | Ambiguous match or missing data | Click to resolve |
| **Quarantined** | Red | Failed ingestion or file integrity issue | Click to investigate |
| **Pending** | Grey | Queued or still processing through the pipeline | Wait |

#### Colour Language

The same five colours are used consistently across the Vault — status pills, pipeline stage dots, and alert banners all follow this palette:

| Colour | Meaning |
|--------|---------|
| **Green** | Complete / healthy — no action needed |
| **Blue** | Provisional — user-provided, not engine-verified |
| **Amber** | Attention needed — ambiguous or incomplete |
| **Red** | Problem — failed or blocked |
| **Grey** | Waiting — queued or in progress |

Pipeline stage dots use the same colours: green (stage complete), grey (queued / not yet run), amber (stage needs review), red (stage failed).

#### Row Behaviour

- **Healthy items (Verified, Provisional):** Two-line row — Title + Creator. Clean, compact.
- **Problem items (Needs Review, Quarantined):** Three-line row — Title + Creator + reason line explaining why the item is flagged (e.g. "Multiple conflicting titles found in folder", "Header mismatch — possible corrupt file").
- **Clicking a row** opens the Detail Drawer.

#### Useful Filters and Grouping

- **"What needs my attention?"** — filter to Needs Review + Quarantined
- **"What's unlinked?"** — filter to items with no Universe assignment
- **"What's incomplete?"** — filter by missing stage gate
- **Group by Universe** — see your library as the engine groups it, spot mis-groupings
- **Group by Library Folder** — manage by source
- **Group by Media Type** — work through one category at a time

---

### Media Detail Drawer

Slides in from the right when a row is clicked. Fixed width, full viewport height. Three fixed zones: pinned header (top), scrollable body (middle), pinned action bar (bottom).

#### Pinned Header (never scrolls)

Always visible at the top of the drawer:

- **Cover art** — large thumbnail. Provisional items show the file's embedded cover or user upload. No cover shows media type placeholder.
- **Title** — bold, large
- **Creator** — smaller, uppercase
- **Status pill** — Verified (green), Provisional (blue), Needs Review (amber), Quarantined (red)
- **Universe link** — clickable chip navigating to the Universe detail page. Empty if standalone.
- **Series + position** — if applicable: "Dune Novels #2"
- **Media type icon + format label** — "EPUB", "M4B", "4K HDR", "CBZ"

#### Scrollable Body (collapsible sections)

Each section has a header bar with title, icon, and expand/collapse toggle. Sections with problems show an amber left-border highlight.

**Default open/closed states vary by item status:**

| Section | Verified | Provisional | Needs Review | Quarantined |
|---------|----------|-------------|--------------|-------------|
| Sync | Open | Open | Open | Open |
| Enrichment | Open | Open | Open | Closed |
| Pipeline | Closed | Closed | **Open** | **Open** |
| File | Closed | Closed | Closed | **Open** |
| Assets | Closed | Closed | Closed | Closed |
| Claims | Closed | Closed | Closed | Closed |

---

##### Section 1: Sync

Tracks whether the physical file's embedded metadata is up to date with what the engine has resolved.

- **Sync state** — Synced / Out of Sync / Never Synced
- **Last synced** — timestamp of last writeback to file
- **Last enrichment** — timestamp of last pipeline run
- **Next scheduled refresh** — date (30-day enrichment cycle)
- **Sync Now button** — manual trigger to write resolved metadata back to file tags

**Sync writeback** is enabled by default (configurable in Settings > Preferences). When enabled, the engine automatically writes enriched metadata (title, author, cover art, series, description) back to the file's embedded tags after each pipeline run.

**30-day refresh cycle:** Each item tracks `last_enrichment_date`. A scheduled background job identifies items past the 30-day mark and re-runs the pipeline to check for updated data from retail providers and Wikidata. Uses the Tuvima.WikidataReconciliation package's refresh capabilities. If anything changed, the file is re-synced automatically.

For provisional items: Sync Now writes the provisional metadata to the file tags. The file becomes the carrier of the user's corrections.

---

##### Section 2: Enrichment

The AI-generated and provider-sourced intelligence about the item. Quick visual confirmation the engine understood the item correctly.

- **TL;DR** — AI-generated summary (if available)
- **Vibe tags** — displayed as chips, with "Regenerate" action
- **Genres / subjects** — from Wikidata or retail providers
- **Ratings** — from retail providers (Goodreads, TMDB, etc.)
- **Description** — longer description from Wikipedia or retail, collapsed with "show more"
- **Bridge IDs** — ISBN, ASIN, TMDB ID as small labelled chips

For provisional items: shows whatever data the user provided or the file contained. Empty fields show "Not available — provisional item" in muted text.

**Why vibe tags and TL;DR live here:** These are AI-generated signals that help verify the engine matched correctly. If the vibe tags say "romantic comedy" on a horror novel, the match is wrong. This is management intelligence, distinct from the consumer-facing library page.

---

##### Section 3: Pipeline

Expanded view of the two enrichment stages. Each stage shows its state, what it found, and — if there's a problem — an inline resolution panel.

**Retail Stage (Stage 1 — RetailIdentification)**

- **State:** Complete / Failed / Pending / Skipped
- **Matched source:** which provider matched (Apple API, Open Library, TMDB, etc.)
- **Logical deduction** — the engine's explanation: "Matched via ISBN 978-0-441-17271-9 against Open Library"

**Retail Resolution (inline, when stage needs review):**

When the Retail stage is unresolved, the resolution panel appears directly within this section:

1. **Current match** (if any) — card showing cover thumbnail, title, source, key metadata
2. **Other candidates** — compact list of runner-up matches from the engine. Each shows title, creator, source, cover thumbnail. Tappable to preview, "Use This" to select.
3. **Search box** — manual search against retail providers if the candidates aren't right
4. **Add Provisional** — pre-populates a form with the file's embedded metadata (title, creator, series, position, publication date, cover art upload/URL, format details). The user corrects what's wrong and submits. Saves as a provisional record.

**Wikidata Stage (Stage 2 — WikidataBridge)**

- **State:** Complete / Failed / Pending / Skipped / Awaiting Retail
- **Matched QID** — clickable link to Wikidata entity page
- **Entity label + description** from Wikidata
- **Key properties** — author (P50), publication date (P577), genre (P136)
- **Logical deduction** — "Resolved via ISBN bridge ID → edition Q12345, work Q67890"

**Wikidata Resolution (inline, when stage needs review):**

1. **Current QID match** (if any) — entity label, description, key properties
2. **QID candidates** — alternatives from reconciliation. Each shows label, description, P31 type, and distinguishing properties to differentiate "Dune (1965 novel)" from "Dune (2021 film)"
3. **Manual QID entry** — text field to paste a known QID directly
4. **Add Provisional** — pre-populates from file metadata. User corrects fields (title, creator, type, series, universe). Saves as provisional identity.

**Stage dependency:** Resolving a retail match with new bridge IDs prompts: "Retail match updated — re-run Wikidata lookup?" Accepting triggers an automatic Wikidata re-run with the corrected data.

**Provisional Add flow:** The "Add Provisional" form is pre-populated from the file's embedded metadata — the user isn't starting from scratch. They correct what's wrong (fix a misspelled author, add a missing ISBN, pick the right media type). This gives the engine better input for the next Identify run, making provisional a stepping stone toward a verified match rather than a permanent state.

---

##### Section 4: File

The physical file on disk — what you actually have.

- **Filename** — full name
- **Path** — full disk path
- **Format** — detailed (codec, bitrate, resolution for video/audio)
- **File size**
- **Fingerprint** — SHA-256, truncated with copy button
- **Source library folder** — which watched folder this came from
- **Date added** — when the engine first saw this file
- **Date last scanned** — when the file was last read for metadata

**Embedded vs Resolved comparison** — compact diff showing fields where the original file metadata differs from the engine's resolved values. Only shows differences, not matching fields. Example:
- Title: "Dune Mesiah" → "Dune Messiah" (typo corrected by engine)
- Cover: [original thumbnail] → [enriched thumbnail]
- Author: matches — not shown

---

##### Section 5: Assets (default closed)

Visual assets associated with this media item. See [Shared Assets Section](#shared-assets-section) for full specification.

All five asset types available (Cover Art, Headshot, Banner, Logo, Backdrop). Films/TV get Cover Art, Banner, Logo, and Backdrop auto-populated from TMDB. Books/audio get Cover Art from retail providers + file embedded. Headshots useful for author photos on books. The embedded artwork from the original file is always preserved as "Embedded Original."

##### Section 6: Claims (default closed)

Full audit trail for investigating metadata disputes. Used only for deep investigation ("why does the engine think the author is X when the file says Y?").

- All metadata claims from all sources
- Each claim shows: field name, value, source provider, timestamp, confidence weight
- Winning claim highlighted with the Priority Cascade tier that decided it (A/B/C/D)
- User locks (Tier A) shown with a lock icon
- Filterable by field name

---

#### Pinned Action Bar (always visible at bottom)

Three actions, accessible regardless of scroll position:

| Button | Style | Action |
|--------|-------|--------|
| **Identify** | Primary (accent colour) | Re-runs the full pipeline from scratch. For provisional items, uses the corrected metadata as input — better data means better chance of finding a match. |
| **Sync Now** | Secondary (outlined) | Writes current resolved or provisional metadata to the file's embedded tags. |
| **Purge** | Destructive (red, outlined) | Removes the item from the library. Confirmation dialog required (VaultDeleteConfirm). |

---

#### Drawer Behaviour

- The drawer remembers which sections the user manually opened/closed during the session.
- For problem items, the relevant sections auto-open so the user sees the issue immediately.
- Resolving a stage inline updates the status pill in real-time without closing the drawer.
- Live SignalR updates keep the drawer current if the engine processes the item in the background while the drawer is open.

---

### Vault Delete Confirmation

**VaultDeleteConfirm** — confirmation dialog before any destructive action. Shows the file path, current status, and a plain-English description of what will be deleted.

---

### People List View

People are always Wikidata-sourced — there is no "unlinked" or "needs review" state. No status column is needed. The role count bar at the top (Actors, Directors, Authors, Narrators, etc.) doubles as filters, same pattern as the media type bar on the Media tab.

#### List Columns

| Column | Content | Width |
|--------|---------|-------|
| Photo / icon | Headshot from Wikidata (P18) or generic person icon | Narrow, fixed |
| Name + description | Name (bold) + Wikipedia short description (smaller, muted). Description is for disambiguation ("Canadian filmmaker"), not biography. | Flex, fills available space |
| Roles | All roles as small chips (Author, Narrator, Director). A person can hold multiple roles. | Medium |
| Library presence | Compact counts by media type: "12 books, 3 audiobooks" | Medium |

#### Row Behaviour

- Clicking a row opens the People Detail Drawer.
- Rows are sortable by name, role, and library presence count.
- Filterable by role (via the role count bar) and searchable by name.

---

### People Detail Drawer

Same slide-from-right pattern as the media drawer. Pinned header, scrollable collapsible sections, pinned action bar.

#### Pinned Header

- **Photo** — larger headshot from Wikidata, or generic person icon
- **Name**
- **Roles** — all roles as chips
- **QID link** — clickable to Wikidata entity page

#### Section 1: Library Presence (default open)

Full list of works in your library attributed to this person, grouped by role and media type. Each work is clickable — navigates to that item's media detail drawer.

Management question: "What do I have by this person, and is it all correctly attributed?"

#### Section 2: Linked Identities (default open)

Pseudonyms, pen names, and stage names the engine has resolved for this person. Shows which file-level author/creator names resolved to this single person record.

Management question: "Has the engine merged the right names together, or has it incorrectly combined two different people?"

#### Section 3: Assets (default closed)

Visual assets associated with this person. See [Shared Assets Section](#shared-assets-section) for full specification.

All five asset types available (Cover Art, Headshot, Banner, Logo, Backdrop). Headshot auto-populated from Wikidata (P18). Other types available for user upload.

#### Action Bar

None. The People detail drawer is purely informational — no manual actions. Person enrichment is handled by the same 30-day refresh cycle as media (via Tuvima.WikidataReconciliation). Person records are automatically cleaned up when all their associated media is removed from the library.

---

### Universes List View

Universes are franchise-level groupings resolved by the engine from Wikidata. Like People, they are always Wikidata-sourced — no "unlinked" or "needs review" state. No status column is needed.

The stats bar at the top shows: **Universes count** and **total Series across all Universes**. Both informational — Universes are engine-resolved with nothing to triage. Not every Series needs a Universe; standalone Series are perfectly valid.

#### List Columns

| Column | Content | Width |
|--------|---------|-------|
| Icon | Universe icon | Narrow, fixed |
| Name + description | Universe name (bold) + Wikipedia short description (smaller, muted): "Fictional setting of J.R.R. Tolkien's works" | Flex, fills available space |
| Series count | How many Series are grouped under this Universe: "4 Series" | Narrow |
| Media breakdown | Compact counts across all Series: "12 books, 3 films, 6 audiobooks" | Medium |
| People | Count of connected people: "8 people" | Narrow |

#### Row Behaviour

- Clicking a row opens the Universes Detail Drawer.
- Rows are sortable by name, Series count, and total media count.
- Searchable by name.

---

### Universes Detail Drawer

Same slide-from-right pattern as media and people drawers. Pinned header, scrollable collapsible sections, no action bar.

#### Pinned Header

- **Universe name**
- **Wikipedia description**
- **QID link** — clickable to Wikidata entity page

#### Section 1: Series (default open)

All Series grouped under this Universe. Each Series shows:
- Series name
- Work count
- Media types present (as small icons)

Each Series is clickable — filters the Media tab to show that Series' items.

Management question: "Are these the right Series under this Universe, or has the engine mis-grouped something?"

#### Section 2: People (default open)

People connected to this Universe — authors, directors, actors. Compact list with role chips. Each clickable → opens People detail drawer.

Management question: "Are the right creators associated with this Universe?"

#### Section 3: Assets (default closed)

Visual assets associated with this Universe. See [Shared Assets Section](#shared-assets-section) for full specification.

All five asset types available (Cover Art, Headshot, Banner, Logo, Backdrop). The engine surfaces the best artwork from child Series and media items as candidates. Franchise logos, banners, and key art are typically user-uploaded.

#### Action Bar

None. Universe enrichment runs on the 30-day refresh cycle. Universes are automatically cleaned up when all their child Series are empty.

---

### Shared Assets Section

The Assets section appears in the detail drawer for **Media items**, **People**, and **Universes**. It provides a unified interface for managing visual assets (artwork, banners, logos) associated with any entity in the library.

#### Purpose

Assets are the raw materials for the library presentation layer. Cover art, banners, logos, and backdrops collected at the management level are available when building the consumer-facing library pages (Universe detail, Series pages, hub displays). Curating assets here means the library pages pull from a vetted pool rather than defaulting to whatever the first provider returned.

#### Asset Types

| Type | Description | Typical sources |
|------|-------------|-----------------|
| **Cover Art** | Primary poster/cover image (2:3 ratio for books/films) | Retail providers, file embedded, user upload |
| **Headshot** | Portrait photo (1:1 or 3:4 ratio — people, author photos on books) | Wikidata (P18), user upload |
| **Banner** | Wide landscape image for hero displays | TMDB (backdrops), publisher, user upload |
| **Logo** | Title treatment or franchise wordmark | TMDB (logos), user upload |
| **Backdrop** | Background atmosphere image | TMDB (backdrops), publisher, user upload |

#### Provider Asset Capabilities

Providers differ significantly in what types of assets they return:

| Provider | Returns | Type tagging | Notes |
|----------|---------|--------------|-------|
| **TMDB** | Posters, backdrops, logos | **Automatic** — TMDB API categorises images by type (`posters`, `backdrops`, `logos`). Each image includes dimensions, language, and user vote average (quality ranking). | Richest source. Films/TV get all four asset types automatically. |
| **Open Library** | Cover image | Default to **Cover Art** | Single image, no type categorisation. |
| **Google Books** | Cover image | Default to **Cover Art** | Single image, no type categorisation. |
| **Apple API** | Cover image | Default to **Cover Art** | Single image, no type categorisation. |
| **Comic Vine** | Cover image | Default to **Cover Art** | Single image, no type categorisation. |
| **File embedded** | Cover image | Tagged as **Embedded Original** | Whatever artwork was embedded in the file's metadata (EPUB cover, ID3 art, MKV poster). Always preserved. |
| **Auto-generated** | Hero banner | Tagged as **Banner (Generated)** | SkiaSharp-rendered hero banner (blurred cover art + vignette). Auto-generated whenever cover art exists. Default banner for media types where no provider supplies real backdrops (books, audiobooks, comics, podcasts). For Universes and People, generated from the most representative child item's cover art. |
| **User upload** | Any type | **User selects** type on upload | User picks which type group (Cover Art, Headshot, Banner, Logo, Backdrop) when uploading. |

**Result:** Films and TV have the richest asset pools (covers + banners + logos + backdrops from TMDB). Books, audiobooks, comics, and podcasts get Cover Art from providers plus an auto-generated hero Banner from SkiaSharp — so every item with cover art has at least a Cover Art and a Banner. User uploads fill remaining slots (Headshot, Logo, Backdrop). Universes and People build their pools from child items, auto-generated banners, and user uploads.

#### Asset Availability by Entity Level

All five asset types (Cover Art, Headshot, Banner, Logo, Backdrop) are available on every entity — Media items, People, and Universes. The same types exist everywhere for uniformity; a book can have a headshot (author photo), a person can have cover art, a Universe can have all five. Types that providers don't automatically fill remain as empty slots that the user can populate via upload. This is especially useful when building library presentation pages (Series hubs, Universe pages) where custom artwork makes the display unique.

| Entity | Auto-populated | Typically user-uploaded |
|--------|----------------|------------------------|
| **Media item (film/TV)** | Cover Art, Banner, Logo, Backdrop (TMDB) + Banner Generated (SkiaSharp) | Headshot |
| **Media item (book/audio/comic/podcast)** | Cover Art (retail + embedded) + Banner Generated (SkiaSharp from cover art) | Headshot, Logo, Backdrop |
| **Person** | Headshot (Wikidata P18) + Banner Generated (SkiaSharp from headshot) | Cover Art, Logo, Backdrop |
| **Universe** | Inherited from child items + Banner Generated (SkiaSharp from best child cover) | All five types as needed |

#### Display

- **Grouped by type** — Cover Art, Headshot, Banner, Logo, Backdrop sections. When building a library page, you're looking for "I need a banner," not "what did TMDB give me." Empty type groups are shown as collapsed with an upload prompt.
- Each asset shows: thumbnail, source label (TMDB, Open Library, Embedded Original, User Upload), dimensions
- One asset per type is marked as **Preferred** (star indicator) — this is what gets used on library pages and written back to the file via sync
- The embedded artwork from the original file is always preserved as an asset tagged "Embedded Original" — it is never lost or overwritten in the asset pool
- Assets from TMDB include the user vote average from TMDB's community — useful for picking the highest-quality option when multiple are available

#### Actions

- **Upload** — button per type group, drag-and-drop or file picker. User selects the asset type on upload. Tagged "User Upload."
- **Set as Preferred** — tap the star on any asset to make it the preferred image for that type
- **Delete** — remove an asset (except Embedded Original, which is always preserved)

#### AI Artwork Matching

During retail identification (Stage 1), the LLM compares the file's embedded cover art against candidate covers from retail providers. Visual similarity becomes an additional signal alongside title, author, and ISBN for picking the correct match. This helps disambiguate editions — the same book with different covers indicates which edition the user actually has.

---

---

### Hubs Tab

The fourth Vault tab. Provides oversight and configuration of all hub types — smart hubs, system lists, personalised mixes, and playlists. Full specification lives in `docs/architecture/hubs-and-playlists.md`.

#### Stats Bar

Four count cards: Smart Hubs (blue), System Lists (green), Personalised Mixes (purple), Playlists (amber). Cards double as filters.

#### List Columns

| Column | Content | Width |
|--------|---------|-------|
| Type icon | Icon representing the hub category | Narrow, fixed |
| Name + description | Name (bold) + rule summary or description (muted) | Flex |
| Type | Chip: Smart (blue), System (green), Mix (purple), Playlist (amber) | Narrow |
| Scope | "Library" or username | Narrow |
| Items | Count | Narrow |
| Status | Pill: Active (green), Disabled (grey), Empty (amber) | Narrow |

Default grouping by type: Smart Hubs → System Lists → Personalised Mixes → Playlists.

#### Detail Drawer

Same slide-from-right pattern. Configuration section varies by hub type (rules/thresholds for smart hubs, logic summary for mixes, owner info for playlists). Items Preview shows first 20 items. Assets section with auto-composed artwork and optional user uploads. Action bar: Enable/Disable + Feature for smart hubs, Enable/Disable for mixes, no actions for system lists and playlists.

---

### Live Updates

All Vault state is kept current via SignalR. When the Engine ingests a file, advances a stage gate, moves an item to the review queue, or quarantines a file, the Vault updates instantly without a page refresh. The Detail Drawer also receives live updates while open.
