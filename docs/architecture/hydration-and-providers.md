---
title: "Hydration Pipeline, Provider Architecture and Enrichment Strategy"
summary: "Deep technical documentation for provider adapters, waterfalling, and Wikidata bridge resolution."
audience: "developer"
category: "architecture"
product_area: "providers"
tags:
  - "providers"
  - "hydration"
  - "wikidata"
---

# Hydration Pipeline, Provider Architecture & Enrichment Strategy

This document describes how Tuvima Library discovers metadata for ingested media files: the provider architecture, the two-stage hydration pipeline, two-pass enrichment, provider response caching, and the review queue data model.

---

## 1. Provider Authority Model

Wikidata is the sole identity authority. Every media item is identified by its Wikidata Q-identifier (QID). Without a confirmed QID, an item does not have a verified identity and will not be promoted from staging into the organised library.

All providers divide cleanly into two categories:

**Wikidata + Wikipedia** â€” the sole sources for canonical structured data: titles, authors, series relationships, franchise links, fictional entities, person biographies, and all bridge identifiers. The Wikidata Reconciliation client (`Tuvima.WikidataReconciliation`) handles QID resolution via the OpenRefine Reconciliation API, property fetching via the Data Extension API, and Wikipedia summaries via `GetWikipediaSummariesAsync`.

**Retail providers** â€” exist solely to supply matching data that aids identity resolution, plus media assets that Wikidata cannot host. Their output is never treated as canonical structured data. Retail providers contribute:
- Cover art and promotional imagery (copyright-safe sources that Wikimedia cannot host)
- Descriptions and ratings (for display and candidate ranking)
- Bridge identifiers: ISBN, ASIN, TMDB ID, Apple Books ID, MusicBrainz ID, Comic Vine ID, Apple Podcasts ID â€” these are used by Stage 2 to resolve the QID precisely

The distinction matters for trust: a title or author name from Apple API is a hint used to rank candidates, not a fact stored as canonical data. Only Wikidata-sourced claims become canonical values.

### Provider Inventory

**Zero-key providers (no API key required):**

| Provider | Media Types | What it contributes |
|---|---|---|
| Apple API | Books, Audiobooks, Podcasts | Cover art (up to 3000Ã—3000 via the 9999 trick), description, rating, Apple Books ID / Apple Podcasts ID |
| Open Library | Books | Cover art, ISBN, bridge IDs |
| MusicBrainz | Music | Cover Art Archive images, MusicBrainz ID (MBID) |
| Apple Podcasts | Podcasts | Cover art (up to 3000Ã—3000), Apple Podcasts ID |
| Wikidata / Wikidata Reconciliation | All | QID, all structured properties, Wikipedia descriptions, person headshots (P18, persons only) |

**Key-required providers (free API key):**

| Provider | Media Types | What it contributes |
|---|---|---|
| TMDB | Movies, TV | Cover art (up to 2000×3000 at w500/w1280), TMDB ID, IMDb ID, network (TV only) |
| Google Books | Books, Audiobooks | Cover art, ISBN, Google Books ID |
| Comic Vine | Comics | Cover art (super_url, ~900px), Comic Vine ID |
| Podcast Index | Podcasts | Episode metadata, Podcast Index GUID |

**Copyright constraint â€” P18 (Image):** Wikidata P18 is exclusively for Person entities (author/director headshots from Wikimedia Commons). P18 is never fetched for media items. Media cover art comes exclusively from retail providers.

---

## 2. Provider Configuration Architecture

All provider behaviour is declared in JSON config files under `config/providers/`. There are no individual adapter classes for REST+JSON providers â€” they all run through a single `ConfigDrivenAdapter`. Adding a new REST+JSON provider is a zero-code operation: drop a config file and restart.

Each provider config file declares:

```
adapter_type          "config_driven" â€” routes to ConfigDrivenAdapter; "reconciliation" â€” ReconciliationAdapter
provider_id           Stable GUID used as the FK value in metadata_claims rows
hydration_stages      Array: [1] = RetailIdentification, [2] = WikidataBridge
cache_ttl_hours       How long raw API responses are cached in provider_response_cache
throttle_ms           Minimum delay between calls to this provider
max_concurrency       Maximum concurrent calls
can_handle            media_types[] scoping â€” which media types this provider serves
search_strategies     Ordered list of URL templates with required_fields and media_type scoping
field_mappings        JSON path extraction rules with named transforms, confidence values, media_type scoping
```

**Media-type scoping on strategies and field mappings:** A single provider config can serve multiple media types. Individual `search_strategies` and `field_mappings` entries carry an optional `media_types` array. When a request includes a media type, only matching entries are used. Entries with no `media_types` array are universal. `MediaType.Unknown` acts as a wildcard.

**ReconciliationAdapter** uses the `Tuvima.WikidataReconciliation` NuGet package. Its configuration lives in `config/providers/wikidata_reconciliation.json` (provider scoring config: weight, field weights, throttle, enabled) and `config/universe/wikidata.json` (knowledge model: property map, bridge lookup order, value transforms, instance_of class mappings, scope exclusions).

**ValueTransformRegistry** provides named transform functions applied to raw API values: `to_string`, `strip_html`, `url_template`, `regex_replace`, `prefer_isbn13`, `array_join`, `array_nested_join`, `first_n_chars`, `fallback_key`, `title_case`. Transform assignment lives in config; transform implementations live in code.

**Required-field short-circuits:** Each search strategy declares `required_fields`. If a required field is missing from the request, the strategy is skipped with no HTTP call made.

**Metron title path validation:** `ConfigDrivenAdapter` validates a response before accepting it by checking that at least one recognised title field is non-empty. The recognised title field names for Metron are: `name`, `title`, `issue`, `series`, `volumeName`. The minimum F1 score for a title-only match (when no other metadata fields are present) is 0.40 — lowered from 0.80 to accommodate Metron's sparser response structure for single-issue lookups.

---

## 3. Two-Stage Hydration Pipeline

### Overview

When a media file is ingested, the hydration pipeline runs in two sequential stages. Stage 1 gathers matching assets from retail providers. Stage 2 uses the bridge IDs deposited by Stage 1 for precise Wikidata identity resolution.

```
File ingested
     â”‚
     â–¼
Stage 1: RetailIdentification
  â”œâ”€ Retail providers run in waterfall order (config/slots.json)
  â”œâ”€ Deposit: cover art, descriptions, ratings, bridge IDs
  â””â”€ Result: cover.jpg on disk, bridge IDs in metadata_claims
     â”‚
     â–¼
Stage 2: WikidataBridge
  â”œâ”€ ReconciliationAdapter uses bridge IDs for edition-first QID resolution
  â”œâ”€ Fallback: work-level title search if no bridge IDs matched
  â”œâ”€ Data Extension API fetches configured properties
  â”œâ”€ Wikipedia descriptions via GetWikipediaSummariesAsync
  â””â”€ On failure: AuthorityMatchFailed review item created
     â”‚
     â–¼
Post-pipeline confidence check
  â”œâ”€ Reload canonical values, compute overall confidence
  â””â”€ If below auto_review_confidence_threshold (0.60): LowConfidence review item created
```

### Stage 1 â€” RetailIdentification

Retail providers run in waterfall order defined in `config/slots.json`. Each slot has a Primary, Secondary, and Tertiary provider per media type. The first provider that returns a result for each field wins; later providers are not called for that field.

Providers participate in Stage 1 by declaring `"hydration_stages": [1]` in their config.

**Retail match confidence gate:** After each provider returns claims, `RetailMatchScoringService` scores the candidate against file metadata (title 45%, author 35%, year 10%, format 10% + cross-field boosts). Scores below 0.50 (ambiguous threshold) are discarded; scores between 0.50–0.85 are accepted with a review flag; scores ≥ 0.85 are auto-accepted. This uses the same unified scoring as manual search from the Vault detail drawer.

