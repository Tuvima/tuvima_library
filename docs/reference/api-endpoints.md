---
title: "Engine API Reference"
summary: "Look up the Engine's HTTP routes, authentication rules, and endpoint responsibilities."
audience: "developer"
category: "reference"
product_area: "api"
tags:
  - "http"
  - "api"
  - "endpoints"
---

# Engine API Reference

Base URL: `http://localhost:61495`

Interactive documentation: `http://localhost:61495/swagger`

All endpoints require authentication unless noted. Three roles: **Administrator** (full access), **Curator** (browse + metadata), **Consumer** (browse only). API keys are passed as `X-Api-Key` header.

---

## System

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/system/status` | Service health, version, uptime | None |
| GET | `/system/watcher-status` | File watcher diagnostic - shows monitored folders and last event | Required |
| POST | `/maintenance/sweep-orphan-assets` | Scan `.data/assets/` for managed files with no database reference and remove them. | Administrator |
| POST | `/maintenance/storage/run` | Run storage maintenance on demand. Supports `dryRun`, cache retention, image retention, and claim compaction batch query parameters. | Administrator |

---

## Library

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/library/works` | All works with their canonical values | Required |

## Works

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/works/{id}` | Work detail with canonical values, editions, and owned assets | Required |
| GET | `/works/{id}/editions` | Editions and owned assets for a work | Required |
| GET | `/works/{id}/cast` | Actor and character credits for a single work | Required |

---

## Collections

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/collections` | Browse-oriented collection list with works and canonical metadata | Required |
| GET | `/collections/management-catalog` | Collections hub catalog: system/user/managed collections plus broader rollups where trusted relationships connect multiple shelves | Required |
| GET | `/collections/{id}/summary` | One Collections hub summary for a detail page without loading the full catalog | Required |
| GET | `/collections/{id}/items` | Items for a collection detail page, including generated rollup aggregation | Required |
| POST | `/collections/reconcile` | Dry-run or run collection shelf repair for already-ingested media. Body: `dry_run`, `batch_size`, `max_items`. Returns candidate, processed, assigned, skipped, failed, and elapsed counts. | Curator |
| GET | `/collections/{collectionId}/series-manifest` | Ordered Wikidata series checklist with total, owned, missing, provisional, ambiguous counts and named entries | Required |
| GET | `/collections/search?q=` | SQL-backed search across visible library works, canonical values, and collection names. Returns up to 20 work results. | Required |

---

## Library Item

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/library/items` | Paginated item list for current browse/detail surfaces. Includes projection-backed fields such as `pipelineStep`, `libraryVisibility`, `isReadyForLibrary`, `artworkState`, `artworkSource`, and `artworkSettledAt`. Supports filtering by status, media type, collection, and search term. | Required |
| GET | `/library/items/{entityId}/detail` | Full item detail including claims, canonical values, pipeline projection fields, artwork truth, and linked persons | Required |
| GET | `/library/items/counts` | Status counts for tab badges and compatibility counters such as review, auto-approved, duplicate, staging, and missing-image counts | Required |
| GET | `/library/items/state-counts?batchId=` | Four-state counts scoped to a specific ingestion batch | Required |

---

## Library Overview

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/library/overview` | Aggregated library health and readiness view, including `hidden_by_quality_gate`, `art_pending`, `retail_needs_review`, `qid_no_match`, and `completed_with_art` | Required |

---

