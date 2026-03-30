# Database Schema Reference

SQLite database located at `.data/database/library.db` (path set in `config/core.json`).

Latest migration: **M-068**. All migrations are idempotent (`IF NOT EXISTS` guards). Schema changes are applied automatically on Engine startup.

**Conventions:**
- UUIDs stored as `TEXT`
- Booleans stored as `INTEGER` (`0` = false, `1` = true)
- Timestamps stored as `TEXT` in ISO-8601 format (`2026-03-29T14:00:00Z`)
- Foreign keys enabled via `PRAGMA foreign_keys = ON`

---

## Core Media

### hubs

Represents a Series (user-facing) or Universe (ParentHub). Both are stored in the same table, distinguished by `hub_type`.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `hub_type` | TEXT | `"Hub"` = Series, `"ParentHub"` = Universe |
| `title` | TEXT | Display title |
| `wikidata_qid` | TEXT | Wikidata entity identifier. Indexed. |
| `parent_hub_id` | TEXT | FK → `hubs.id`. NULL for top-level Universes and standalone Series. |
| `media_type` | TEXT | Primary media type for this hub |
| `description` | TEXT | Wikipedia or provider description |
| `sort_title` | TEXT | Normalized title for alphabetical sorting |
| `created_at` | TEXT | Timestamp |
| `updated_at` | TEXT | Timestamp |
| `last_enriched_at` | TEXT | Timestamp of last Wikidata enrichment |

**Indices:** `wikidata_qid`, `parent_hub_id`, `hub_type`

### works

A single title, independent of version or format.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `hub_id` | TEXT | FK → `hubs.id`. The Series this work belongs to. |
| `wikidata_qid` | TEXT | Wikidata entity identifier. Indexed. |
| `title` | TEXT | Canonical display title |
| `original_title` | TEXT | Title in the work's source language (Phase 2 localization) |
| `media_type` | TEXT | `Books`, `Audiobooks`, `Movies`, `TV`, `Music`, `Comics`, `Podcasts` |
| `sort_title` | TEXT | Normalized for sorting |
| `status` | TEXT | `Verified`, `Provisional`, `NeedsReview`, `Quarantined`, `Pending` |
| `created_at` | TEXT | Timestamp |
| `updated_at` | TEXT | Timestamp |
| `last_enriched_at` | TEXT | Timestamp |

**Indices:** `wikidata_qid`, `hub_id`, `media_type`, `status`

### editions

A specific version of a Work (e.g., "4K HDR Blu-ray Remux", "First Edition Hardcover").

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `work_id` | TEXT | FK → `works.id` |
| `title` | TEXT | Edition label |
| `created_at` | TEXT | Timestamp |

### media_assets

A single file on disk.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `edition_id` | TEXT | FK → `editions.id` |
| `file_path` | TEXT | Absolute path on disk |
| `file_name` | TEXT | Filename only |
| `fingerprint` | TEXT | SHA-256 hash. Used for deduplication and move detection. |
| `file_size_bytes` | INTEGER | |
| `media_type` | TEXT | Resolved media type |
| `container` | TEXT | File container format (e.g., `mkv`, `epub`, `m4b`) |
| `duration_sec` | INTEGER | For audio/video assets |
| `ingested_at` | TEXT | Timestamp |
| `status` | TEXT | Current pipeline status |
| `staging_path` | TEXT | Path within `.data/staging/` while in-flight |

**Indices:** `fingerprint`, `edition_id`, `status`

### hub_items

Links works to hubs (Series to Universe relationships).

| Column | Type | Notes |
|---|---|---|
| `hub_id` | TEXT | FK → `hubs.id` |
| `work_id` | TEXT | FK → `works.id` |
| `sort_order` | INTEGER | Position within the hub |

### hub_work_links

Many-to-many cross-links between works and hubs for works that span multiple Series.

| Column | Type | Notes |
|---|---|---|
| `hub_id` | TEXT | FK → `hubs.id` |
| `work_id` | TEXT | FK → `works.id` |
| `link_type` | TEXT | Relationship type (e.g., `"adaptation"`, `"companion"`) |

---

## Metadata

### metadata_claims