Stage 1 never waits on Stage 2. Cover art is written to disk during Stage 1 (`cover.jpg` alongside the staged file). If Stage 1 fails to find any matching provider, the item routes directly to the review queue — **no text-only Wikidata fallback is attempted**. The principle: no retail match = no Wikidata.

### Stage 2 â€” WikidataBridge

The `ReconciliationAdapter` runs second, using bridge IDs deposited by Stage 1 for precise QID resolution:

1. **Bridge ID lookup (edition-first):** The adapter searches for editions matching the deposited bridge IDs (ISBN, ASIN, TMDB ID, etc.). Audiobook editions get audiobook-edition ISBNs; book editions get print ISBNs. This is filtered by P31 (instance_of) to ensure the returned QID matches the right media type.
2. **Text fallback (gated):** If all bridge ID lookups fail, a text-based reconciliation with type filtering is attempted — but **only when at least one real bridge ID lookup was attempted**. If the call contains only sentinel keys (`_title`, `_author`) with no real bridge IDs, text fallback is blocked and the item returns `NotFound` (routes to review).
3. **Auto-accept:** Score ≥ 95 and `match: true` → QID accepted automatically.
4. **Multiple candidates:** Multiple candidates without auto-accept â†’ `MultipleQidMatches` review item. Conservative matching â€” no auto-accept when ambiguous.
5. **Data Extension fetch:** After QID confirmation, a single Data Extension API POST fetches all configured properties from `config/universe/wikidata.json`.
6. **Wikipedia descriptions:** Fetched via `_reconciler.GetWikipediaSummariesAsync` as part of Stage 2.

Providers participate in Stage 2 by declaring `"hydration_stages": [2]`. Currently only `ReconciliationAdapter` participates in Stage 2.

**Pipeline continuation on failure:** If Stage 2 fails to resolve a QID and `continue_pipeline_on_authority_failure` is `true`, the pipeline continues (the file retains its Stage 1 metadata). An `AuthorityMatchFailed` review item is created for manual resolution.

### Slot Assignments (config/slots.json)

| Media Type | Primary | Secondary | Tertiary | Bridge to Wikidata |
|---|---|---|---|---|
| Books | Apple API | Google Books | Open Library | ISBN (P212), Apple Books ID (P6395) |
| Audiobooks | Apple API | Google Books | â€” | ASIN, Apple Books ID (P6395) |
| Movies | TMDB | â€” | â€” | TMDB ID (P4947), IMDb ID (P345) |
| TV | TMDB | â€” | â€” | TMDB TV ID (P4983), IMDb ID (P345) |
| Comics | Comic Vine | â€” | â€” | Comic Vine ID (P5905) |
| Music | MusicBrainz | â€” | â€” | MusicBrainz ID (P434/P436) |
| Podcasts | Apple Podcasts | Podcast Index | â€” | Apple Podcasts ID (P5842) |

### Pipeline Configuration (config/hydration.json)

```json
{
  "stage_concurrency": 3,
  "stage1_timeout_seconds": 45,
  "stage2_timeout_seconds": 30,
  "disambiguation_threshold": 0.7,
  "auto_review_confidence_threshold": 0.60,
  "max_qid_candidates": 5,
  "continue_pipeline_on_authority_failure": true,
  "universe_title_search_auto_accept": 0.80,
  "stage2_waterfall_confidence_threshold": 0.65
}
```

### Dual-Path Architecture

The pipeline maintains two separate processing paths that are safe to run concurrently:

- **`HydrationPipelineService`** handles `MediaAsset`-type requests using the two-stage pipeline (Stage 1 retail â†’ Stage 2 Wikidata).
- **`MetadataHarvestingService`** handles `Person`-type requests from `RecursiveIdentityService`, running Wikidata enrichment directly without the retail Stage 1.

Person creation is idempotent â€” both paths can run simultaneously without conflict.

---

## 4. Two-Pass Enrichment Architecture

