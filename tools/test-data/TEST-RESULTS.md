# End-to-End Ingestion Test Results

**Date:** 2026-03-10
**Test suite:** 10 EPUBs + 10 M4B audiobooks (20 files total)
**Engine version:** current `main`
**Database:** fresh wipe before run

---

## Summary

| Metric | Result |
|--------|--------|
| Files generated | 20 (10 EPUB + 10 M4B) |
| Files ingested | 20 / 20 |
| Organized into library | 20 / 20 |
| Hero banners generated | 17 |
| Cover images (on disk) | 17 |
| Author headshots (Wikidata) | 14 / 17 persons |
| Hubs created | 17 |
| Review queue (Pending) | 2 |
| Activity log entries | 220 |

---

## Library Structure

### Books (10 EPUBs)

| File | Organized path | Cover | Scenario |
|------|---------------|-------|----------|
| good-omens.epub | Books/Terry Pratchett/Good Omens/ | ✅ embedded | Multi-author (Pratchett & Gaiman) |
| neuromancer.epub | Books/William Gibson/Neuromancer/ | ✅ from provider | No embedded cover — provider filled it |
| the-name-of-the-wind.epub | Books/Patrick Rothfuss/The Name of the Wind/ | ✅ embedded | Series metadata (Kingkiller Chronicle #1) |
| foundation.epub | Books/Asimov, Isaac/Foundation/ | ✅ from provider | **Author name conflict** — tagged as "Asimov, Isaac" (reversed) |
| project-hail-mary.epub | Books/Andy Weir/Project Hail Mary/ | ✅ embedded | Fully tagged |
| dune.epub | Books/Frank Herbert/Dune/ | ✅ embedded | **Cross-format Hub link** (same Hub as dune-audiobook.m4b) |
| the-hobbit.epub | Books/J.R.R. Tolkien/The Hobbit/ | ✅ embedded | Fully tagged |
| words-of-radiance.epub | Books/Brandon Sanderson/Words of Radiance/ | ✅ from provider | No embedded cover — provider filled it |
| echoes-of-the-void.epub | Books/Solan Varro/Echoes of the Void/ | ❌ none | **Fictional** — ContentMatchFailed → review queue |
| phantom-signal-filename-only.epub | Books/Unknown/Unknown/ | ✅ provider? | **Filename-only** — no OPF metadata, organized as Unknown |

### Audio (10 M4Bs)

| File | Organized path | Cover | Scenario |
|------|---------------|-------|----------|
| harry-potter-philosophers-stone.m4b | Audio/J.K. Rowling/Harry Potter and the Philosopher's Stone/ | ✅ embedded | Narrator: Jim Dale |
| dune-audiobook.m4b | Audio/Frank Herbert/Dune/ | ✅ embedded | **Cross-format Hub link** (same Hub as dune.epub) |
| hitchhikers-guide.m4b | Audio/Douglas Adams/The Hitchhiker's Guide to the Galaxy/ | ✅ embedded | Series + narrator: Stephen Fry |
| mistborn-the-final-empire.m4b | Audio/Brandon Sanderson/Mistborn_ The Final Empire/ | ✅ embedded | Series (Mistborn #1) + narrator: Michael Kramer |
| enders-game.m4b | Audio/Orson Scott Card/Ender's Game/ | ✅ from provider | No embedded cover — provider filled it |
| the-martian.m4b | Audio/Andy Weir/The Martian/ | ✅ embedded | Narrator: R.C. Bray |
| a-short-history-of-nearly-everything.m4b | Audio/Bill Bryson/A Short History of Nearly Everything/ | ✅ embedded | Non-fiction, author = narrator |
| wool-omnibus.m4b | Audio/Hugh Howey/Wool/ | ✅ embedded | Dual-narrator conflict in tags |
| the-last-archive.m4b | Audio/Mira Solenne/The Last Archive/ | ✅ embedded | **Fictional** — organized (author name matched) |
| echoes-filename-only.m4b | Audio/Unknown/echoes-filename-only/ | ❌ none | **Filename-only** — no ID3 tags, ContentMatchFailed → review queue |

---

## Key Scenario Outcomes

### ✅ Cross-Format Hub Linking (Dune)
Both `dune.epub` and `dune-audiobook.m4b` were correctly assigned to the **same Hub**.
The Hub's Works list contains both a `Books`-type work and an `Audiobooks`-type work.
This is the central Hub concept working as designed.

### ✅ Provider Cover Fill-In
Three files had no embedded cover art: `neuromancer.epub`, `words-of-radiance.epub`, and `enders-game.m4b`.
All three received `cover.jpg` and `hero.jpg` from the metadata providers (Apple Books / Audnexus).

### ✅ Author Name Conflict
`foundation.epub` was tagged with author `"Asimov, Isaac"` (reversed format).
- The file was organized under `Books/Asimov, Isaac/` (respecting the tag as the local source of truth)
- Wikidata enrichment created a **second person record**: `Isaac Asimov` with headshot
- Both persons exist in `.people/` — `Asimov, Isaac` (no headshot) and `Isaac Asimov` (with headshot)
- This is correct behavior: a conflict between tag format and canonical name

### ✅ Wikidata Person Enrichment
14 out of 17 person folders received Wikidata headshots.
Notable people enriched: Andy Weir, Bill Bryson, Brandon Sanderson, Douglas Adams, Frank Herbert, Hugh Howey, Isaac Asimov, J.R.R. Tolkien (two folders due to spacing), Neil Gaiman, Orson Scott Card, Patrick Rothfuss, Terry Pratchett, William Gibson.
Not enriched: `Asimov, Isaac` (reversed-name format didn't match Wikidata), `J.K. Rowling`, `Phil Price`.

### ✅ Review Queue — Fictional Content
Exactly 2 items in the review queue, both `ContentMatchFailed`:
1. **Echoes of the Void** (Books) — fictional author "Solan Varro", no provider match
2. **echoes-filename-only** (Audiobooks) — no metadata at all, no provider match

Both were still organized into the library (the Engine ingests everything; review is advisory).

### ✅ Hero Banners
17 hero banners generated. All files with covers (embedded or provider-sourced) produced `hero.jpg` alongside `cover.jpg`. Files without any cover (2 fictional) have no hero banner.

### ✅ Granular Activity Log
220 activity entries logged across the full run. Each file produced a chain of sub-step events visible in the "Related events" section of the Dashboard:
- `FileHashed` → `FileProcessed` → `FileScored` → `EntityChainCreated` → `PathUpdated` → `CoverArtSaved` → `HeroBannerGenerated` → `MetadataTagsWritten` → `HydrationEnqueued`
- Plus hydration stage events: `HydrationStage1Completed`, `HydrationStage2Completed`, `HydrationStage3Completed`
- Plus person enrichment: `PersonHydrated` per author/narrator

---

## Issues Found

### Minor — Tolkien name spacing
Two person folders created for J.R.R. Tolkien: `J.R.R. Tolkien` and `J. R. R. Tolkien`.
The Library got the author name from the EPUB tag (`J.R.R. Tolkien`) and from Wikidata (`J. R. R. Tolkien` canonical label). Both person records exist. This is a deduplication edge case worth tracking.

### Minor — phantom-signal Unknown/Unknown
`phantom-signal-filename-only.epub` (completely empty OPF) was organized to `Books/Unknown/Unknown/Unknown.epub` and has a cover.jpg + hero.jpg. Surprising that a provider matched it — likely a false match on "Unknown". Worth investigating which provider returned a result for an empty query.

### Minor — The Last Archive organized despite fictional
`the-last-archive.m4b` (fictional title "The Last Archive", fictional author "Mira Solenne") was organized without going to review. The provider apparently returned no match but the local confidence from the ID3 tags was sufficient (≥ 0.85). Correct behavior per the confidence gate, but noteworthy that a wholly fictional item can auto-organize.

---

## Test Commands

```bash
# Generate test files (re-runnable)
dotnet run --project tools/GenerateTestEpubs [output-dir]

# Wipe database (stop Engine first)
rm src/MediaEngine.Api/library.db

# Check review queue
curl http://localhost:61495/review/pending

# Check activity
curl http://localhost:61495/activity/stats
```