## Metadata

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/metadata/claims/{entityId}` | All claims for an entity, grouped by field, with source and confidence | Required |
| GET | `/metadata/conflicts` | All unresolved metadata conflicts across the library | Curator |
| GET | `/metadata/{entityId}/canon-discrepancies` | Field-level mismatches between the canonical value and file-embedded metadata | Required |
| GET | `/metadata/{entityId}/artwork` | Artwork context for the media editor, including variants by artwork type and preferred selections | Curator |
| GET | `/metadata/{entityId}/artwork/{scopeId}` | Artwork variants for a specific editor scope | Curator |
| POST | `/metadata/{entityId}/artwork/{scopeId}/{assetType}` | Upload a user-owned artwork variant for the selected type | Curator |
| POST | `/metadata/{entityId}/artwork/{scopeId}/{assetType}/from-url` | Add an artwork variant from a provider or user-supplied image URL | Curator |
| POST | `/metadata/{entityId}/artwork/{assetType}` | Compatibility upload route for an artwork type | Curator |
| PUT | `/metadata/artwork/{variantId}/preferred` | Make an artwork variant the preferred image for its artwork type | Curator |
| DELETE | `/metadata/artwork/{variantId}` | Remove an artwork variant from the item. Shared provider/image cache files are retained when still referenced elsewhere. | Curator |

---

## Streaming

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/stream/{assetId}` | Stream media file. Supports HTTP 206 byte-range requests for seeking. | Required |
| GET | `/stream/{assetId}/cover` | Full-size cover art (JPEG) | Required |
| GET | `/stream/{assetId}/cover-thumb` | 200px-wide thumbnail (JPEG, quality 75, SkiaSharp-generated) | Required |

---

## Ingestion

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/ingestion/scan` | Dry-run scan of configured library folders. Reports what would be ingested without making changes. | Administrator |
| POST | `/ingestion/library-scan` | Scan library folders and update known file paths. Triggers ingestion for new files. | Administrator |
| GET | `/ingestion/operations` | Dashboard snapshot backed by durable ingestion operation counts, numbered `stage_progress` rows, current activity, review state, provider health, and recent batch summaries. | Curator |
| GET | `/ingestion/batches` | Recent ingestion batches. | Curator |
| GET | `/ingestion/batches/{batchId}` | Single ingestion batch summary. | Curator |
| GET | `/ingestion/batches/{batchId}/items` | Durable per-file item ledger for a batch, sourced from `media_operations`. | Curator |
| GET | `/ingestion/watch-folder` | Returns the derived current watch folder view from configured library source folders. | Required |

`/ingestion/operations.stage_progress` contains numbered ingestion stage rows with `stage_number`, `stage_key`, `label`, `completed_files`, `total_files`, `percent_complete`, `active_count`, `queued_count`, `status_label`, `active_item_label`, `active_group_label`, `active_group_count`, `label_accuracy`, `artifact_label`, `artifact_count`, `detail_items`, `last_updated_time`, and `is_stale`. `detail_items` is an optional list of `{ label, value, tone?, icon? }` rows populated by the Engine so clients do not hardcode provider math. Grouped provider work, such as batched Wikidata resolution, uses group labels instead of fake exact file labels unless per-file correlation is available.

The Dashboard renders Stages 1-8 as compact progress rows. Review/attention state remains in the snapshot for API consumers, but the Dashboard surfaces it through the top **Need Review** metric and the `recent_batches` review count rather than as another progress bar. `recent_batches` rows include batch id, status, timing, file totals, media-type counts, registered/review/failed counts, and artifact totals such as people, artwork, metadata, matched, and review.

---

## Operations And Capabilities

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/operations` | Durable work queue ordered by queue priority and position. Supports `queueName` and `limit`. | Curator |
| GET | `/operations/{id}` | One operation plus its event timeline. | Curator |
| GET | `/operations/summary` | Counts by durable operation status. | Curator |
| POST | `/operations/{id}/retry` | Requeue a durable operation for another attempt. | Curator |
| POST | `/operations/{id}/cancel` | Cancel a durable operation. | Curator |
| GET | `/assets/{id}/capabilities` | Explicit capability/readiness states for one media asset. | Curator |
| GET | `/capabilities/summary` | Counts by capability and status. | Curator |

---

## Search

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/search/universe` | Search Wikidata for entity candidates by title, author, and media type | Curator |
| POST | `/search/retail` | Search configured retail providers for matching candidates | Curator |
| POST | `/search/resolve` | Unified resolve search - queries all active providers and returns ranked candidates | Curator |

---

## Review Queue

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/review/pending` | All items currently in the review queue | Curator |
| GET | `/review/count` | Count of pending review items. Used for media library badge. | Required |
| GET | `/review/{id}` | Full detail for a single review item including candidates | Curator |
| POST | `/review/{id}/resolve` | Resolve a review item by selecting a candidate or confirming corrected local metadata | Curator |
| POST | `/review/{id}/dismiss` | Dismiss a review item without resolving it | Curator |
| POST | `/review/{id}/skip-universe` | Accept the item without a Wikidata QID. The item can still remain browse surfaces-visible if it passes the browse readiness gate. | Curator |