The two-stage pipeline runs twice, at different times, to different depths. This separation ensures files appear on the Dashboard within seconds while the deeper universe intelligence work runs in the background when the system is idle.

### Pass 1 â€” Quick Match (immediate, during ingestion)

Pass 1 runs as part of normal ingestion and executes a shallow version of both stages:

- **Stage 1 (core subset):** Retail providers gather cover art, descriptions, and bridge IDs.
- **Stage 2 (core subset):** Wikidata QID resolved from bridge IDs. Core properties only fetched: title, author/artist, year, genre, series, series_position. Wikipedia descriptions fetched. The full 50+ property Data Extension deep hydration is skipped.
- **Basic person creation:** Author, narrator, and director Person records are created with name, headshot, and occupation. Social links and biographical details are deferred to Pass 2.

Result: the file appears on the Dashboard within seconds with title, author, cover art, and author photo.

### Pass 2 â€” Universe Lookup (deferred, background)

Pass 2 runs in the background and handles everything that makes the library intelligent:

- Full Data Extension deep hydration â€” all 50+ properties from `config/universe/wikidata.json`
- Hub Intelligence â€” franchise resolution, narrative root assignment (P1434, P8345, P179)
- Fictional entity discovery â€” characters, locations, organisations
- Relationship population â€” father, spouse, member_of, performer links (depth limit configurable via `lineage_depth`, default 2)
- Deep person enrichment â€” social links (Instagram, TikTok, Mastodon, website), biographical details (birth/death dates, nationality), pseudonym resolution (P1773/P742)
- Character-performer links â€” which actor played which character in each adaptation
- Universe graph population â€” fictional entities, relationships, and narrative roots written to SQLite

**Recursive enrichment in Pass 2:** When Pass 2 discovers a new connection â€” a pen name, an actor who played a character from a book, a director's other works â€” it enriches those people too. This recursive chain only runs in Pass 2 to avoid load during initial ingestion. Pass 1 creates Person records; Pass 2 follows the web.

### Scheduling

Three mechanisms ensure all files eventually receive Pass 2 enrichment:

1. **Priority queue (primary):** Pass 2 requests go onto a low-priority background channel. When the ingestion pipeline is idle (no Pass 1 work pending), the service picks up Pass 2 requests with a configurable rate limit (default 2-second gap between Reconciliation calls). New file arrivals preempt Pass 2 work.

2. **Nightly sweep (safety net):** A configurable cron job scans for Pass 2 requests older than `pass2_stale_threshold_hours` that the queue has not yet processed. Runs in batches with inter-batch delay.

3. **User-triggered override:** The Hydrate button in the Dashboard runs both passes synchronously via `RunSynchronousAsync`, bypassing the queue entirely for immediate results.

4. **On-demand deep enrichment:** `POST /universe/entity/{qid}/deep-enrich` â€” triggered when a user navigates to an un-enriched entity in the Chronicle Explorer. Enqueues via `IMetadataHarvestingService`. Depth capped at 3. Returns within 2â€“3 seconds.

### Two-Pass Configuration (config/hydration.json additions)

```json
{
  "two_pass_enabled": true,
  "pass1_core_properties_only": true,
  "pass2_idle_delay_seconds": 10,
  "pass2_rate_limit_ms": 2000,
  "pass2_nightly_cron": "0 2 * * *",
  "pass2_stale_threshold_hours": 24,
  "pass2_batch_size": 50
}
```

---

## 5. Provider Response Caching

The `provider_response_cache` table stores raw JSON responses from metadata provider API calls. This eliminates redundant requests when multiple files share the same entity â€” TV episodes from one series, album tracks, comic issues from one volume.

### How It Works

Before making an HTTP call, `ConfigDrivenAdapter` computes a SHA-256 hash of the full request URL. It checks `provider_response_cache` for a non-expired entry:

- **Cache hit (not expired):** Returns cached response. No HTTP call made.
- **Cache hit (expired, has ETag):** Sends `If-None-Match` header. HTTP 304 Not Modified â†’ reuses cached response, refreshes expiry.
- **Cache miss:** Makes HTTP call, writes response to cache with per-provider TTL.

### Per-Provider TTL Defaults

| Provider | TTL | Rationale |
|---|---|---|
| Apple API | 168 hours (7 days) | Retail data changes infrequently |
| TMDB | 168 hours (7 days) | Retail data changes infrequently |
| Open Library | 336 hours (14 days) | Bibliographic data is stable |
| Google Books | 168 hours (7 days) | Retail data changes infrequently |
| MusicBrainz | 336 hours (14 days) | Discography data is stable |
| Comic Vine | 720 hours (30 days) | Strict rate limits â€” aggressive caching |

TTL is configured per-provider via `cache_ttl_hours` in each provider config file.

### Scope

The response cache is a performance optimisation only. It is not part of the data model. On a fresh install or database rebuild, the cache starts empty and repopulates naturally during re-hydration. Canonical values are always rebuilt from file re-ingestion and batch Reconciliation API calls â€” never from the response cache.

### Rate Limit Context

| Provider | Rate Limit | 10,000 files (uncached) | With Cache |
|---|---|---|---|
| Apple API / Podcasts | ~20 req/sec | ~33 min | ~5 min |
| TMDB | 50 req/sec | ~42 min | ~5 min |
| MusicBrainz | 1 req/sec | ~3 hours | ~15 min |
| Comic Vine | 200 req/hour | ~14 hours | ~2 hours |
| Wikidata Reconciliation | ~5 req/sec | ~83 min | ~20 min |

---

## 6. Description Signal Extraction

When a file's metadata is missing the narrator, translator, or illustrator â€” or when Wikidata resolves to the work level instead of a specific edition â€” the Engine mines retail provider descriptions for person names. "Read by Scott Brick" in an Apple API description becomes a narrator claim after Wikidata verification.

### Two Purposes

**Candidate ranking improvement (inline, during Stage 1):** Person names are extracted from each candidate's description and compared name-to-name against hints in the file's embedded metadata. A matching name boosts the candidate's score; a mismatch penalises it. This is more precise than fuzzy-matching the name against the full description paragraph.

**Person record creation (background, after Stage 1):** After Stage 1 selects a winning candidate, all person names are extracted from the description, validated (minimum 2 words, uppercase start, not in stop list), and queued as pending signals. A background worker batch-verifies them against Wikidata: searching for each unique name, fetching P31 (is human?) and P106 (occupation), and confirming the person works in the right field for the extracted role.

### Extraction Rules (config/signal_extraction.json)

Extraction rules are configured per media type with regex patterns, role assignments, and Wikidata occupation classes for verification:

| Media Type | Extracted Roles | Example Patterns |
|---|---|---|
| Audiobooks | Narrator | "Read by", "Narrated by", "Performed by" |
| Books | Translator, Editor, Illustrator, Author (foreword) | "Translated by", "Edited by", "Illustrated by", "Foreword by" |
| Movies | Director, Cast Member, Producer | "Directed by", "Starring", "Produced by" |
| TV | Director, Cast Member | "Directed by", "Starring" |
| Comics | Author, Illustrator | "Written by", "Art by", "Pencils by" |
| Podcasts | Host | "Hosted by", "Presented by" |
| Music | Producer, Featured Artist | "Produced by", "feat." |

Each extraction rule carries Wikidata occupation class Q-identifiers used to confirm the person works in the right role. For example, the Narrator role verifies against Q1622272 (narrator), Q33999 (actor), and Q2405480 (voice actor).

### Confidence Tiers

| Verification result | Confidence |
|---|---|
| Extracted from description, unverified | 0.60 |
| Extracted from file metadata, unverified | 0.75 |
| QID found + occupation matches role | 0.85 |
| QID found + human but no matching occupation | 0.65 |
| QID found but not human, or no match | Discarded |

### Batch Processing Architecture

