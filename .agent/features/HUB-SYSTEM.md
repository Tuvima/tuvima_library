# Feature: Hub System (Domain Model & Library Structure)

> **Mirrors:** `CLAUDE.md` §1 (Hub Concept) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## User Experience

The Hub is the central concept in Tuvima Library. Instead of browsing by file type (all my books, all my movies), you browse by *story*. A single Hub — say "Dune" — contains every version of that story in your collection: the ebook, the audiobook, the movie, the graphic novel.

### What the user sees today

- **Home page** — A grid of Hub tiles, each showing a name, work count, and media-type icons.
- **Hero tile** — The selected Hub is highlighted with artwork and progress indicators.
- **Search** — The Command Palette finds Hubs and Works by keyword.

### What the user cannot do yet

- **Browse inside a Hub** — There is no Hub detail page. You can see the list of Hubs but cannot drill into one to see its Works, Editions, or individual files.
- **Manually create or merge Hubs** — Hub creation is automatic (during ingestion). There is no UI to create, rename, merge, or split Hubs.
- **See Universe groupings** — The Universe concept (grouping related Hubs, like "Marvel Cinematic Universe") exists in the domain but is never shown in the Dashboard.

---

## The Hub Hierarchy

```
Universe  (e.g., "Tolkien's Middle-earth")
  └── Hub  (e.g., "The Lord of the Rings")
        └── Work  (e.g., "The Fellowship of the Ring")
              └── Edition  (e.g., "Extended Director's Cut Blu-ray")
                    └── Media Asset  (e.g., the actual MKV file on disk)
```

- **Universe** — A narrative world containing related Hubs. Optional. Not yet surfaced in UI.
- **Hub** — The story. One Hub per intellectual property.
- **Work** — A single title within that story (could be a book, a film, an episode).
- **Edition** — A specific physical version of that Work.
- **Media Asset** — The actual file on disk, identified by its SHA-256 fingerprint.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| HBR-01 | Every Media Asset has a unique fingerprint (SHA-256 hash). Duplicate files are rejected. | MediaAssetRepository (UNIQUE constraint) |
| HBR-02 | A Work must always belong to a Hub. If a Hub is deleted, the Work's Hub link is set to null (unassigned). | Schema (ON DELETE SET NULL on works.hub_id) |
| HBR-03 | Metadata Claims are append-only — historical claims are never deleted. | Domain convention + repository design |
| HBR-04 | Canonical Values are the scored winners — one per field per entity. | CanonicalValueRepository (composite PK) |
| HBR-05 | Conflicted assets (scoring too close to call) are not auto-assigned to a Hub. | ScoringEngine (AssetStatus.Conflicted) |
| HBR-06 | Orphaned assets (file deleted from disk) should be flagged with AssetStatus.Orphaned. | Domain design (not yet implemented) |
| HBR-07 | The seed Owner profile cannot be deleted, and the last Administrator cannot be deleted. | ProfileService |
| HBR-08 | Person records are linked to Media Assets via a many-to-many join (idempotent). | PersonRepository (INSERT OR IGNORE) |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| Hub aggregate (domain) | **PASS** | Clean POCO with Works collection and DisplayName. |
| Work aggregate (domain) | **PASS** | MediaType, SequenceIndex for series, Claims + CanonicalValues. |
| Edition aggregate (domain) | **PASS** | FormatLabel, child MediaAssets, own Claims + CanonicalValues. |
| MediaAsset (domain) | **PASS** | ContentHash as identity anchor, Status enum (Normal/Conflicted/Orphaned). |
| Person entity (domain) | **PASS** | Name, Role, Wikidata enrichment fields. |
| Hub repository | **PASS** | Two-query loading (no N+1), case-insensitive DisplayName search, idempotent upsert. |
| Hub API (listing) | **PASS** | `GET /hubs` returns all hubs with works and canonical values. |
| Hub API (search) | **WARN** | Brute-force in-memory search over all hubs. Will not scale to very large libraries. |
| Hub detail page | **FAIL** | **Does not exist.** No `/hub/{id}` route. The Command Palette and search link to it but land on 404. |
| Hub management UI | **FAIL** | No UI to create, rename, merge, or split Hubs. All Hub creation is automatic. |
| Hub repository FindByIdAsync | **FAIL** | Method does not exist — cannot efficiently load a single Hub for a detail page. |
| Work/Edition repositories | **FAIL** | `IWorkRepository` and `IEditionRepository` do not exist. Cannot query Works or Editions independently. |
| Universe surfacing | **WARN** | Universe entity exists in domain but is never shown in the Dashboard. |
| Orphan detection | **FAIL** | Deleted files are logged but assets are never marked as Orphaned in the database. |
| Work proliferation | **WARN** | Each ingested file creates a new Work + Edition chain, even if the same Work already exists under the same Hub. May produce redundant records over time. |

---

## PO Summary

The Hub concept is well-designed at the domain level — the hierarchy of Universe → Hub → Work → Edition → Media Asset is clean and complete. Hub creation, scoring, and organisation all work automatically. **The biggest gap is the absence of a Hub detail page — users can see their Hubs in the grid but cannot drill into one to see its contents. Supporting infrastructure is also missing: no Work/Edition repositories, no Hub management UI, and no orphan detection for deleted files.**
