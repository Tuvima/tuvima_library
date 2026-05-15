---
title: "Ingestion Pipeline"
summary: "Deep technical documentation for file watching, fingerprinting, staging, browse surfacing, promotion, and organization."
audience: "developer"
category: "architecture"
product_area: "ingestion"
tags:
  - "ingestion"
  - "pipeline"
  - "watchers"
---

# Ingestion Pipeline

This document describes how Tuvima Library discovers, processes, organises, and stages media files â€” from the moment a file appears in a watched folder to the moment it is promoted into the organised library.

---

## Library Folders

The Engine is configured with one or more **Library Folders**, each declaring:

| Field | Description |
|---|---|
| `category` | The content category: Books, TV, Movies, Music, Comics |
| `media_types` | The expected media types within this folder (e.g. `["Epub", "Audiobook"]`) |
| `source_path` | The folder the Engine monitors or imports from |
| `source_paths` | Optional multi-path form for multiple folders in one logical library |
| `library_root` | The destination root where organised files are placed |
| `intake_mode` | `watch` (ongoing monitoring) or `import` (one-time scan) |
| `import_action` | For import mode only: `move` or `copy` |
| `include_subdirectories` | Whether to scan nested folders within the source path |

Configuration lives in `config/libraries.json`. When this file is absent, the Engine falls back to a single default library folder derived from the legacy `WatchDirectory` and `LibraryRoot` values in `core.json`, treating all media types as eligible.

```json
{
  "libraries": [
    {
      "category": "Movies",
      "media_types": ["Movies"],
      "source_path": "/media/downloads/movies",
      "library_root": "/media/library",
      "intake_mode": "watch"
    },
    {
      "category": "Books",
      "media_types": ["Epub", "Audiobook"],
      "source_path": "/media/existing-books",
      "library_root": "/media/library",
      "intake_mode": "import",
      "import_action": "copy"
    }
  ]
}
```

The `category` tells the Engine where to organise files on disk. The `media_types` tell it how to process them â€” which processor to use, which metadata providers to query, and what confidence prior to apply during identification. A library folder designated for Movies gives any MP4 it finds an 0.80 prior confidence for that media type, skipping much of the heuristic disambiguation that would otherwise be needed.

---

## Ingestion Snapshot

The Dashboard's Ingestion page uses `GET /ingestion/operations`, backed by `IIngestionOperationsStatusService`, as the application-facing ingestion status model. The service aggregates existing persisted state rather than keeping a separate demo task model:

- `ILibraryItemRepository` for total, registered, provisional, and review lifecycle counts
- `IIngestionBatchRepository` for active and recent batches
- `identity_jobs` and `ingestion_log` for pipeline stage counts
- `review_queue` for actionable review reason groups
- `config/libraries.json` for Watch, Listen, and Read source folders, including multi-path libraries
- `provider_health` and provider config files for provider status
- runtime `IngestionOptions` and `core.json` for organization rule summaries

Live Dashboard updates come from existing SignalR Intercom events (`BatchProgress` and `IngestionProgress`) plus bounded polling of the snapshot endpoint. Polling is faster while jobs are active and slower while idle.

Music remains a conservative organization lane. The status surface emphasizes tag/fingerprint-first handling and preserving album folders instead of implying aggressive rename/move behavior.

---

## Intake Modes

### Watch Mode

The Engine monitors the source folder for new files. When a file appears, it is processed through the full ingestion pipeline and then **moved** into the organised library structure. Watch mode is designed as a permanent inbox â€” files dropped in are consumed and relocated automatically.

The `.staging/` directory within the library root is excluded from Watch Folder monitoring to prevent re-ingestion loops.

### Import Mode

Import mode performs a one-time scan of an existing collection. It follows the same processing steps as Watch mode, then either **moves** or **copies** the file depending on `import_action`. Copy mode leaves originals untouched. After an import completes, the folder can optionally be switched to Watch mode for ongoing monitoring.

---

## Processing Steps

Every file â€” regardless of intake mode â€” goes through the same sequential processing pipeline:

### 1. Settle

The Engine waits briefly after detecting a file to confirm it has finished being written to disk. This prevents reading partially-copied files from network shares or slow storage.

### 2. Lock Check

The Engine verifies that no other process has an exclusive lock on the file before attempting to read it.

### 3. Fingerprint