Inline extraction runs during hydration with zero API calls â€” pure regex plus name validation. All Wikidata verification is deferred to a background worker (`PersonSignalVerificationWorker`) that polls every 5 minutes, deduplicates names across entities, and batch-verifies in a single `wbgetentities` call. For 500 audiobooks sharing 30 unique narrators: 30 search calls plus 1 batch properties call.

---

## 7. Recursive Person Enrichment

### 7.1 Person Role Extraction

The Engine extracts person roles from both structured Wikidata properties and file metadata across all media types:

| Wikidata Property | Role | Media Types |
|---|---|---|
| P50 | Author | Books, Audiobooks, Comics |
| P57 | Director | Movies, TV |
| P58 | Screenwriter | Movies, TV |
| P86 | Composer | Movies, TV, Music |
| P110 | Illustrator | Books, Comics |
| P161 | Cast Member | Movies, TV (capped at 20 per work) |
| P175 | Narrator | Audiobooks (via edition resolution) |

**Media-type-aware Performer mapping:** The generic `Performer` role from file tags is mapped to a more specific role based on media type before person records are created:

| Media Type | Performer maps to |
|---|---|
| Music | Performer |
| Audiobooks | Narrator |
| TV, Movies | Actor |

This prevents audiobook narrator names from being stored with the generic Performer role, which would cause them to appear under the Musicians filter in the People tab rather than under Authors.

These are fetched during Stage 2 (WikidataBridge) via the `work_properties.core` config. Each property emits both a name claim (e.g. `director`) and a companion QID claim (e.g. `director_qid`) at confidence 0.90.

### 7.2 QID-First Person Creation

Person records are only created when a Wikidata QID is confirmed. The pipeline:

1. Extract person references from raw claims â€” pairing name claims with companion QID claims by index.
2. Apply the QID-first gate: only references with a confirmed QID proceed.
3. Look up or create a Person record (QID-first via `FindByQidAsync`).
4. Link the Person to the media asset (INSERT OR IGNORE â€” idempotent).
5. Add the role to the `person_roles` junction table (idempotent â€” one person can be Director on Film A, Cast Member on Film B).
6. If the Person has not been enriched (or enrichment is stale >30 days), return a harvest request.

### 7.3 Standalone Person Reconciliation

After Stage 2, some person names from file metadata remain unlinked â€” e.g. a narrator from an M4B file when Wikidata has no audiobook edition, or a director from video tags when the work QID has no P57 data.

**`PersonReconciliationService`** resolves these via standalone Wikidata search:

1. Search `wbsearchentities` for the person name, limit 10 candidates.
2. Fetch P31 (instance_of), P106 (occupation), P800 (notable_work) for each candidate.
3. Filter: must be Q5 (human).
4. Score: name similarity (0.50 weight) + occupation match (+0.20 if P106 matches expected role) + notable work match (+0.10 if P800 fuzzy-matches the work title).
5. Auto-accept at score â‰¥ 0.80. Deposit companion QID claim at confidence 0.80.
6. Auto-skip below threshold. Retry at next 30-day refresh cycle.

**Three-tier confidence model:**
- Tier 1 (0.90): Structured Wikidata properties (P50, P57, P161, P175)
- Tier 2 (0.80): Standalone person search with occupation match
- Tier 3 (0.75): AI description extraction fallback

### 7.4 AI Person Signal Fallback

The Description Intelligence batch service (LLM-powered) extracts people and roles from text descriptions. When a person is mentioned in a description but no QID exists from higher-tier sources, the batch service feeds the name into `PersonReconciliationService` at confidence 0.75. This only fires when:
- The AI extraction confidence is â‰¥ 0.50
- No QID claim already exists for that role from Tier 1 or Tier 2

### 7.4a Person Headshot Download Logging

When the Engine downloads a headshot for a Person record during Stage 2 enrichment, it logs the outcome at `Information` level. Both successful downloads and skip conditions (file already present, no P18 value on the Wikidata entity) are logged so headshot coverage can be audited in the activity log.

