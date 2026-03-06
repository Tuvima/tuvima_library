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
7. **Hub assignment** — The system decides which Hub (story group) this file belongs to, or creates a new one.
7. **Organising** — If scoring confidence is high enough (≥85%) or the user has locked any metadata value, the file is moved to a clean, human-readable folder structure in the Library.
8. **Sidecar writing** — A companion `tanaste.xml` file is written alongside the organised file, preserving all metadata in a human-readable format.
9. **Cover art** — The cover image is extracted and saved as `cover.jpg` next to the file.
10. **Background enrichment** — External sources (Apple Books, Audnexus, Wikidata) are quietly queried for better metadata. Results pop in moments later.
11. **Person enrichment** — Authors and narrators are identified, linked, and enriched with portraits from Wikidata.

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
| IPR-06 | Cover art is never stored in the database — it lives as `cover.jpg` on disk next to the file. | IngestionEngine (filesystem-first rule) |
| IPR-07 | The tanaste.xml sidecar is the portable source of truth — if the database is wiped, the library can be rebuilt from XML. | SidecarWriter + LibraryScanner (Great Inhale) |
| IPR-08 | External metadata enrichment is never in the critical path — a failed network call returns empty results. The file remains in the library with its local metadata. | MetadataHarvestingService (non-blocking queue) |
| IPR-09 | The Watch Folder is monitored in real time — new files are detected within seconds. | FileWatcher (FileSystemWatcher with 64KB buffer) |
| IPR-10 | Rapid-fire OS events for the same file are coalesced — only the final state is processed. | DebounceQueue (2-second settle delay) |
| IPR-11 | Failed file-lock probes (file still in use after ~127 seconds) emit a failed candidate for logging, not a silent drop. | DebounceQueue (IsFailed flag) |
| IPR-12 | On startup, existing files in the Watch Folder are scanned — the system reconciles its state without missing anything. | IngestionEngine (startup differential scan) |
| IPR-13 | Ambiguous media types (MP3→Audiobook/Music/Podcast, MP4→Movie/TV) are resolved by heuristic signals. Confidence ≥0.70 = auto-accept; 0.40–0.70 = review queue; <0.40 = Unknown. | IngestionEngine (Step 6a) |
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
| Sidecar & cover art gate | **PASS** | Writes sidecar XML and cover.jpg whenever the file is in LibraryRoot (path-based check, not confidence re-check). |
| Great Inhale (LibraryScanner) | **WARN** | Cannot restore the full Hub→Work→Edition→Asset chain after a complete database wipe. Only Hub records and existing-asset editions are restored. |
| Background enrichment | **PASS** | Non-blocking queue with 3-way concurrency. |
| Person enrichment | **WARN** | Working, but the `PersonEnriched` SignalR event has an empty person name (known bug — passes `Guid.Empty` to asset lookup). |
| Deleted file handling | **PASS** | LibraryReconciliationService scans for missing files on a configurable interval (default 24h). Orphaned assets are fully cleaned (DB + filesystem). Duplicate check also handles orphans when file is missing. |
| Watcher health monitoring | **FAIL** | Non-overflow FileSystemWatcher errors (e.g., network share disconnect) are swallowed. No recovery or notification mechanism. |
| Standalone worker host | **FAIL** | Missing 6+ dependency registrations from Phase 9. Cannot start independently. Only the API host works. |

---

## PO Summary

The ingestion pipeline is fully operational from file detection through organisation, enrichment, and reconciliation. Files land in the Watch Folder, get fingerprinted, scored, organised, and enriched — all automatically. The sidecar/cover art gate now correctly uses a path-based check. Orphaned duplicates are cleaned automatically. A scheduled reconciliation service detects missing files and cleans up artifacts. The activity log shows rich match cards with cover thumbnails, confidence bars, and reprocess buttons. **Two gaps remain: (1) if the filesystem watcher loses its connection (e.g., network drive goes offline), there's no recovery mechanism, and (2) the standalone worker mode is broken due to missing dependencies — only the full Engine works.**