Append-only log of every metadata value ever received for an entity, with its source and confidence.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `entity_id` | TEXT | The work, edition, hub, or person this claim belongs to |
| `entity_type` | TEXT | `"Work"`, `"Hub"`, `"Person"`, etc. |
| `field_key` | TEXT | Field identifier (e.g., `"title"`, `"author"`, `"genre"`). See `MetadataFieldConstants.cs`. |
| `value` | TEXT | Claim value |
| `source_id` | TEXT | Provider GUID |
| `source_name` | TEXT | Human-readable provider name |
| `confidence` | REAL | Score 0.0–1.0 |
| `source_language` | TEXT | BCP-47 language code of this claim's value (Phase 6) |
| `is_user_lock` | INTEGER | 1 if user explicitly set this value (Tier A — always wins) |
| `created_at` | TEXT | Timestamp |
| `decayed_at` | TEXT | Timestamp when stale decay was applied. NULL if fresh. |

**Indices:** `entity_id + field_key`, `source_id`, `is_user_lock`

### canonical_values

The winning single-valued claim for each field after Priority Cascade resolution.

| Column | Type | Notes |
|---|---|---|
| `entity_id` | TEXT | |
| `entity_type` | TEXT | |
| `field_key` | TEXT | |
| `value` | TEXT | Winning value |
| `source_id` | TEXT | Which provider won |
| `confidence` | REAL | Winning confidence score |
| `resolved_at` | TEXT | Timestamp of last resolution |

**Unique index:** `entity_id + field_key`

### canonical_value_arrays

The winning multi-valued claims (genres, authors, vibe tags, cast members).

| Column | Type | Notes |
|---|---|---|
| `entity_id` | TEXT | |
| `entity_type` | TEXT | |
| `field_key` | TEXT | |
| `values` | TEXT | JSON array of winning values |
| `source_id` | TEXT | |
| `resolved_at` | TEXT | Timestamp |

**Unique index:** `entity_id + field_key`

### search_index

FTS5 full-text search index with trigram tokenizer (migration M-062). Handles CJK and languages without word boundaries. Short queries under 3 characters fall back to a `LIKE` scan.

| Column | Type | Notes |
|---|---|---|
| `entity_id` | TEXT | UNINDEXED — used for JOIN back to source tables |
| `title` | TEXT | Primary display title |
| `original_title` | TEXT | Source-language title |
| `alternate_titles` | TEXT | Wikidata aliases and romanizations (e.g., "Sen to Chihiro no Kamikakushi") |
| `author` | TEXT | Author/creator names |
| `description` | TEXT | Full description text |

---

## Persons

### persons

Wikidata-sourced person records. Always enriched from Wikidata; never manually created.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `wikidata_qid` | TEXT | Wikidata identifier. Indexed. |
| `name` | TEXT | Display name |
| `description` | TEXT | Wikipedia short description |
| `birth_date` | TEXT | ISO-8601 date |
| `death_date` | TEXT | ISO-8601 date. NULL if living. |
| `nationality` | TEXT | |
| `headshot_url` | TEXT | Cached headshot image URL |
| `last_enriched_at` | TEXT | Timestamp. Records enriched within 30 days skip re-fetch. |
| `last_revision_id` | TEXT | Wikidata revision ID. Used for freshness checks before full property re-fetch. |

**Index:** `wikidata_qid`

### person_roles

Roles a person plays in the library (author, director, narrator, composer, etc.).

| Column | Type | Notes |
|---|---|---|
| `person_id` | TEXT | FK → `persons.id` |
| `role` | TEXT | Wikidata property code (e.g., `P50` = author, `P57` = director, `P161` = cast member) |
| `work_id` | TEXT | FK → `works.id` |

### person_media_links

Aggregated library presence — which media types a person appears in and how many works.

| Column | Type | Notes |
|---|---|---|
| `person_id` | TEXT | FK → `persons.id` |
| `media_type` | TEXT | |
| `work_count` | INTEGER | |

### person_aliases

Pseudonyms and alternate names, including Wikidata-resolved aliases.

| Column | Type | Notes |
|---|---|---|
| `person_id` | TEXT | FK → `persons.id` |
| `alias` | TEXT | Alternate name |
| `alias_type` | TEXT | `"pseudonym"`, `"birth_name"`, `"wikidata_alias"` |
| `merge_target_id` | TEXT | If this alias resolves to a different person record, the canonical person's id |