---

## Persons

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/persons/{id}` | Person detail - biographical data, roles, library presence, social links | Required |
| GET | `/persons/{id}/aliases` | Pseudonym list for a person, including resolved Wikidata aliases | Required |

---

## Universes

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/universes` | All narrative roots (Universes) in the library | Required |
| GET | `/universe/{qid}` | Universe detail - Series, People, and asset counts | Required |
| GET | `/universe/{qid}/health` | Health score - completeness, enrichment freshness, missing metadata indicators | Required |
| GET | `/universe/{qid}/graph` | Graph data in Cytoscape.js format for the Chronicle Explorer visualization | Required |
| GET | `/universe/{qid}/paths` | Find shortest paths between two entities within the universe graph | Required |
| GET | `/universe/{qid}/family-tree` | Character family tree rooted at a specified character entity | Required |
| GET | `/universe/{qid}/cross-media` | Entities that appear across more than one media type within the universe | Required |
| GET | `/universe/{qid}/cast` | Characters with their linked performers, including era-correct actor data | Required |
| GET | `/universe/{qid}/adaptations` | Adaptation chain - all works derived from or adapted into each other | Required |

---

## AI

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/ai/status` | AI subsystem health - model load state, hardware tier, resource pressure | Required |
| GET | `/ai/models` | Status of all configured models (loaded, unloaded, downloading, unavailable) | Required |
| POST | `/ai/models/{role}/download` | Trigger download for a model role (`text_fast`, `text_quality`, `text_scholar`, `text_cjk`, `audio`) | Administrator |
| POST | `/ai/models/{role}/load` | Load a model into memory | Administrator |
| POST | `/ai/models/{role}/unload` | Unload a model from memory | Administrator |
| GET | `/ai/config` | Full AI configuration | Administrator |
| PUT | `/ai/config` | Update AI configuration | Administrator |
| GET | `/ai/profile` | Hardware profile - benchmark results, tier classification, GPU backend | Required |
| POST | `/ai/benchmark` | Re-run hardware benchmark and reclassify tier | Administrator |
| GET | `/ai/resources` | Live system resource usage - CPU load, RAM pressure, active transcoding tasks | Required |
| GET | `/ai/enrichment/progress` | Background enrichment batch progress | Required |
| GET | `/ai/enrich/tldr/{entityId}` | Generate a TL;DR summary for a work using its description | Curator |
| GET | `/ai/enrich/vibes/{entityId}` | Generate vibe tags for a work | Curator |
| POST | `/ai/enrich/search/intent` | Parse a natural-language search query into structured field filters | Required |
| POST | `/ai/enrich/extract-url` | Extract metadata from a URL (book page, IMDB entry, etc.) | Curator |

---

## Enrichment

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/metadata/pass2/trigger` | Manually trigger the optional Pass 2 deferred enrichment flow for one or more entity IDs | Curator |
| GET | `/metadata/pass2/status` | Current Pass 2 deferred-enrichment status, including whether two-pass mode is enabled | Required |

---

## Activity

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/activity/recent` | Recent system activity entries, newest first | Required |
| POST | `/activity/prune` | Prune activity entries older than the configured retention period | Administrator |
| GET | `/activity/stats` | Aggregated activity statistics - counts by type and outcome | Required |
| PUT | `/activity/retention` | Update the activity retention period (days) | Administrator |
| GET | `/activity/by-types` | Filter activity log by one or more activity type codes | Required |
| GET | `/activity/run/{runId}` | All activity entries for a specific ingestion or enrichment run | Required |

---

## Plugins

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/plugins` | List built-in and dynamic plugins loaded by the Engine | Administrator |
| GET | `/plugins/approved` | Fetch the approved plugin discovery catalog from the configured GitHub source | Administrator |
| GET | `/plugins/{pluginId}` | Plugin detail, manifest metadata, settings, permissions, and load state | Administrator |
| POST | `/plugins/{pluginId}/enable` | Enable a plugin | Administrator |
| POST | `/plugins/{pluginId}/disable` | Disable a plugin | Administrator |
| PUT | `/plugins/{pluginId}/settings` | Save plugin user settings JSON | Administrator |
| GET | `/plugins/{pluginId}/manifest` | Read dynamic plugin manifest JSON. Built-in manifests are compiled and are not returned here. | Administrator |
| PUT | `/plugins/{pluginId}/manifest` | Save dynamic plugin manifest JSON without changing plugin id | Administrator |
| DELETE | `/plugins/{pluginId}` | Delete a dynamic plugin folder and saved plugin configuration | Administrator |
| POST | `/plugins/{pluginId}/health` | Run plugin health checks | Administrator |
| GET | `/plugins/{pluginId}/jobs` | List recent durable plugin operation rows for one plugin. | Administrator |
| POST | `/plugins/jobs/segment-detection/run` | Run scheduled playback segment detector plugins immediately | Administrator |