### 7.5 Person Data Freshness

To avoid redundant Wikidata API calls when the same person appears across multiple works (e.g. Tom Hanks in 15 movies):

- **Fresh (â‰¤30 days):** Person already enriched â†’ just link to new media asset, skip re-fetch. Zero API calls.
- **Stale (>30 days):** Check `last_revision_id` against Wikidata entity revision. If unchanged, skip full fetch. If changed, re-fetch all properties.
- **New:** Full property fetch and enrichment.

`last_revision_id` is stored on the Person record (migration M-065) and passed as a hint in harvest requests.

### 7.6 Pseudonym Resolution

After Wikidata enrichment, P1773 (attributed_to) links pen names to real people; P742 (pseudonym) links real people to their pen names. Both directions are stored in the `person_aliases` table.

### 7.7 Actor-Character Mapping

For works with cast members, the pipeline fetches P161 (cast member) statements with P453 (character role) qualifiers from the work's QID. For each actor-character pair, a Person record is created for the actor and linked to the FictionalEntity for the character.

---

## 8. Bridge ID Normalization

Identifiers flow between Wikidata (dashed ISBNs, mixed-case ASINs, full IMDb URLs) and retail providers (bare digit strings, uppercase codes). `IdentifierNormalizationService` normalizes 12 identifier types across three directions:

| Direction | Method | Purpose |
|---|---|---|
| `NormalizeRaw` | Cleans up from any source | Input normalization; includes ISBN-13 Mod10 checksum validation |
| `ToWikidataFormat` | Converts to Wikidata's expected format | Used when writing claims or comparing against Wikidata values |
| `ToRetailFormat` | Strips to bare form | Used when calling retail provider APIs |

**Supported identifier types:** ISBN-13, ISBN-10, ASIN, IMDb, Apple Books ID, TMDB, MusicBrainz, Goodreads, ComicVine, ISRC, LCCN, Apple Podcasts.

**Key aliases:** `isbn_13` â†’ `isbn`, `isbn_10` â†’ `isbn` (provided by `GetClaimKeyAlias`).

**Edition bridge ID filtering:** When `ReconciliationAdapter` resolves editions, it filters by P31 (instance_of) to ensure the correct edition type is matched â€” audiobooks get audiobook-edition ISBNs, books get print ISBNs.

---

## 9. Review Queue Data Model

The review queue surfaces items that need human attention. The Dashboard interaction layer (Vault page, VaultResolutionOverlay) is described in the UI architecture document. This section covers the data model and API.

### Review Item Types

| Trigger | Cause |
|---|---|
| `AuthorityMatchFailed` | Stage 2 (Wikidata) failed to resolve a QID â€” no match found |
| `LowConfidence` | Pipeline completed but overall confidence fell below `auto_review_confidence_threshold` (0.60) |
| `MultipleQidMatches` | Stage 2 found multiple Wikidata candidates; user must pick one |
| `UserFixMatch` | User manually flagged an item for re-review |
| `ArbiterNeedsReview` | Hub Arbiter flagged an uncertain Hub assignment |
| `AmbiguousMediaType` | Media type disambiguation could not determine the content type with sufficient confidence |

Each review item carries: entity reference, trigger reason, confidence score, optional disambiguation candidates (JSON array of `{ qid, label, description }`), and a human-readable detail string.

### Resolution Flow

1. User opens the Vault page in Settings â†’ Metadata section.
2. Selects a review item â†’ sees current metadata versus proposed match.
3. For `MultipleQidMatches`: picks a QID candidate from a card grid.
4. Clicks Resolve â†’ `POST /review/{id}/resolve` fires.
5. Engine creates user-locked claims for any field overrides.
6. If a QID was selected â†’ Stage 2 (WikidataBridge) re-runs synchronously with the pre-resolved QID.
7. Activity ledger records `ReviewItemResolved`.
8. SignalR broadcasts `ReviewItemResolved` â†’ review badge count decrements.

### API Endpoints