A SHA-256 content hash is computed from the file's bytes. This hash is the file's permanent identity throughout its lifetime in the library. It survives renaming, moving, and metadata edits. If a file is ingested a second time (e.g. after a database rebuild), the hash allows the Engine to recognise it immediately.

### 4. Scan

The appropriate processor for the file's format opens the file and extracts all embedded metadata:

- **EpubProcessor** â€” reads OPF package metadata: title, author, publisher, year, series, language, cover image
- **AudioProcessor** â€” reads ID3v2 (MP3), iTunes atoms (M4B/M4A), Vorbis comments (FLAC/OGG): title, artist, album, track number, chapter markers, genre, ASIN, embedded artwork
- **VideoProcessor** â€” reads container metadata (MP4, MKV): title, resolution, duration, codec, embedded subtitles, chapter list
- **ComicProcessor** â€” reads ComicInfo.xml from CBZ/CBR archives: title, series, issue number, writer, artist, publisher

The processor also emits media type candidates when the format is ambiguous. See the Media Type Disambiguation section below.

### 5. Identify

The Priority Cascade Engine scores all available claims for this file â€” from embedded metadata, filename parsing, and any prior library folder hints â€” and assigns the file to an existing Collection or creates a new one. This is where the title, author, series, and other canonical values are resolved.

If multiple files from the same source folder have already been processed (e.g. a TV season with 22 episodes), the Engine uses **Ingestion Hinting**: the first file's resolved metadata is cached as a folder-level prior. Subsequent siblings receive the collection ID, QID, and bridge IDs from that prior as high-confidence claims, dramatically reducing the number of Wikidata lookups needed.

**Work deduplication fallback:** `MediaEntityChainFactory` checks whether a Work already exists before creating a new one (matching by title + author + media type via `IWorkRepository`). When `canonical_values` has not yet been populated for an in-flight asset, the deduplication check falls back to a raw `metadata_claims` lookup so that duplicate files arriving close together in time do not bypass the check. Duplicate files create a new Edition under the existing Work rather than creating a duplicate Work.

### 6. Move to Staging

The file is moved from its source location into `{LibraryRoot}/.staging/`, where it waits for hydration and promotion. Cover art is extracted and written alongside the file at this stage â€” the processor's cover image bytes are only available during the Scan step and must be persisted immediately.

Cover art for items that have not yet received a Wikidata QID is written to `.data/images/_pending/{GUID12}/` (previously named `_provisional/`). When a QID is assigned, `ImagePathService.PromoteToQid()` renames the directory to `.data/images/{QID}/`.

---

## Staging-First Flow

All ingested files land in `.staging/` before reaching the organised library. The library invariant is that every file within the library root (outside `.staging/`) has been hydrated, has reached a settled identity outcome, and has a settled artwork outcome. That may mean a resolved QID with art present, or a precision-preserving QID-missing result with artwork explicitly confirmed missing.

```
Watch Folder  â”€â”€(detect + process)â”€â”€>  .staging/  â”€â”€(hydration + promote)â”€â”€>  Library
                                           â”‚
                                      stays here if:
                                      - low confidence
                                      - unidentifiable
                                      - needs review
                                      - media type ambiguous
```

### Staging Subcategories

Files are routed to one of four subcategories based on their overall confidence score after the Identify step:

| Subcategory | Condition | Behaviour |
|---|---|---|
| `.staging/pending/` | Confidence â‰¥ 0.85, or any user-locked claim | AutoOrganizeService promotes after hydration |
| `.staging/low-confidence/` | Confidence 0.40â€”0.85, no user locks | Awaits hydration improvement or manual review |
| `.staging/unidentifiable/` | Confidence < 0.40, no user locks | Requires user to provide a title or match |
| `.staging/other/` | Resolves to “Other” category | Requires media type classification |

**Staging lifecycle logging:** The Engine logs staging progress at `Information` level at four points: (1) when an asset is moved into staging, (2) when a review queue item is created, (3) when a gap is detected in expected review creation (e.g. a confidence score that should have triggered a review but did not), and (4) when an asset is promoted out of staging into the organised library. These log entries allow the staging pipeline to be audited from the activity log.

**browse readiness gate:** main browse surfaces visibility is no longer a simple "in staging or not" decision. The shared library item projection computes browse visibility, pipeline step, artwork state, and readiness from identity jobs, review state, and canonical artwork flags. An item is visible in the main browse surfaces only after it has a non-placeholder title, a resolved media type, and settled artwork (`present`, or `missing` after explicit settlement). Review-only or still-hidden items remain available in Activity, Review, and the Review Queue.

