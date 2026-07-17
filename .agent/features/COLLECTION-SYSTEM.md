# Feature: Universal Parameterized Collection System & Grouping Model

> **Mirrors:** `CLAUDE.md` §1 (Grouping Model), §3.13 (Universal Parameterized Collection System) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-07-16 | Auditor: Codex

> **Terminology:** User-facing names differ from internal code names. See table below.

---

## Terminology

| Level | User-facing name | Internal code name | Example |
|---|---|---|---|
| Entire library | **Library** | Library | Everything you own |
| Franchise grouping | **Universe** | ParentCollection | Dune, Marvel, Tolkien |
| Series / collection | **Series** | Collection | Dune Novels, Dune Films |
| Single title | **Work** | Work | Dune Part One |
| Specific version | **Edition** | Edition | 4K HDR Blu-ray Remux |
| File on disk | **Media Asset** | MediaAsset | the .mkv file |

**Rule:** Anything the user sees (UI labels, column headers, tab names) uses the user-facing name. Internal code names (ParentCollection, Collection) stay in the domain/engine layer only.

**Shelf vs Collection rule:** Read, Watch, and Listen show immediate shelves: book series, movie series, TV shows, music albums, and audio series. The Collections page shows broader rollups only when a shared series/franchise/universe relationship connects at least two shelves. A single shelf, even with multiple works, remains in its lane; multiple formats of one Work are variants and do not create a rollup.

**Container card rule:** Non-TV series and collection containers use a fixed-size landscape card backed by owned-child previews. Hover or keyboard focus reveals an in-place carousel without growing the card. The resting action opens the container; the selected-child action follows the child route supplied by the display API. Ordered series preserve Engine order, while broader curated collections may use a decorative resting stack. TV shows are rendered as rich media identities instead: show cover at rest and the show-level cinematic background, logo, facts, description, and actions on hover; owned episodes stay available on the show detail surface.

---

## Universal Collection Model

Every collection in Tuvima Library is a **collection** — a parameterized query container. Albums, TV shows, genre categories, user playlists, and AI recommendations all use the same mechanism: normalized filter predicates evaluated against library metadata.

### Collection Rules

Rules are JSON arrays of `{field, op, value}` predicates:
```json
[
  { "field": "media_type", "op": "eq", "value": "Movie" },
  { "field": "genre", "op": "eq", "value": "Horror" }
]
```

Operators: eq, neq, contains, gt, lt, between, in, like. Match mode: ALL (AND) or ANY (OR).

### Collection Types (Presentation Hints)

| Type | Created by | Resolution | Editable |
|------|-----------|------------|----------|
| **ContentGroup** | Engine (during ingestion) | Materialized | Read-only, drillable |
| **Smart** | Engine (auto-generated) | Query-resolved | Disable/enable, feature |
| **System** | System (pre-created) | Materialized | Add/remove/reorder |
| **Mix** | Engine + AI | Materialized | Enable/disable |
| **Playlist** | User | Materialized | Full CRUD |
| **Custom** | User (collection builder) | Query-resolved | Full CRUD + edit rules |

### Hybrid Resolution

- **Query-resolved** (Smart, Custom): predicates evaluated at display time, always fresh
- **Materialized** (ContentGroup, System, Playlist, Mix): items explicitly linked in `collection_works`

### Content Groups

Created during collection finalization after quick hydration or retained-retail organization. Albums, TV shows, book series, and movie series power the shelves in Read, Watch, and Listen. A shelf can be identified by a Wikidata series QID, provider-backed grouping key, or local series/show/album metadata. It becomes eligible for a top-level Collections rollup only when another shelf shares a broader trusted series/franchise/universe relationship.

### Collection Placements

`collection_placements` table maps collections to UI locations (home, media lanes, browse lanes, and the collections page) with display limits and position. Location is a data-driven display constraint.

---

## The Grouping Hierarchy