### pending_person_signals

Person names from file metadata or AI extraction awaiting standalone Wikidata reconciliation.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `entity_id` | TEXT | The work or edition this name came from |
| `name` | TEXT | Person name to reconcile |
| `signal_source` | TEXT | `"file_metadata"` (confidence 0.80) or `"ai_extracted"` (confidence 0.75) |
| `created_at` | TEXT | Timestamp |
| `resolved_at` | TEXT | NULL until reconciliation completes |

### character_performer_links

Links fictional characters to the real-world performers who portray them.

| Column | Type | Notes |
|---|---|---|
| `character_id` | TEXT | FK → `fictional_entities.id` |
| `person_id` | TEXT | FK → `persons.id` |
| `work_id` | TEXT | FK → `works.id`. Scopes the link to a specific adaptation. |
| `era_qualifier` | TEXT | Temporal qualifier (e.g., "young", "2024 series") for era-correct actor matching |

---

## Universe Graph

### fictional_entities

Characters, locations, factions, and other fictional elements within Universes.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `universe_qid` | TEXT | The Universe (ParentHub) this entity belongs to |
| `wikidata_qid` | TEXT | Wikidata QID if the entity is notable enough to have one |
| `entity_type` | TEXT | `"Character"`, `"Location"`, `"Faction"`, `"Concept"` |
| `name` | TEXT | Display name |
| `description` | TEXT | |
| `p31_type` | TEXT | Wikidata P31 (instance of) value. Used to detect animated character art. |

### fictional_entity_work_links

Which works a fictional entity appears in.

| Column | Type | Notes |
|---|---|---|
| `entity_id` | TEXT | FK → `fictional_entities.id` |
| `work_id` | TEXT | FK → `works.id` |
| `appearance_type` | TEXT | `"primary"`, `"supporting"`, `"mention"` |

### entity_relationships

Directed relationships between fictional entities (e.g., "Frodo" → `parent_of` → "Sam").

| Column | Type | Notes |
|---|---|---|
| `source_entity_id` | TEXT | FK → `fictional_entities.id` |
| `target_entity_id` | TEXT | FK → `fictional_entities.id` |
| `relationship_type` | TEXT | Relationship label |
| `temporal_qualifier` | TEXT | Era or time scope for this relationship |
| `lore_delta_type` | TEXT | Canon discrepancy flag (e.g., `"adaptation_change"`) |

### narrative_roots

Universe-level root records with Wikidata provenance.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `hub_id` | TEXT | FK → `hubs.id` (the ParentHub) |
| `wikidata_qid` | TEXT | |
| `franchise_qid` | TEXT | Wikidata P8345 franchise identifier |
| `series_qid` | TEXT | Wikidata P179 series identifier |

### qid_labels

Cached Wikidata display labels for QIDs, avoiding repeated API lookups.

| Column | Type | Notes |
|---|---|---|
| `qid` | TEXT | Primary key |
| `label` | TEXT | Display label in the configured metadata language |
| `fetched_at` | TEXT | Timestamp |

---

## Providers

### provider_registry

Registered providers and their runtime state.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | GUID — matches `provider_id` in config files. Primary key. |
| `name` | TEXT | Display name |
| `media_types` | TEXT | JSON array of served media types |
| `stage` | TEXT | `"Stage1"` or `"Stage2"` |
| `enabled` | INTEGER | |
| `last_checked_at` | TEXT | Timestamp of last health check |
| `health_status` | TEXT | `"Healthy"`, `"Degraded"`, `"Unavailable"` |

### provider_config

Live provider configuration values (mirrors `config/providers/*.json` after load).

| Column | Type | Notes |
|---|---|---|
| `provider_id` | TEXT | FK → `provider_registry.id` |
| `config_key` | TEXT | Configuration field name |
| `config_value` | TEXT | Configuration field value |

### provider_response_cache

Cached provider API responses to eliminate redundant calls.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `provider_id` | TEXT | |
| `cache_key` | TEXT | Hashed request parameters |
| `response_body` | TEXT | Cached JSON response |
| `cached_at` | TEXT | Timestamp |
| `expires_at` | TEXT | TTL-based expiry timestamp |

### provider_health

Historical health check log per provider.

