# Skill: Ingestion Pipeline Operations

> **Mirrors:** `CLAUDE.md` §3.1 (Watch Folder), §3.7 (Library Organization) — keep both in sync per `.agent/SYNC-MAP.md`

> Last updated: 2026-03-01

---

## Purpose

This skill covers the full file ingestion lifecycle — from Watch Folder detection through organisation and enrichment.

---

## Key files

| File | Role |
|------|------|
| `src/MediaEngine.Ingestion/FileWatcher.cs` | OS-level file detection (FileSystemWatcher wrapper) |
| `src/MediaEngine.Ingestion/DebounceQueue.cs` | Event coalescing, settle timer, file-lock probing |
| `src/MediaEngine.Ingestion/IngestionEngine.cs` | 12-step pipeline orchestrator (BackgroundService) |
| `src/MediaEngine.Ingestion/AssetHasher.cs` | SHA-256 streaming fingerprinting |
| `src/MediaEngine.Ingestion/BackgroundWorker.cs` | Bounded-concurrency task queue |
| `src/MediaEngine.Ingestion/FileOrganizer.cs` | Template-based path resolution + collision-safe moves |
| `src/MediaEngine.Ingestion/SidecarWriter.cs` | library.xml read/write (Collection + Edition schemas) |
| `src/MediaEngine.Ingestion/LibraryScanner.cs` | Great Inhale — rebuild DB from sidecar XML |
| `src/MediaEngine.Ingestion/EpubMetadataTagger.cs` | Write-back metadata into EPUB files |
| `src/MediaEngine.Providers/Services/MetadataHarvestingService.cs` | Background external metadata enrichment queue |
| `src/MediaEngine.Providers/Services/RecursiveIdentityService.cs` | Author/narrator person creation + linking |
| `src/MediaEngine.Storage/MediaEntityChainFactory.cs` | Collection→Work→Edition chain creation |
| `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs` | Dry-run scan + Great Inhale API surface |

---

## Pipeline steps (in order)

```
1. FileWatcher detects OS event → FileEvent
2. DebounceQueue coalesces events per path, waits settle delay, probes file lock
3. IngestionEngine dequeues IngestionCandidate
4. Skip if IsFailed or file is deleted
5. AssetHasher computes SHA-256 fingerprint
6. MediaAssetRepository.FindByHashAsync → duplicate check
7. ProcessorRouter.ProcessAsync → extract metadata claims + cover art
8. Skip if ProcessorResult.IsCorrupt
9. Convert ExtractedClaims to MetadataClaims → persist
10. ScoringEngine.ScoreEntityAsync → persist CanonicalValues
11. MediaEntityChainFactory → create Collection→Work→Edition
12. MediaAssetRepository.InsertAsync (INSERT OR IGNORE)
13. If confidence ≥ 0.85 or user-locked: FileOrganizer.ExecuteMoveAsync + SidecarWriter + cover.jpg
14. If WriteBack enabled: EpubMetadataTagger.WriteTagsAsync
15. MetadataHarvestingService.EnqueueAsync → background enrichment
16. RecursiveIdentityService.ProcessAsync → person creation + linking
```

---

## How to add a new file processor

1. Create a class implementing `IMediaProcessor` in `src/MediaEngine.Processors/Processors/`.
2. Set `SupportedType` to the matching `MediaType` enum value.
3. Set `Priority` (higher = tried first; existing: EPUB=100, Video=90, Comic=85).
4. Implement `CanProcess(byte[])` — inspect magic bytes to identify the format.
5. Implement `ProcessAsync()` — return `ProcessorResult` with `ExtractedClaim[]` and optional cover image.
6. Register in `Program.cs` DI container.
7. The `MediaProcessorRouter` will automatically include it in the dispatch chain.

---

## How to add a new metadata tagger (write-back)

1. Create a class implementing `IMetadataTagger` in `src/MediaEngine.Ingestion/`.
2. Implement `CanHandle(MediaType)` — return true for supported types.
3. Implement `WriteTagsAsync()` and `WriteCoverArtAsync()`.
4. Register as `IMetadataTagger` in `Program.cs` DI container.
5. The IngestionEngine iterates all registered taggers in step 14.

---

## Configuration defaults

| Setting | Default | Source |
|---------|---------|--------|
| Settle delay | 2 seconds | DebounceOptions |
| File-lock probe interval | 500ms (exponential backoff) | DebounceOptions |
| Max probe attempts | 8 (~127s total window) | DebounceOptions |
| Queue capacity | 512 (debounce) / 1000 (worker) / 500 (harvest) | Various |
| Auto-organise threshold | 0.85 (85% confidence) | ScoringConfiguration |
| Organisation template | `{Category}/{CollectionName} ({Year})/{Format}/{CollectionName} ({Edition}){Ext}` | IngestionOptions |

---

## Known gaps

1. **Deleted files are not cleaned up** — `HandleDeletedAsync` only logs. No orphan reconciler.
2. **FileWatcher error recovery** — non-overflow errors (network disconnect) are swallowed silently.
3. **Standalone worker host is broken** — missing 6+ Phase 9 DI registrations.
4. **PersonEnriched event has empty name** — known bug in MetadataHarvestingService.
5. **Work+Edition proliferation** — new chain created per asset, even for same work under same Collection.
6. **Great Inhale cannot restore from complete wipe** — requires assets to already exist in DB.

