# Feature: Grouping Model — Universes & Series (Domain Model & Library Structure)

> **Mirrors:** `CLAUDE.md` §1 (Grouping Model) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-26 | Auditor: Claude (Product-Led Architect)

> **Terminology:** User-facing names differ from internal code names. See table below.

---

## Terminology

| Level | User-facing name | Internal code name | Example |
|---|---|---|---|
| Entire library | **Library** | Library | Everything you own |
| Franchise grouping | **Universe** | ParentHub | Dune, Marvel, Tolkien |
| Series / collection | **Series** | Hub | Dune Novels, Dune Films |
| Single title | **Work** | Work | Dune Part One |
| Specific version | **Edition** | Edition | 4K HDR Blu-ray Remux |
| File on disk | **Media Asset** | MediaAsset | the .mkv file |

**Rule:** Anything the user sees (UI labels, column headers, tab names) uses the user-facing name. Internal code names (ParentHub, Hub) stay in the domain/engine layer only.

---

## User Experience

The Universe and Series concepts are central to Tuvima Library. Instead of browsing by file type (all my books, all my movies), you browse by *story*. A single Universe — say "Dune" — contains every version of that story in your collection, organised into Series: Dune Novels, Dune Films, Dune Audiobooks.

### What the user sees today

- **Home page** — A grid of Series tiles, each showing a name, work count, and media-type icons.
- **Hero tile** — The selected Series is highlighted with artwork and progress indicators.
- **Search** — The Command Palette finds Series and Works by keyword.

### What the user cannot do yet

- **Browse inside a Series** — There is no Series/Universe detail page. You can see the list of Series but cannot drill into one to see its Works, Editions, or individual files.
- **Manually create or merge Series** — Series creation is automatic (during ingestion). There is no UI to create, rename, merge, or split Series.
- **See Universe groupings** — The Universe concept (grouping related Series, like "Marvel Cinematic Universe") exists in the domain but is never shown in the Dashboard.

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

- **Library** — Your entire media collection.
- **Universe** — A creative world containing related Series. Optional. Not yet surfaced in UI. Internal code: `ParentHub`.
- **Series** — The story grouping. One Series per intellectual property sub-grouping. Internal code: `Hub`.
- **Work** — A single title within that Series (could be a book, a film, an episode).
- **Edition** — A specific physical version of that Work.
- **Media Asset** — The actual file on disk, identified by its SHA-256 fingerprint.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| HBR-01 | Every Media Asset has a unique fingerprint (SHA-256 hash). Duplicate files are rejected. | MediaAssetRepository (UNIQUE constraint) |
| HBR-02 | A Work must always belong to a Series (Hub). If a Series is deleted, the Work's link is set to null (unassigned). | Schema (ON DELETE SET NULL on works.hub_id) |
| HBR-03 | Metadata Claims are append-only — historical claims are never deleted. | Domain convention + repository design |
| HBR-04 | Canonical Values are the scored winners — one per field per entity. | CanonicalValueRepository (composite PK) |
| HBR-05 | Conflicted assets (scoring too close to call) are not auto-assigned to a Series. | ScoringEngine (AssetStatus.Conflicted) |
| HBR-06 | Orphaned assets (file deleted from disk) should be flagged with AssetStatus.Orphaned. | Domain design (not yet implemented) |
| HBR-07 | The seed Owner profile cannot be deleted, and the last Administrator cannot be deleted. | ProfileService |
| HBR-08 | Person records are linked to Media Assets via a many-to-many join (idempotent). | PersonRepository (INSERT OR IGNORE) |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| Hub aggregate (domain) | **PASS** | Clean POCO with Works collection and DisplayName. Internal name: `Hub` (user-facing: Series). |
| Work aggregate (domain) | **PASS** | MediaType, SequenceIndex for series, Claims + CanonicalValues. |
| Edition aggregate (domain) | **PASS** | FormatLabel, child MediaAssets, own Claims + CanonicalValues. |
| MediaAsset (domain) | **PASS** | ContentHash as identity anchor, Status enum (Normal/Conflicted/Orphaned). |
| Person entity (domain) | **PASS** | Name, Role, Wikidata enrichment fields. |
| Hub repository | **PASS** | Two-query loading (no N+1), case-insensitive DisplayName search, idempotent upsert. |
| Hub API (listing) | **PASS** | `GET /hubs` returns all Series with works and canonical values. |
| Hub API (search) | **WARN** | Brute-force in-memory search over all Series. Will not scale to very large libraries. |
| Series detail page | **FAIL** | **Does not exist.** No `/universe/{id}` route. The Command Palette and search link to it but land on 404. |
| Series management UI | **FAIL** | No UI to create, rename, merge, or split Series. All Series creation is automatic. |
| Hub repository FindByIdAsync | **FAIL** | Method does not exist — cannot efficiently load a single Series for a detail page. |
| Work/Edition repositories | **FAIL** | `IWorkRepository` and `IEditionRepository` do not exist. Cannot query Works or Editions independently. |
| Universe surfacing | **WARN** | ParentHub entity exists in domain but is never shown in the Dashboard as Universe groupings. |
| Orphan detection | **FAIL** | Deleted files are logged but assets are never marked as Orphaned in the database. |
| Work proliferation | **WARN** | Each ingested file creates a new Work + Edition chain, even if the same Work already exists under the same Series. May produce redundant records over time. |

---

## PO Summary

The grouping model is well-designed at the domain level — the hierarchy of Library → Universe → Series → Work → Edition → Media Asset is clean and complete. Series creation, scoring, and organisation all work automatically. **The biggest gap is the absence of a Universe/Series detail page — users can see their Series in the grid but cannot drill into one to see its contents. Supporting infrastructure is also missing: no Work/Edition repositories, no Series management UI, and no orphan detection for deleted files.**