### AutoOrganize Gate

`AutoOrganizeService` promotes a staged file to the organised library when:

```
overallConfidence >= 0.85  OR  any claim has IsUserLocked = true
```

This threshold (`AutoLinkThreshold = 0.85`) is defined once in `ScoringConfiguration` and reused by both the staging router and the promotion gate. It governs filesystem promotion, not main browse surfaces visibility.

### Hero Banner

Hero banner generation (blur + vignette + grain, via SkiaSharp) runs during promotion by `AutoOrganizeService`. It is a post-hydration step, not an ingestion step, because it benefits from the enriched metadata and high-resolution cover art that hydration provides.

### Manual Reclamation

Staged files retain their fingerprint and metadata in the database. A user can manually resolve a staged file from the Dashboard â€” by dragging it to a Collection or providing a user-locked title â€” triggering promotion to the organised library structure. The `.staging/` directory is excluded from Watch Folder monitoring to prevent re-ingestion loops.

On startup, if `{LibraryRoot}/.orphans/` exists and `.staging/` does not, the Engine renames the directory and updates all database file paths automatically.

---

## File Organisation

### Data Authority

The database is the authoritative data store for all metadata, relationships, and canonical values. User metadata edits are additionally written back into the file's embedded metadata via `IMetadataTagger` (EPUB OPF, ID3 tags, M4B atoms), ensuring portability â€” the file carries its own metadata independently of the database.

Wikidata properties are re-fetchable via the batch Reconciliation API as a recovery fallback. Cover art is never stored in the database; `cover.jpg` lives alongside the file on disk and is always read from there.

Recovery scenarios:
- **Standard:** Scheduled SQLite backups (by domain: universe, people, library) as primary recovery
- **Wikidata data loss:** Re-fetch via batch Reconciliation API
- **Full wipe:** Re-ingest from library root; file embedded metadata and batch Wikidata reconciliation rebuild the library

### Folder Structure Templates

The default organisation template is:

```
{LibraryRoot}/{Category}/{Title} - {QID}/{Title}{Ext}
```

Per-media-type overrides:

| Media Type | Template |
|---|---|
| Books | `{Category}/{Title} - {QID}/Epub/{Title}{Ext}` |
| Audiobooks | `{Category}/{Title} - {QID}/Audiobook/{Title}{Ext}` |
| TV | `{Category}/{Title} - {QID}/S{Season:00}E{Episode:00} - {EpisodeTitle}{Ext}` |
| Music | `{Category}/{Artist}/{Album} - {QID}/{TrackNumber:00} - {Title}{Ext}` |
| Movies | `{Category}/{Title} - {QID}/{Title}{Ext}` |
| Comics | `{Category}/{Title} - {QID}/{Title}{Ext}` |

Books and Audiobooks share the same title folder under the `Books` category, distinguished by their format subfolder. This means an ebook and its audiobook counterpart live at:

```
{LibraryRoot}/Books/Dune - Q190159/Epub/Dune.epub
{LibraryRoot}/Books/Dune - Q190159/Audiobook/Dune.m4b
{LibraryRoot}/Books/Dune - Q190159/cover.jpg
```

Cover art lives at the title folder level, not inside the format subfolder, so both formats share the same cover image.

### Category Mapping

The `{Category}` path segment is derived from the file's media type:

| Media Types | Category folder |
|---|---|
| Epub, Audiobook | `Books` |
| TV | `TV` |
| Movies | `Movies` |
| Music | `Music` |
| Comics | `Comics` |
| Unknown | `Other` |

### Migration Note

Existing libraries organised under older folder patterns continue to work. On the next hydration pass or a manual "Re-organise Library" action, files are moved to the current structure automatically.

---

## Media Type Disambiguation

Some file formats map to multiple possible media types. Magic bytes identify the container format but not the content type. An MP3 file could be an audiobook chapter or a music track. An MP4 could be a feature film or a TV episode. The disambiguation system resolves this using heuristic signals treated as voted claims.

### Signal Sources

Media type is resolved using the same Weighted Voter architecture as all other metadata fields. Multiple signals emit competing candidates with associated confidence values:

