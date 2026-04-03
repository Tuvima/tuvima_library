---
title: "Universe Graph"
summary: "Deep technical documentation for universe relationships, graph modeling, and cross-media linking."
audience: "developer"
category: "architecture"
product_area: "concepts"
tags:
  - "universes"
  - "graph"
  - "linking"
---

# Universe Graph

## Purpose

The Universe Graph connects fictional characters, locations, organizations, and events across all media in the Library. When a book, film, audiobook, and comic all belong to the same creative universe, the graph captures the relationships that bind them â€” characters who are siblings, locations that exist within other locations, actors who played specific roles across adaptations.

The graph is populated automatically during hydration using Wikidata as the data source. It is stored in SQLite and loaded into an in-memory EntityGraph for graph queries and pathfinding.

---

## Narrative Root Resolution

Before the graph can be built, the Engine determines which fictional universe a work belongs to. The Narrative Root Resolver walks a property chain in priority order:

1. P1434 (takes place in fictional universe)
2. P8345 (media franchise)
3. P179 (part of series)
4. Hub DisplayName (fallback)

The resolved universe QID and label are stored in the `narrative_roots` table. All fictional entities discovered within a work are linked to their narrative root, enabling cross-media queries â€” for example, finding all characters from the Dune universe across novels, films, and audiobooks.

---

## Entity Discovery

`RecursiveFictionalEntityService` handles entity creation and enrichment:

1. For each entity QID discovered in a work's Wikidata properties, finds or creates a `FictionalEntity` record (keyed by `wikidata_qid`, UNIQUE constraint)
2. Links the entity to the work via `fictional_entity_work_links`
3. If the entity has not yet been enriched (`enriched_at IS NULL`), enqueues a Data Extension request for its properties
4. After enrichment, `RelationshipPopulationService` reads the entity's `_qid` claims and creates graph edges

Entity types are stored in a single `fictional_entities` table with a sub-type discriminator:

| Type | Examples |
|---|---|
| Character | Paul Atreides, Frodo Baggins, Ellen Ripley |
| Location | Arrakis, Mordor, Nostromo |
| Organization | House Atreides, The Fellowship, Weyland-Yutani |
| Event | Harkonnen assault on Arrakis, Battle of Helm's Deep |

---

## Relationship Types

`RelationshipPopulationService` creates graph edges by reading entity-valued claims after enrichment. The traversal depth is configurable (default: 2 hops) via `lineage_depth` in `config/hydration.json`.

22 relationship types are supported:

| Relationship | Wikidata property |
|---|---|
| father | P22 |
| mother | P25 |
| spouse | P26 |
| sibling | P3373 |
| child | P40 |
| opponent | P1344 |
| student_of | P1066 |
| partner | P451 |
| member_of | P463 |
| allegiance | P945 |
| educated_at | P69 |
| residence | P551 |
| creator | P170 |
| located_in | P131 |
| part_of | P361 |
| head_of | P488 |
| parent_organization | P749 |
| has_parts | P527 |
| position_held | P39 |
| conflict | P607 |
| significant_person | P3342 |
| affiliation | P1416 |

Actor-to-character links are stored separately in `character_performer_links` (person_id, fictional_entity_id, work_qid) and merged into the graph at query time.

---

## EntityGraph In-Memory Graph

The `UniverseGraphQueryService` loads entities and relationships from SQLite into a Tuvima.Wikidata.Graph in-memory graph. The graph is lazy-loaded per universe and cached â€” it is not rebuilt on every query.

Capabilities served from the in-memory graph:

- Pathfinding between any two entities
- Family tree traversal
- Cross-media queries (all characters from universe X appearing in media type Y)
- Era-filtered queries (entities and relationships active at a given timeline year)

---

## Person Infrastructure

Persons (authors, directors, narrators, actors) are distinct from fictional entities but participate in the same graph via performer links.

Each Person record carries:

- Core identity: name, WikidataQid, role (Author, Narrator, Director, Cast Member, Voice Actor, Screenwriter, Composer, Translator, Editor, Host, Producer, Illustrator)
- Biographical fields: date_of_birth, date_of_death, place_of_birth, place_of_death, nationality, is_pseudonym
- Social links stored as Actionable URI Schemes (see below)
- Pseudonym links via `person_aliases` table (bidirectional: P1773 attributed_to, P742 pseudonym)

Person folders on disk: `.people/{person_qid}/` containing `headshot.jpg` sourced from Wikimedia Commons P18. P18 is strictly Person-only â€” never used for media cover art.

### Actionable URI Schemes

Social links are stored in a format that enables native app launching on mobile and automotive device profiles:

| Platform | Stored format | Web fallback |
|---|---|---|
| Instagram | `instagram://user?username={handle}` | `https://instagram.com/{handle}` |
| Twitter/X | `twitter://user?screen_name={handle}` | `https://x.com/{handle}` |
| TikTok | `tiktok://user?username={handle}` | `https://tiktok.com/@{handle}` |
| Mastodon | `https://{instance}/@{user}` | Same |
| Website | Direct URL | Same |

`DeviceContextService` selects the appropriate URI format at render time: URI scheme for Mobile/Automotive, HTTPS for Web/Television.

---

## Chronicle Engine

The Chronicle Engine extends the Universe Graph with time-awareness.

### Temporal Qualifiers

Relationships carry `StartTime` and `EndTime` (nullable ISO 8601 strings) sourced from Wikidata P580/P582 temporal qualifiers. A character may be married for part of a story, a faction may exist only during a specific era, an actor may have played a role in one adaptation but not another.

### Lore Delta Detection

`ILoreDeltaService` batch-fetches current Wikidata revision IDs via `wbgetentities?props=info` and compares them against the stored `WikidataRevisionId` on each `FictionalEntity`. Changed entities are reported as `LoreDeltaResult` records. When changes are detected, a `LoreDeltaDiscoveredEvent` is broadcast via SignalR and a banner appears in the Chronicle Explorer.

### Era-Correct Actor Resolution

`IEraActorResolverService` resolves which actor played a character for a given timeline year. It queries performer edges for the character, filters by temporal range, and returns the matching actor's headshot URL. Falls back to the most recent performer when no temporal match exists.

### Canon Discrepancy Detection

`ICanonDiscrepancyService` compares an edition's canonical values against its master work (P629 edition_or_translation_of) across six core fields: title, author, year, genre, series, series_position. Discrepancies surface via `GET /metadata/{entityId}/canon-discrepancies`.

---

## Chronicle Explorer

Route: `/universe/{Qid}/explore`

The Chronicle Explorer Dashboard page renders the full universe graph using Cytoscape.js (vendored at `wwwroot/lib/cytoscape/cytoscape.min.js`, MIT license). JS interop module at `wwwroot/js/cytoscape-interop.js` exposes: `initGraph`, `updateGraph`, `filterByTimelineYear`, `focusNode`, `setLayout`, `destroy`.

Features:
- Universe header with entity and edge count chips
- Lore Delta amber alert banner when Wikidata changes are detected
- Timeline slider (visible when edges carry temporal data)
- Type filter toggle chips (Character / Location / Organization)
- Layout selector (force-directed / concentric / grid)
- Cytoscape.js graph panel (60%) with node-click detail drawer
- Searchable entity list panel (40%)

Device constraints: Chronicle Explorer is disabled on mobile. The timeline slider is disabled on television.

---

## API Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/universes` | List all narrative roots with entity counts |
| GET | `/universe/{qid}` | Universe detail: entities, relationships, metadata |
| GET | `/universe/{qid}/graph` | Cytoscape.js-ready JSON; supports `?type=`, `?work=`, `?ego=`, `?timeline_year=` filters |
| GET | `/universe/{qid}/lore-delta` | Check for Wikidata revisions newer than stored |
| GET | `/metadata/{entityId}/canon-discrepancies` | Compare edition against master work |

---

## Configuration

Universe graph parameters in `config/hydration.json`:

| Key | Default | Purpose |
|---|---|---|
| `fetch_temporal_qualifiers` | true | Include P580/P582 in Data Extension requests |
| `batch_query_size` | 50 | Max entities per batch Data Extension call |
| `lineage_depth` | 2 | Maximum relationship traversal depth |
| `lore_delta_check_on_explorer_open` | true | Auto-check for Wikidata changes when Chronicle Explorer loads |
| `canon_discrepancy_detection` | true | Enable edition vs. master work comparison |
| `era_actor_resolution` | true | Enable temporal actor resolution |

---

## Query Efficiency

Three layers prevent redundant Wikidata calls:

1. **Skip-if-enriched** â€” entity level. If `enriched_at IS NOT NULL`, no new Data Extension call is made.
2. **Provider response cache** â€” HTTP level. SHA-256 of request URL is checked in `provider_response_cache` before any outbound call. ETag revalidation on cache expiry.
3. **Universe-level deduplication** â€” if a QID is already known in the graph, it is not re-fetched from Wikidata.

## Related

- [How Universes and Series Work](../explanation/how-universes-work.md)
- [Hubs and Playlists](hubs-and-playlists.md)
- [Glossary](../reference/glossary.md)
