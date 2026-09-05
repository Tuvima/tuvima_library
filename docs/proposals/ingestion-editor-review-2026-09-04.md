# Ingestion, identity, and editor review

Review date: September 4, 2026. Baseline: `9664a552`.

Status: implementation in progress. The critical and first high-priority packages described below were implemented in the same change; the remaining medium and low packages stay ordered in section 8.

Implemented September 4, 2026:

- Startup reconciliation preserves catalog records when a configured source root is unavailable.
- Automatic Wikidata matching resolves TV identity at show scope and music identity at album scope; child identifiers remain context for provider metadata, playback, lyrics, and subtitles. Detail source links identify these explicitly as show/album rollups.
- A file changed at the same path is reprocessed against the existing asset ID, including changes made while the Engine was stopped. Current local observations are replaced, user/provider observations and progress remain intact, and identity refresh is queued.
- Lyrics and subtitle refresh now returns typed outcomes, rejects invalid kinds, uses stable variant IDs and atomic managed-file publication, and serves exact variants by track ID.
- The shared editor File tab lists managed lyrics/subtitles and can fetch them without a full ingestion run.
- Optional subtitle sidecar export now obeys per-source metadata writeback policy and does not overwrite an unmanaged sidecar.
- Movie, TV, and music-album Fanart refresh can use a direct provider bridge ID without requiring a Wikidata QID.

Still planned: the durable shared command envelope across every refresh type, selective field refresh, provider artwork parity for books/comics/audiobooks, richer subtitle candidate semantics, targeted schedule reads, mobile operation drill-down, and consolidated bounded image publication.

The recommendation is to strengthen the existing pipeline, resolve Wikidata identity at the TV show and music album level, and make repairs available through small, explicit editor actions. Source availability and truthful operation results come before adding providers or deeper enrichment.

## 1. Scope and evidence

Reviewed the active Engine dependency registration, intake and duplicate paths, identity workers and bridge configuration, claim routing, enrichment scheduling, artwork and text-track services, editor APIs, shared editor, ingestion screens, and playback integration. Compared product workflows with current official Plex, Jellyfin, calibre, Audiobookshelf, Kavita, TMDB, and MusicBrainz documentation.

Validation performed:

- `dotnet restore MediaEngine.slnx` completed successfully.
- `dotnet build MediaEngine.slnx --no-restore` completed with zero warnings and zero errors.
- The full solution test run passed: 3,197 tests passed and 37 credential/network integration tests were skipped.
- A live browser check loaded Home and a music-album detail surface from the local Engine and Dashboard. The editor itself requires administrator elevation; credential entry and Windows Hello were deliberately left to the user, so the new File-tab panel was compile/test validated but not visually inspected inside an authenticated modal.
- No provider credential check or throughput benchmark was performed. Passing tests do not establish production reliability or provider coverage.

Some ingestion tests assert source-code structure. They are useful architectural checks but do not replace interruption, network outage, file mutation, or rendered-UI tests.

## 2. Direct answers: TV and music Wikidata scope

| Area | Implemented Wikidata behavior | Child-level behavior retained |
| --- | --- | --- |
| TV episode | Automatic matching always requests `BridgeMediaKind.TvSeries`, uses the canonical show name, and accepts only show-level target IDs. The stored method is `tv_show_rollup`. | Provider episode IDs and show/season/episode coordinates remain available for episode metadata, stills, subtitles, and playback. |
| Music track | Automatic matching always uses the `MusicAlbum` scope and `BridgeMediaKind.MusicAlbum`, searches with the album title, and accepts release/release-group/collection IDs. The stored method is `music_album_rollup`. | Exact release, disc, track, recording, ISRC, and Apple track identifiers remain available for track lists, lyrics, and playback. |
| Music album | The same album/release-group path is used whether matching begins from an album or one of its owned tracks. The QID and its scope are routed to the parent album work. | Track identity remains local/provider-backed and tracks do not gain standalone Wikidata or detail surfaces. |

Evidence:

