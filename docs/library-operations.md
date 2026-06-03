---
title: "Library Admin Pages"
summary: "Understand the Settings pages for libraries, ingestion, providers, activity, review, source folders, provider health, and recent batches."
audience: "user"
category: "guide"
product_area: "ingestion"
tags:
  - "ingestion-dashboard"
  - "ingestion"
  - "review"
---

# Library Admin Pages

Libraries, Ingestion, Providers, Activity, and the temporary Test Harness are first-class Settings pages for the part of Tuvima Library that turns messy folders into registered media. They are available directly in the Settings left navigation under **Admin Settings**, without a secondary tab bar. The Ingestion page remains available at `/settings/ingestion`; the temporary development harness is available at `/settings/dev-harness`.

Together, these pages answer six operational questions:

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
- numbered stage rows and artifact counts come from persisted batch, job, metadata, artwork, people, relationship, and review state
- review reason counts come from pending `review_queue` rows
- source folders come from `config/libraries.json`
- folder reachability is checked from the filesystem at snapshot time
- provider health comes from configured providers plus `provider_health`
- organization rules come from runtime ingestion options and `config/core.json`

The page does not fabricate production counts. If a signal is not persisted yet, it is shown as unknown, unavailable, or not tracked yet. Grouped provider calls, including batched `Tuvima.Wikidata` lookups, may show a group label such as `Resolving Wikidata batch: 12 files` instead of a specific file name until the backend has an exact per-file result.

## Ingestion Stages

The Ingestion page shows one active batch with one overall progress view and numbered stage bars. Stage bars report file progress against the batch total and show artifact counts as they are found or persisted.

| # | Stage | What It Means | Artifact Count |
| ---: | --- | --- | --- |
| 1 | Scan folders | Source folders are being scanned and files are being discovered. | Files found |
| 2 | Read media details | Files are parsed for embedded metadata, duration, chapters, tracks, pages, and local artwork evidence. | Files read |
| 3 | Retail metadata & primary artwork | Retail/catalog providers identify the item, add quick metadata, and provide the first cover or poster evidence. | Provider matches, metadata fields, cover/poster URLs, stored primary covers |
| 4 | Wikidata lookup | Bridge IDs from Stage 3 are resolved to canonical Wikidata QIDs where available. | QIDs resolved |
| 5 | File ready | The item is visible or otherwise reaches a terminal file outcome. Retail-only retained items can complete here without waiting for a QID. | Files added |
| 6 | People & cast | Authors, narrators, cast, crew, creators, and related people are linked or enriched. | People linked/resolved |
| 7 | Series & relationships | Series, albums, seasons, episodes, franchises, narrative roots, and other relationships are built. | Relationships, series, child items |
| 8 | Deep artwork | Later artwork enrichment fetches backgrounds, banners, logos, disc art, album variants, season posters, and episode stills. | Deep artwork assets |
| 9 | Review / attention | Live exception count collected from any stage that needs a curator decision. | Items needing review |

Stage 3 intentionally combines retail lookup, quick metadata, and the first cover/poster pass because those signals arrive together from retail/catalog providers. Stage 8 is separate because rich artwork requires later bridge IDs or QIDs and can run after the item is already visible.

Stages 1 and 2 are sequential for each file. Stage 3 starts after Stage 2. Stage 4 starts per file as soon as Stage 3 has a retained retail identity and usable bridge data. Stage 6, Stage 7, and Stage 8 can run concurrently once their prerequisites exist, so their bars can move at the same time.

## Live Refresh

The Dashboard subscribes to the existing SignalR Intercom connection and uses live `BatchProgress` and `IngestionProgress` events to refresh the same snapshot model from `GET /ingestion/operations`. SignalR does not maintain a competing progress model in the page. The page also polls the snapshot endpoint:

- about every 3 seconds while ingestion jobs are active
- about every 45 seconds while idle

The component disposes its polling cancellation token and SignalR state subscription when the page is left, so it does not keep duplicate refresh loops running.

## Temporary Test Harness

The Test Harness page is a development-only admin shortcut for repeatable wipe and ingestion validation. It calls the Engine's `/dev/*` endpoints directly and does not edit existing library records in place.

- **Clean synthetic ingestion** runs `/dev/full-test` with the generated-state wipe scope, media-type filters, and direct fixture scans.
- **Configured-source reingest** runs `/dev/reingest-library`, which resets generated database/cache state and scans configured source folders without deleting source files.
- **Validation report** runs `/dev/integration-test` with selectable stage depth and media-type filters.
- **Full source wipe** is exposed only as an explicitly unlocked dangerous option for disposable test source folders.

## Source Folders

Configured folders are grouped by user intent:

- **Watch**: Movies and TV Shows
- **Listen**: Music and audiobooks
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

Provider health shows ingestion-relevant providers such as Apple Books, TMDB, Wikidata, Wikipedia, Comic Vine, Fanart.tv, LRCLIB, OpenSubtitles, and any other configured metadata provider with ingestion or enrichment capabilities. Disabled configs such as Open Library and MusicBrainz are shown as disabled rather than active sources.

Statuses are based on configuration and `provider_health` rows:

- Healthy
- Degraded
- Offline
- Disabled
- Missing Configuration
- Unknown

The page never displays API keys or secret values. Provider secrets are config-file data: base provider definitions live under `config/providers/*.json`, and long-lived credentials should live under `config/secrets/{provider}.json` so blank base provider files do not imply that keys were deleted.

## Organization Rules

The Organization Rules card summarizes whether automatic rename/move behavior is enabled, whether preview is required, and which folder and filename templates are active. Ingestion does not trigger destructive file operations. It is a status and preview surface, not a file manager.

## Real Today vs. Not Yet Tracked

Real today:

- registered, provisional, and review lifecycle counts
- active batch records and live SignalR batch progress
- recent ingestion batches
- configured source folders, including multiple folders per library
- folder reachability and permission checks
- provider configuration and provider health records
- pipeline counts from durable identity jobs and ingestion logs
- numbered stage progress from `stage_progress` in `GET /ingestion/operations`
- batch artifact ledger rows in `ingestion_batch_artifacts` for later Activity page rollups
- review reason grouping from review queue records

Not yet fully tracked:

- per-folder item counts for folders that have never produced an ingestion batch
- last organization run timestamp
- provider rate-limit remaining values when providers do not persist them
- direct stage filtering from the Pipeline Health cards
