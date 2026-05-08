---
title: "Library Operations"
summary: "Understand the Library Operations dashboard for ingestion, review, source folders, provider health, and recent batches."
audience: "user"
category: "guide"
product_area: "ingestion"
tags:
  - "library-operations"
  - "ingestion"
  - "review"
---

# Library Operations

Library Operations is the product-facing admin dashboard for the part of Tuvima Library that turns messy folders into registered media. It is available from Settings under **Library Operations**, while preserving the existing `/settings/ingestion` route.

The page answers six operational questions:

- what is happening right now
- what has been processed recently
- what needs review
- which Watch, Listen, and Read folders are configured
- which provider or pipeline stage is failing, waiting, or unknown
- how close the library is to being registered and healthy

## Data Sources

The Engine exposes one snapshot endpoint for the page:

`GET /ingestion/operations`

That snapshot is built by `IIngestionOperationsStatusService` from real application state:

- library totals and registered/provisional/review counts come from `ILibraryItemRepository`
- active and recent batches come from `IIngestionBatchRepository`
- pipeline stage counts come from `identity_jobs` and `ingestion_log`
- review reason counts come from pending `review_queue` rows
- source folders come from `config/libraries.json`
- folder reachability is checked from the filesystem at snapshot time
- provider health comes from configured providers plus `provider_health`
- organization rules come from runtime ingestion options and `config/core.json`

The page does not fabricate production counts. If a signal is not persisted yet, it is shown as unknown, unavailable, or not tracked yet.

## Live Refresh

The Dashboard subscribes to the existing SignalR Intercom connection and merges live `BatchProgress` and `IngestionProgress` events into the Active Ingestion section. It also polls the snapshot endpoint:

- about every 3 seconds while ingestion jobs are active
- about every 45 seconds while idle

The component disposes its polling cancellation token and SignalR state subscription when the page is left, so it does not keep duplicate refresh loops running.

## Source Folders

Configured folders are grouped by user intent:

- **Watch**: Movies and TV Shows
- **Listen**: Music, Audiobooks, and Podcasts
- **Read**: Books and Comics

Each logical library can contain multiple source paths through `source_paths` in `config/libraries.json`. The UI renders every path as its own row with item count, unresolved count, last scan, scan mode, purpose, reachability, and permission status.

Music is intentionally conservative. The page calls out that music should preserve album folders and prefer tags or fingerprints before organization. It does not present aggressive rename or move actions for music.

## Review Counts

Review reasons are grouped from pending `review_queue` triggers and root causes into product-facing buckets:

- Unmatched
- Low Confidence
- Duplicates
- Missing Artwork
- Missing Wikidata Identity
- Naming Issues
- Provider Failures
- Metadata Conflicts

Each bucket links to the Review Queue. Filtered review links are not enabled until the Review Queue supports URL filters.

## Provider Health

Provider health shows ingestion-relevant providers such as Apple Books, TMDB, MusicBrainz, Wikidata, Wikipedia, ComicVine, Google Books, Open Library, and any other configured metadata provider with ingestion capabilities.

Statuses are based on configuration and `provider_health` rows:

- Healthy
- Degraded
- Offline
- Disabled
- Missing Configuration
- Unknown

The page never displays API keys or secret values.

## Organization Rules

The Organization Rules card summarizes whether automatic rename/move behavior is enabled, whether preview is required, and which folder and filename templates are active. Library Operations does not trigger destructive file operations. It is a status and preview surface, not a file manager.

## Real Today vs. Not Yet Tracked

Real today:

- registered, provisional, and review lifecycle counts
- active batch records and live SignalR batch progress
- recent ingestion batches
- configured source folders, including multiple folders per library
- folder reachability and permission checks
- provider configuration and provider health records
- pipeline counts from durable identity jobs and ingestion logs
- review reason grouping from review queue records

Not yet fully tracked:

- per-folder item counts for folders that have never produced an ingestion batch
- last organization run timestamp
- provider rate-limit remaining values when providers do not persist them
- direct stage filtering from the Pipeline Health cards
