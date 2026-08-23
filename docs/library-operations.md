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

**Media Management**, Ingestion, Metadata Providers, Activity & Audit, and
Developer Tools are first-class Settings pages for the part of Tuvima Library
that turns folders into registered media. Media Management is available at
`/settings/media-management` and contains Overview, Incoming, Libraries, and
Activity. Ingestion operations are part of Media Management at
`/settings/media-management/activity`; the feature-gated development harness is
available at `/settings/developer/options`.

Together, these pages answer six operational questions:

- what is happening right now
- what has been processed recently
- what needs review
- which Read, Watch, Listen, View, and universal incoming folders are configured
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

The Ingestion page shows one active batch with one overall progress view and compact numbered stage rows. Rows report file progress against the batch total and show artifact counts as they are found or persisted. Each row can expand for details such as exact file counts, active or queued work, label accuracy, current item or group label, freshness, and server-provided `detail_items`.

| # | Stage | What It Means | Artifact Count |
| ---: | --- | --- | --- |
| 1 | Scan | Source folders are scanned and files are accepted, skipped, duplicated, or failed. | Accepted files |
| 2 | Read Details | Files are parsed for embedded metadata, duration, chapters, tracks, pages, and local artwork evidence. | Parsed files |
| 3 | Retail Match | Retail/catalog providers identify the item, add quick metadata, and provide first cover or poster evidence. | Provider matches |
| 4 | Wikidata | Bridge IDs from Stage 3 are resolved to canonical Wikidata QIDs where available. | QIDs |
| 5 | Ready | The item is visible or otherwise reaches a terminal file outcome. Retail-only retained items can complete here without waiting for a QID. | Added files |
| 6 | People | Authors, narrators, cast, crew, creators, and related people are linked or enriched. | People |
| 7 | Relationships | Series, albums, seasons, episodes, franchises, narrative roots, and other relationships are built. | Links |
| 8 | Artwork | Later artwork enrichment fetches backgrounds, banners, logos, disc art, album variants, season posters, and episode stills. | Assets |

Stage 3 intentionally combines retail lookup, quick metadata, and the first cover/poster pass because those signals arrive together from retail/catalog providers. Stage 8 is separate because rich artwork requires later bridge IDs or QIDs and can run after the item is already visible.

Stage 7 prefers explicit local, retail, or Wikidata series order values over Wikidata previous/next backlink consistency. If Book 1 and Book 2 both have clear series positions, the shelf can be correct even when Wikidata's public previous/next chain is incomplete. Those ordering warnings are stored as diagnostics for Activity and troubleshooting; they do not create Review Queue rows unless there is a local user-impacting conflict, such as multiple owned works claiming the same Wikidata identity.

Stage 7 also separates owned children from external manifest/count facts. Sparse Wikidata manifests can still report expected totals such as issue, volume, chapter, episode, or track counts, so the UI can show owned-versus-total without creating fake owned media rows. Existing library data is not rewritten by this rule; the corrected behavior is proven by fresh ingestion.

Stages 1 and 2 are sequential for each file. Stage 3 starts after Stage 2. Stage 4 starts per file as soon as Stage 3 has a retained retail identity and usable bridge data. Stage 6, Stage 7, and Stage 8 can run concurrently once their prerequisites exist, so their bars can move at the same time.

Review is not shown as a progress row. The top **Need Review** metric is the source of truth for the current pending review total, and it shows a batch delta such as `+15 this batch` when the latest batch created new review items. Recent batch rows repeat that review count beside matched, people, artwork, and metadata totals.

The right side of the page is a recent-batches panel. It pins the active batch to the top, shows status, timing, file totals, media-type chips, and artifact totals, and links each batch to `/settings/activity?batchId={batchId}` for the detailed activity view.

## Live Refresh

The Dashboard subscribes to the existing SignalR Intercom connection and uses live `BatchProgress` and `IngestionProgress` events to refresh the same snapshot model from `GET /ingestion/operations`. SignalR does not maintain a competing progress model in the page. The page also polls the snapshot endpoint:

- about every 3 seconds while ingestion jobs are active
- about every 45 seconds while idle

The component disposes its polling cancellation token and SignalR state subscription when the page is left, so it does not keep duplicate refresh loops running.

## Developer Tools

Developer Tools is an internal-tools, Administrator-only shortcut for repeatable wipe and ingestion validation. It calls the Engine's `/dev/*` endpoints directly, links to the Provider and Enrichment testers, and does not edit existing library records in place.

- **Clean synthetic ingestion** runs `/dev/full-test` with the generated-state wipe scope, media-type filters, and direct fixture scans.
- **Configured-source reingest** runs `/dev/reingest-library`, which resets generated database/cache state and scans configured source folders without deleting source files.
- **Validation report** runs `/dev/integration-test` with selectable stage depth and media-type filters.
- **Full source wipe** is exposed only as an explicitly unlocked dangerous option for disposable test source folders.

## Source Folders

Configured folders are grouped by user intent:

- **Watch**: Movies and TV Shows
- **Listen**: Music and audiobooks
- **Read**: Books and Comics
- **View**: Personal mixed media such as photos, short videos, documents, and audio notes

Each logical library contains stable `sources` in `config/libraries.json`. The UI renders every source with its role, management mode, access mode, intake role, reachability, and permission status. Existing library sources are read-only and indexed in place; only managed writable sources can participate in organization or writeback.

Music is intentionally conservative. The page calls out that music should preserve album folders and prefer tags or fingerprints before organization. It does not present aggressive rename or move actions for music.

## Universal Incoming

Media Management's **Incoming** section lists unassigned intake folders by
stable ID, path, purpose, default handling, and health. Files arriving there
retain that source identity while Tuvima classifies them. Routing selects a
destination only when exactly one library accepts incoming-folder intake and
the detected media type; ambiguous or unsupported files stop for attention
rather than being guessed into a library.

Direct browser, drag-and-drop, mobile, device, and API intake retains the
selected destination library ID through the pipeline. Users do not configure
or browse internal staging paths.

## Managed and Existing Sources

An **Existing library** source may be scanned and indexed, but Tuvima does not
move, rename, tag, overwrite, or delete its files. A **Managed by Tuvima** source
may participate in organization only when it is writable and its source and
library policies allow the requested operation. Primary destinations are
explicit stable source IDs; source array order has no meaning.

Changing a source from Existing to Managed changes future policy only. It does
not immediately mutate files. Administrators can request a reorganization
preview, review unchanged/moved/renamed/conflicting/unresolved/blocked counts,
and confirm the exact short-lived plan fingerprint. Execution revalidates every
item before moving it and reports partial failures for safe retry.

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

Provider health shows ingestion-relevant providers such as Apple Books, MusicBrainz, TMDB, Wikidata, Wikipedia, Comic Vine, Fanart.tv, LRCLIB, OpenSubtitles, and any other configured metadata provider with ingestion or enrichment capabilities. Disabled configs such as Open Library are shown as disabled rather than active sources.

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