- `src/MediaEngine.Providers/Workers/Internals/WikidataBridgeWorker.JobResolution.cs`: request scope, show/album hints, target-ID filtering, rollup method, and QID scope.
- `src/MediaEngine.Providers/Adapters/Internals/ReconciliationAdapter.FictionalAndEditions.cs`: TV and music conversion to `TvSeries` and `MusicAlbum` bridge kinds.
- `config/providers/wikidata_reconciliation.json`: show-level TV targets and album/release-group music targets, with child identifiers excluded from automatic Wikidata matching.
- `src/MediaEngine.Domain/Constants/ClaimScopeCatalog.cs`: TV and music QID, scope, and resolution method route to the parent work.
- `tests/MediaEngine.Providers.Tests/WorkerPipelineTests.cs` and `tests/MediaEngine.Domain.Tests/ClaimScopeRollupTests.cs`: rollup request and claim-routing coverage.

The previous request/configuration/routing inconsistency is removed. Results still depend on available parent identifiers and provider responses, but the automatic path no longer attempts episode or track Wikidata matching. Rollup identity does not erase child identity: the QID is stored on the show/album owner, while detail source links say “TV show on Wikidata” or “Album on Wikidata.”

Music album identity and exact release identity must remain distinct. Standard and deluxe releases can share a release group while having different track lists; MusicBrainz documents that distinction explicitly. Group lookup work at the album level while preserving release-specific ownership and sequence data. [MusicBrainz releases](https://musicbrainz.org/doc/Release)

## 3. What is worth preserving

| Existing strength | Why it matters | Current visibility |
| --- | --- | --- |
| File settling, lock retry, hash cache, hash locking, and duplicate resolution | Avoid processing incomplete files and repeatedly hashing unchanged content; reduce duplicate work under concurrent intake. | Ingestion progress, operation records, and logs; detailed explanations are mostly operational. |
| Watcher error recovery and polling | Provides another opportunity to find events missed by the filesystem watcher. | Processing details and logs. |
| Durable identity jobs and media operations | Provides a foundation for restart recovery, retries, correlation, and an operation history. | Ingestion dashboard, activity, editor History. |
| Retail/provider identity before optional Wikidata enrichment | Core media identity can remain useful even when Wikidata cannot resolve it. | Matching, provenance, and retained retail metadata. |
| No QID does not automatically create Review | A confident retail match can remain usable; missing graph enrichment is not necessarily a human problem. | Review Queue excludes many harmless no-match outcomes. |
| Parent/self claim routing and typed work hierarchy | Supports show/episode and album/track distinctions already reflected in browse and detail UI. | Show and album detail, owned episode lists, track tables, editor scope navigation. |
| Managed artwork storage, renditions, source URL coordination, and content deduplication | Keeps browsing local and reduces redundant image downloads. | Artwork editor, detail hero, cards. |
| User-locked metadata and uploaded artwork support | Gives user corrections a stable place beside provider facts. | Details overrides, Artwork choices, History. |
| Shared modal editor for normal, Review, and batch work | Users can fix the item in context without leaving its detail page. | Existing shared editor and launcher. |
| Source mutation policy in organization and metadata writeback | Allows coexistence with media trees managed by other applications. | Library/source settings; operation-level feedback needs improvement. |
| Local subtitle/lyrics import, provider adapters, normalization, and playback consumption | Most foundational pieces exist; this is primarily an integration and reliability task. | Captions and lyrics in players; management controls are incomplete. |

Representative code: `IngestionEngine.Pipeline.cs`, `IngestionEngine.Watching.cs`, `IngestionEngine.FileMaintenance.cs`, `SourceMutationPolicyGate.cs`, `FileSourceMutationPolicyFactory.cs`, `WriteBackService.cs`, `ImageDownloadCoordinator.cs`, `CoverArtWorker.cs`, `QuickHydrationWorker.cs`, and `WikidataBridgeWorker.Finalization.cs`.

## 4. Findings, ordered by impact

### C1 — Critical, safety fix delivered: unavailable sources were treated as deleted media

Previously, `LibraryReconciliationService.ReconcileAsync` checked `File.Exists`, then deleted claims, canonical values, and the asset row when it returned false. It subsequently pruned empty hierarchy. `IngestionEngine` calls reconciliation at startup before scanning.

A configured root availability check now preserves the asset and hierarchy when a NAS, removable drive, or other source root is inaccessible. Settings-level availability and reconnect presentation remain planned.

**Fix:** resolve source availability first; represent unavailable sources separately from missing files; only prune confirmed deletions according to an explicit policy. Preserve source paths and item IDs across temporary outages. This is ordinary runtime behavior, not a request for legacy migrations or compatibility support. Route sidecar cleanup through the same source ownership policy as other mutations.

**UI:** Settings library/source status should show “Source unavailable.” Existing media should retain identity and explain temporary playback unavailability. Activity should distinguish unavailable, missing, and removed.

Evidence: `src/MediaEngine.Api/Services/LibraryReconciliationService.cs:167`, `src/MediaEngine.Ingestion/IngestionEngine.cs:284`, `src/MediaEngine.Ingestion/IngestionEngine.FileMaintenance.cs:34`.

### H1 — High, delivered: Wikidata show and album rollups

The policy in section 2 is implemented across request construction, bridge ID filtering, class selection, canonical routing, stored resolution method, and detail source-link presentation.

Deduplicate jobs by the trusted show/album identity, provider, requested operation, and policy version. Retain per-file ingestion and playback state. Do not deduplicate unrelated containers by normalized display title.

**UI:** show/album matching belongs to the parent scope; child views expose the inherited identity and retain child-local editing. No automatic episode/song Wikidata search or Review task for the absence of a child QID.

### H2 — High, ingestion path delivered: reread changed files at the same path

The same-path path now compares the stored fingerprint, reruns local processors when bytes changed, replaces the prior local observation snapshot, preserves the asset ID and non-local claims, and queues identity refresh. Startup scanning also rechecks stale fingerprints so edits made while the Engine was stopped are imported. An explicit editor “Reread file metadata” command remains planned.

**Fix:** separate discovery from local metadata refresh. Compare a persisted last-processed fingerprint, then reread changed tags/technical metadata while preserving the asset ID, progress, and user overrides. If the bytes now represent a different work, require an identity decision rather than blindly retaining the old match. Watch sidecar changes as refresh inputs, not new catalog media.

**UI:** File gets “Reread file metadata” with an outcome summary; metadata changes are recorded in History. “Scan now” continues to mean discover/reconcile sources.

Evidence: `src/MediaEngine.Ingestion/IngestionEngine.Pipeline.cs:334`, `src/MediaEngine.Ingestion/IngestionEngine.Watching.cs:98`.

### H3 — High, partially delivered: targeted refresh operations

The editor already has “Refresh enrichment,” but it requests a full enrichment cycle. It does not hash or physically reingest the file; it does pass through identity/enrichment jobs and can do substantially more than the user needs.

Provider artwork refresh can now use direct bridge identity without a QID for movies, TV, and music albums. Books, comics, audiobooks, selective metadata fields, and the shared durable command envelope remain planned. URL/file artwork replacement already exists.

**Fix:** add purpose-specific refresh commands using existing worker services. Known provider identity should be enough for supported provider assets. Reserve rematching for changing identity and full enrichment for an advanced action. Make replacement policy explicit: fill missing by default, refresh selected unlocked provider fields when requested, preserve user choices.

Evidence: `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:637`, `src/MediaEngine.Api/Services/Metadata/ArtworkScopeService.cs:133`, `src/MediaEngine.Api/Services/EnrichmentRefreshScheduleService.cs:112`, `src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor:266`.

### H4 — High, manual workflow delivered: text-track management in the shared editor

The current source has `TimedLyrics` and `Subtitles` dispatch in `RunSingleEnrichmentAsync` and the stream refresh endpoint. The normal Quick/Universe pass methods do not invoke those workers. Merely registering providers does not schedule lyrics/subtitle enrichment. This disagrees with architecture documentation describing them as part of late enrichment.

The API now validates the requested kind and returns a typed, truthful result. The shared editor lists managed tracks and can fetch lyrics or subtitles without full ingestion. Existing clients consume preferred text tracks. Optional automatic acquisition and richer import/select controls remain planned.

**Fix:** create explicit text-track jobs and typed outcomes; wire optional automatic acquisition separately from Wikidata and expose manual fetch/import in the editor. Reject invalid kinds. Lack of lyrics or optional subtitles is a capability result, not an identity Review problem.

Evidence: `src/MediaEngine.Providers/Services/EnrichmentService.cs:65`, `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:339`, `src/MediaEngine.Web/Services/Integration/EngineApiClient.Playback.cs:191`, `src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor:260`.

### H5 — High, delivered foundation: idempotent and atomic text-track refresh

Text-track identity is now deterministic for asset, kind, provider, source, language, and variant. Managed filenames are variant-specific, files publish atomically, exact track URLs select the intended row, and repeated refresh does not create a new logical track.

**Fix:** stable identity for asset/kind/provider/source/language/variant, unique storage per variant, transactional preferred selection, exact track URLs, temporary download files and atomic publication. Importing the same local sidecar twice must produce one logical track. Missing local files must not leave an unrepairable preferred record.

The worker also exports subtitles directly beside media without consulting the source mutation policy and makes outbound calls without its own library metadata policy check. Enforce these policies in the shared command/worker layer so manual and scheduled entry points cannot bypass them.

Evidence: `src/MediaEngine.Domain/Entities/TextTrack.cs:10`, `src/MediaEngine.Storage/TextTrackRepository.cs:99`, `src/MediaEngine.Providers/Workers/TextTrackEnrichmentWorker.cs:87`, `src/MediaEngine.Providers/Workers/TextTrackEnrichmentWorker.cs:181`, `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:294`.

### H6 — High, outcomes delivered and matching depth planned: text-track semantics

OpenSubtitles searches by IMDb or title plus TV coordinates, ranks mainly by download popularity, and the worker tries only the top candidate. It does not use a media hash or verified release matching in the reviewed code. LRCLIB supports only the exact synchronized-lyrics result path in this adapter; plain lyrics and instrumental responses are discarded. HTTP no-result responses and cancellation can flow into provider-failure reporting.

Typed results now distinguish updated, preserved user-owned content, no result, unsupported media, and missing assets. Provider-specific authentication/rate-limit states, release/hash evidence, forced/SDH distinctions, candidate choice, plain lyrics, instrumental states, and multi-language acquisition remain planned.

Evidence: `src/MediaEngine.Providers/Providers/OpenSubtitlesTextTrackProvider.cs:59`, `src/MediaEngine.Providers/Providers/LrclibTextTrackProvider.cs:59`, `src/MediaEngine.Providers/Workers/TextTrackEnrichmentWorker.cs:60`.

### M1 — Medium: status is richer in the backend than the item experience

The ingestion snapshot includes jobs, sources, stages, provider health and activity, review reasons, and batch results. The standard screen shows current activity and a small result summary; processing details and refresh schedules are collapsed and absent on mobile. “Refresh” on this page reloads status, whereas “Refresh enrichment” in the editor queues work. Those meanings need clearer labels.

Duplicate processing logs can say “skipped and deleted” before the source mutation decision is made, including when the file is retained. This makes activity history less trustworthy than the underlying protection policy.

**Fix:** show a compact per-item capability summary and exact operation result in the editor. Let users reach a failed job from that result; keep detailed provider telemetry in Settings. Show actionable source/provider problems on mobile. Publish deletion wording only after its actual result.

Evidence: `src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor:8`, `src/MediaEngine.Contracts/Ingestion/IngestionOperationsDtos.cs`, `src/MediaEngine.Ingestion/IngestionEngine.Pipeline.cs:249`.

### M2 — Medium: avoidable whole-library work in single-item editing

Opening one editor loads up to 1,000 refresh schedules and searches locally for its item. The schedule GET synchronizes schedules for all QID-bearing people/works, including upserts inside the global write transaction. This increases contention and can omit an item's schedule beyond the limit. Polling also loads every known file path and enumerates configured trees each cycle.

**Fix:** a targeted schedule/capability read for the current owner; move seeding to bounded background work; use indexed due-work queries, per-source checkpoints, and measured scan intervals. Keep scans streaming where possible. Preserve existing bridge batching and image deduplication rather than increasing parallelism indiscriminately.

Evidence: `src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs:457`, `src/MediaEngine.Api/Services/EnrichmentRefreshScheduleService.cs:231`, `src/MediaEngine.Ingestion/IngestionEngine.Watching.cs:309`.

### M3 — Medium: artwork download paths need one publication contract

Provider covers coordinate downloads and reuse cached content, but several editor download paths buffer the whole response and write directly to the destination. The inspected paths do not impose an application-specific image byte ceiling before buffering. Full enrichment may also trigger metadata writeback; the `quick_hydration` trigger falls through to the global enabled flag rather than `WriteOnAutoMatch`.

**Fix:** reuse one bounded download/validation/persistence service for editor and background artwork. Validate decoded dimensions and size; retain the previous usable variant on failed replacement; publish atomically and record the result. Make metadata refresh, local writeback, and file organization distinct operations. Review unknown writeback trigger behavior.

Evidence: `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:994`, `src/MediaEngine.Providers/Workers/CoverArtWorker.cs:403`, `src/MediaEngine.Ingestion/Services/WriteBackService.cs:82`.

## 5. Editor and API implementation shape

Reuse the shared editor and existing service layer. Do not create a separate management workspace.

| User intent | Editor location | Existing foundation | Proposed behavior |
| --- | --- | --- | --- |
| Fix wrong identity | Matching | Canonical search/apply, retail match and Wikidata preview | Change show/album identity at parent scope; preview affected owned children. Preserve child-specific facts. |
| Update facts for the correct item | Details | Provider adapters, claim scoring and user locks | Refresh selected metadata from confirmed provider IDs. Show changed fields and sources. |
| Replace cover/still/logo | Artwork | Scoped variants, upload/from-URL, movie/TV provider refresh | Find images, add candidate, set preferred, or refetch missing file across supported media. |
| Get lyrics or subtitles | Conditional Lyrics/Subtitles section | Text-track providers, parsers, repository and streaming | Show language, source, timing, local/user ownership, preferred variant, result and last check; Find, Import, Preview, Use. |
| Reread edited tags or media details | File | Processors, hashing and inspection | Refresh local facts without matching, moving, or resetting progress. |
| Understand what happened | History | Entity timeline and operations | Exact operation, target scope, source, differences, outcome, retry eligibility. |
| Diagnose a provider or source | Settings/Ingestion | Operation snapshot and provider telemetry | Failed work links back to the same editor. Setup problems link to the relevant provider/source settings. |

Proposed command contract: target entity and scope, operation kind, selected fields/assets/languages, replacement policy, freshness policy, expected identity revision, and idempotency key. Return an operation ID and a typed result with changed/skipped counts and reasons. Large operations return accepted/queued promptly; progress survives closing the editor.

Reuse `media_operations`, existing capability states, provider caches, and concurrency controls. Extend missing operation types instead of creating a parallel queue/schema. Use a typed API capability response so the editor does not duplicate provider rules.

Expected identity revision prevents a slow refresh for an old match overwriting a newer manual match. Jobs should retry only transient failures; a genuine no-result should have a configurable next-check time. Manual “Check again” can revalidate the selected provider cache without flushing the whole library cache.

The player consumes the same managed tracks as the editor. Refresh completion invalidates relevant manifests and lyric caches without restarting playback. Guest playback access must not imply permission to modify library-wide preferred tracks.

## 6. Downloads: distinguish three different jobs

1. **Download supporting assets:** artwork, subtitles, lyrics, metadata. Existing APIs and workers can support this without reingestion. This is the immediate priority.
2. **Download an owned media file for consumption:** existing playback/offline endpoints and offline settings provide a separate foundation. Validate profile/device permissions, readiness, progress, cancellation, and file availability. Do not present metadata providers as sources of the original film/book/audio bytes.
3. **Acquire or replace the original media:** keep acquisition with existing external tools for now. Tuvima can accept files through watched sources or upload. The upload path already stages `.uploading` files and checks space. A future explicit “Replace file” workflow would need content verification, identity review, progress-preservation rules, and source policy; it should not be hidden behind “Refresh.”

Evidence: `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:344`, `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:163`.

Provider APIs can fetch individual episode facts and artwork using the known show ID and episode coordinates, without Wikidata or a file scan. Reuse the existing TMDB adapter's extraction/mapping logic behind a targeted refresh service. [TMDB episode details](https://developer.themoviedb.org/reference/tv-episode-details), [TMDB episode images](https://developer.themoviedb.org/reference/tv-episode-images)

Lyrics remain track-specific even when Wikidata is album-scoped. LRCLIB can supply lyrics from track signatures; its documented response model includes plain lyrics, so lack of timed lyrics need not mean there is no useful text. [LRCLIB API](https://lrclib.net/docs)

## 7. Lessons from other media applications

These are documented workflow comparisons, not measured claims that another product is faster or more reliable.

| Application | Useful pattern | Tuvima fit and decision |
| --- | --- | --- |
| Plex | Distinguishes scanning files from refreshing metadata and fixing a match. | Adopt these distinct intents and clear labels; retain Tuvima's more explicit provenance and shared editor. [Plex scan versus refresh](https://support.plex.tv/articles/200289306-scanning-vs-refreshing-a-library/) |
| Jellyfin | Local NFO/provider metadata, embedded music tags, local artwork, and synchronized or plain lyrics. | Improve reread and sidecar workflows; make text tracks manageable. Preserve Tuvima source ownership rules. [Metadata](https://jellyfin.org/docs/general/server/metadata/), [Music and lyrics](https://jellyfin.org/docs/general/server/media/music/) |
| calibre | Fetch metadata or covers inside the editor; choose candidates; manage formats and edit multiple books. | Extend current editor scope and batch preview. Keep provider facts attributed and user overrides separate. Do not expand into a full ebook conversion suite. [calibre metadata editor](https://manual.calibre-ebook.com/metadata.html) |
| Audiobookshelf | Separates scanning local metadata from manually matching against online sources. | Preserve local chapter/track titles and playback tools; add explicit reread and targeted refresh without restarting the whole process. [Audiobookshelf scanning and matching](https://audiobookshelf.org/docs/faq/server/) |
| Kavita | Structured file/ComicInfo metadata and controlled application of external metadata. | Prioritize trustworthy series/issue ordering and imported local edits; avoid speculative issue-level Wikidata links and noisy tags. [Kavita file management](https://wiki.kavitareader.com/guides/scanner/managefiles/), [metadata controls](https://wiki.kavitareader.com/kavita%2B/metadata-controls/) |

Tuvima's distinctive value is a unified Read/Watch/Listen experience, work relationships across media, source provenance, and contextual correction. Preserve those advantages while making everyday library maintenance predictable.

Interoperability work should start with a tested inventory of the local formats already supported (embedded tags, ComicInfo, OPF/NFO, audiobook metadata sidecars). Add only concrete missing formats. Define import precedence and explicitly opt-in export; avoid bidirectional synchronization or competing file organizers.

## 8. Prioritized delivery plan

| Order | Priority | Status | Work package | Depends on | Completion gate |
| --- | --- | --- | --- | --- | --- |
| 1 | Critical | Backend delivered; source-status UI remains | Source availability and deletion policy (C1) | None | Starting with a source offline removes no catalog rows or user originals; reconnect restores availability. Explicit confirmed removal still works. |
| 2 | High | Planned | Shared scoped refresh command/outcome contract and policy gate | Existing operations/capabilities | Duplicate submissions coalesce; job survives restart; no-result/auth/rate-limit/cancel outcomes differ; local-only and read-only rules apply at execution. |
| 3 | High | Delivered | Show/album Wikidata rollups (H1) | Scope contract | Zero automatic child Wikidata requests; child metadata and exact release distinctions preserved. |
| 4 | High | Ingestion delivered; explicit editor command remains | Local reread and changed-file detection (H2) | Source handling | External tag edits while running or stopped are reflected; same ID, progress, and overrides remain. |
| 5 | High | Persistence and core outcomes delivered; provider semantics remain | Text-track persistence and result corrections (H4-H6) | Existing provider adapters | Repeated refresh creates one logical variant; failures preserve the current preferred file; missing credentials and no results are explicit. |
| 6 | High | Movies, TV, and music albums delivered | Targeted metadata/artwork editor actions (H3) | Scope contract, rollups | Refresh one asset without hashing, rematching, unrelated enrichment, or source mutation; expand parity to Read and audiobooks. |
| 7 | High | Fetch/list delivered; import/select remain | Lyrics/subtitle editor and player connection | Text-track corrections | Fetch/import/select works in the shared editor; live player sees changes; only requested owned episodes/tracks are affected. |
| 8 | Medium | Planned | Per-item status, mobile actionability and operation history (M1) | Typed outcomes; basic feedback ships with every high-priority action | Every user-triggered job has an understandable result and route to retry/setup; optional gaps never masquerade as identity failure. |
| 9 | Medium | Planned | Targeted schedule reads, bounded scheduler and scan work (M2) | Scope model | Opening an editor does not seed/write the whole schedule; due selection is indexed; representative large-library benchmark shows bounded work. |
| 10 | Medium | Planned | Shared asset publication and explicit writeback semantics (M3) | Refresh infrastructure | Interrupted/oversized/invalid downloads preserve prior assets; metadata refresh cannot unexpectedly rename or retag files. |
| 11 | Medium | Planned | Verified interoperability and selective batch repair | Stable single-item operations | Local metadata imports are repeatable; batch preview deduplicates shared parents and reports partial results. |
| 12 | Low | Deferred | Convenience features justified by use | Above | Optional subtitle offset, more lyric formats, artwork alternatives, and finer refresh schedules improve real workflows without increasing baseline ingestion cost. |

Safety portions of download publication and source policy must ship with the first affected refresh feature, even where general consolidation is medium priority. These packages are dependencies and acceptance criteria, not delivery-time estimates; estimate after the contract and provider capability inventory are agreed.

### Validation required for implementation

- Fresh disposable ingestion fixtures covering movies, multiple TV seasons, specials, multi-disc/compilation/deluxe albums, loose tracks, books, comics, and segmented audiobooks.
- Restart during scan, matching, download, persistence, and organization; repeat commands concurrently and after restart.
- NAS/removable source offline, locked file, partial upload, out-of-space, changed file, duplicate content, provider timeout/429/authentication/no-result.
- Assert no source writes for existing/read-only media and no provider calls for local-only/manual content. Include direct API calls, scheduled jobs, and editor actions.
- Assert known provider IDs can refresh assets without a QID. Assert child details do not inherit the parent's title/runtime/still as child facts.
- Record time until playable/browsable, provider requests per parent, cache hit rate, queue age, retry counts, SQLite writer wait, and optional capability outcomes. Establish the baseline first; do not invent throughput targets.
- Run restore/build/full tests before application changes are finalized. For scope/artwork ingestion fixes use the repository's fresh-ingestion procedure, protecting all original sources. No legacy migrations or compatibility shims.
- Validate affected editor, detail, player, Review, and Ingestion surfaces at 1920x1080 plus lower-height/mobile layouts; keep the underlying detail URL and playback stable.
- Update architecture and user documentation alongside implementation. In particular, remove claims that automatic text-track enrichment exists until that behavior is actually wired and tested.

## 9. Deliberately deferred or excluded

- Automatic Wikidata resolution for individual TV episodes, seasons, songs, or recordings.
- Exhaustive fictional-universe expansion and external child catalogs on the critical path to browsing/playback.
- Requiring Wikidata to display an owned item or download provider artwork, lyrics, or subtitles.
- Acquisition/download-client orchestration, full ebook conversion, automatic retagging of another application's source tree, and two-way synchronization across multiple media managers.
- Automatic AI-generated lyrics/subtitles or broad expensive analysis during intake. Consider optional measured jobs only after the core workflow is dependable.
- A replacement all-in-one media management workspace, redundant detail pages for songs, or new global navigation for every repair function.

## Product-owner summary

Tuvima already has many of the right building blocks. The first update should prevent a disconnected drive from looking like a deleted library. Next, match shows and albums once, keep episode and track facts specific, and let users refresh just the metadata, picture, lyrics, or subtitles they need from the existing editor. Clear results and reliable retries matter more than adding more enrichment or trying to replace every specialist media application.

