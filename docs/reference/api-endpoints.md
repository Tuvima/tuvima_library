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
| GET | `/system/watcher-status` | File watcher diagnostic â€” shows monitored folders and last event | Required |
| POST | `/maintenance/sweep-orphan-images` | Scan `.data/images/` for directories with no matching database record and remove them. Skips user-uploaded images (`user_override` flag). | Administrator |

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
| GET | `/collections` | All media collections (Series) with their child works | Required |
| GET | `/collections/{collectionId}/series-manifest` | Ordered Wikidata series checklist with total, owned, missing, provisional, ambiguous counts and named entries | Required |
| GET | `/collections/search?q=` | SQL-backed search across visible library works, canonical values, and collection names. Returns up to 20 work results. | Required |

---

## Library Item

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/library/items` | Paginated item list. Includes projection-backed fields such as `pipelineStep`, `vaultVisibility`, `isReadyForVault`, `artworkState`, `artworkSource`, and `artworkSettledAt`. Supports filtering by status, media type, collection, and search term. | Required |
| GET | `/library/items/{entityId}/detail` | Full item detail including claims, canonical values, pipeline projection fields, artwork truth, and linked persons | Required |
| GET | `/library/items/counts` | Status counts for tab badges and compatibility counters such as review, auto-approved, duplicate, staging, and missing-image counts | Required |
| GET | `/library/items/state-counts?batchId=` | Four-state counts scoped to a specific ingestion batch | Required |

---

## media library

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/media library/overview` | Aggregated media library health and readiness view, including `hidden_by_quality_gate`, `art_pending`, `retail_needs_review`, `qid_no_match`, and `completed_with_art` | Required |

---

## Metadata

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/metadata/claims/{entityId}` | All claims for an entity, grouped by field, with source and confidence | Required |
| GET | `/metadata/conflicts` | All unresolved metadata conflicts across the library | Curator |
| GET | `/metadata/{entityId}/canon-discrepancies` | Field-level mismatches between the canonical value and file-embedded metadata | Required |

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
| GET | `/ingestion/watch-folder` | Returns current watch folder configuration | Required |

---

## Search

| Method | Path | Description | Auth |
|---|---|---|---|
| POST | `/search/universe` | Search Wikidata for entity candidates by title, author, and media type | Curator |
| POST | `/search/retail` | Search configured retail providers for matching candidates | Curator |
| POST | `/search/resolve` | Unified resolve search â€” queries all active providers and returns ranked candidates | Curator |

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
| GET | `/persons/{id}` | Person detail â€” biographical data, roles, library presence, social links | Required |
| GET | `/persons/{id}/aliases` | Pseudonym list for a person, including resolved Wikidata aliases | Required |

---

## Universes

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/universes` | All narrative roots (Universes) in the library | Required |
| GET | `/universe/{qid}` | Universe detail â€” Series, People, and asset counts | Required |
| GET | `/universe/{qid}/health` | Health score â€” completeness, enrichment freshness, missing metadata indicators | Required |
| GET | `/universe/{qid}/graph` | Graph data in Cytoscape.js format for the Chronicle Explorer visualization | Required |
| GET | `/universe/{qid}/paths` | Find shortest paths between two entities within the universe graph | Required |
| GET | `/universe/{qid}/family-tree` | Character family tree rooted at a specified character entity | Required |
| GET | `/universe/{qid}/cross-media` | Entities that appear across more than one media type within the universe | Required |
| GET | `/universe/{qid}/cast` | Characters with their linked performers, including era-correct actor data | Required |
| GET | `/universe/{qid}/adaptations` | Adaptation chain â€” all works derived from or adapted into each other | Required |

---

## AI

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/ai/status` | AI subsystem health â€” model load state, hardware tier, resource pressure | Required |
| GET | `/ai/models` | Status of all configured models (loaded, unloaded, downloading, unavailable) | Required |
| POST | `/ai/models/{role}/download` | Trigger download for a model role (`text_fast`, `text_quality`, `text_scholar`, `text_cjk`, `audio`) | Administrator |
| POST | `/ai/models/{role}/load` | Load a model into memory | Administrator |
| POST | `/ai/models/{role}/unload` | Unload a model from memory | Administrator |
| GET | `/ai/config` | Full AI configuration | Administrator |
| PUT | `/ai/config` | Update AI configuration | Administrator |
| GET | `/ai/profile` | Hardware profile â€” benchmark results, tier classification, GPU backend | Required |
| POST | `/ai/benchmark` | Re-run hardware benchmark and reclassify tier | Administrator |
| GET | `/ai/resources` | Live system resource usage â€” CPU load, RAM pressure, active transcoding tasks | Required |
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
| GET | `/activity/stats` | Aggregated activity statistics â€” counts by type and outcome | Required |
| PUT | `/activity/retention` | Update the activity retention period (days) | Administrator |
| GET | `/activity/by-types` | Filter activity log by one or more activity type codes | Required |
| GET | `/activity/run/{runId}` | All activity entries for a specific ingestion or enrichment run | Required |

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
| GET | `/settings/providers` | Provider configuration status â€” enabled state, last health check, rate limit info | Required |
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
| GET | `/read/{assetId}/metadata` | EPUB metadata â€” title, author, language, cover | Required |
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
| GET | `/reader/{assetId}/statistics` | Reading statistics â€” time spent, completion percentage, sessions | Required |
| POST | `/read/{assetId}/whispersync` | Sync reading position across devices | Required |
| GET | `/read/{assetId}/whispersync` | Retrieve last sync position | Required |
| DELETE | `/read/{assetId}/whispersync` | Clear sync position | Required |

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

## media library

| Method | Path | Description | Auth |
|---|---|---|---|
| GET | `/media library/characters/{id}/portraits` | Portrait images for a fictional character | Required |
| GET | `/media library/persons/{id}/character-roles` | Characters a person has performed, linked to works | Required |
| GET | `/media library/universes/{qid}/characters` | All characters in a universe | Required |
| GET | `/media library/assets/{entityId}` | Shared assets for an entity (Cover Art, Headshot, Banner, Logo, Backdrop) | Required |
| POST | `/media library/enrichment/universe/trigger` | Trigger universe enrichment for a specific QID | Curator |

---

## SignalR

| Collection | Path | Direction | Description |
|---|---|---|---|
| Intercom | `/intercom` | Server â†’ Client | Real-time push events: ingestion progress, enrichment completion, pipeline state changes, review queue updates. Authentication required. Server-push only â€” clients do not send messages to the collection. |

---

## Development

Available in development environments only. These endpoints are removed in production builds.

| Method | Path | Description |
|---|---|---|
| POST | `/dev/seed-library` | Seed the library with 22 EPUB test cases covering edge cases (pen names, foreign languages, series grouping, multi-author) |
| POST | `/dev/wipe` | Wipe the database and staging area. Irreversible. |
| POST | `/dev/full-test` | Run full ingestion and enrichment pipeline on the seeded test library |
| POST | `/dev/integration-test` | Run the integration test suite and return an HTML report |

## Related

- [How to Build, Test, and Verify Changes](../guides/running-tests.md)
- [Database Schema Reference](database-schema.md)
- [Security Architecture](../architecture/security.md)

