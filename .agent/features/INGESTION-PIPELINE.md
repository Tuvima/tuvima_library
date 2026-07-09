# Feature: Ingestion Pipeline

> **Mirrors:** `CLAUDE.md` §3.1 (Watch Folder), §3.13 (Hydration Pipeline), §3.14 (Media Type Disambiguation) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## User Experience

The user drops a file (book, movie, audiobook, comic) into a designated "Watch Folder" on their computer. From that moment, everything is automatic:

1. **Detection** — The system notices the file within seconds.
2. **Waiting** — It pauses briefly to make sure the file is fully copied (not half-written).
3. **Fingerprinting** — It creates a unique barcode (SHA-256 hash) that permanently identifies this file, even if it's renamed or moved later.
4. **Scanning** — The appropriate reader (EPUB, video, audio, comic, or generic) opens the file and extracts all embedded information: title, author, year, cover art, series, etc.
5. **Media Type Disambiguation** — For ambiguous formats (MP3, M4A, MP4), heuristic signals (duration, genre tags, chapter markers, filename patterns, folder context) vote on the most likely media type. High-confidence results are accepted automatically; uncertain ones are sent to the review queue.
6. **Scoring** — The Weighted Voter evaluates all extracted data and determines the most trustworthy value for each field.
7. **Collection assignment** — The system decides which Collection (story group) this file belongs to, or creates a new one.
8. **Stage 1 provider identification** — Enabled configured providers try to identify the item and return artwork, descriptions, ratings, people, and bridge identifiers. Music uses MusicBrainz first for recording/release identity and Apple second for artwork and retail metadata; other lanes use their configured retail/catalogue providers.
9. **Stage 2 Wikidata bridge resolution** — If Stage 1 found a retail match with bridge identifiers, Wikidata can resolve the canonical QID. If Stage 1 fails, the item goes to review and Wikidata is not attempted.
10. **Quick Hydration** — The item becomes visible quickly with core identity, canonical values, and managed artwork.
11. **Organising** — If scoring confidence is high enough (≥85%) or the user has locked any metadata value, the file is moved to a clean, human-readable folder structure in the Library.
12. **Sidecar writing** — A companion `library.xml` file can be written alongside the organised file, preserving portable metadata.
13. **Managed artwork** — Covers, backgrounds, banners, logos, portraits, and other images are stored under `.data/assets/...` and indexed through `entity_assets` or the relevant person/entity table.
14. **Stage 3 universe enrichment** — Background jobs expand people, fictional entities, narrative roots, relationships, additional artwork, lyrics, subtitles, and readiness states.