| Method | Route | Auth |
|---|---|---|
| `GET` | `/review/pending?limit=50` | Admin, Curator |
| `GET` | `/review/{id}` | Admin, Curator |
| `GET` | `/review/count` | Admin, Curator |
| `POST` | `/review/{id}/resolve` | Admin, Curator |
| `POST` | `/review/{id}/dismiss` | Admin, Curator |

The review count is used in two places in the Dashboard: the notification bell badge in the TopBar and the profile avatar badge in the AppBar. Both are kept current via SignalR `ReviewItemCreated` and `ReviewItemResolved` events.

---

## 10. Artwork Quality Strategy

Cover art is never stored in the database. `cover.jpg` lives alongside the media file on disk and is always read from there. Art is sourced exclusively from retail providers â€” Wikidata P18 is reserved for Person headshots only.

| Media Type | Primary Art Source | Max Resolution | Notes |
|---|---|---|---|
| Books & Audiobooks | Apple API | Up to 3000Ã—3000 | 9999 trick in URL template |
| Movies & TV | TMDB | Up to 2000Ã—3000 | Backdrop available at w1280 |
| Comics | Comic Vine | ~900px | `super_url` field |
| Music | Cover Art Archive (MusicBrainz) | 500px | `front-500` path |
| Podcasts | Apple Podcasts | Up to 3000Ã—3000 | Same 9999 trick as Apple API |

**Cover art timing:** `cover.jpg` is written alongside the file in `.staging/` during Stage 1. Hero banner generation (SkiaSharp blur + vignette + grain) happens later when `AutoOrganizeService` promotes the file from staging to the organised library.

**Image hash validation:** Cover art and provider thumbnails are tracked by content hash (SHA-256) in the `image_cache` table to prevent redundant re-downloads. When the same image URL appears across multiple entities, the hash is checked first; if found, the cached file path is reused.

## 11. Ranked Pipeline System

Stage 1 retail identification now supports unlimited ranked providers per media type, replacing the fixed 3-slot waterfall system.

### Execution Strategies

| Strategy | Behaviour | Default for |
|---|---|---|
| **Waterfall** | First provider to return a match wins; remaining providers are skipped | Movies, TV, Comics |
| **Cascade** | All providers run independently; their claims are merged | Books, Podcasts |
| **Sequential** | Providers run in order; each passes its bridge IDs to the next | Audiobooks, Music |

### Configuration

Pipeline configuration lives in `config/pipelines.json`:

```json
{
  "pipelines": {
    "Audiobooks": {
      "strategy": "Sequential",
      "providers": [
        { "rank": 1, "name": "musicbrainz" },
        { "rank": 2, "name": "apple_api" }
      ]
    }
  }
}
```

Falls back to `slots.json` auto-conversion via `PipelineConfiguration.FromLegacySlots()`.

### Sequential Bridge ID Passing

In Sequential mode, `PriorProviderBridgeIds` on `ProviderLookupRequest` carries bridge IDs from Provider A to Provider B. `ConfigDrivenAdapter.ResolveRequestField` checks these before falling back to the original request properties.

### Key Files

- `src/MediaEngine.Domain/Enums/ProviderStrategy.cs` — Waterfall, Cascade, Sequential enum
- `src/MediaEngine.Storage/Models/PipelineConfiguration.cs` — Pipeline config model + legacy converter
- `src/MediaEngine.Domain/Constants/MediaTypeFieldRegistry.cs` — Fields, search display, searchable fields per media type
- `src/MediaEngine.Providers/Services/HydrationPipelineService.cs` — Strategy execution loop
- `src/MediaEngine.Intelligence/PriorityCascadeEngine.cs` — Tier B reads per-media-type field priorities
- `config/pipelines.json` — Pipeline configuration

---

## Related

- [How Two-Stage Enrichment Works](../explanation/how-hydration-works.md)
- [Providers Reference](../reference/providers.md)
- [How to Add a New Metadata Provider](../guides/adding-a-provider.md)
