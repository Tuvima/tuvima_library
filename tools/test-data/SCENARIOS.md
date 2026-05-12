# Tuvima Library — Ingestion Test Scenarios

**47 standard scenarios** covering Books, Audiobooks, Movies, TV, Music, Comics, and the general drop zone.
**118 large-corpus scenarios** are available with `--large` for stress testing canonical matching, people enrichment, series linking, and batched provider flow.
The generator now includes cross-media person and series coverage so the ingestion harness can validate links across formats.

---

## How to run

```powershell
# One command - wipes dev environment, regenerates all 47 files:
powershell -ExecutionPolicy Bypass -File tools/test-data/reset-and-generate.ps1

# Large stress corpus - wipes dev environment, regenerates 118 files:
powershell -ExecutionPolicy Bypass -File tools/test-data/reset-and-generate.ps1 -Large

# Generator only (no wipe):
dotnet run --project tools/GenerateTestEpubs

# Generator only, large corpus:
dotnet run --project tools/GenerateTestEpubs -- --large

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
| 1 | `dune.epub` | Fully tagged: title, author, ISBN, series, embedded cover | Staged to `.staging/pending/`; after hydration, promoted to library; "Dune" Collection created; cover served from file |
| 2 | `neuromancer.epub` | Rich metadata, **no embedded cover** | Staged to `.staging/pending/`; after hydration, promoted to library; cover fetched from provider |
| 8 | `phantom-signal-filename-only.epub` | **Filename only** — all OPF fields empty | confidence < 0.40 → moved to `.staging/`; review queue entry created |
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

### Group D — Audiobook Cross-Format Collection Linking (M4B)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 11 | `dune-audiobook.m4b` | Same title/author/series as EPUB #1 | Joins **existing** Dune Collection — no second Collection created |

### Group E — Narrator Person Records (M4B)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 12 | `hitchhikers-guide.m4b` | Narrator credited in ID3 comment tag | Narrator Person record created for **Stephen Fry** |
| 13 | `wool-omnibus.m4b` | Narrator field = **two names** joined by " and " | Two separate Narrator Person records (Amanda Donahoe + Tim Gerard Reynolds) |
| 14 | `enders-game.m4b` | Series audiobook, **no embedded cover** | Collection + series_pos set; cover filled from provider (Audnexus / Apple Books) |

### Group F — Audiobook Edge Cases (M4B)

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 15 | `echoes-filename-only.m4b` | **No ID3 tags at all** | confidence < 0.40 → `.staging/`; review queue entry |
| 16 | `the-wasp-factory.m4b` | Author = **"Iain Banks"** (has pen name "Iain M. Banks") | Wikidata P742 link discovered; both Person records linked |

### Group G — Ingestion Hinting (M4B, sibling files)

Files in the same source subfolder. The Engine primes a `FolderHint` from the
first file, then applies it to siblings — pre-assigning them to the same Collection and
skipping a redundant Stage 1 SPARQL lookup.

| # | File | Scenario | Expected outcome |
|---|------|----------|-----------------|
| 17 | `hp-series/harry-potter-philosophers-stone.m4b` | HP #1 — **first in folder** | Full three-stage pipeline; `FolderHint` primed with HP Collection ID + QID |
| 18 | `hp-series/harry-potter-chamber-of-secrets.m4b` | HP #2 — **sibling in same folder** | `FolderHint` applied; Collection pre-assigned; Stage 1 SPARQL **skipped** |
| 19 | `expanse-audio/leviathan-wakes-audio.m4b` | Expanse #1 — **first in folder** | Full pipeline; `FolderHint` primed with Expanse Collection ID + bridge IDs |
| 20 | `expanse-audio/calibans-war-audio.m4b` | Expanse #2 — **sibling in same folder** | `FolderHint` applied; same Collection; Stage 1 SPARQL **skipped** |

---

## Video, music, comic, and general fixtures

The harness now writes these folders in addition to `books/`:

```
C:\temp\tuvima-watch\movies\   # scenarios 33-39
C:\temp\tuvima-watch\tv\       # scenarios 40-42
C:\temp\tuvima-watch\music\    # scenarios 43-44
C:\temp\tuvima-watch\comics\   # scenarios 45-46
```

Movies and TV use MP4 fixtures; music uses MP3 fixtures. If FFmpeg is unavailable, the generator writes minimal valid MP4/MP3 files so these media types are still present in every run.

| # | Category | Scenario focus |
|---|----------|----------------|
| 33-37 | Movies | Dune and Middle-earth movie series grouping and TMDB/IMDb bridge IDs |
| 38-39 | Movies | Standalone Arrival and The Shawshank Redemption movie fixtures |
| 40-41 | TV | Breaking Bad episode grouping and cast/person enrichment, including Aaron Paul and Anna Gunn |
| 42 | TV | Shogun 2024 episode grouping and TV disambiguation |
| 43-44 | Music | David Bowie album grouping and artist/person enrichment |
| 45-46 | Comics | Watchmen issue grouping |
| 47 | General | Unsorted text drop-zone smoke fixture |

The generated manifest's `expected_person_enrichment` section names the repeated people that should be checked after ingestion. It includes cross-format creators, series-level TV cast, comic creators, and music artists so person enrichment regressions are visible across media types.

### Large corpus additions

The `--large` corpus adds 71 more files: 16 books, 8 audiobooks, 21 movies, 14 TV episodes, and 12 music tracks. It intentionally stresses:

| Area | Added titles |
|---|---|
| Stephen King disambiguation | `The Shining`, `Doctor Sleep`, `It`, `The Shining (1980)`, `Doctor Sleep (2019)` |
| Blade Runner canonical matching | `Do Androids Dream of Electric Sheep?`, `Blade Runner`, `Blade Runner 2049` |
| Foundation series | `Foundation and Empire`, `Second Foundation`, `Foundation` TV episodes |
| Middle-earth | `The Hobbit`, LOTR books, Hobbit movies, LOTR movies |
| Cross-format author/adaptation | `The Martian`, `Project Hail Mary`, `A Game of Thrones`, `The Expanse`, `The Last of Us` |
| Repeated filmmakers/composers | Christopher Nolan films, Hans Zimmer film and soundtrack credits |
| Music batching | David Bowie, Radiohead, The Beatles, Taylor Swift, Kendrick Lamar, Hans Zimmer albums |

---

## Pass / Fail checklist

Copy this block into `TEST-RESULTS.md` after a test run:

```
Date: ___________   Engine version: ___________

