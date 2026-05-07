# Feature: Universal Parameterized Collection System & Grouping Model

> **Mirrors:** `CLAUDE.md` §1 (Grouping Model), §3.13 (Universal Parameterized Collection System) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-04-01 | Auditor: Claude (Product-Led Architect)

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

Created during ingestion by `MediaEntityChainFactory` — albums, TV shows, book series work immediately when files are scanned, not waiting for Wikidata. Power the container views in media lane container views.

### Collection Placements

`collection_placements` table maps collections to UI locations (home, media lanes, media library, collections page) with display limits and position. Location is a data-driven display constraint.

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
| HBR-02 | A Work must always belong to a Series (Collection). If a Series is deleted, the Work's link is set to null (unassigned). | Schema (ON DELETE SET NULL on works.collection_id) |
| HBR-03 | Metadata Claims are append-only — historical claims are never deleted. | Domain convention + repository design |
| HBR-04 | Canonical Values are the scored winners — one per field per entity. | CanonicalValueRepository (composite PK) |
| HBR-05 | Content groups are created at ingestion time, not deferred to enrichment. | MediaEntityChainFactory |
| HBR-06 | Collection rules use normalized JSON predicates — no raw SQL in rule definitions. | CollectionRulePredicate model |
| HBR-07 | Only user-created collections (Playlist, Custom) can be deleted. System, Smart, Mix, and ContentGroup are managed by the Engine. | Collection CRUD endpoints |
| HBR-08 | Person records are linked to Media Assets via a many-to-many join (idempotent). | PersonRepository (INSERT OR IGNORE) |

---

## PO Summary

The collection system has been unified into a single universal model. Every collection — whether an auto-detected album, a genre category, or a user playlist — uses the same rule-based mechanism. Content groups (albums, TV shows, series) are created the moment files are scanned, so they work immediately. A dedicated Collections page replaces the old Collections page for managing all collection types. Users can create their own smart collections using an intuitive filter builder inspired by Apple Music's Smart Playlist interface.

