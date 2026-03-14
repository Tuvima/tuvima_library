# End-to-End Ingestion Test Results

---

## Run: 2026-03-14

**Test suite:** 20 files (10 EPUB + 10 M4B) — full scenario coverage per SCENARIOS.md
**Engine version:** current `main`
**Database:** fresh wipe before run
**Reset command:** `powershell -ExecutionPolicy Bypass -File tools/test-data/reset-and-generate.ps1`

### Summary

| Metric | Result | Notes |
|--------|--------|-------|
| Files generated | 20 (10 EPUB + 10 M4B) | |
| Files ingested | 20 / 20 | |
| Hubs created | 14 | Some files shared hubs (Dune cross-format) |
| Person records | 14 | Author + narrator roles |
| Review queue (Pending) | 23 | Many expected + some unexpected |
| Activity log entries | ~400 | |

### Scenario Outcomes

| # | File | Expected | Actual | Result |
|---|------|----------|--------|--------|
| 1 | `dune.epub` | Auto-organized; Dune Hub created | ✅ Dune Hub created; organized | ✅ PASS |
| 2 | `neuromancer.epub` | Hub created; cover from provider | ✅ Hub created; cover fetched | ✅ PASS |
| 3 | `foundation.epub` | Author conflict flagged | ✅ Conflict created; two Person records | ✅ PASS |
| 4 | `the-name-of-the-wind.epub` | series + series_pos set | ✅ Kingkiller Chronicle #1 set | ✅ PASS |
| 5 | `leviathan-wakes.epub` | Pseudonym: James S.A. Corey → Abraham + Franck | ⚠️ No pseudonym links created | ❌ FAIL |
| 6 | `the-running-man.epub` | Bachman → King P1773 link | ⚠️ No pseudonym links created | ❌ FAIL |
| 7 | `the-cuckoos-calling.epub` | Galbraith → Rowling P1773 link | ⚠️ No pseudonym links created | ❌ FAIL |
| 8 | `phantom-signal-filename-only.epub` | confidence < 0.40 → `.orphans/` | ❌ Organized normally (score normalized to 1.0) | ❌ FAIL |
| 9 | `corrupt-epub.epub` | `IsCorrupt=true`; `MediaFailed` logged | ❌ Ingested as "Other" category | ❌ FAIL |
| 10 | `dune-duplicate.epub` | `DuplicateSkipped` activity; no new asset | ❌ Organized as `dune (2).epub` variant | ❌ FAIL |
| 11 | `dune-audiobook.m4b` | Joins existing Dune Hub | ✅ Cross-format link worked | ✅ PASS |
| 12 | `hitchhikers-guide.m4b` | Narrator Person record: Stephen Fry | ❌ No narrator Person record created | ❌ FAIL |
| 13 | `wool-omnibus.m4b` | Two narrator Person records | ❌ No narrator Person records created | ❌ FAIL |
| 14 | `enders-game.m4b` | Hub + series_pos; cover from provider | ✅ Hub created; cover fetched | ✅ PASS |
| 15 | `echoes-filename-only.m4b` | confidence < 0.40 → `.orphans/` | ❌ Organized normally (score normalized to 1.0) | ❌ FAIL |
| 16 | `the-wasp-factory.m4b` | Iain Banks P742 link discovered | ⚠️ No pseudonym links created | ❌ FAIL |
| 17 | `hp-series/harry-potter-philosophers-stone.m4b` | Full pipeline; FolderHint primed | ✅ Full pipeline ran | ⚠️ PARTIAL (hint primed not confirmed) |
| 18 | `hp-series/harry-potter-chamber-of-secrets.m4b` | FolderHint applied; Stage 1 SPARQL skipped | ⚠️ Hint applied to batch scan (not live watch) | ⚠️ PARTIAL |
| 19 | `expanse-audio/leviathan-wakes-audio.m4b` | Full pipeline; FolderHint primed | ✅ Full pipeline ran | ⚠️ PARTIAL (hint primed not confirmed) |
| 20 | `expanse-audio/calibans-war-audio.m4b` | FolderHint applied; Stage 1 SPARQL skipped | ⚠️ Hint applied to batch scan (not live watch) | ⚠️ PARTIAL |

### Organized Library Structure

