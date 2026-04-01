---
title: "Universal Parameterized Hub System"
summary: "Architecture of the universal hub model — every collection is a parameterized query container with normalized filter predicates."
audience: "developer"
category: "architecture"
product_area: "concepts"
tags:
  - "hubs"
  - "playlists"
  - "collections"
  - "rules"
  - "content-groups"
---

# Universal Parameterized Hub System

## Overview

Every collection in Tuvima Library — an album, a TV show, a genre category, a user playlist, an AI recommendation — is a **hub**. A hub is a parameterized query container: a set of normalized filter predicates that resolve to a collection of items. The hub type determines how it looks and who controls it, but the underlying mechanism is always the same.

This model unifies what were previously separate systems (content groups, smart hubs, system lists, playlists, AI mixes) into a single architecture. A hub is defined by its **rules** (what items belong), its **type** (how it's presented), and its **placements** (where it appears in the Dashboard).

---

## Hub Rules — The Universal Predicate Model

Every hub stores its membership criteria as a JSON array of predicates. Each predicate is a simple object:

```json
{ "field": "genre", "op": "eq", "value": "Science Fiction" }
```

A hub's full rule set might look like:

```json
[
  { "field": "media_type", "op": "eq", "value": "Movie" },
  { "field": "genre", "op": "eq", "value": "Horror" },
  { "field": "vibe", "op": "in", "value": "atmospheric,tense,unsettling" }
]
```

### Supported Operators

| Operator | Meaning | Example |
|----------|---------|---------|
| `eq` | Equals | `genre eq "Horror"` |
| `neq` | Not equals | `media_type neq "Podcast"` |
| `contains` | Text contains substring | `title contains "Dune"` |
| `gt` | Greater than | `user_rating gt 3` |
| `lt` | Less than | `duration lt 480` |
| `between` | Range (inclusive) | `year between "1980,1989"` |
| `in` | Any of (comma-separated) | `vibe in "atmospheric,cerebral"` |
| `like` | Pattern match | `author like "Herbert%"` |

### Match Mode

Each hub has a `match_mode` that controls how predicates combine:

- **ALL** (default) — item must satisfy every predicate (AND logic)
- **ANY** — item must satisfy at least one predicate (OR logic)

### Field Vocabulary

Rules reference fields from six categories:

#### Identity fields

| Field | Description | Example use |
|-------|-------------|-------------|
| `title` | Work title | `title contains "Dune"` |
| `author` | Creator / author | `author eq "Frank Herbert"` |
| `media_type` | Media category | `media_type eq "Book"` |
| `genre` | Genre tag | `genre eq "Science Fiction"` |
| `series` | Series name | `series eq "Dune Novels"` |
| `universe` | Universe name | `universe eq "Dune"` |
| `format` | File format label | `format eq "EPUB"` |
| `publisher` | Publisher name | `publisher contains "Penguin"` |
| `language` | Content language | `language eq "en"` |
| `narrator` | Audiobook narrator | `narrator eq "Steven Pacey"` |

#### Wikidata fields

| Field | Description | Example use |
|-------|-------------|-------------|
| `wikidata_qid` | Wikidata Q-identifier | `wikidata_qid eq "Q190228"` |
| `director` | Director (from Wikidata P57) | `director eq "Denis Villeneuve"` |
| `composer` | Composer (from Wikidata P86) | `composer eq "Hans Zimmer"` |

#### Engagement fields

| Field | Description | Example use |
|-------|-------------|-------------|
| `user_rating` | User star rating (1–5) | `user_rating gt 3` |
| `provider_rating` | Retail provider rating (0–5) | `provider_rating gt 4` |
| `play_count` | Times played/read | `play_count gt 5` |
| `completion_status` | Not Started / In Progress / Completed | `completion_status eq "Not Started"` |
| `in_list` | Membership in a system list | `in_list eq "Favorites"` |

#### Temporal fields

| Field | Description | Example use |
|-------|-------------|-------------|
| `date_added` | When added to library | `date_added gt "2026-03-01"` |
| `date_published` | Publication/release year | `date_published between "1980,1989"` |
| `last_played` | Last consumption date | `last_played lt "2025-10-01"` |
| `date_completed` | When marked complete | `date_completed gt "2026-01-01"` |

#### File fields

| Field | Description | Example use |
|-------|-------------|-------------|
| `file_size` | Size in bytes | `file_size lt 524288000` |
| `duration` | Length in minutes | `duration between "120,360"` |
| `quality` | Quality label (4K, 1080p, 320kbps) | `quality eq "4K HDR"` |
| `bitrate` | Audio bitrate in kbps | `bitrate gt 256` |

#### AI-derived fields

| Field | Description | Example use |
|-------|-------------|-------------|
| `vibe` | AI-generated vibe tag | `vibe in "atmospheric,cerebral"` |
| `tldr` | AI summary text | `tldr contains "space exploration"` |
| `taste_match` | AI taste match percentage | `taste_match gt 80` |

---

## Hub Types — Presentation Hints

The hub type is a **presentation hint** — it tells the Dashboard how to render the hub, who owns it, and what the user can do with it. The underlying rule mechanism is the same for all types.

| Type | Created by | Resolution | Scoped to | Editable | Drillable |
|------|-----------|------------|-----------|----------|-----------|
| **ContentGroup** | Engine (during ingestion) | Materialized | Library | Read-only | Yes — navigates to sub-page |
| **Smart** | Engine (auto-generated from templates) | Query-resolved | Library | Disable/enable, feature, adjust threshold | No |
| **System** | System (pre-created, always present) | Materialized | User (per profile) | Add/remove/reorder items | No |
| **Mix** | Engine + AI (from taste profile) | Materialized | User (per profile) | Enable/disable | No |
| **Playlist** | User | Materialized | User (per profile) | Full CRUD | No |
| **Custom** | User (via hub builder) | Query-resolved | User (per profile) | Full CRUD + edit rules | No |

### Hybrid Resolution

Hubs resolve their items in one of two ways:

- **Query-resolved** — the Engine evaluates the hub's predicates against the data store at display time. Items are always fresh. Used by Smart hubs and Custom collections.
- **Materialized** — items are explicitly linked to the hub in the data store. Membership changes when files are ingested (ContentGroup), when the user adds/removes items (System, Playlist), or when the AI refreshes recommendations (Mix).

Query-resolved hubs store their predicates in the `rule_json` column. Materialized hubs may also have rules (for display purposes or re-evaluation) but their membership is tracked via the `hub_works` junction table.

---

## ContentGroup Hubs — Created at Ingestion

ContentGroup hubs represent natural groupings that emerge from the media itself: TV shows, music albums, book series, podcast shows, comic series. They are created **during ingestion** by `MediaEntityChainFactory`, not during Wikidata enrichment — so they work immediately when files are scanned.

When a file is ingested:
1. The processor extracts grouping metadata (show name, album name, series name).
2. `MediaEntityChainFactory` checks for an existing ContentGroup hub matching the group identity.
3. If none exists, it creates one with a rule like `{ "field": "series", "op": "eq", "value": "Breaking Bad" }`.
4. The new Work is linked to the hub.

ContentGroup hubs have additional fields:
- **`group_by_field`** — the metadata field that defines the group (e.g. `series`, `album`, `show`)
- **`sort_field`** / **`sort_direction`** — default ordering for items (e.g. episode number ascending, track number ascending)

These hubs power the **container views** in the Vault's media tabs. When you switch TV to "By Show" view, the table displays ContentGroup hubs filtered to TV. Clicking a container navigates to `MediaGroupPage.razor` with the show's seasons and episodes.

### ContentGroup by Media Type

| Media type | Group identity | Sort default | Example |
|-----------|---------------|-------------|---------|
| TV | Show name | Season → Episode ascending | "Breaking Bad" |
| Music | Album name (+ artist) | Track number ascending | "OK Computer — Radiohead" |
| Books | Series name (+ author) | Sequence index ascending | "Dune Novels — Frank Herbert" |
| Audiobooks | Series name (+ author) | Sequence index ascending | "First Law — Joe Abercrombie" |
| Podcasts | Show name | Episode date descending | "Hardcore History" |
| Comics | Series name | Issue number ascending | "Saga — Brian K. Vaughan" |
| Movies | Franchise name | Release year ascending | "Star Wars" |

---

## Smart Hubs — Auto-Generated from Library Data

Smart hubs are auto-generated from library metadata using templates. One hub is created per qualifying instance (one per genre, one per decade, etc.). Minimum item thresholds prevent clutter from one-off entries.

### Template Categories

| Template | Rule pattern | Default threshold | Media types | Icon |
|----------|-------------|-------------------|-------------|------|
| **By Genre: [name]** | `genre eq "{name}"` | 3+ items | Any | Tag |
| **By Vibe: [tag]** | `vibe eq "{tag}"` | 5+ items | Any | Mood/wave |
| **By Author: [name]** | `author eq "{name}"` | 3+ works | Books, Audiobooks, Comics | Person |
| **By Director: [name]** | `director eq "{name}"` | 3+ works | Movies, TV | Person |
| **By Narrator: [name]** | `narrator eq "{name}"` | 3+ works | Audiobooks | Person |
| **By Decade: [period]** | `date_published between "{start},{end}"` | 5+ items | Any | Calendar |
| **Recently Added** | `date_added gt "{30_days_ago}"` | Always shown | Any | Clock |
| **Highest Rated** | `provider_rating gt 4` | Always shown | Any | Star |
| **Unrated** | `user_rating eq "unrated"` | Always shown | Any | Circle-dashed |

Smart hubs refresh automatically — their rules are re-evaluated as the library changes.

### Configuration

- **Generation threshold** — adjustable minimum item count per template (default 3 or 5 depending on template)
- **Featured toggle** — pin a hub to appear prominently on the relevant media lane page
- **Enabled toggle** — disable to hide from browsing without deleting

These are managed on the Collections page (`/hubs`).

---

## System Lists

Pre-created lists for progress tracking. One per media type family, always present, cannot be deleted. Per-user — each user profile has their own instances.

| List | Media types accepted | Progress tracking | Default "Add to" target for |
|------|---------------------|-------------------|-----------------------------|
| **Reading List** | Books, Comics | Always on | Books, Comics |
| **Watchlist** | Movies | Always on | Movies |
| **Currently Watching** | TV | Always on | TV |
| **Listening Queue** | Audiobooks, Music, Podcasts | Always on | Audiobooks, Music, Podcasts |
| **Favorites** | Any | Off | None (uses heart icon instead) |

System lists support:
- Add/remove items (via library browsing actions)
- Reorder items (drag to reorder — "I want to read this next")
- Progress tracking per item (not started / in progress / completed, with position)

System lists do not support: rename, delete, change media type scope, creation of new system lists.

---

## Personalised Mixes

Engine-generated per-user collections powered by the AI Taste Profiling feature. Different for every user profile because they reflect individual consumption patterns.

| Mix | Logic | Refresh |
|-----|-------|---------|
| **Continue** | In-progress items across all lists | Real-time |
| **Heavy Rotation** | Most consumed in last 30 days | Daily |
| **Discovery Queue** | In your library, matches your taste, untouched | Daily |
| **New For You** | Recently added items matching taste profile | On new ingestion |
| **Because You Liked [X]** | Items similar to a specific highly-rated item (vibe + genre overlap) | On rating change |
| **Taste Mix: [cluster]** | Per taste cluster — your atmospheric sci-fi cluster, your cozy mystery cluster | Weekly |
| **On Repeat** | Current obsessions (high consumption recently) | Daily |
| **Rediscover** | Things you loved 6+ months ago but haven't touched since | Weekly |

Personalised mixes use: genres (from Wikidata/retail), vibe tags (AI-generated), consumption history, user ratings, and the Taste Profile model. See `docs/architecture/ai-integration.md` for the Genre vs Vibe discovery model.

### Configuration

- **Enabled toggle** — per mix type, disable to hide for all users
- No other configuration — the AI handles everything

---

## Playlists & Custom Collections

User-created, user-owned collections. Unlimited in number. Any media type.

### Playlists (Materialized)

Traditional playlists where the user hand-picks items. Items are explicitly linked.

- **Create** — from the My Library page ("+ New Playlist") or the Collections page
- **Rename / Delete** — from within the playlist detail page
- **Add items** — from library browsing via the "Add to Playlist..." picker
- **Remove items** — from within the playlist detail page
- **Reorder** — drag to reorder within the playlist
- **Progress tracking** — optional toggle per playlist
- **Artwork** — auto-composed from items' cover art. User can upload a custom override.

### Custom Collections (Query-Resolved)

Smart collections built with the hub builder. Items auto-populate from user-defined rules. This is the successor to the previous "Smart Playlist" concept — now a first-class hub type rather than a playlist sub-type.

- **Create** — from the Collections page (`/hubs`) via the hub builder
- **Edit rules** — from the collection detail page (opens hub builder)
- **Rename / Delete** — from the collection detail page
- **Live update** — items auto-add/remove as they match or stop matching rules (toggleable via `live_updating` flag, default on)
- **Artwork** — auto-composed from matched items' cover art. User can upload a custom override.

Custom collections do not support: manual add/remove of items, manual reordering (sort rules control order).

---

## Hub Builder

The hub builder is an Apple Music Smart Playlist-inspired filter interface for creating Custom collections. It lives on the Collections page (`/hubs`) and follows a row-based filter pattern.

### Builder Layout

1. **Collection name** — text field at top
2. **Match mode** — toggle: Match ALL rules / Match ANY rule
3. **Filter rows** — each row is: Field dropdown → Operator dropdown → Value input. Plus/minus buttons to add/remove rows. A "Make group" button creates nested ALL/ANY logic for complex queries.
4. **Live preview** — section below the rules showing how many items currently match and a scrollable list of the first 20 matches. Updates as rules are added or changed. Powered by `POST /hubs/preview`.
5. **Limit and sort** — optional item limit (10, 25, 50, 100, 200, or no limit) and sort controls (field + direction)
6. **Live updating toggle** — when on, items auto-add/remove as they match or stop matching rules
7. **Save** — creates the Custom collection

### Field Autocomplete

The value input adapts based on the selected field:
- **Genre, Vibe** — multi-select picker populated from `GET /hubs/field-values/genre`
- **Author, Director, Narrator** — person picker with search
- **Media Type** — media type picker
- **Format, Quality** — format/quality picker
- **Series, Universe** — text picker with search
- **Dates** — date picker or duration selector (last N days/weeks/months)
- **Numbers** — numeric input with range support

### Example Custom Collections

| Collection name | Rules | Limit / Sort |
|----------------|-------|--------------|
| "Unread Sci-Fi" | Genre is "Science Fiction" + Completion Status is "Not Started" | Sort by Provider Rating, descending |
| "Short Listens" | Media Type is "Audiobook" + Duration < 8 hours + User Rating > 3 | Sort by Duration, ascending |
| "Atmospheric Horror" | Genre is "Horror" + Vibe includes any of "atmospheric, haunting, unsettling" | Limit 50, sort by Taste Match |
| "Denis Villeneuve Marathon" | Director is "Denis Villeneuve" | Sort by Date Published, ascending |
| "4K Movie Night" | Media Type is "Movie" + Quality is "4K HDR" + Duration between 90–180 min | Sort by Random, limit 10 |
| "New This Month" | Date Added is in the last 30 days + Taste Match > 70% | Sort by Taste Match, descending |

---

## "Add to..." Interaction

When browsing the library (poster cards, detail pages), the user can add items to lists and playlists.

### Primary action (one tap)

Adds to the default system list for that media type. The button label changes based on context:

| Media type | Button label | Target |
|-----------|-------------|--------|
| Books, Comics | "Add to Reading List" | Reading List |
| Movies | "Add to Watchlist" | Watchlist |
| TV | "Add to Currently Watching" | Currently Watching |
| Audiobooks, Music, Podcasts | "Add to Listening Queue" | Listening Queue |

### Heart action

Separate from the primary action. A heart icon toggles Favorites membership. Available on all media types.

### Secondary action (expand)

Opens a picker showing all materialized hubs that accept this media type:
- System lists (with checkmarks for lists the item is already in)
- All user-created playlists
- "Create New Playlist" at the bottom

Smart hubs, Custom collections, and personalised mixes do not appear in this picker — you don't manually add to them.

### Where the actions appear

1. **Poster card** — small bookmark/plus icon in the corner (primary action on tap, picker on long-press/right-click). Heart icon separately.
2. **Item detail page** (library page) — explicit button with label + "Add to other list..." secondary action. Heart icon separately.
3. **Vault detail drawer** — not present. The Vault is for management, not consumption.

---

## Progress Tracking

Available on system lists and playlists (when toggled on). Tracks consumption state per item.

### Per-item state

| State | Meaning |
|-------|---------|
| **Not Started** | In the list but untouched |
| **In Progress** | Started — position tracked (page number, timestamp, episode number) |
| **Completed** | Finished |

### Per-hub state

- **Completion percentage** — items completed / total items
- **Items remaining** — count of not-started + in-progress
- **Next up** — the next item based on list order and progress state

### The "Continue" mix

The Continue personalised mix aggregates in-progress items from all system lists and playlists with progress tracking enabled. It surfaces them in one "pick up where you left off" view. It does not duplicate items — it references them from their source lists.

---

## Hub Artwork

Hubs are virtual containers — they don't have their own artwork. Artwork is derived from contents.

### Auto-composed artwork

The Engine generates a composite thumbnail from the first few items' cover art (2x2 grid of covers). Refreshes as items change.

### Auto-generated banner

SkiaSharp renders a hero banner (blurred composite of item covers + vignette), same technique used for book hero banners on the Dashboard. Available for all hub types.

### User override

For featured hubs or collections the user wants to customise, all five asset types are available (Cover Art, Headshot, Banner, Logo, Backdrop) via the shared Assets section. User uploads take precedence over auto-composed artwork.

---

## Hub Placements — Location as Display Constraint

The `hub_placements` table maps hubs to UI locations. A single hub can appear in multiple locations with different display constraints.

### Placement Model

Each placement record contains:

| Field | Description |
|-------|-------------|
| `hub_id` | Which hub |
| `location` | Where it appears (e.g. `home`, `media_lane_books`, `vault_tv`, `my_library`, `hubs_page`) |
| `display_limit` | Maximum items to show at this location (e.g. 10 for a home swimlane, null for full detail) |
| `sort_override` | Optional sort that overrides the hub's default for this location |
| `position` | Display order within the location |

### Location Vocabulary

| Location | What appears there | Hub types shown |
|----------|-------------------|----------------|
| `home` | Personalised dashboard | Mix, Smart (Recently Added), System (shortcuts) |
| `media_lane_{type}` | Category browsing (e.g. `/books`) | Smart (filtered to media type) |
| `my_library` | Personal collections | System, Playlist |
| `hubs_page` | Collections page (`/hubs`) | All types |
| `vault_{type}` | Vault container views | ContentGroup (filtered to media type) |

This model replaces hardcoded rules about where each hub type appears. Adding a new location or changing which hubs appear where is a data change, not a code change.

---

## The Collections Page (`/hubs`)

A dedicated page for browsing, creating, and managing all hub types. This replaces the former Vault Hubs tab — hub management now lives at its own top-level route rather than being buried inside the Vault.

### Page Layout

- **Header** — "Collections" title + "New Collection" button (opens hub builder)
- **Type filter chips** — All, Content Groups, Smart, System Lists, Mixes, Playlists, Custom
- **Search** — filter by name
- **Grid/List toggle**

### Grid View

Hub cards showing:
- Auto-composed artwork (or custom override)
- Hub name
- Type chip (colour-coded: blue=Smart, green=System, purple=Mix, amber=Playlist, teal=Custom, slate=ContentGroup)
- Item count
- For ContentGroup: media type chip + creator subtitle

### List View

Table with columns: Artwork, Name, Type, Items, Status, Actions.

### Hub Detail

Click into any hub to see:
- Hub artwork (auto-composed or custom)
- Name, description, type indicator
- Rules displayed as readable chips (for Smart and Custom types)
- Items as a grid or list
- Add/remove/reorder controls (only for System lists and Playlists)
- Progress indicators (only for lists/playlists with progress tracking)
- Configuration controls (thresholds for Smart, rules for Custom, placements for all)

### Navigation

The Collections page is accessible from the LeftDock navigation, positioned between Vault and the provider tester. The LeftDock entry uses a collections/grid icon.

---

## Engine Actions

### Rule Evaluation

`HubRuleEvaluator` (Storage layer) translates hub predicates into SQL queries against the data store. It:

1. Parses the JSON predicate array
2. Maps each field to its data store column (joining across works, editions, canonical values, metadata claims as needed)
3. Applies the match mode (AND/OR)
4. Applies sort and limit constraints
5. Returns matching Work IDs

For performance, Smart hubs cache a `rule_hash` — the hash of the serialized rule JSON. When rules haven't changed, cached results can be served. The `live_updating` flag controls whether materialized hubs re-evaluate on library changes.

### Available Actions

| Action | Method | Purpose |
|--------|--------|---------|
| Resolve hub items | `GET /hubs/resolve/{id}` | Evaluate rules, return matching items |
| Preview rules | `POST /hubs/preview` | Evaluate rules without saving — powers the live preview in the hub builder |
| Hubs at location | `GET /hubs/by-location/{location}` | All hubs placed at a UI location, with display limits applied |
| Field values | `GET /hubs/field-values/{field}` | Distinct values for a field — powers autocomplete in the hub builder |
| Create hub | `POST /hubs` | Create a new hub (Playlist or Custom) |
| Update hub | `PUT /hubs/{id}` | Update hub properties or rules |
| Delete hub | `DELETE /hubs/{id}` | Delete a user-created hub (Playlist or Custom only) |
| Get placements | `GET /hubs/{id}/placements` | Where a hub appears |
| Set placements | `PUT /hubs/{id}/placements` | Update hub placements |

---

## Platform Inspiration

The universal hub model draws from patterns proven by major media platforms:

| Platform | Pattern | How it maps to hubs |
|----------|---------|-------------------|
| **Netflix** | "Because you watched X", category rows, "My List" | Mix (Because You Liked), Smart (genre rows), System (Watchlist) |
| **Spotify** | Daily Mixes, Discover Weekly, user playlists, album pages | Mix (Taste Mix), Mix (Discovery Queue), Playlist, ContentGroup (albums) |
| **Apple Music** | Smart Playlists with field+operator+value rules | Custom collections via hub builder — direct inspiration for the filter UI |
| **Plex** | Auto-generated collections by genre/decade/director, user collections | Smart hubs (auto-generated templates), Custom collections |

---

## Data Store Changes (Migration M-070)

New columns on the `hubs` table:

| Column | Type | Purpose |
|--------|------|---------|
| `resolution` | TEXT | `query` or `materialized` |
| `rule_json` | TEXT | JSON array of predicates |
| `rule_hash` | TEXT | SHA-256 of rule_json for cache invalidation |
| `group_by_field` | TEXT | Field that defines ContentGroup identity |
| `match_mode` | TEXT | `all` or `any` |
| `sort_field` | TEXT | Default sort field |
| `sort_direction` | TEXT | `asc` or `desc` |
| `live_updating` | INTEGER | 1 = re-evaluate on changes, 0 = static |

New table: `hub_placements`

| Column | Type | Purpose |
|--------|------|---------|
| `id` | INTEGER PK | Auto-increment |
| `hub_id` | TEXT FK | References hubs |
| `location` | TEXT | UI location key |
| `display_limit` | INTEGER | Max items at this location (null = unlimited) |
| `sort_override` | TEXT | Optional sort override |
| `position` | INTEGER | Display order |

Backfill migration converts existing hubs to the new schema, generating `rule_json` from existing hub properties.

---

## Implementation Phases

| Phase | Scope |
|-------|-------|
| **Phase 1 — Foundation** | Hub entity changes, migration M-070, `HubRuleEvaluator`, predicate model, actions (resolve/preview/field-values) |
| **Phase 2 — ContentGroup** | Ingestion-time creation in `MediaEntityChainFactory`, Vault container views driven by ContentGroup hubs |
| **Phase 3 — Smart Hub Generation** | Template engine, auto-generation from library data, threshold configuration |
| **Phase 4 — Collections Page & Hub Builder** | `/hubs` page, hub builder UI, Custom collection CRUD, placement management |
| **Phase 5 — Migration** | Vault Hubs tab removal, navigation updates, existing hub backfill |

---

## Related

- [How Universes and Series Work](../explanation/how-universes-work.md)
- [Universe Graph](universe-graph.md)
- [Settings and Vault](settings-and-vault.md)
- [Target State](target-state.md)