Group A — Confidence Gates
  [ ] #1  dune.epub               → auto-organized
  [ ] #2  neuromancer.epub        → cover from provider
  [ ] #8  phantom-signal          → .staging/ quarantine
  [ ] #9  corrupt-epub.epub       → MediaFailed, not in DB
  [ ] #10 dune-duplicate.epub     → DuplicateSkipped

Group B — Person Records & Conflicts
  [ ] #3  foundation.epub         → author conflict flagged
  [ ] #4  name-of-the-wind.epub   → series + series_pos set

Group C — Pseudonyms
  [ ] #5  leviathan-wakes.epub    → James S.A. Corey → Abraham + Franck
  [ ] #6  the-running-man.epub    → Bachman → King link
  [ ] #7  the-cuckoos-calling.epub→ Galbraith → Rowling link

Group D — Cross-Format Collection Link
  [ ] #11 dune-audiobook.m4b      → joins Dune Collection (not a new Collection)

Group E — Narrator Records
  [ ] #12 hitchhikers-guide.m4b   → Stephen Fry person record
  [ ] #13 wool-omnibus.m4b        → two narrator records
  [ ] #14 enders-game.m4b         → cover from provider

Group F — Audiobook Edge Cases
  [ ] #15 echoes-filename-only    → .staging/ quarantine
  [ ] #16 the-wasp-factory.m4b    → Iain Banks P742 link

Group G — Ingestion Hinting
  [ ] #17 hp-stone.m4b            → hint primed
  [ ] #18 hp-chamber.m4b          → hint applied, Stage 1 skipped
  [ ] #19 expanse-lw-audio.m4b    → hint primed
  [ ] #20 expanse-cw-audio.m4b    → hint applied, Stage 1 skipped

Summary
  Total ingested  : __ / 18  (10, 15 are quarantine; verify via .staging/)
  Collections created    : __
  Review queue    : __ items
  Person records  : __
  Activity entries: __
```