---

## Admin

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/admin/api-keys` | List all API keys with labels, roles, and creation dates (keys are not returned after creation) | Administrator |
| POST | `/admin/api-keys` | Generate a new API key with a label and role | Administrator |
| DELETE | `/admin/api-keys/{keyId}` | Revoke an API key immediately | Administrator |
| GET | `/admin/provider-configs` | List all provider configurations | Administrator |
| PUT | `/admin/provider-configs/{providerId}` | Update a provider configuration | Administrator |
| DELETE | `/admin/provider-configs/{providerId}` | Remove a provider configuration | Administrator |

---

## Settings

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/settings/folders` | List configured library folder paths | Required |
| PUT | `/settings/folders` | Update library folder configuration | Administrator |
| POST | `/settings/test-path` | Test whether a filesystem path is accessible by the Engine | Administrator |
| GET | `/settings/providers` | Provider configuration status - enabled state, last health check, rate limit info | Required |
| GET | `/settings/server-general` | Core server settings (name, language preferences, country) | Required |
| PUT | `/settings/server-general` | Update core server settings | Administrator |
| POST | `/settings/organization-template/preview` | Validate an organization template and return a sample preview without saving | Administrator |
| PUT | `/settings/organization-template` | Save the organization template after validation | Administrator |

---

## Profiles

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/profiles` | List all user profiles | Required |
| POST | `/profiles` | Create a new profile | Administrator |
| GET | `/profiles/{id}` | Profile detail | Required |
| PUT | `/profiles/{id}` | Update a profile | Administrator |
| DELETE | `/profiles/{id}` | Delete a profile | Administrator |

---

## UI Settings

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/settings/ui/global` | Global UI defaults | Required |
| PUT | `/settings/ui/global` | Update global UI defaults | Administrator |
| GET | `/settings/ui/device/{class}` | Device profile settings for a device class (`web`, `mobile`, `television`, `automotive`) | Required |
| PUT | `/settings/ui/device/{class}` | Update device profile settings | Administrator |
| GET | `/settings/ui/profile/{id}` | Per-user UI preference overrides for a profile | Required |
| PUT | `/settings/ui/profile/{id}` | Update per-user UI preferences | Required |
| GET | `/settings/ui/resolved` | Effective resolved settings for the current request context (global + device + profile merged) | Required |

---