All of this happens without the user lifting a finger after the initial folder setup.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| IPR-01 | Files below 85% confidence with no user-locked values stay in the Watch Folder — they are not auto-organised. | IngestionEngine (confidence gate) |
| IPR-02 | Duplicate files (same SHA-256 hash) are silently skipped — no duplicate entries in the library. | MediaAssetRepository (INSERT OR IGNORE + UNIQUE constraint) |
| IPR-03 | Corrupt files are quarantined and flagged — they never enter the organised library. | IngestionEngine (ProcessorResult.IsCorrupt check) |
| IPR-04 | File moves use collision-safe renaming — existing files are never overwritten. Suffixes ` (2)`, ` (3)`, etc. are appended. | FileOrganizer (collision handling) |
| IPR-05 | File moves retry with exponential backoff on I/O errors (up to 5 attempts). | FileOrganizer (retry logic) |
| IPR-06 | Managed artwork is indexed in the database and stored under `.data/assets/...`; local sidecar images are optional export mirrors only. | AssetPathService + entity_assets |
| IPR-07 | The library.xml sidecar is the portable source of truth — if the database is wiped, the library can be rebuilt from XML. | SidecarWriter + LibraryScanner (Great Inhale) |
| IPR-08 | External metadata enrichment is never in the critical path — a failed network call returns empty results. The file remains in the library with its local metadata. | MetadataHarvestingService (non-blocking queue) |
| IPR-09 | The Watch Folder is monitored in real time — new files are detected within seconds. | FileWatcher (FileSystemWatcher with 64KB buffer) |
| IPR-10 | Rapid-fire OS events for the same file are coalesced — only the final state is processed. | DebounceQueue (2-second settle delay) |
| IPR-11 | Failed file-lock probes (file still in use after ~127 seconds) emit a failed candidate for logging, not a silent drop. | DebounceQueue (IsFailed flag) |
| IPR-12 | On startup, existing files in the Watch Folder are scanned — the system reconciles its state without missing anything. | IngestionEngine (startup differential scan) |
| IPR-13 | Ambiguous media types (MP3→Audiobook/Music, MP4→Movie/TV) are resolved by heuristic signals. Confidence ≥0.70 = auto-accept; 0.40–0.70 = review queue; <0.40 = Unknown. | IngestionEngine (Step 6a) |
| IPR-14 | Files with unresolved media type ambiguity are blocked from auto-organisation regardless of overall confidence. | IngestionEngine (auto-organize gate) |
| IPR-15 | Post-hydration auto-resolve: if Stage 1 providers return ≥3 claims, pending AmbiguousMediaType review items are auto-resolved. | HydrationPipelineService |
| IPR-16 | Users can reclassify media type at any time via `POST /metadata/{entityId}/reclassify`, which creates a user-locked claim and re-triggers hydration. | MetadataEndpoints |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| File detection (FileWatcher) | **PASS** | Correct, with 64KB buffer to reduce missed events. |
| Debounce & settle (DebounceQueue) | **PASS** | Sophisticated concurrency model with proper cancellation. |
| Hashing (AssetHasher) | **PASS** | Performant, zero-allocation streaming SHA-256. |
| Media processing (Processors) | **PASS** | EPUB, Video, Audio, Comic, and Generic processors all working. |
| Media type disambiguation | **PASS** | AudioProcessor and VideoProcessor emit heuristic candidates. Step 6a resolves with confidence thresholds. Review queue integration working. |
| Scoring integration | **PASS** | Per-field scoring with conflict detection. |
| Auto-organisation | **PASS** | Confidence gate, template-based paths, collision-safe moves. |
| Sidecar & managed artwork gate | **PASS** | Writes portable sidecar metadata when enabled and stores managed artwork through `.data/assets` plus database references. |
| Great Inhale (LibraryScanner) | **WARN** | Cannot restore the full Collection→Work→Edition→Asset chain after a complete database wipe. Only Collection records and existing-asset editions are restored. |
| Background enrichment | **PASS** | Non-blocking queue with 3-way concurrency. |
| Person enrichment | **WARN** | Working, but the `PersonEnriched` SignalR event has an empty person name (known bug — passes `Guid.Empty` to asset lookup). |
| Deleted file handling | **PASS** | LibraryReconciliationService scans for missing files on a configurable interval (default 24h). Orphaned assets are fully cleaned (DB + filesystem). Duplicate check also handles orphans when file is missing. |
| Watcher health monitoring | **FAIL** | Non-overflow FileSystemWatcher errors (e.g., network share disconnect) are swallowed. No recovery or notification mechanism. |
| Standalone worker host | **FAIL** | Missing several dependency registrations. Cannot start independently. Only the API host works. |

---

## PO Summary

The ingestion pipeline is fully operational from file detection through organisation, enrichment, and reconciliation. Files land in configured library folders, get fingerprinted, scored, identified through retail providers, optionally bridged to Wikidata, organised, and enriched automatically. Artwork and headshots now use one managed `.data/assets` store with database references, while sidecars are portable/export mirrors. A scheduled reconciliation service detects missing files and cleans up managed assets. **Two gaps remain: (1) if the filesystem watcher loses its connection (e.g., network drive goes offline), there's no recovery mechanism, and (2) the standalone worker mode is broken due to missing dependencies — only the full Engine works.**