| Signal source | Confidence range | Examples |
|---|---|---|
| Magic bytes (unambiguous formats) | 0.95â€“1.0 | EPUB â†’ Books, CBZ â†’ Comics, M4B â†’ Audiobooks |
| Processor heuristics | 0.30â€“0.80 | File duration, bitrate, chapter markers, genre tag |
| Filename and path patterns | 0.25â€“0.65 | `S01E01` in filename â†’ TV, `audiobooks` in path â†’ Audiobooks |
| User lock | 1.0 | Manual override â€” always wins |

### Confidence Thresholds

| Threshold | Behaviour |
|---|---|
| â‰¥ 0.70 (`auto_assign_threshold`) | Accept automatically, proceed normally |
| 0.40â€“0.70 (`review_threshold`) | Accept provisionally, create `AmbiguousMediaType` review queue entry |
| < 0.40 | Assign `MediaType.Unknown`, block auto-organize, create review entry |

### AudioProcessor Disambiguation

The `AudioProcessor` runs at priority 95 (above VideoProcessor at 90) and handles audio format detection.

Unambiguous assignments:
- `.m4b` â†’ Audiobooks (0.98 confidence)
- `.flac`, `.ogg`, `.wav` â†’ Music (0.95 confidence)

For ambiguous formats (`.mp3`, `.m4a`), the processor emits weighted candidates using additive heuristic signals:

- **Duration:** Very long files (> 60 min) bias toward Audiobooks; short files (< 5 min) bias toward Music
- **Chapter markers:** Presence of chapter metadata strongly indicates Audiobooks
- **Genre tags:** Genre values matching known audiobook indicators (e.g. "Spoken Word", "Audiobook") or music genres (e.g. "Rock", "Jazz") push the score in respective directions
- **Album and track metadata:** Presence of track numbers and album names strongly indicates Music
- **Bitrate:** Low bitrate speech-range audio biases toward Audiobooks
- **Path keywords:** Parent folder names like `audiobooks`, `music` in the source path
- **File size:** Very large single files bias toward Audiobooks

Each type (Audiobook, Music) starts at a base score of 0.25. Signals are additive and the final scores are normalized to `[0.0, 1.0]` before comparison against the confidence thresholds.

### VideoProcessor Disambiguation

The `VideoProcessor` resolves ambiguity between Movies and TV:

- **TV filename patterns:** `SxxExx` or `NxNN` patterns in the filename strongly indicate TV
- **Duration:** Short files bias toward TV episodes; feature-length files bias toward Movies
- **Path keywords:** Parent folder structures containing season or series names
- **Sibling file count:** Many similarly-named files in the same folder indicate a TV series

Base score per type (Movie, TV) is 0.35. Signals are additive and normalized to `[0.20, 0.90]`.

### Configuration

All disambiguation thresholds and heuristic parameters â€” duration bands, bitrate thresholds, path keywords, genre tag lists, TV filename patterns â€” are configurable in `config/disambiguation.json`. No code changes are needed to tune the system's behaviour.

### Review Resolution

When a file lands in the review queue with an `AmbiguousMediaType` trigger, the user selects the correct media type from candidate cards in the Needs Review tab. The selected type is saved as a user-locked claim at confidence 1.0, the review item is resolved, and the hydration pipeline re-runs for that entity.

After Stage 1 hydration (retail providers), if 3 or more claims are returned, the pipeline can auto-resolve pending `AmbiguousMediaType` review items â€” the provider results provide enough signal to confirm the media type without user input.

---

## Writeback & Auto Re-tag Sweep

Once a file is identified and enriched, `WriteBackService` embeds the canonical metadata back into the file itself so external players, re-ingestion, and library rebuilds see it without consulting the database. The per-media-type field list lives in `config/writeback-fields.json` — the single source of truth shared by the taggers and the media detail editor.

### Per-media-type writeback hash

Every media asset carries a `writeback_fields_hash` column (migration **M-084**) that combines:

1. The SHA-256 of the JSON slice for the asset's media type in `writeback-fields.json`.
2. The version constant of the specific tagger that wrote the file (`VideoMetadataTagger.TaggerVersion`, `AudioMetadataTagger.TaggerVersion`, `EpubMetadataTagger.TaggerVersion`, `ComicMetadataTagger.TaggerVersion`).

A file is considered stale when its stored hash differs from the currently-computed hash. Bumping a tagger version or editing the field list for that media type invalidates the hash for every matching file.

### Pending diff + Apply flow