## Reader

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/read/{assetId}/metadata` | EPUB metadata - title, author, language, cover | Required |
| GET | `/read/{assetId}/toc` | Table of contents | Required |
| GET | `/read/{assetId}/chapter/{index}` | Chapter content by index | Required |
| GET | `/read/{assetId}/resource/{path}` | Embedded resource (CSS, image) by path within the EPUB | Required |
| GET | `/read/{assetId}/search` | Full-text search within EPUB content | Required |
| GET | `/reader/{assetId}/bookmarks` | List bookmarks for an asset | Required |
| POST | `/reader/{assetId}/bookmarks` | Create a bookmark | Required |
| DELETE | `/reader/{assetId}/bookmarks/{bookmarkId}` | Delete a bookmark | Required |
| GET | `/reader/{assetId}/highlights` | List highlights | Required |
| POST | `/reader/{assetId}/highlights` | Create a highlight | Required |
| DELETE | `/reader/{assetId}/highlights/{highlightId}` | Delete a highlight | Required |
| GET | `/reader/{assetId}/statistics` | Reading statistics - time spent, completion percentage, sessions | Required |
| POST | `/read/{assetId}/whispersync` | Sync reading position across devices | Required |
| GET | `/read/{assetId}/whispersync` | Retrieve last sync position | Required |
| DELETE | `/read/{assetId}/whispersync` | Clear sync position | Required |

---

## Player

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/player/state` | Current playback session, queue, position, and audiobook history | Required |
| POST | `/player/queue/replace` | Replace the active playback queue | Required |
| POST | `/player/queue/items` | Add items to the active playback queue | Required |
| POST | `/player/command` | Apply transport commands such as play, pause, seek, volume, or speed | Required |
| POST | `/player/heartbeat` | Persist current playback timing and resume progress | Required |
| GET | `/player/audiobooks/{workId}/history` | Recent audiobook listening checkpoints | Required |
| GET | `/player/audiobooks/{workId}/bookmarks` | Saved audiobook playback bookmarks | Required |
| POST | `/player/audiobooks/{workId}/bookmarks` | Save an audiobook playback bookmark at a position in seconds | Required |
| DELETE | `/player/audiobooks/bookmarks/{bookmarkId}` | Delete an audiobook playback bookmark | Required |
| POST | `/player/audiobooks/{workId}/chapters/suggest-names` | Suggest display-only audiobook chapter names using local AI | Required |
| GET | `/player/audiobooks/{workId}/chapter-overrides` | List saved display-only audiobook chapter title overrides | Required |
| POST | `/player/audiobooks/{workId}/chapter-overrides` | Save one display-only audiobook chapter title override | Required |
| DELETE | `/player/audiobooks/{workId}/chapter-overrides/{assetId}/{chapterIndex}` | Delete one display-only audiobook chapter title override | Required |

---

## Progress

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/progress/{assetId}` | Playback or reading progress for an asset | Required |
| PUT | `/progress/{assetId}` | Update progress | Required |
| GET | `/progress/recent` | Recently accessed assets with progress | Required |
| GET | `/progress/journey` | Full reading/watching journey across all assets for the current profile | Required |

---

## Reports

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/reports` | Submit a metadata quality report for an entity | Required |
| GET | `/reports/entity/{id}` | All reports for a specific entity | Curator |
| POST | `/reports/{id}/resolve` | Mark a report as resolved | Curator |
| POST | `/reports/{id}/dismiss` | Dismiss a report | Curator |

---

## Library Character and Asset Data

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/library/characters/{id}/portraits` | Portrait images for a fictional character | Required |
| PUT | `/library/characters/{id}/portraits/{portraitId}/default` | Set the default portrait for a fictional character | Curator |
| GET | `/library/persons/{id}/character-roles` | Characters a person has performed, linked to works | Required |
| GET | `/library/universes/{qid}/characters` | All characters in a universe | Required |
| GET | `/library/assets/{entityId}` | Shared assets for an entity (Cover Art, Headshot, Banner, Logo, Backdrop) | Required |
| POST | `/library/enrichment/universe/trigger` | Trigger universe enrichment for a specific QID | Curator |

---

## SignalR

| Collection | Path | Direction | Description |
|---|---|---|---|
| Intercom | `/intercom` | Server -> Client | Real-time push events: ingestion progress, enrichment completion, `MediaOperationChanged`, `CapabilityStateChanged`, pipeline state changes, and review queue updates. Authentication required. Server-push only - clients do not send messages to the collection. |

---

## Development

Available in development environments only. These endpoints are removed in production builds.

| Method | Path | Description |
|---|---|---|
| POST | `/dev/seed-library` | Seed the library with 22 EPUB test cases covering edge cases (pen names, foreign languages, series grouping, multi-author) |
| POST | `/dev/wipe` | Wipe the database and staging area. Irreversible. |
| POST | `/dev/full-test` | Run full ingestion and enrichment pipeline on the seeded test library |
| POST | `/dev/reingest-library` | Pause file watching, reset generated database/cache/artwork state, scan every configured library source path, and leave watching paused until restart or explicit resume. |
| POST | `/dev/integration-test` | Run the integration test suite and return an HTML report |

## Related

- [How to Build, Test, and Verify Changes](../guides/running-tests.md)
- [Database Schema Reference](database-schema.md)
- [Security Architecture](../architecture/security.md)