```
C:\temp\tuvima-library\
  Books/
    Dune (Q190159)/
      Epub/  dune.epub
      Audiobook/  dune-audiobook.m4b
    Neuromancer (Q...)/ Epub/ neuromancer.epub
    Foundation (Q...)/ Epub/ foundation.epub
    The Name of the Wind (Q...)/ Epub/ the-name-of-the-wind.epub
    Leviathan Wakes (Q...)/ Epub/ leviathan-wakes.epub
    The Running Man (Q...)/ Epub/ the-running-man.epub
    The Cuckoo's Calling (Q...)/ Epub/ the-cuckoos-calling.epub
    phantom-signal-filename-only (NF000001)/ Epub/  [should be in .orphans/]
    dune-duplicate (Q190159-2)/ Epub/  [should be DuplicateSkipped]
    The Hitchhiker's Guide to the Galaxy (Q...)/ Audiobook/ hitchhikers-guide.m4b
    Wool Omnibus (Q...)/ Audiobook/ wool-omnibus.m4b
    Ender's Game (Q...)/ Audiobook/ enders-game.m4b
    echoes-filename-only (NF000002)/ Audiobook/  [should be in .orphans/]
    The Wasp Factory (Q...)/ Audiobook/ the-wasp-factory.m4b
    Harry Potter and the Philosopher's Stone (Q...)/ Audiobook/
    Harry Potter and the Chamber of Secrets (Q...)/ Audiobook/
    Leviathan Wakes (Q...)-audio/ Audiobook/
    Caliban's War (Q...)/ Audiobook/
```

### Issues Found

#### Issue #1 — MANIFEST.json picked up by Engine (Minor)
**What happened:** The generator writes `MANIFEST.json` into the watch folder root. The Engine picked it up as a candidate media file.
**Root cause:** Output path for MANIFEST.json is `{outputDir}/MANIFEST.json` which is inside the watch directory.
**Fix:** Move MANIFEST.json output to the parent directory: `{outputDir}/../MANIFEST.json` (i.e., `C:\temp\tuvima-watch\MANIFEST.json`).
**File:** `tools/GenerateTestEpubs/Program.cs` — manifest write path.

#### Issue #2 — Duplicate detection creates variant instead of skipping (High)
**What happened:** `dune-duplicate.epub` (byte-identical to `dune.epub`) was organized as a new asset with a `(2)` variant filename instead of triggering `DuplicateSkipped`.
**Root cause:** Hash-based duplicate detection is not active during the initial batch scan (only during live watch events). Batch scan processes all files in sequence without checking the hash store.
**Investigation:** Check `IngestionEngine.ProcessBatchAsync` vs `ProcessCandidateAsync` — the hash lookup likely runs in the candidate path but the batch path may skip it.
**Expected fix:** Hash check must run in both batch and watch paths before any processing proceeds.

#### Issue #3 — No-metadata files not orphaned (High)
**What happened:** `phantom-signal-filename-only.epub` (no OPF fields) and `echoes-filename-only.m4b` (no ID3 tags) were auto-organized instead of being moved to `.orphans/`.
**Root cause:** The scoring engine normalizes weights — when only one claim exists (the filename), it wins with weight 1.0 normalized to confidence 1.0. The orphanage gate `< 0.40` never fires.
**Investigation:** Check `ScoringEngine.NormalizeWeights` — normalization should not apply when the only claims have zero semantic value (empty title, no author, no year). A secondary gate is needed: if the single winning claim came from a filename-only source with confidence ≤ 0.25 before normalization, apply orphanage logic.
**Workaround:** A separate `FilenameOnlyOrphanageCheck` pass after scoring could detect files where `CanonicalTitle == filename_stem && AuthorCanonical == null`.

#### Issue #4 — Narrator Person records not created (High)
**What happened:** M4B files with narrator tags (e.g., `hitchhikers-guide.m4b` — Stephen Fry; `wool-omnibus.m4b` — Amanda Donahoe and Tim Gerard Reynolds) produced no narrator Person records.
**Root cause:** `RecursiveIdentityService.EnrichAsync` creates Person records for `Author` role only. The narrator field extracted by the audio processor is not being passed through to identity enrichment.
**Investigation:** Check `IngestionEngine.ProcessCandidateAsync` → look for where author PersonRef is created. The same pattern needs to apply for `narrator` canonical value → create `PersonRef` with `role = "Narrator"` → pass to `RecursiveIdentityService`.
**Fix scope:** `IngestionEngine` + `RecursiveIdentityService` — add narrator handling branch.

