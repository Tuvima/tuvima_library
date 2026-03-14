# Tuvima Library — Ingestion Test Scenarios

**20 scenarios** covering every major ingestion edge case for Books and Audiobooks.
Designed to be expanded: add new rows to each table as Music, Movies, and other media types are implemented.

---

## How to run

```powershell
# One command — wipes dev environment, regenerates all 20 files:
powershell -ExecutionPolicy Bypass -File tools/test-data/reset-and-generate.ps1

# Generator only (no wipe):
dotnet run --project tools/GenerateTestEpubs

# Generator with clean output directory:
dotnet run --project tools/GenerateTestEpubs -- --clean
```

After the files land in the watch folder, start (or restart) the Engine:
```
dotnet run --project src/MediaEngine.Api
```

---

## Watch folder layout

```
C:\temp\tuvima-watch\books\          ← Engine watch_directory (core.json)
  dune.epub                           #  1
  neuromancer.epub                    #  2
  foundation.epub                     #  3
  the-name-of-the-wind.epub           #  4
  leviathan-wakes.epub                #  5
  the-running-man.epub                #  6
  the-cuckoos-calling.epub            #  7
  phantom-signal-filename-only.epub   #  8
  corrupt-epub.epub                   #  9
  dune-duplicate.epub                 # 10
  dune-audiobook.m4b                  # 11
  hitchhikers-guide.m4b               # 12
  wool-omnibus.m4b                    # 13
  enders-game.m4b                     # 14
  echoes-filename-only.m4b            # 15
  the-wasp-factory.m4b                # 16
  hp-series/
    harry-potter-philosophers-stone.m4b  # 17
    harry-potter-chamber-of-secrets.m4b  # 18
  expanse-audio/
    leviathan-wakes-audio.m4b            # 19
    calibans-war-audio.m4b               # 20
  MANIFEST.json                       ← generated file list
```

---

## Scenario table

### Group A — Confidence Gates (EPUB)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 1 | `dune.epub` | Fully tagged: title, author, ISBN, series, embedded cover | Auto-organized into library; "Dune" Hub created; cover served from file |
| 2 | `neuromancer.epub` | Rich metadata, **no embedded cover** | Hub created; cover fetched from provider (Apple Books / Open Library) |
| 8 | `phantom-signal-filename-only.epub` | **Filename only** — all OPF fields empty | confidence < 0.40 → moved to `.orphans/`; review queue entry created |
| 9 | `corrupt-epub.epub` | **Corrupt bytes** — not a valid ZIP | `IsCorrupt = true` → not in database; `MediaFailed` activity logged |
| 10 | `dune-duplicate.epub` | **Byte-identical copy** of scenario 1 | Hash check fires before processor; `DuplicateSkipped` activity; no new asset |

### Group B — Person Records & Metadata Conflicts (EPUB)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 3 | `foundation.epub` | Author tagged as **"Asimov, Isaac"** (Last, First reversed) | Author conflict flagged; two Person records created until Wikidata normalises |
| 4 | `the-name-of-the-wind.epub` | Series + series_pos in OPF | `series` = "The Kingkiller Chronicle" and `series_pos` = "1" set on Work |

### Group C — Pseudonyms (EPUB)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 5 | `leviathan-wakes.epub` | Author = **"James S.A. Corey"** (collective pen name) | Pseudonym link; two real-author Person records (Daniel Abraham + Ty Franck) |
| 6 | `the-running-man.epub` | Author = **"Richard Bachman"** (Stephen King pen name) | Person record for Bachman; P1773 link → Stephen King resolved via Wikidata |
| 7 | `the-cuckoos-calling.epub` | Author = **"Robert Galbraith"** (J.K. Rowling pen name) | Person record for Galbraith; P1773 link → J.K. Rowling resolved via Wikidata |

### Group D — Audiobook Cross-Format Hub Linking (M4B)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 11 | `dune-audiobook.m4b` | Same title/author/series as EPUB #1 | Joins **existing** Dune Hub — no second Hub created |

