-- =============================================================================
-- Tuvima Library - SQLite initialization script
-- Current storage epoch: guid-blob-v1
--
-- Internal UUIDs are stored as 16-byte BLOBs where the current domain model owns
-- the identifier. External provider identifiers, QIDs, hashes, URLs, and file
-- paths remain TEXT. Unsupported database epochs are rejected unless an
-- explicit destructive reset is requested.
-- =============================================================================

PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
PRAGMA temp_store = MEMORY;

CREATE TABLE IF NOT EXISTS alignment_jobs (
    id                  TEXT NOT NULL PRIMARY KEY,
    ebook_asset_id      TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    audiobook_asset_id  TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    status              TEXT NOT NULL DEFAULT 'Pending'
                            CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed')),
    alignment_data      TEXT,
    error_message       TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    completed_at        TEXT
);

CREATE TABLE IF NOT EXISTS api_keys (
    id          BLOB NOT NULL PRIMARY KEY,       -- UUID
    label       TEXT NOT NULL,                   -- human-readable, e.g. "Radarr Integration"
    hashed_key  TEXT NOT NULL UNIQUE,            -- SHA-256 hex of the plaintext key
    role        TEXT NOT NULL DEFAULT 'Administrator'
                    CHECK (role IN ('Administrator', 'Curator', 'Consumer')),
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS audio_fingerprints (
    asset_id      TEXT NOT NULL PRIMARY KEY,
    fingerprint   BLOB NOT NULL,
    duration_sec  REAL NOT NULL DEFAULT 0,
    created_at    TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (asset_id) REFERENCES media_assets(id)
);

CREATE TABLE IF NOT EXISTS bridge_ids (
    id                BLOB NOT NULL PRIMARY KEY,
    entity_id         BLOB NOT NULL,
    id_type           TEXT NOT NULL,
    id_value          TEXT NOT NULL,
    wikidata_property TEXT,
    provider_id       TEXT,
    created_at        TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(entity_id, id_type)
);

CREATE TABLE IF NOT EXISTS canonical_value_arrays (
    entity_id BLOB    NOT NULL,
    key       TEXT    NOT NULL,
    ordinal   INTEGER NOT NULL DEFAULT 0,
    value     TEXT    NOT NULL,
    value_qid TEXT,
    PRIMARY KEY (entity_id, key, ordinal)
);

CREATE TABLE IF NOT EXISTS canonical_values (
    entity_id           BLOB    NOT NULL,
    key                 TEXT    NOT NULL,
    value               TEXT    NOT NULL,
    last_scored_at      TEXT    NOT NULL,
    -- Phase B: tracks whether the scoring engine could not pick a clear winner.
    -- 1 = conflicted (runner-up within epsilon of winner); 0 = resolved.
    is_conflicted       INTEGER NOT NULL DEFAULT 0
                            CHECK (is_conflicted IN (0, 1)),
    -- Unit 4: the provider that supplied the winning claim for this field.
    -- NULL for user-locked values or rows created before this column was added.
    winning_provider_id BLOB,
    -- Unit 5: field-level review flag.  1 = needs human review (conflicted,
    -- missing expected field, or local-only source with low confidence); 0 = ok.
    needs_review        INTEGER NOT NULL DEFAULT 0
                            CHECK (needs_review IN (0, 1)),
    PRIMARY KEY (entity_id, key)
);

CREATE TABLE IF NOT EXISTS character_performer_links (
    person_id           BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    fictional_entity_id BLOB NOT NULL REFERENCES fictional_entities(id) ON DELETE CASCADE,
    work_qid            TEXT, character_image_path TEXT,
    PRIMARY KEY (person_id, fictional_entity_id, work_qid)
);

CREATE TABLE IF NOT EXISTS character_portraits (
    id                  BLOB PRIMARY KEY,
    person_id           BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    fictional_entity_id BLOB NOT NULL REFERENCES fictional_entities(id) ON DELETE CASCADE,
    image_url           TEXT,
    local_image_path    TEXT,
    source_provider     TEXT,
    is_default          INTEGER NOT NULL DEFAULT 0,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT
);

CREATE TABLE IF NOT EXISTS collection_items (
    id                BLOB NOT NULL PRIMARY KEY,  -- UUID
    collection_id            BLOB NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    work_id           BLOB NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    sort_order        INTEGER NOT NULL DEFAULT 0,
    progress_state    TEXT NOT NULL DEFAULT 'not_started'
                          CHECK (progress_state IN ('not_started','in_progress','completed')),
    progress_position TEXT,
    added_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS collection_placements (
    id            BLOB PRIMARY KEY,
    collection_id BLOB NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    location      TEXT NOT NULL,
    position      INTEGER NOT NULL DEFAULT 0,
    display_limit INTEGER NOT NULL DEFAULT 0,
    display_mode  TEXT NOT NULL DEFAULT 'swimlane',
    is_visible    INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS collection_relationships (
    id            BLOB NOT NULL PRIMARY KEY,
    collection_id BLOB NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    rel_type      TEXT NOT NULL,
    rel_qid       TEXT NOT NULL,
    rel_label     TEXT,
    confidence    REAL NOT NULL DEFAULT 0.9,
    discovered_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS collections (
    id                BLOB NOT NULL PRIMARY KEY,  -- UUID
    universe_id       BLOB,                       -- NULLABLE: cross-hub grouping
    parent_collection_id     BLOB REFERENCES collections(id) ON DELETE SET NULL,  -- franchise parent
    display_name      TEXT,                       -- Phase 7: human-readable hub name
    collection_type          TEXT NOT NULL DEFAULT 'Universe',
    description       TEXT,
    icon_name         TEXT,
    scope             TEXT NOT NULL DEFAULT 'library',
    profile_id        BLOB REFERENCES profiles(id) ON DELETE CASCADE,
    is_enabled        INTEGER NOT NULL DEFAULT 1,
    is_featured       INTEGER NOT NULL DEFAULT 0,
    min_items         INTEGER NOT NULL DEFAULT 0,
    rule_json         TEXT,
    refresh_schedule  TEXT,
    last_refreshed_at TEXT,
    modified_at       TEXT,
    wikidata_qid      TEXT,
    universe_status   TEXT NOT NULL DEFAULT 'Unknown',
    created_at        TEXT NOT NULL DEFAULT (datetime('now'))
, resolution TEXT NOT NULL DEFAULT 'query', rule_hash TEXT, group_by_field TEXT, match_mode TEXT NOT NULL DEFAULT 'all', sort_field TEXT, sort_direction TEXT NOT NULL DEFAULT 'desc', live_updating INTEGER NOT NULL DEFAULT 1, square_artwork_path TEXT, square_artwork_mime_type TEXT);

CREATE TABLE IF NOT EXISTS deferred_enrichment_queue (
    id           BLOB NOT NULL PRIMARY KEY,
    entity_id    BLOB NOT NULL,
    wikidata_qid TEXT,
    media_type   TEXT NOT NULL,
    hints_json   TEXT,
    created_at   TEXT NOT NULL,
    status       TEXT NOT NULL DEFAULT 'Pending',
    processed_at TEXT
, failure_type TEXT, failed_provider_name TEXT);

CREATE TABLE IF NOT EXISTS editions (
    id           BLOB NOT NULL PRIMARY KEY,  -- UUID
    work_id      BLOB NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    format_label TEXT                        -- e.g. '4K Bluray', 'First Edition'
, wikidata_qid TEXT);

CREATE TABLE IF NOT EXISTS encode_jobs (
    id             BLOB NOT NULL PRIMARY KEY,
    asset_id       BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    profile_key    TEXT NOT NULL,
    source_hash    TEXT NOT NULL,
    status         TEXT NOT NULL,
    created_at     TEXT NOT NULL,
    scheduled_for  TEXT,
    started_at     TEXT,
    completed_at   TEXT,
    progress_pct   REAL NOT NULL DEFAULT 0,
    output_path    TEXT,
    output_bytes   INTEGER,
    last_error     TEXT,
    retry_count    INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS entity_assets (
    id               BLOB PRIMARY KEY,
    entity_id        BLOB NOT NULL,
    entity_type      TEXT NOT NULL CHECK(entity_type IN ('Work','Person','Universe','FictionalEntity')),
    asset_type       TEXT NOT NULL CHECK(asset_type IN ('CoverArt','Headshot','Banner','SquareArt','Logo','DiscArt','ClearArt','Background','SeasonPoster','SeasonThumb','EpisodeStill','CharacterPortrait')),
    image_url        TEXT,
    local_image_path TEXT,
    local_image_path_s TEXT,
    local_image_path_m TEXT,
    local_image_path_l TEXT,
    source_provider  TEXT,
    width_px         INTEGER,
    height_px        INTEGER,
    aspect_class     TEXT NOT NULL DEFAULT 'UnsupportedRect',
    primary_hex      TEXT,
    secondary_hex    TEXT,
    accent_hex       TEXT,
    asset_class      TEXT NOT NULL DEFAULT 'Artwork',
    storage_location TEXT NOT NULL DEFAULT 'Central',
    owner_scope      TEXT NOT NULL DEFAULT 'Unknown',
    is_preferred     INTEGER NOT NULL DEFAULT 0,
    is_user_override INTEGER NOT NULL DEFAULT 0,
    is_locally_exported   INTEGER NOT NULL DEFAULT 0,
    is_preferred_exported INTEGER NOT NULL DEFAULT 0,
    created_at       TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at       TEXT
);

CREATE TABLE IF NOT EXISTS entity_capability_states (
  id                  BLOB PRIMARY KEY,
  entity_id           BLOB NOT NULL,
  entity_kind         TEXT NOT NULL,
  media_type          TEXT,
  capability_id       TEXT NOT NULL,
  capability_kind     TEXT NOT NULL,
  capability_version  TEXT,
  sub_key             TEXT,
  status              TEXT NOT NULL,
  requiredness        TEXT NOT NULL DEFAULT 'optional',
  source              TEXT,
  confidence          REAL,
  artifact_count      INTEGER NOT NULL DEFAULT 0,
  artifact_summary    TEXT,
  result_summary      TEXT,
  last_operation_id   BLOB,
  first_attempted_at  TEXT,
  last_attempted_at   TEXT,
  succeeded_at        TEXT,
  next_retry_at       TEXT,
  stale               INTEGER NOT NULL DEFAULT 0,
  needs_rerun         INTEGER NOT NULL DEFAULT 0,
  missing_reason      TEXT,
  last_error          TEXT,
  created_at          TEXT NOT NULL,
  updated_at          TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS entity_events (
    id                BLOB NOT NULL PRIMARY KEY,
    entity_id         BLOB NOT NULL,
    entity_type       TEXT NOT NULL,
    event_type        TEXT NOT NULL,
    stage             INTEGER,
    trigger           TEXT NOT NULL,
    provider_id       TEXT,
    provider_name     TEXT,
    bridge_id_type    TEXT,
    bridge_id_value   TEXT,
    resolved_qid      TEXT,
    confidence        REAL,
    score_title       REAL,
    score_author      REAL,
    score_year        REAL,
    score_format      REAL,
    score_cross_field REAL,
    score_cover_art   REAL,
    score_composite   REAL,
    occurred_at       TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    ingestion_run_id  BLOB,
    detail            TEXT
);

CREATE TABLE IF NOT EXISTS entity_field_changes (
    id              BLOB NOT NULL PRIMARY KEY,
    event_id        BLOB NOT NULL REFERENCES entity_events(id) ON DELETE CASCADE,
    entity_id       BLOB NOT NULL,
    field           TEXT NOT NULL,
    old_value       TEXT,
    new_value       TEXT,
    old_provider_id TEXT,
    new_provider_id TEXT,
    confidence      REAL,
    is_file_original INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS entity_relationships (
    id                      TEXT NOT NULL PRIMARY KEY,
    subject_qid             TEXT NOT NULL,
    relationship_type       TEXT NOT NULL,
    object_qid              TEXT NOT NULL,
    confidence              REAL NOT NULL DEFAULT 0.9,
    context_work_qid        TEXT,
    discovered_at           TEXT NOT NULL, start_time TEXT, end_time TEXT,
    UNIQUE (subject_qid, relationship_type, object_qid)
);

CREATE TABLE IF NOT EXISTS fictional_entities (
    id                       BLOB NOT NULL PRIMARY KEY,
    wikidata_qid             TEXT NOT NULL UNIQUE,
    label                    TEXT NOT NULL,
    description              TEXT,
    entity_sub_type          TEXT NOT NULL
                                 CHECK (entity_sub_type IN ('Character', 'Location', 'Organization', 'Event')),
    fictional_universe_qid   TEXT,
    fictional_universe_label TEXT,
    image_url                TEXT,
    local_image_path         TEXT,
    created_at               TEXT NOT NULL,
    enriched_at              TEXT
, wikidata_revision_id INTEGER);

CREATE TABLE IF NOT EXISTS fictional_entity_work_links (
    entity_id   BLOB NOT NULL REFERENCES fictional_entities(id) ON DELETE CASCADE,
    work_qid    TEXT NOT NULL,
    work_label  TEXT,
    link_type   TEXT NOT NULL DEFAULT 'appears_in',
    PRIMARY KEY (entity_id, work_qid, link_type)
);

CREATE TABLE IF NOT EXISTS file_hash_cache (
    absolute_path TEXT    NOT NULL PRIMARY KEY,
    size_bytes    INTEGER NOT NULL,
    mtime_utc     TEXT    NOT NULL,
    sha256        TEXT    NOT NULL,
    cached_at     TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS identity_jobs (
    id                     BLOB PRIMARY KEY,
    entity_id              BLOB NOT NULL,
    entity_type            TEXT NOT NULL,
    media_type             TEXT NOT NULL,
    ingestion_run_id       BLOB,
    state                  TEXT NOT NULL DEFAULT 'Queued',
    pass                   TEXT NOT NULL DEFAULT 'Quick',
    attempt_count          INTEGER NOT NULL DEFAULT 0,
    lease_owner            TEXT,
    lease_expires_at       TEXT,
    selected_candidate_id  BLOB,
    resolved_qid          TEXT,
    last_error             TEXT,
    next_retry_at          TEXT,
    created_at             TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at             TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS image_cache (
    content_hash  TEXT NOT NULL PRIMARY KEY,
    file_path     TEXT NOT NULL,
    source_url    TEXT,
    downloaded_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
, is_user_override INTEGER NOT NULL DEFAULT 0, phash INTEGER);

CREATE TABLE IF NOT EXISTS ingestion_batches (
    id                BLOB NOT NULL PRIMARY KEY,
    status            TEXT NOT NULL DEFAULT 'running',
    source_path       TEXT,
    category          TEXT,
    files_total       INTEGER NOT NULL DEFAULT 0,
    files_processed   INTEGER NOT NULL DEFAULT 0,
    files_registered  INTEGER NOT NULL DEFAULT 0,
    files_review      INTEGER NOT NULL DEFAULT 0,
    files_no_match    INTEGER NOT NULL DEFAULT 0,
    files_failed      INTEGER NOT NULL DEFAULT 0,
    started_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    completed_at      TEXT,
    created_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    updated_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS ingestion_log (
    id                BLOB NOT NULL PRIMARY KEY,
    file_path         TEXT NOT NULL,
    media_asset_id    BLOB,
    content_hash      TEXT,
    status            TEXT NOT NULL DEFAULT 'detected',
    media_type        TEXT,
    confidence_score  REAL,
    detected_title    TEXT,
    normalized_title  TEXT,
    wikidata_qid      TEXT,
    error_detail      TEXT,
    ingestion_run_id  BLOB,
    created_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    updated_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS ingestion_batch_artifacts (
    id                BLOB NOT NULL PRIMARY KEY,
    batch_id          BLOB NOT NULL,
    artifact_type     TEXT NOT NULL,
    artifact_id       BLOB,
    parent_entity_id  BLOB,
    parent_entity_type TEXT,
    action            TEXT NOT NULL,
    display_name      TEXT,
    provider_id       TEXT,
    source            TEXT,
    detail_json       TEXT,
    occurred_at       TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS media_assets (
    id             BLOB NOT NULL PRIMARY KEY,  -- UUID
    edition_id     BLOB NOT NULL REFERENCES editions(id) ON DELETE CASCADE,
    content_hash   TEXT NOT NULL UNIQUE,       -- reconciliation key; SHA-256 hex, lowercase
    file_path_root TEXT NOT NULL,              -- FS path, no BLOBs
    status         TEXT NOT NULL DEFAULT 'Normal'
                       CHECK (status IN ('Normal', 'Conflicted', 'Orphaned')),

    -- â”€â”€ Auto re-tag sweep state (M-084) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    -- writeback_fields_hash: SHA-256 of (writeback-fields.json slice for
    -- this asset's media type) + tagger version constant. NULL when the
    -- asset has never been re-tagged through the sweep.
    writeback_fields_hash    TEXT,
    -- writeback_status: 'ok' | 'pending' | 'retry' | 'failed'. NULL until
    -- the sweep first touches the asset.
    writeback_status         TEXT,
    writeback_last_error     TEXT,
    writeback_attempts       INTEGER NOT NULL DEFAULT 0,
    -- writeback_next_retry_at: unix epoch seconds â€” used by the sweep
    -- worker to skip rows whose retry window hasn't opened yet.
    writeback_next_retry_at  INTEGER
, library_id TEXT, is_orphaned INTEGER NOT NULL DEFAULT 0, orphaned_at TEXT);

CREATE TABLE IF NOT EXISTS media_operation_events (
  id             BLOB PRIMARY KEY,
  operation_id   BLOB NOT NULL,
  entity_id      BLOB,
  batch_id       BLOB,
  event_type     TEXT NOT NULL,
  old_status     TEXT,
  new_status     TEXT,
  old_stage      TEXT,
  new_stage      TEXT,
  message        TEXT,
  detail_json    TEXT,
  occurred_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS media_operations (
  id                  BLOB PRIMARY KEY,
  operation_type      TEXT NOT NULL,
  operation_kind      TEXT NOT NULL,
  entity_id           BLOB,
  entity_kind         TEXT,
  batch_id            BLOB,
  source_path         TEXT,
  content_hash        TEXT,
  capability_id       TEXT,
  capability_version  TEXT,
  sub_key             TEXT,
  plugin_id           TEXT,
  plugin_version      TEXT,
  provider_id         TEXT,
  model_id            TEXT,
  status              TEXT NOT NULL,
  stage               TEXT,
  priority            INTEGER NOT NULL DEFAULT 100,
  queue_name          TEXT NOT NULL DEFAULT 'default',
  position_key        INTEGER NOT NULL,
  attempt_count       INTEGER NOT NULL DEFAULT 0,
  lease_owner         TEXT,
  lease_expires_at    TEXT,
  heartbeat_at        TEXT,
  next_retry_at       TEXT,
  progress_percent    INTEGER NOT NULL DEFAULT 0,
  items_total         INTEGER NOT NULL DEFAULT 0,
  items_completed     INTEGER NOT NULL DEFAULT 0,
  items_failed        INTEGER NOT NULL DEFAULT 0,
  result_summary      TEXT,
  last_error          TEXT,
  missing_reason      TEXT,
  created_at          TEXT NOT NULL,
  started_at          TEXT,
  updated_at          TEXT NOT NULL,
  completed_at        TEXT,
  idempotency_key     TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS metadata_claims (
    id          BLOB NOT NULL PRIMARY KEY,  -- UUID
    entity_id   BLOB NOT NULL,              -- FK Ã¢â€ â€™ works.id | editions.id (polymorphic)
    provider_id BLOB NOT NULL REFERENCES metadata_providers(id),
    claim_key   TEXT NOT NULL,
    claim_value TEXT NOT NULL,
    confidence  REAL NOT NULL DEFAULT 1.0,
    -- Timestamp used by the scoring engine for stale-claim time-decay.
    -- Spec: Phase 6 Ã¢â‚¬â€œ Stale Claim Handling.
    claimed_at     TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    -- When 1, the scoring engine treats this claim as unconditional winner
    -- (confidence 1.0); no automated provider may set this to 1.
    -- Spec: Phase 8 Ã¢â‚¬â€œ Field-Level Arbitration Ã‚Â§ User-Locked Claims.
    is_user_locked INTEGER NOT NULL DEFAULT 0
                       CHECK (is_user_locked IN (0, 1))
);

CREATE TABLE IF NOT EXISTS metadata_providers (
    id         BLOB    NOT NULL PRIMARY KEY,  -- UUID
    name       TEXT    NOT NULL UNIQUE,
    version    TEXT    NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1     -- BOOLEAN: 1=true, 0=false
);

CREATE TABLE IF NOT EXISTS narrative_roots (
    qid         TEXT NOT NULL PRIMARY KEY,
    label       TEXT NOT NULL,
    level       TEXT NOT NULL
                    CHECK (level IN ('Universe', 'Franchise', 'Series', 'Standalone')),
    parent_qid  TEXT,
    created_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS offline_variants (
    id            BLOB NOT NULL PRIMARY KEY,
    asset_id      BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    profile_key   TEXT NOT NULL,
    source_hash   TEXT NOT NULL,
    display_name  TEXT NOT NULL,
    status        TEXT NOT NULL,
    output_path   TEXT,
    file_size     INTEGER,
    container     TEXT,
    video_codec   TEXT,
    audio_codec   TEXT,
    width         INTEGER,
    height        INTEGER,
    bitrate_kbps  INTEGER,
    created_at    TEXT NOT NULL,
    expires_at    TEXT,
    UNIQUE(asset_id, profile_key, source_hash)
);

CREATE TABLE IF NOT EXISTS pending_person_signals (
    id          BLOB NOT NULL PRIMARY KEY,
    entity_id   BLOB NOT NULL,
    name        TEXT NOT NULL,
    role        TEXT NOT NULL,
    source      TEXT NOT NULL,
    pattern     TEXT,
    media_type  TEXT NOT NULL,
    created_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS person_aliases (
    pseudonym_person_id BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    real_person_id      BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    PRIMARY KEY (pseudonym_person_id, real_person_id)
);

CREATE TABLE IF NOT EXISTS person_group_members (
    group_id    BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    member_id   BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    start_date  TEXT,   -- ISO-8601 date when member joined (from Wikidata P580 qualifier)
    end_date    TEXT,   -- ISO-8601 date when member left (from Wikidata P582 qualifier)
    PRIMARY KEY (group_id, member_id)
);

CREATE TABLE IF NOT EXISTS person_media_links (
    media_asset_id  BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    person_id       BLOB NOT NULL REFERENCES persons(id)       ON DELETE CASCADE,
    role            TEXT NOT NULL,
    PRIMARY KEY (media_asset_id, person_id, role)
);

CREATE TABLE IF NOT EXISTS person_roles (
    person_id BLOB NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    role      TEXT NOT NULL CHECK (role IN (
                  'Author','Narrator','Director',
                  'Actor','Voice Actor','Composer',
                  'Artist','Performer','Screenwriter')),
    PRIMARY KEY (person_id, role)
);

CREATE TABLE IF NOT EXISTS persons (
    id                BLOB    NOT NULL PRIMARY KEY,  -- UUID
    name              TEXT    NOT NULL,
    wikidata_qid      TEXT,                          -- e.g. Q42
    headshot_url      TEXT,                          -- Wikimedia Commons image URL
    biography         TEXT,                          -- Wikidata entity description
    occupation        TEXT,                          -- Wikidata P106 (e.g. "Writer", "Actor")
    instagram         TEXT,                          -- Wikidata P2003 (Instagram handle)
    twitter           TEXT,                          -- Wikidata P2002 (Twitter/X handle)
    tiktok            TEXT,                          -- Wikidata P7085 (TikTok handle)
    mastodon          TEXT,                          -- Wikidata P4033 (Mastodon address)
    website           TEXT,                          -- Wikidata P856 (Official website URL)
    local_headshot_path TEXT,                        -- Path to locally cached headshot
    date_of_birth     TEXT,                          -- Wikidata P569
    date_of_death     TEXT,                          -- Wikidata P570
    place_of_birth    TEXT,                          -- Wikidata P19
    place_of_death    TEXT,                          -- Wikidata P20
    nationality       TEXT,                          -- Wikidata P27
    is_pseudonym      INTEGER NOT NULL DEFAULT 0,    -- 1 = pen name / stage name
    created_at        TEXT    NOT NULL,              -- ISO-8601
    enriched_at       TEXT                           -- NULL = not yet enriched
, is_group INTEGER NOT NULL DEFAULT 0);

CREATE TABLE IF NOT EXISTS playback_inspection_cache (
    asset_id       BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    source_hash    TEXT NOT NULL,
    inspected_at   TEXT NOT NULL,
    file_size      INTEGER,
    duration_secs  REAL,
    container      TEXT,
    metadata_json  TEXT,
    PRIMARY KEY (asset_id, source_hash)
);

CREATE TABLE IF NOT EXISTS playback_segments (
    id            BLOB NOT NULL PRIMARY KEY,
    asset_id      BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    kind          TEXT NOT NULL,
    start_seconds REAL NOT NULL,
    end_seconds   REAL,
    confidence    REAL NOT NULL DEFAULT 0.0,
    source        TEXT NOT NULL,
    plugin_id     TEXT,
    is_skippable  INTEGER NOT NULL DEFAULT 1,
    review_status TEXT NOT NULL DEFAULT 'detected',
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS profile_external_logins (
    id            BLOB NOT NULL PRIMARY KEY,
    profile_id    BLOB NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    provider      TEXT NOT NULL,
    subject       TEXT NOT NULL,
    email         TEXT,
    display_name  TEXT,
    linked_at     TEXT NOT NULL,
    last_login_at TEXT,
    UNIQUE(provider, subject)
);

CREATE TABLE IF NOT EXISTS profiles (
    id           BLOB NOT NULL PRIMARY KEY,  -- UUID
    display_name TEXT NOT NULL,
    avatar_color TEXT NOT NULL DEFAULT '#7C4DFF',
    avatar_image_path TEXT,
    role         TEXT NOT NULL DEFAULT 'Consumer'
                     CHECK (role IN ('Administrator', 'Curator', 'Consumer')),
    pin_hash     TEXT,                       -- SHA-256 of 4-digit PIN; NULL = no PIN set
    created_at   TEXT NOT NULL
, navigation_config TEXT);

CREATE TABLE IF NOT EXISTS provider_config (
    provider_id BLOB    NOT NULL REFERENCES metadata_providers(id) ON DELETE CASCADE,
    key         TEXT    NOT NULL,
    value       TEXT    NOT NULL,
    is_secret   INTEGER NOT NULL DEFAULT 0,   -- BOOLEAN: marks credentials
    PRIMARY KEY (provider_id, key)
);

CREATE TABLE IF NOT EXISTS provider_health (
    provider_id          TEXT NOT NULL PRIMARY KEY,
    status               TEXT NOT NULL DEFAULT 'Healthy',
    consecutive_failures INTEGER NOT NULL DEFAULT 0,
    last_check_at        TEXT,
    last_success_at      TEXT,
    last_failure_at      TEXT,
    last_failure_reason  TEXT,
    next_check_at        TEXT,
    down_since           TEXT
);

CREATE TABLE IF NOT EXISTS provider_response_cache (
    cache_key     TEXT NOT NULL PRIMARY KEY,
    provider_id   TEXT NOT NULL,
    query_hash    TEXT NOT NULL,
    response_json TEXT NOT NULL,
    etag          TEXT,
    fetched_at    TEXT NOT NULL,
    expires_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS qid_labels (
    qid         TEXT NOT NULL PRIMARY KEY,
    label       TEXT NOT NULL,
    description TEXT,
    entity_type TEXT,
    fetched_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS reader_bookmarks (
    id             TEXT NOT NULL PRIMARY KEY,
    user_id        TEXT NOT NULL,
    asset_id       TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    chapter_index  INTEGER NOT NULL,
    cfi_position   TEXT,
    label          TEXT,
    created_at     TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS reader_highlights (
    id             TEXT NOT NULL PRIMARY KEY,
    user_id        TEXT NOT NULL,
    asset_id       TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    chapter_index  INTEGER NOT NULL,
    start_offset   INTEGER NOT NULL,
    end_offset     INTEGER NOT NULL,
    selected_text  TEXT NOT NULL,
    color          TEXT NOT NULL DEFAULT '#EAB308',
    note_text      TEXT,
    created_at     TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS reader_statistics (
    id                      TEXT NOT NULL PRIMARY KEY,
    user_id                 TEXT NOT NULL,
    asset_id                TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    chapters_read           INTEGER NOT NULL DEFAULT 0,
    total_reading_time_secs INTEGER NOT NULL DEFAULT 0,
    words_read              INTEGER NOT NULL DEFAULT 0,
    sessions_count          INTEGER NOT NULL DEFAULT 0,
    avg_words_per_minute    REAL NOT NULL DEFAULT 0.0,
    last_session_at         TEXT,
    UNIQUE (user_id, asset_id)
);

CREATE TABLE IF NOT EXISTS resolver_cache (
    cache_key        TEXT NOT NULL PRIMARY KEY,
    normalized_title TEXT NOT NULL,
    media_type       TEXT NOT NULL,
    wikidata_qid     TEXT,
    confidence       REAL,
    entity_label     TEXT,
    created_at       TEXT NOT NULL,
    expires_at       TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS retail_match_candidates (
    id                    BLOB PRIMARY KEY,
    job_id                BLOB NOT NULL REFERENCES identity_jobs(id) ON DELETE CASCADE,
    provider_id           BLOB NOT NULL,
    provider_name         TEXT NOT NULL,
    provider_item_id      TEXT,
    rank                  INTEGER NOT NULL DEFAULT 0,
    title                 TEXT NOT NULL,
    creator               TEXT,
    year                  TEXT,
    score_total           REAL NOT NULL DEFAULT 0.0,
    score_breakdown_json  TEXT,
    bridge_ids_json       TEXT,
    description           TEXT,
    image_url             TEXT,
    outcome               TEXT NOT NULL DEFAULT 'Rejected',
    created_at            TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS review_queue (
    id               BLOB NOT NULL PRIMARY KEY,
    entity_id        BLOB NOT NULL,
    entity_type      TEXT NOT NULL,
    trigger          TEXT NOT NULL,
    status           TEXT NOT NULL DEFAULT 'Pending'
                         CHECK (status IN ('Pending', 'Resolved', 'Dismissed')),
    proposed_collection_id  TEXT,
    confidence_score REAL,
    candidates_json  TEXT,
    detail           TEXT,
    created_at       TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    resolved_at      TEXT,
    resolved_by      TEXT
, source_operation_id BLOB, source_capability_id TEXT, source_capability_sub_key TEXT, review_ready_at TEXT, automation_completed_at TEXT);

CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
    entity_id UNINDEXED,
    title,
    original_title,
    alternate_titles,
    author,
    description,
    tokenize = 'trigram'
);

CREATE TABLE IF NOT EXISTS search_results_cache (
    entity_id    BLOB NOT NULL PRIMARY KEY,
    results_json TEXT NOT NULL,
    searched_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS series_manifest_hydrations (
    series_qid            TEXT PRIMARY KEY,
    collection_id         BLOB NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    series_label          TEXT,
    manifest_source       TEXT NOT NULL DEFAULT 'Tuvima.Wikidata',
    manifest_version      TEXT,
    manifest_hash         TEXT,
    known_item_qids_hash  TEXT,
    warnings_json         TEXT NOT NULL DEFAULT '[]',
    api_metadata_json     TEXT NOT NULL DEFAULT '{}',
    last_hydrated_at      TEXT NOT NULL,
    created_at            TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at            TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS series_manifest_items (
    id                          BLOB PRIMARY KEY,
    collection_id               BLOB NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    series_qid                  TEXT NOT NULL,
    item_qid                    TEXT NOT NULL,
    item_label                  TEXT,
    item_description            TEXT,
    media_type                  TEXT,
    raw_ordinal                 TEXT,
    parsed_ordinal              REAL,
    sort_order                  REAL,
    publication_date            TEXT,
    previous_qid                TEXT,
    next_qid                    TEXT,
    parent_collection_qid       TEXT,
    parent_collection_label     TEXT,
    is_collection               INTEGER NOT NULL DEFAULT 0,
    is_expanded_from_collection INTEGER NOT NULL DEFAULT 0,
    source_properties_json      TEXT NOT NULL DEFAULT '[]',
    relationships_json          TEXT NOT NULL DEFAULT '[]',
    order_source                TEXT NOT NULL,
    ownership_state             TEXT NOT NULL DEFAULT 'Missing'
        CHECK (ownership_state IN ('Owned','Missing','Provisional','Ambiguous')),
    linked_work_id              BLOB REFERENCES works(id) ON DELETE SET NULL,
    last_hydrated_at            TEXT NOT NULL,
    created_at                  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at                  TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(collection_id, item_qid)
);

CREATE TABLE IF NOT EXISTS series_members (
    series_qid TEXT NOT NULL,
    work_qid   TEXT NOT NULL,
    work_label TEXT,
    position   TEXT,
    owned      INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (series_qid, work_qid)
);

CREATE TABLE IF NOT EXISTS storage_metadata (
    key   TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT OR REPLACE INTO storage_metadata (key, value)
VALUES ('storage_epoch', 'guid-blob-v1');

CREATE TABLE IF NOT EXISTS system_activity (
    id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    occurred_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    action_type  TEXT    NOT NULL,
    collection_name     TEXT,
    entity_id    BLOB,
    entity_type  TEXT,
    profile_id   BLOB,
    changes_json TEXT,
    detail       TEXT,
    ingestion_run_id BLOB
);

CREATE TABLE IF NOT EXISTS text_tracks (
    id                  TEXT PRIMARY KEY,
    asset_id            TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    kind                TEXT NOT NULL CHECK(kind IN ('Lyrics','Subtitles')),
    language            TEXT NOT NULL DEFAULT 'und',
    provider            TEXT NOT NULL,
    confidence          REAL NOT NULL DEFAULT 0,
    source_id           TEXT,
    source_url          TEXT,
    source_format       TEXT NOT NULL,
    normalized_format   TEXT NOT NULL,
    local_path          TEXT NOT NULL,
    sidecar_path        TEXT,
    timing_mode         TEXT NOT NULL DEFAULT 'Line',
    duration_match_score REAL,
    is_hearing_impaired INTEGER NOT NULL DEFAULT 0,
    is_preferred        INTEGER NOT NULL DEFAULT 0,
    is_user_owned       INTEGER NOT NULL DEFAULT 0,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT
);

CREATE TABLE IF NOT EXISTS transaction_log (
    id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    event_type  TEXT    NOT NULL,
    entity_type TEXT    NOT NULL,
    entity_id   BLOB    NOT NULL,  -- UUID of affected entity
    timestamp   TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS ui_settings_cache (
    scope       TEXT NOT NULL PRIMARY KEY,
    settings    TEXT NOT NULL,
    cached_at   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS user_playback_settings (
    profile_id    BLOB NOT NULL PRIMARY KEY REFERENCES profiles(id) ON DELETE CASCADE,
    settings_json TEXT NOT NULL,
    updated_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS user_states (
    user_id             BLOB NOT NULL,
    asset_id            BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    content_hash        TEXT,
    progress_pct        REAL NOT NULL DEFAULT 0.0,
    last_accessed       TEXT NOT NULL DEFAULT (datetime('now')),
    extended_properties TEXT,
    PRIMARY KEY (user_id, asset_id)
);

CREATE TABLE IF NOT EXISTS user_taste_profiles (
    user_id       TEXT NOT NULL PRIMARY KEY,
    profile_json  TEXT NOT NULL DEFAULT '{}',
    summary       TEXT,
    updated_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS wikidata_bridge_candidates (
    id                    BLOB PRIMARY KEY,
    job_id                BLOB NOT NULL REFERENCES identity_jobs(id) ON DELETE CASCADE,
    qid                   TEXT NOT NULL,
    label                 TEXT NOT NULL,
    description           TEXT,
    matched_by            TEXT NOT NULL,
    bridge_id_type        TEXT,
    is_exact_match        INTEGER NOT NULL DEFAULT 0,
    score_total           REAL NOT NULL DEFAULT 0.0,
    score_breakdown_json  TEXT,
    outcome               TEXT NOT NULL DEFAULT 'Rejected',
    created_at            TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS works (
    id                   BLOB    NOT NULL PRIMARY KEY,  -- UUID
    collection_id               BLOB    REFERENCES collections(id) ON DELETE SET NULL,
    media_type           TEXT    NOT NULL,              -- e.g. 'Movies', 'Books'

    -- Parent/child hierarchy
    work_kind            TEXT    NOT NULL DEFAULT 'standalone'
                                 CHECK (work_kind IN ('standalone','parent','child','catalog')),
    parent_work_id       BLOB    REFERENCES works(id) ON DELETE SET NULL,
    ordinal              INTEGER,                       -- track #, episode #, issue #, volume #
    is_catalog_only      INTEGER NOT NULL DEFAULT 0,    -- 1 = no file in library yet
    external_identifiers TEXT,                          -- JSON: {"isbn_13":"...","tmdb_id":"..."}
    display_overrides_json TEXT,                        -- JSON: presentation-only aliases and sort/display overrides

    -- Parent key shadow column for indexed find-or-create lookups.
    -- Populated for parent Works only (work_kind = 'parent') as a normalized
    -- "key1|key2" string the HierarchyResolver uses to dedup albums, shows, and
    -- series before any QID is known. NULL on standalone/child/catalog rows.
    parent_key           TEXT,

    -- Ownership flag. 'Owned' = file is in the library;
    -- 'Unowned' = catalog-only row discovered from Wikidata or a retail
    -- provider but not yet ingested. Kept in sync with is_catalog_only.
    ownership            TEXT NOT NULL DEFAULT 'Owned'
, universe_mismatch INTEGER NOT NULL DEFAULT 0 CHECK (universe_mismatch IN (0, 1)), universe_mismatch_at TEXT, wikidata_status TEXT DEFAULT 'pending', wikidata_checked_at TEXT, wikidata_qid TEXT, curator_state TEXT, rejected_at TEXT, provisional_metadata_json TEXT, match_level TEXT DEFAULT 'work', wikidata_match_source TEXT, wikidata_match_locked INTEGER NOT NULL DEFAULT 0, wikidata_rejected_qids_json TEXT);

CREATE INDEX IF NOT EXISTS idx_api_keys_hashed_key
    ON api_keys (hashed_key);

CREATE INDEX IF NOT EXISTS idx_bridge_ids_entity
    ON bridge_ids(entity_id);

CREATE INDEX IF NOT EXISTS idx_bridge_ids_type_value
    ON bridge_ids(id_type, id_value);

CREATE INDEX IF NOT EXISTS idx_canonical_value_arrays_key_value_entity
    ON canonical_value_arrays(key, value, entity_id);

CREATE INDEX IF NOT EXISTS idx_canonical_values_key_value_entity
    ON canonical_values(key, value, entity_id);

CREATE INDEX IF NOT EXISTS idx_character_portraits_character
    ON character_portraits(fictional_entity_id);

CREATE UNIQUE INDEX IF NOT EXISTS idx_character_portraits_pair
    ON character_portraits(person_id, fictional_entity_id);

CREATE INDEX IF NOT EXISTS idx_character_portraits_person
    ON character_portraits(person_id);

CREATE INDEX IF NOT EXISTS idx_collection_placements_collection_id
    ON collection_placements(collection_id);

CREATE INDEX IF NOT EXISTS idx_collection_placements_location
    ON collection_placements(location);

CREATE INDEX IF NOT EXISTS idx_collection_rel_collection_id
    ON collection_relationships(collection_id);

CREATE INDEX IF NOT EXISTS idx_collection_rel_type_qid
    ON collection_relationships(rel_type, rel_qid);

CREATE INDEX IF NOT EXISTS idx_collections_collection_type ON collections(collection_type);

CREATE INDEX IF NOT EXISTS idx_collections_resolution ON collections(resolution);

CREATE INDEX IF NOT EXISTS idx_collections_rule_hash ON collections(rule_hash);

CREATE INDEX IF NOT EXISTS idx_cva_key_qid
    ON canonical_value_arrays(key, value_qid);

CREATE INDEX IF NOT EXISTS idx_deferred_queue_created
    ON deferred_enrichment_queue(created_at);

CREATE INDEX IF NOT EXISTS idx_deferred_queue_failure_type
    ON deferred_enrichment_queue(failure_type)
    WHERE failure_type IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_deferred_queue_status
    ON deferred_enrichment_queue(status);

CREATE INDEX IF NOT EXISTS idx_editions_work_id
    ON editions(work_id);

CREATE INDEX IF NOT EXISTS idx_encode_jobs_status_schedule
    ON encode_jobs(status, scheduled_for, created_at);

CREATE INDEX IF NOT EXISTS idx_entity_assets_entity
    ON entity_assets(entity_id, entity_type);

CREATE INDEX IF NOT EXISTS idx_entity_assets_type
    ON entity_assets(entity_id, asset_type);

CREATE INDEX IF NOT EXISTS idx_entity_capability_states_entity
ON entity_capability_states(entity_id);

CREATE INDEX IF NOT EXISTS idx_entity_capability_states_status
ON entity_capability_states(capability_id, status, next_retry_at);

CREATE INDEX IF NOT EXISTS idx_entity_rel_object
    ON entity_relationships (object_qid);

CREATE INDEX IF NOT EXISTS idx_entity_rel_subject
    ON entity_relationships (subject_qid);

CREATE INDEX IF NOT EXISTS idx_events_entity ON entity_events(entity_id);

CREATE INDEX IF NOT EXISTS idx_events_entity_stage ON entity_events(entity_id, stage);

CREATE INDEX IF NOT EXISTS idx_events_entity_type ON entity_events(entity_type);

CREATE INDEX IF NOT EXISTS idx_events_occurred ON entity_events(occurred_at);

CREATE INDEX IF NOT EXISTS idx_events_provider ON entity_events(provider_id);

CREATE INDEX IF NOT EXISTS idx_events_type ON entity_events(event_type);

CREATE INDEX IF NOT EXISTS idx_fewl_work_qid
    ON fictional_entity_work_links (work_qid);

CREATE INDEX IF NOT EXISTS idx_fictional_entities_type
    ON fictional_entities (entity_sub_type);

CREATE INDEX IF NOT EXISTS idx_fictional_entities_universe
    ON fictional_entities (fictional_universe_qid);

CREATE INDEX IF NOT EXISTS idx_field_changes_entity ON entity_field_changes(entity_id);

CREATE INDEX IF NOT EXISTS idx_field_changes_event ON entity_field_changes(event_id);

CREATE INDEX IF NOT EXISTS idx_field_changes_field ON entity_field_changes(entity_id, field);

CREATE INDEX IF NOT EXISTS idx_file_hash_cache_sha256
    ON file_hash_cache(sha256);

CREATE INDEX IF NOT EXISTS idx_identity_jobs_entity_id ON identity_jobs (entity_id);

CREATE INDEX IF NOT EXISTS idx_identity_jobs_ingestion_run_id ON identity_jobs (ingestion_run_id);

CREATE INDEX IF NOT EXISTS idx_identity_jobs_lease ON identity_jobs (state, lease_expires_at);

CREATE INDEX IF NOT EXISTS idx_identity_jobs_run_entity_updated
    ON identity_jobs(ingestion_run_id, entity_id, updated_at);

CREATE INDEX IF NOT EXISTS idx_identity_jobs_state ON identity_jobs (state);

CREATE INDEX IF NOT EXISTS idx_image_cache_phash
    ON image_cache(phash) WHERE phash IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_ingestion_batches_created ON ingestion_batches(created_at);

CREATE INDEX IF NOT EXISTS idx_ingestion_batches_status ON ingestion_batches(status);

CREATE INDEX IF NOT EXISTS idx_ingestion_batch_artifacts_batch
    ON ingestion_batch_artifacts(batch_id, occurred_at);

CREATE INDEX IF NOT EXISTS idx_ingestion_batch_artifacts_type
    ON ingestion_batch_artifacts(batch_id, artifact_type);

CREATE INDEX IF NOT EXISTS idx_ingestion_batch_artifacts_artifact
    ON ingestion_batch_artifacts(artifact_type, artifact_id);

CREATE INDEX IF NOT EXISTS idx_ingestion_batch_artifacts_parent
    ON ingestion_batch_artifacts(parent_entity_id);

CREATE INDEX IF NOT EXISTS idx_ingestion_log_media_asset ON ingestion_log(media_asset_id);

CREATE INDEX IF NOT EXISTS idx_ingestion_log_run
    ON ingestion_log(ingestion_run_id);

CREATE INDEX IF NOT EXISTS idx_ingestion_log_run_created
    ON ingestion_log(ingestion_run_id, created_at);

CREATE INDEX IF NOT EXISTS idx_ingestion_log_status
    ON ingestion_log(status);

CREATE INDEX IF NOT EXISTS idx_media_assets_content_hash
    ON media_assets (content_hash);

CREATE INDEX IF NOT EXISTS idx_media_assets_file_path_root
    ON media_assets (file_path_root COLLATE NOCASE);

CREATE INDEX IF NOT EXISTS idx_media_assets_edition_id
    ON media_assets (edition_id);

CREATE INDEX IF NOT EXISTS idx_media_assets_library_id
    ON media_assets(library_id) WHERE library_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_media_assets_orphaned
    ON media_assets(is_orphaned) WHERE is_orphaned = 1;

CREATE INDEX IF NOT EXISTS idx_media_operation_events_batch
ON media_operation_events(batch_id, occurred_at);

CREATE INDEX IF NOT EXISTS idx_media_operation_events_entity
ON media_operation_events(entity_id, occurred_at);

CREATE INDEX IF NOT EXISTS idx_media_operation_events_operation
ON media_operation_events(operation_id, occurred_at);

CREATE INDEX IF NOT EXISTS idx_media_operations_batch
ON media_operations(batch_id, status);

CREATE INDEX IF NOT EXISTS idx_media_operations_capability
ON media_operations(capability_id, status, next_retry_at);

CREATE INDEX IF NOT EXISTS idx_media_operations_entity
ON media_operations(entity_id);

CREATE INDEX IF NOT EXISTS idx_media_operations_lease
ON media_operations(status, lease_expires_at);

CREATE INDEX IF NOT EXISTS idx_media_operations_queue
ON media_operations(queue_name, status, priority, position_key);

CREATE INDEX IF NOT EXISTS idx_media_operations_source_path
ON media_operations(operation_type, source_path, status);

CREATE INDEX IF NOT EXISTS idx_metadata_claims_claimed_at
    ON metadata_claims (claimed_at);

CREATE INDEX IF NOT EXISTS idx_metadata_claims_entity_id
    ON metadata_claims (entity_id);

CREATE INDEX IF NOT EXISTS idx_metadata_claims_provider_id
    ON metadata_claims (provider_id);

CREATE INDEX IF NOT EXISTS idx_offline_variants_asset
    ON offline_variants(asset_id, status);

CREATE INDEX IF NOT EXISTS idx_pending_person_signals_name_role
    ON pending_person_signals (name, role);

CREATE INDEX IF NOT EXISTS idx_person_aliases_real ON person_aliases (real_person_id);

CREATE INDEX IF NOT EXISTS idx_person_group_members_member
    ON person_group_members (member_id);

CREATE INDEX IF NOT EXISTS idx_person_media_links_asset
    ON person_media_links (media_asset_id);

CREATE INDEX IF NOT EXISTS idx_person_media_links_person
    ON person_media_links(person_id);

CREATE INDEX IF NOT EXISTS idx_persons_name
    ON persons (name);

CREATE UNIQUE INDEX IF NOT EXISTS idx_persons_wikidata_qid
    ON persons (wikidata_qid) WHERE wikidata_qid IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_playback_segments_asset
    ON playback_segments(asset_id, kind, start_seconds);

CREATE INDEX IF NOT EXISTS idx_prc_expires ON provider_response_cache (expires_at);

CREATE INDEX IF NOT EXISTS idx_prc_provider ON provider_response_cache (provider_id);

CREATE INDEX IF NOT EXISTS idx_profile_external_logins_profile
    ON profile_external_logins(profile_id);

CREATE INDEX IF NOT EXISTS idx_qid_labels_type ON qid_labels(entity_type);

CREATE INDEX IF NOT EXISTS idx_reader_bookmarks_user_asset
    ON reader_bookmarks (user_id, asset_id);

CREATE INDEX IF NOT EXISTS idx_reader_highlights_user_asset
    ON reader_highlights (user_id, asset_id);

CREATE INDEX IF NOT EXISTS idx_resolver_cache_expires
    ON resolver_cache(expires_at);

CREATE INDEX IF NOT EXISTS idx_retail_candidates_job_id ON retail_match_candidates (job_id);

CREATE INDEX IF NOT EXISTS idx_retail_candidates_outcome ON retail_match_candidates (outcome);

CREATE INDEX IF NOT EXISTS idx_review_queue_entity_id
    ON review_queue (entity_id);

CREATE INDEX IF NOT EXISTS idx_review_queue_status
    ON review_queue (status);

CREATE UNIQUE INDEX IF NOT EXISTS ux_review_queue_pending_entity_trigger
    ON review_queue (entity_id, trigger)
    WHERE status = 'Pending';

CREATE INDEX IF NOT EXISTS idx_series_manifest_hydrations_collection
    ON series_manifest_hydrations(collection_id);

CREATE INDEX IF NOT EXISTS idx_series_manifest_items_collection
    ON series_manifest_items(collection_id);

CREATE INDEX IF NOT EXISTS idx_series_manifest_items_item_qid
    ON series_manifest_items(item_qid);

CREATE INDEX IF NOT EXISTS idx_series_manifest_items_linked_work
    ON series_manifest_items(linked_work_id);

CREATE INDEX IF NOT EXISTS idx_series_manifest_items_ownership
    ON series_manifest_items(ownership_state);

CREATE INDEX IF NOT EXISTS idx_series_manifest_items_series_qid
    ON series_manifest_items(series_qid);

CREATE INDEX IF NOT EXISTS idx_series_members_series
    ON series_members(series_qid);

CREATE INDEX IF NOT EXISTS idx_system_activity_action_type
    ON system_activity (action_type);

CREATE INDEX IF NOT EXISTS idx_system_activity_occurred_at
    ON system_activity (occurred_at);

CREATE INDEX IF NOT EXISTS idx_text_tracks_asset_kind
    ON text_tracks(asset_id, kind);

CREATE INDEX IF NOT EXISTS idx_text_tracks_preferred
    ON text_tracks(asset_id, kind, language, is_preferred);

CREATE INDEX IF NOT EXISTS idx_wikidata_candidates_job_id ON wikidata_bridge_candidates (job_id);

CREATE INDEX IF NOT EXISTS idx_wikidata_candidates_outcome ON wikidata_bridge_candidates (outcome);

CREATE INDEX IF NOT EXISTS idx_works_collection_id
    ON works (collection_id);

CREATE INDEX IF NOT EXISTS idx_works_media_type
    ON works (media_type);

CREATE INDEX IF NOT EXISTS idx_works_ownership_media_type
    ON works(ownership, media_type);

CREATE INDEX IF NOT EXISTS idx_works_parent_key
    ON works(media_type, parent_key) WHERE parent_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_works_parent_work_id ON works(parent_work_id);

CREATE INDEX IF NOT EXISTS idx_works_wikidata_qid
                    ON works(wikidata_qid) WHERE wikidata_qid IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_works_work_kind      ON works(work_kind) WHERE work_kind != 'standalone';

CREATE UNIQUE INDEX IF NOT EXISTS ux_entity_capability_states_key
ON entity_capability_states(entity_id, capability_id, COALESCE(sub_key, ''));

CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_jobs_entity_pass_active
    ON identity_jobs (entity_id, pass)
    WHERE state NOT IN ('Ready', 'ReadyWithoutUniverse', 'Failed', 'RetailNoMatch', 'QidNoMatch', 'QidNeedsReview');

CREATE UNIQUE INDEX IF NOT EXISTS ux_media_operations_idempotency
ON media_operations(idempotency_key);