```
Library   (your entire collection)
  └── Universe   (e.g., "Tolkien's Middle-earth")
        └── Series   (e.g., "The Lord of the Rings")
              └── Work   (e.g., "The Fellowship of the Ring")
                    └── Edition   (e.g., "Extended Director's Cut Blu-ray")
                          └── Media Asset   (e.g., the actual MKV file on disk)
```

---

## Collections Page (`/collections`)

Dedicated page for browsing, creating, and managing all collection types. Replaces the former Collections page. Features:
- Type filter chips (All, Content Groups, Smart, System Lists, Mixes, Playlists, Custom)
- Grid/list view toggle
- Collection builder for creating Custom collections (Apple Music Smart Playlist-style filter rows)
- Live preview powered by `POST /collections/preview`

---

## Key Files

| File | Purpose |
|------|---------|
| `src/MediaEngine.Domain/Models/CollectionRulePredicate.cs` | Predicate model |
| `src/MediaEngine.Domain/Entities/CollectionPlacement.cs` | Placement entity |
| `src/MediaEngine.Storage/CollectionRuleEvaluator.cs` | Rule-to-SQL translator |
| `src/MediaEngine.Storage/CollectionPlacementRepository.cs` | Placement persistence |
| `src/MediaEngine.Web/Components/Collections/CollectionsPage.razor` | Collections page |
| `src/MediaEngine.Web/Components/Collections/CollectionBuilder.razor` | Filter builder UI |

Migration: M-070 (collection columns + collection_placements table + backfill)

---

## API Endpoints

| Endpoint | Purpose |
|----------|---------|
| `GET /collections/management-catalog` | Collections hub catalog: system/user/managed collections plus broader multi-shelf rollups |
| `GET /collections/{id}/summary` | One Collections hub summary for the detail page |
| `GET /collections/{id}/items` | Detail-page items, including generated rollup aggregation |
| `POST /collections/reconcile` | Repair missing shelf assignments for already-ingested media |
| `GET /collections/resolve/{id}` | Evaluate rules, return items |
| `POST /collections/preview` | Evaluate rules without saving |
| `GET /collections/by-location/{location}` | Collections at a UI location |
| `GET /collections/field-values/{field}` | Autocomplete for builder |
| `POST/PUT/DELETE /collections` | Full CRUD |
| `GET/PUT /collections/{id}/placements` | Placement management |

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| HBR-01 | Every Media Asset has a unique fingerprint (SHA-256 hash). Duplicate files are rejected. | MediaAssetRepository (UNIQUE constraint) |
| HBR-02 | A Work should be assigned to a shelf when grouping metadata exists; standalone or unresolved works may remain unassigned. If a Series is deleted, the Work's link is set to null. | CollectionFinalizationService + schema ON DELETE SET NULL |
| HBR-03 | Metadata Claims are append-only — historical claims are never deleted. | Domain convention + repository design |
| HBR-04 | Canonical Values are the scored winners — one per field per entity. | CanonicalValueRepository (composite PK) |
| HBR-05 | Content groups are finalized after quick hydration or retained-retail organization, and repaired by collection reconciliation when older items are missing shelves. | CollectionFinalizationService + CollectionBackfillService |
| HBR-06 | Collection rules use normalized JSON predicates — no raw SQL in rule definitions. | CollectionRulePredicate model |
| HBR-07 | Only user-created collections (Playlist, Custom) can be deleted. System, Smart, Mix, and ContentGroup are managed by the Engine. | Collection CRUD endpoints |
| HBR-08 | Person records are linked to Media Assets via a many-to-many join (idempotent). | PersonRepository (INSERT OR IGNORE) |

---

## PO Summary

The collection system has been unified into a single universal model. Every collection, whether an auto-detected album, a genre category, or a user playlist, uses the same storage model. Content groups are finalized after identity/readiness work so shelves still appear even when Wikidata cannot resolve a QID. The Collections page stays focused on broader rollups and authored collections instead of duplicating every shelf from Read, Watch, and Listen.