#### Issue #5 — Corrupt EPUB not quarantined (Medium)
**What happened:** `corrupt-epub.epub` (512 random bytes, not a ZIP) was ingested as media type "Other" and organized into the library instead of being flagged `IsCorrupt = true` with a `MediaFailed` activity.
**Root cause:** The EPUB processor likely throws on open, falls through to a generic handler, and the error is caught silently. The `IsCorrupt` path is not wired.
**Investigation:** Check `EpubProcessor.ProcessAsync` exception handling — a `ZipException` or `InvalidDataException` on open should set `ProcessorResult.IsCorrupt = true`. Ingestion pipeline should then route to quarantine instead of organizing.

#### Issue #6 — Ingestion hinting not verifiable in batch scan (Low)
**What happened:** Scenarios 17–20 (HP series and Expanse Audio subdirectories) ran correctly, but there's no log evidence that `FolderHint` was primed after file 17 and consumed by file 18.
**Root cause:** `IngestionHintCache` primes hints during live watch events where file arrival is sequential. During a batch scan, all files are queued simultaneously and the hint may not be available when sibling files are processed.
**Note:** This is a test infrastructure issue — hinting works correctly during live watch. For a definitive test, drop the two HP files into the watch folder with a 5-second gap between them.

### What Worked Well

- ✅ Core ingestion pipeline: all 20 files processed without crashes
- ✅ Cross-format Hub linking (Dune EPUB + M4B → same Hub)
- ✅ Provider cover art fill-in (Neuromancer, Ender's Game, etc.)
- ✅ Series + series_pos metadata (Name of the Wind, Ender's Game)
- ✅ Author name conflict detection (Foundation — Asimov, Isaac)
- ✅ Wikidata Stage 1 QID resolution for well-known titles
- ✅ Wikipedia Stage 2 description enrichment
- ✅ Hero banner generation for all files with cover art
- ✅ Organization template applied correctly (`{Category}/{Title} ({Qid})/{Format}/`)
- ✅ Activity ledger: ~400 entries, full audit trail visible

### Prioritized Next Actions

| Priority | Issue | Effort | Impact |
|----------|-------|--------|--------|
| 1 — High | #2 Duplicate detection in batch scan | Medium | Prevents data duplication |
| 2 — High | #4 Narrator Person records not created | Small | Core feature gap |
| 3 — High | #3 No-metadata orphanage gate | Medium | Confidence gate accuracy |
| 4 — Medium | #5 Corrupt EPUB quarantine path | Small | Data integrity |
| 5 — Low | #1 MANIFEST.json in watch folder | Trivial | Clean test runs |
| 6 — Low | #6 Ingestion hint verification | Trivial | Needs live-watch test |

---

## Run: 2026-03-10

**Test suite:** 10 EPUBs + 10 M4B audiobooks (20 files total)
**Engine version:** current `main`
**Database:** fresh wipe before run

### Summary

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

### Key Scenario Outcomes

**✅ Cross-Format Hub Linking (Dune)** — Both `dune.epub` and `dune-audiobook.m4b` correctly assigned to the same Hub.

**✅ Provider Cover Fill-In** — Neuromancer, Words of Radiance, Ender's Game all received cover.jpg from Apple Books / Audnexus.

**✅ Author Name Conflict** — `foundation.epub` tagged `"Asimov, Isaac"` (reversed). Two Person records created: `Asimov, Isaac` (no headshot) and `Isaac Asimov` (Wikidata headshot). Correct behaviour.

**✅ Wikidata Person Enrichment** — 14/17 persons enriched with headshots.

**✅ Review Queue — Fictional Content** — 2 items: `Echoes of the Void` and `echoes-filename-only`.

**✅ Hero Banners** — 17 hero banners generated for all files with cover art.

**✅ Activity Log** — 220 entries covering full ingestion chain per file.

### Issues Found (2026-03-10)

**Minor — Tolkien name spacing:** Two person folders for `J.R.R. Tolkien` vs `J. R. R. Tolkien` (EPUB tag vs Wikidata canonical label).

**Minor — phantom-signal Unknown/Unknown:** Filename-only EPUB organized to `Unknown/Unknown/Unknown.epub` with a cover — likely a false provider match on empty query.

**Minor — The Last Archive organized despite fictional:** Fictional title/author auto-organized because local confidence from ID3 tags ≥ 0.85. Correct per confidence gate; noteworthy.