`WritebackConfigState` (singleton) watches `writeback-fields.json` via `IConfigurationLoader`. When the file changes, the state computes a **pending diff** (added and removed fields per media type) and surfaces it without running anything. The Auto Re-tag Sweep card on the Maintenance settings tab shows the diff and two buttons:

- **Apply** — commits the pending diff to `CurrentHashes` and signals the worker to start a sweep.
- **Run Now** — re-runs the sweep against the current hashes without applying a new diff (useful if a tagger version was bumped).

No files are touched until the user clicks Apply or Run Now.

### RetagSweepWorker

A `BackgroundService` that wakes on either a cron schedule (`config/maintenance.json` → `schedules.retag_sweep`, default `0 3 * * *`) or the `PendingApplied` signal from `WritebackConfigState`. Each pass:

1. Calls `IMediaAssetRepository.GetStaleForRetagAsync` to find identified assets whose `writeback_fields_hash` differs from the current hash (or is NULL).
2. Processes in batches, calling `WriteBackService.WriteMetadataAsync(assetId, "config_change")` for each asset. On success, the service stamps the new hash on the row.
3. Classifies failures via `RetagFailureClassifier`:
   - **Locked** / **IoFailed** → `ScheduleRetagRetryAsync` with the next off-hours window start. The sweep picks these up on the next run.
   - **Corrupt** / **Unknown** (after retries exhausted) → inserts a `ReviewQueueEntry` with trigger `WritebackFailed`, routing the file to the Review Queue.
4. Broadcasts live progress via SignalR (`RetagSweepProgress` and `RetagSweepCompleted` events) so the Maintenance tab shows a processed / succeeded / transient / terminal counter during a sweep.

### Endpoints

| Route | Role | Purpose |
|---|---|---|
| `GET /maintenance/retag-sweep/state` | Admin or Curator | Returns pending diff + current hashes |
| `POST /maintenance/retag-sweep/apply` | Admin | Commits pending diff, signals worker |
| `POST /maintenance/retag-sweep/run-now` | Admin | Signals worker without applying a diff |
| `POST /maintenance/retag-sweep/retry/{assetId}` | Admin | Manual retry for a specific failed asset |

### Review Queue integration

the current media surfaces Review Queue surfaces `WritebackFailed` review items alongside other review triggers. The message reads "Re-tag failed — file may be locked or corrupt"; resolving the item either manually (fix the file, click retry) or via a successful next-sweep-pass clears the review row.

---

## Supported Library Types

| Library Type | Formats | Notes |
|---|---|---|
| **Books** | EPUB, PDF | Combined with Audiobooks under the `Books` category |
| **Audiobooks** | M4B, MP3, M4A | Combined with Books under the `Books` category |
| **TV** | MP4, MKV, AVI, WebM | Season/episode folder structure |
| **Movies** | MP4, MKV, AVI, WebM | Single-work, flat folder structure |
| **Music** | MP3, FLAC, OGG, M4A, WAV | Album = Collection, Track = Work |
| **Comics** | CBZ, CBR | Sequential art; ComicInfo.xml metadata |

**Future library types planned but not yet implemented:**

- **Other** â€” YouTube videos, lectures, personal recordings, and any media that does not fit the primary types. Files would be stored and user-provided metadata accepted, but automated enrichment would be limited.
- **Photos** â€” Photo collections with EXIF/XMP extraction, GPS geolocation, face detection, event-based organisation, and timeline views. The scope is large enough that it may become a separate product built on the same base Engine infrastructure.

## Related

- [How File Ingestion Works](../explanation/how-ingestion-works.md)
- [Supported Media Types and Formats](../reference/media-types.md)
- [How to Write a New File Format Processor](../guides/writing-a-processor.md)

## Series Manifest Hydration

After Stage 2 resolves a Wikidata QID and full property claims have been persisted, `WikidataBridgeWorker` asks `WikidataSeriesManifestHydrationService` whether the item belongs to a canonical series. The service only uses QID-backed relationship facts such as P179/`series_qid`, never fuzzy title matching.

For books, audiobooks, comics, and TV, a canonical series QID triggers a Tuvima.Wikidata manifest fetch. Tuvima stores every named item in `series_manifest_items`, including missing works the user does not own. Later imports from the same series first link against the cached named manifest, so adding another Dune ebook or audiobook usually does not require downloading the whole series again while the cache is fresh.