| Column | Type | Notes |
|---|---|---|
| `provider_id` | TEXT | |
| `checked_at` | TEXT | Timestamp |
| `status` | TEXT | |
| `latency_ms` | INTEGER | |

---

## Security

### api_keys

API keys for Engine authentication.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `key_hash` | TEXT | SHA-256 hash of the key. The plaintext key is never stored after creation. |
| `label` | TEXT | Human-readable label |
| `role` | TEXT | `"Administrator"`, `"Curator"`, `"Consumer"` |
| `created_at` | TEXT | Timestamp |
| `last_used_at` | TEXT | Timestamp. NULL if never used. |
| `revoked_at` | TEXT | NULL if active. Timestamp if revoked. |

### profiles

User profiles for multi-user support.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `name` | TEXT | Display name |
| `pin_hash` | TEXT | PIN hash. NULL if no PIN set. |
| `role` | TEXT | |
| `created_at` | TEXT | Timestamp |

---

## Operations

### ingestion_log

One row per file processed through the ingestion pipeline.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `batch_id` | TEXT | FK → `ingestion_batches.id` |
| `asset_id` | TEXT | FK → `media_assets.id` |
| `file_path` | TEXT | Original file path |
| `fingerprint` | TEXT | SHA-256 hash |
| `outcome` | TEXT | `"Promoted"`, `"Rejected"`, `"NeedsReview"`, `"Duplicate"`, `"Failed"` |
| `outcome_reason` | TEXT | Natural-language explanation for non-promoted outcomes |
| `pipeline_state` | TEXT | JSON snapshot of pipeline stage completion |
| `created_at` | TEXT | Timestamp |

### ingestion_batches

Groups ingestion_log rows into named runs.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `started_at` | TEXT | Timestamp |
| `completed_at` | TEXT | NULL until batch finishes |
| `file_count` | INTEGER | Total files in batch |
| `promoted_count` | INTEGER | |
| `rejected_count` | INTEGER | |

### deferred_enrichment_queue

Works queued for background enrichment (Pass 2).

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `entity_id` | TEXT | |
| `entity_type` | TEXT | |
| `priority` | INTEGER | Lower = higher priority |
| `queued_at` | TEXT | Timestamp |
| `started_at` | TEXT | NULL until processing begins |

### review_queue

Items the Engine could not confidently match and that require human review.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `entity_id` | TEXT | |
| `entity_type` | TEXT | |
| `review_reason` | TEXT | Why this item is in the queue |
| `candidates` | TEXT | JSON array of ranked candidates |
| `created_at` | TEXT | Timestamp |
| `resolved_at` | TEXT | NULL until resolved or dismissed |
| `resolution_type` | TEXT | `"Selected"`, `"Provisional"`, `"Dismissed"`, `"SkippedUniverse"` |

### resolver_cache

Cached candidate lists from previous search/resolve operations.

| Column | Type | Notes |
|---|---|---|
| `cache_key` | TEXT | Hashed query parameters. Primary key. |
| `candidates` | TEXT | JSON array |
| `cached_at` | TEXT | Timestamp |

---

## Content

### image_cache

Tracks all images stored in `.data/images/`.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `entity_id` | TEXT | |
| `entity_type` | TEXT | |
| `image_type` | TEXT | `"CoverArt"`, `"Headshot"`, `"Banner"`, `"Logo"`, `"Backdrop"` |
| `file_path` | TEXT | Absolute path on disk |
| `source_url` | TEXT | Original provider URL |
| `provider_id` | TEXT | |
| `user_override` | INTEGER | 1 if uploaded by user. Protected from orphan sweep. |
| `phash` | TEXT | Perceptual hash for visual similarity matching |
| `created_at` | TEXT | Timestamp |
| `is_preferred` | INTEGER | 1 if this is the selected image for display |

### search_results_cache

Cached results from Intent Search queries.

| Column | Type | Notes |
|---|---|---|
| `cache_key` | TEXT | Hashed query. Primary key. |
| `results` | TEXT | JSON array of entity IDs |
| `cached_at` | TEXT | Timestamp |

### ui_settings_cache

Server-side cache of resolved UI settings per profile and device class.

| Column | Type | Notes |
|---|---|---|
| `profile_id` | TEXT | |
| `device_class` | TEXT | |
| `settings_json` | TEXT | Resolved merged settings |
| `computed_at` | TEXT | Timestamp |