### Group E — Narrator Person Records (M4B)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 12 | `hitchhikers-guide.m4b` | Narrator credited in ID3 comment tag | Narrator Person record created for **Stephen Fry** |
| 13 | `wool-omnibus.m4b` | Narrator field = **two names** joined by " and " | Two separate Narrator Person records (Amanda Donahoe + Tim Gerard Reynolds) |
| 14 | `enders-game.m4b` | Series audiobook, **no embedded cover** | Hub + series_pos set; cover filled from provider (Audnexus / Apple Books) |

### Group F — Audiobook Edge Cases (M4B)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 15 | `echoes-filename-only.m4b` | **No ID3 tags at all** | confidence < 0.40 → `.orphans/`; review queue entry |
| 16 | `the-wasp-factory.m4b` | Author = **"Iain Banks"** (has pen name "Iain M. Banks") | Wikidata P742 link discovered; both Person records linked |

### Group G — Ingestion Hinting (M4B, sibling files)

Files in the same source subfolder. The Engine primes a `FolderHint` from the
first file, then applies it to siblings — pre-assigning them to the same Hub and
skipping a redundant Stage 1 SPARQL lookup.

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 17 | `hp-series/harry-potter-philosophers-stone.m4b` | HP #1 — **first in folder** | Full three-stage pipeline; `FolderHint` primed with HP Hub ID + QID |
| 18 | `hp-series/harry-potter-chamber-of-secrets.m4b` | HP #2 — **sibling in same folder** | `FolderHint` applied; Hub pre-assigned; Stage 1 SPARQL **skipped** |
| 19 | `expanse-audio/leviathan-wakes-audio.m4b` | Expanse #1 — **first in folder** | Full pipeline; `FolderHint` primed with Expanse Hub ID + bridge IDs |
| 20 | `expanse-audio/calibans-war-audio.m4b` | Expanse #2 — **sibling in same folder** | `FolderHint` applied; same Hub; Stage 1 SPARQL **skipped** |

---

## Expanding to other media types

When Music, Movies, or TV are ready to test, add new rows to a new Group table here and add the corresponding specs to `tools/GenerateTestEpubs/Program.cs`. The folder structure follows the same pattern:

```
C:\temp\tuvima-watch\movies\   ← future movies intake
C:\temp\tuvima-watch\music\    ← future music intake
C:\temp\tuvima-watch\tv\       ← future TV intake
```

Each new category also needs an entry in `config/libraries.json`.

---

## Pass / Fail checklist

Copy this block into `TEST-RESULTS.md` after a test run:

```
Date: ___________   Engine version: ___________

Group A — Confidence Gates
  [ ] #1  dune.epub               → auto-organized
  [ ] #2  neuromancer.epub        → cover from provider
  [ ] #8  phantom-signal          → .orphans/ quarantine
  [ ] #9  corrupt-epub.epub       → MediaFailed, not in DB
  [ ] #10 dune-duplicate.epub     → DuplicateSkipped

Group B — Person Records & Conflicts
  [ ] #3  foundation.epub         → author conflict flagged
  [ ] #4  name-of-the-wind.epub   → series + series_pos set

Group C — Pseudonyms
  [ ] #5  leviathan-wakes.epub    → James S.A. Corey → Abraham + Franck
  [ ] #6  the-running-man.epub    → Bachman → King link
  [ ] #7  the-cuckoos-calling.epub→ Galbraith → Rowling link

Group D — Cross-Format Hub Link
  [ ] #11 dune-audiobook.m4b      → joins Dune Hub (not a new Hub)

Group E — Narrator Records
  [ ] #12 hitchhikers-guide.m4b   → Stephen Fry person record
  [ ] #13 wool-omnibus.m4b        → two narrator records
  [ ] #14 enders-game.m4b         → cover from provider

Group F — Audiobook Edge Cases
  [ ] #15 echoes-filename-only    → .orphans/ quarantine
  [ ] #16 the-wasp-factory.m4b    → Iain Banks P742 link

Group G — Ingestion Hinting
  [ ] #17 hp-stone.m4b            → hint primed
  [ ] #18 hp-chamber.m4b          → hint applied, Stage 1 skipped
  [ ] #19 expanse-lw-audio.m4b    → hint primed
  [ ] #20 expanse-cw-audio.m4b    → hint applied, Stage 1 skipped

Summary
  Total ingested  : __ / 18  (10, 15 are quarantine; verify via .orphans/)
  Hubs created    : __
  Review queue    : __ items
  Person records  : __
  Activity entries: __
```