### bridge_ids

External provider identifiers (ISBN, ASIN, TMDB ID, MBID) used for cross-provider linking.

| Column | Type | Notes |
|---|---|---|
| `entity_id` | TEXT | |
| `entity_type` | TEXT | |
| `id_type` | TEXT | `"ISBN"`, `"ASIN"`, `"TMDB"`, `"MBID"`, etc. |
| `value` | TEXT | The identifier value |
| `source_id` | TEXT | Which provider supplied this ID |

**Index:** `entity_id + id_type`, `value + id_type` (for reverse lookup)

---

## Reader

### reader_bookmarks

Saved reading positions.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `asset_id` | TEXT | FK → `media_assets.id` |
| `profile_id` | TEXT | FK → `profiles.id` |
| `chapter_index` | INTEGER | |
| `cfi` | TEXT | EPUB Canonical Fragment Identifier for precise position |
| `created_at` | TEXT | Timestamp |

### reader_highlights

Text highlights and annotations.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `asset_id` | TEXT | |
| `profile_id` | TEXT | |
| `chapter_index` | INTEGER | |
| `cfi_range` | TEXT | CFI range for highlighted text |
| `text` | TEXT | Highlighted text content |
| `note` | TEXT | User annotation. NULL if no note. |
| `color` | TEXT | Highlight color |
| `created_at` | TEXT | Timestamp |

### reader_statistics

Aggregated reading session data.

| Column | Type | Notes |
|---|---|---|
| `asset_id` | TEXT | |
| `profile_id` | TEXT | |
| `total_seconds` | INTEGER | Total reading time |
| `completion_pct` | REAL | 0.0–1.0 |
| `last_position_cfi` | TEXT | Last known reading position |
| `session_count` | INTEGER | Number of reading sessions |
| `last_opened_at` | TEXT | Timestamp |

### alignment_jobs

Whispersync audio-to-text alignment jobs.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `asset_id` | TEXT | |
| `status` | TEXT | `"Pending"`, `"Running"`, `"Completed"`, `"Failed"` |
| `alignment_data` | TEXT | JSON alignment map |
| `created_at` | TEXT | Timestamp |

---

## User

### user_states

Per-profile, per-asset playback and reading state.

| Column | Type | Notes |
|---|---|---|
| `profile_id` | TEXT | |
| `asset_id` | TEXT | |
| `state_type` | TEXT | `"Reading"`, `"Watching"`, `"Listening"` |
| `position` | TEXT | Progress position (CFI for EPUB, seconds for audio/video) |
| `updated_at` | TEXT | Timestamp |

### user_taste_profiles

AI-generated per-user taste vectors, updated by the Taste Profiling feature.

| Column | Type | Notes |
|---|---|---|
| `profile_id` | TEXT | Primary key. FK → `profiles.id`. |
| `genre_weights` | TEXT | JSON map of genre → weight |
| `vibe_weights` | TEXT | JSON map of vibe tag → weight |
| `creator_weights` | TEXT | JSON map of person QID → weight |
| `computed_at` | TEXT | Timestamp |

---

## Activity

### system_activity

Rolling log of Engine actions. Pruned automatically per `config/maintenance.json`.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | UUID, primary key |
| `run_id` | TEXT | Groups related activities into a single run |
| `activity_type` | TEXT | Type code (e.g., `"Ingestion"`, `"Enrichment"`, `"Writeback"`) |
| `entity_id` | TEXT | Related entity. NULL for system-level activities. |
| `outcome` | TEXT | `"Success"`, `"Failure"`, `"Skipped"` |
| `message` | TEXT | Human-readable description |
| `created_at` | TEXT | Timestamp |

**Index:** `run_id`, `activity_type`, `created_at`

### transaction_log

Ordered log of all database writes, used for debugging and rollback analysis.

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER | Auto-increment primary key |
| `table_name` | TEXT | Which table was modified |
| `row_id` | TEXT | The affected row's primary key |
| `operation` | TEXT | `"INSERT"`, `"UPDATE"`, `"DELETE"` |
| `old_values` | TEXT | JSON snapshot before change. NULL for INSERT. |
| `new_values` | TEXT | JSON snapshot after change. NULL for DELETE. |
| `created_at` | TEXT | Timestamp |
