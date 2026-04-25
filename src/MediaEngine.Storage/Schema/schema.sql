-- =============================================================================
-- Tuvima Library â€” SQLite Initialisation Script
-- Phase 4: Storage Schema & Persistent State
--
-- Conventions
--   â€¢ UUIDs are stored as TEXT (SQLite has no native UUID type).
--   â€¢ BOOLEAN is stored as INTEGER: 1 = true, 0 = false.
--   â€¢ DATETIME is stored as TEXT in ISO-8601 format (datetime('now') default).
--   â€¢ All CREATE statements are idempotent (IF NOT EXISTS).
--   â€¢ Foreign keys are enforced; enable them per-connection with PRAGMA.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Connection-level PRAGMAs
-- These are emitted here so the file can be run standalone (e.g. via sqlite3
-- CLI).  DatabaseConnection.cs also sets them programmatically on Open().
-- ---------------------------------------------------------------------------
PRAGMA journal_mode = WAL;      -- Write-Ahead Logging (spec: failure handling)
PRAGMA foreign_keys = ON;       -- Enforce FK constraints
PRAGMA temp_store  = MEMORY;    -- Keep temp tables in RAM


-- =============================================================================
-- 1. SYSTEM & PROVIDER MANAGEMENT
-- =============================================================================

CREATE TABLE IF NOT EXISTS provider_registry (
    id         TEXT    NOT NULL PRIMARY KEY,  -- UUID
    name       TEXT    NOT NULL UNIQUE,
    version    TEXT    NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1     -- BOOLEAN: 1=true, 0=false
);

-- Key-value bag for per-provider configuration (api keys, base urls, etc.)
-- Composite PK prevents duplicate keys for the same provider.
CREATE TABLE IF NOT EXISTS provider_config (
    provider_id TEXT    NOT NULL REFERENCES provider_registry(id) ON DELETE CASCADE,
    key         TEXT    NOT NULL,
    value       TEXT    NOT NULL,
    is_secret   INTEGER NOT NULL DEFAULT 0,   -- BOOLEAN: marks credentials
    PRIMARY KEY (provider_id, key)
);


-- =============================================================================
-- 2. MEDIA CORE & COLLECTIONS
-- =============================================================================

CREATE TABLE IF NOT EXISTS collections (
    id                TEXT NOT NULL PRIMARY KEY,  -- UUID
    universe_id       TEXT,                       -- NULLABLE: cross-hub grouping
    parent_collection_id     TEXT REFERENCES collections(id) ON DELETE SET NULL,  -- franchise parent
    display_name      TEXT,                       -- Phase 7: human-readable hub name
    collection_type          TEXT NOT NULL DEFAULT 'Universe',
    description       TEXT,
    icon_name         TEXT,
    scope             TEXT NOT NULL DEFAULT 'library',
    profile_id        TEXT REFERENCES profiles(id) ON DELETE CASCADE,
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
);

-- Curated item membership for System Lists, Playlists, and Mixes.
-- Separate from works.collection_id which is used by Series/Universe collections.
CREATE TABLE IF NOT EXISTS collection_items (
    id                TEXT NOT NULL PRIMARY KEY,  -- UUID
    collection_id            TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    work_id           TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    sort_order        INTEGER NOT NULL DEFAULT 0,
    progress_state    TEXT NOT NULL DEFAULT 'not_started'
                          CHECK (progress_state IN ('not_started','in_progress','completed')),
    progress_position TEXT,
    added_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Placement metadata for surfacing collections in different UI locations.
CREATE TABLE IF NOT EXISTS collection_placements (
    id            TEXT PRIMARY KEY,
    collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    location      TEXT NOT NULL,
    position      INTEGER NOT NULL DEFAULT 0,
    display_limit INTEGER NOT NULL DEFAULT 0,
    display_mode  TEXT NOT NULL DEFAULT 'swimlane',
    is_visible    INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_collection_placements_collection_id
    ON collection_placements(collection_id);
CREATE INDEX IF NOT EXISTS idx_collection_placements_location
    ON collection_placements(location);

-- External universe/franchise relationships attached to collections.
CREATE TABLE IF NOT EXISTS collection_relationships (
    id            TEXT NOT NULL PRIMARY KEY,
    collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
    rel_type      TEXT NOT NULL,
    rel_qid       TEXT NOT NULL,
    rel_label     TEXT,
    confidence    REAL NOT NULL DEFAULT 0.9,
    discovered_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_collection_rel_type_qid
    ON collection_relationships(rel_type, rel_qid);
CREATE INDEX IF NOT EXISTS idx_collection_rel_collection_id
    ON collection_relationships(collection_id);

-- Works form a parent/child hierarchy among themselves (M-081):
--   • An album is a Parent Work; its tracks are Child Works whose
--     parent_work_id points at the album row.
--   • A TV show is a Parent of seasons, which are themselves Parents
--     of episodes — three rows linked by parent_work_id.
--   • A standalone movie or single-volume novel has work_kind='standalone'
--     and parent_work_id=NULL.
--   • A Catalog row exists when Wikidata or a retail provider knows about
--     a title that the user does not yet have a file for.
--
-- collection_id is the legacy ContentGroup link, kept until Phase 4 collapses
-- the collection system onto the parent_work_id hierarchy.
CREATE TABLE IF NOT EXISTS works (
    id                   TEXT    NOT NULL PRIMARY KEY,  -- UUID
    collection_id               TEXT    REFERENCES collections(id) ON DELETE SET NULL,
    media_type           TEXT    NOT NULL,              -- e.g. 'Movies', 'Books'

    -- M-081: parent/child hierarchy
    work_kind            TEXT    NOT NULL DEFAULT 'standalone'
                                 CHECK (work_kind IN ('standalone','parent','child','catalog')),
    parent_work_id       TEXT    REFERENCES works(id) ON DELETE SET NULL,
    ordinal              INTEGER,                       -- track #, episode #, issue #, volume #
    is_catalog_only      INTEGER NOT NULL DEFAULT 0,    -- 1 = no file in library yet
    external_identifiers TEXT,                          -- JSON: {"isbn_13":"...","tmdb_id":"..."}
    display_overrides_json TEXT,                        -- JSON: presentation-only aliases and sort/display overrides

    -- M-082: parent_key shadow column for indexed find-or-create lookups.
    -- Populated for parent Works only (work_kind = 'parent') as a normalized
    -- "key1|key2" string the HierarchyResolver uses to dedup albums, shows, and
    -- series before any QID is known. NULL on standalone/child/catalog rows.
    parent_key           TEXT,

    -- M-083: ownership flag. 'Owned' = file is in the library;
    -- 'Unowned' = catalog-only row discovered from Wikidata or a retail
    -- provider but not yet ingested. Kept in sync with is_catalog_only.
    ownership            TEXT NOT NULL DEFAULT 'Owned'
);

CREATE INDEX IF NOT EXISTS idx_works_parent_work_id ON works(parent_work_id);
CREATE INDEX IF NOT EXISTS idx_works_work_kind      ON works(work_kind) WHERE work_kind != 'standalone';
CREATE INDEX IF NOT EXISTS idx_works_parent_key
    ON works(media_type, parent_key) WHERE parent_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_works_ownership_media_type
    ON works(ownership, media_type);

CREATE TABLE IF NOT EXISTS editions (
    id           TEXT NOT NULL PRIMARY KEY,  -- UUID
    work_id      TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    format_label TEXT                        -- e.g. '4K Bluray', 'First Edition'
);

-- content_hash is the primary reconciliation key (spec: Hash Dominance invariant).
-- Media binaries MUST NOT be stored here; file_path_root points to the FS.
-- status reflects the ingestion lifecycle (Phase 7): Normal | Conflicted | Orphaned.
CREATE TABLE IF NOT EXISTS media_assets (
    id             TEXT NOT NULL PRIMARY KEY,  -- UUID
    edition_id     TEXT NOT NULL REFERENCES editions(id) ON DELETE CASCADE,
    content_hash   TEXT NOT NULL UNIQUE,       -- reconciliation key; SHA-256 hex, lowercase
    file_path_root TEXT NOT NULL,              -- FS path, no BLOBs
    status         TEXT NOT NULL DEFAULT 'Normal'
                       CHECK (status IN ('Normal', 'Conflicted', 'Orphaned')),

    -- ── Auto re-tag sweep state (M-084) ──────────────────────────────
    -- writeback_fields_hash: SHA-256 of (writeback-fields.json slice for
    -- this asset's media type) + tagger version constant. NULL when the
    -- asset has never been re-tagged through the sweep.
    writeback_fields_hash    TEXT,
    -- writeback_status: 'ok' | 'pending' | 'retry' | 'failed'. NULL until
    -- the sweep first touches the asset.
    writeback_status         TEXT,
    writeback_last_error     TEXT,
    writeback_attempts       INTEGER NOT NULL DEFAULT 0,
    -- writeback_next_retry_at: unix epoch seconds — used by the sweep
    -- worker to skip rows whose retry window hasn't opened yet.
    writeback_next_retry_at  INTEGER
);


-- =============================================================================
-- 3. CANONICAL METADATA & CLAIMS
-- =============================================================================

-- Append-only claim log.  Rows MUST NOT be deleted when a new claim arrives;
-- historical claims enable re-scoring when provider weights change.
-- entity_id is a polymorphic FK pointing to either works.id or editions.id;
-- SQLite cannot enforce polymorphic FKs, so it is left as plain TEXT.
CREATE TABLE IF NOT EXISTS metadata_claims (
    id          TEXT NOT NULL PRIMARY KEY,  -- UUID
    entity_id   TEXT NOT NULL,              -- FK â†’ works.id | editions.id (polymorphic)
    provider_id TEXT NOT NULL REFERENCES provider_registry(id),
    claim_key   TEXT NOT NULL,
    claim_value TEXT NOT NULL,
    confidence  REAL NOT NULL DEFAULT 1.0,
    -- Timestamp used by the scoring engine for stale-claim time-decay.
    -- Spec: Phase 6 â€“ Stale Claim Handling.
    claimed_at     TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    -- When 1, the scoring engine treats this claim as unconditional winner
    -- (confidence 1.0); no automated provider may set this to 1.
    -- Spec: Phase 8 â€“ Field-Level Arbitration Â§ User-Locked Claims.
    is_user_locked INTEGER NOT NULL DEFAULT 0
                       CHECK (is_user_locked IN (0, 1))
);

-- Composite PK (entity_id, key) forms the property-bag for canonical values.
-- Every row here MUST be derivable from â‰¥1 rows in metadata_claims
-- (spec: Canonical Integrity invariant).
CREATE TABLE IF NOT EXISTS canonical_values (
    entity_id           TEXT    NOT NULL,
    key                 TEXT    NOT NULL,
    value               TEXT    NOT NULL,
    last_scored_at      TEXT    NOT NULL,
    -- Phase B: tracks whether the scoring engine could not pick a clear winner.
    -- 1 = conflicted (runner-up within epsilon of winner); 0 = resolved.
    is_conflicted       INTEGER NOT NULL DEFAULT 0
                            CHECK (is_conflicted IN (0, 1)),
    -- Unit 4: the provider that supplied the winning claim for this field.
    -- NULL for user-locked values or rows created before this column was added.
    winning_provider_id TEXT,
    -- Unit 5: field-level review flag.  1 = needs human review (conflicted,
    -- missing expected field, or local-only source with low confidence); 0 = ok.
    needs_review        INTEGER NOT NULL DEFAULT 0
                            CHECK (needs_review IN (0, 1)),
    PRIMARY KEY (entity_id, key)
);


-- =============================================================================
-- 4. USER & OPERATIONS
-- =============================================================================

-- Composite PK (user_id, asset_id) binds progress to a specific media asset.
-- Reconciliation is via media_assets.content_hash, ensuring user_states survive
-- file moves (spec: Hash Dominance invariant â€“ enforced at application layer).
CREATE TABLE IF NOT EXISTS user_states (
    user_id             TEXT NOT NULL,
    asset_id            TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    content_hash        TEXT,
    progress_pct        REAL NOT NULL DEFAULT 0.0,
    last_accessed       TEXT NOT NULL DEFAULT (datetime('now')),
    extended_properties TEXT,
    PRIMARY KEY (user_id, asset_id)
);

-- Audit trail for system-level entity changes.
-- AUTOINCREMENT ensures monotonically increasing IDs for ordered pruning.
CREATE TABLE IF NOT EXISTS transaction_log (
    id          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    event_type  TEXT    NOT NULL,
    entity_type TEXT    NOT NULL,
    entity_id   TEXT    NOT NULL,  -- UUID of affected entity
    timestamp   TEXT    NOT NULL DEFAULT (datetime('now'))
);


-- =============================================================================
-- 5. SECURITY
-- =============================================================================

-- Inbound API keys for external integrations (Radarr, Sonarr, automation scripts).
-- Only the SHA-256 hex hash of the plaintext key is stored; plaintext is NEVER persisted.
-- Label is shown to the admin; hashed_key is used solely for authentication lookups.
CREATE TABLE IF NOT EXISTS api_keys (
    id          TEXT NOT NULL PRIMARY KEY,       -- UUID
    label       TEXT NOT NULL,                   -- human-readable, e.g. "Radarr Integration"
    hashed_key  TEXT NOT NULL UNIQUE,            -- SHA-256 hex of the plaintext key
    role        TEXT NOT NULL DEFAULT 'Administrator'
                    CHECK (role IN ('Administrator', 'Curator', 'Consumer')),
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);


-- =============================================================================
-- 6. PROFILES (Identity & Multi-User)
-- Spec: Settings & Management Layer — Identity & Multi-User
-- =============================================================================

CREATE TABLE IF NOT EXISTS profiles (
    id           TEXT NOT NULL PRIMARY KEY,  -- UUID
    display_name TEXT NOT NULL,
    avatar_color TEXT NOT NULL DEFAULT '#7C4DFF',
    role         TEXT NOT NULL DEFAULT 'Consumer'
                     CHECK (role IN ('Administrator', 'Curator', 'Consumer')),
    pin_hash     TEXT,                       -- SHA-256 of 4-digit PIN; NULL = no PIN set
    created_at   TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS profile_external_logins (
    id            TEXT NOT NULL PRIMARY KEY,
    profile_id    TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    provider      TEXT NOT NULL,
    subject       TEXT NOT NULL,
    email         TEXT,
    display_name  TEXT,
    linked_at     TEXT NOT NULL,
    last_login_at TEXT,
    UNIQUE(provider, subject)
);

CREATE INDEX IF NOT EXISTS idx_profile_external_logins_profile
    ON profile_external_logins(profile_id);


-- =============================================================================
-- 7. PERSONS & PERSON-ASSET LINKS
-- Spec: Phase 9 - Recursive Person Enrichment
-- =============================================================================

-- Authors, narrators, and directors linked to media assets.
-- Persons are created when file metadata contains author/narrator fields and
-- enriched asynchronously via the Wikidata adapter.
-- Roles are stored in the person_roles junction table (multi-role support).
CREATE TABLE IF NOT EXISTS persons (
    id                TEXT    NOT NULL PRIMARY KEY,  -- UUID
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
);

-- Junction table for person roles (multi-role support).
-- A single person can have multiple roles (e.g. Clint Eastwood = Director + Actor).
CREATE TABLE IF NOT EXISTS person_roles (
    person_id TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
    role      TEXT NOT NULL CHECK (role IN (
                  'Author','Narrator','Director',
                  'Actor','Voice Actor','Composer',
                  'Artist','Performer')),
    PRIMARY KEY (person_id, role)
);

-- Junction table linking persons to the media assets they contributed to.
-- Uses media_assets.id (not works.id) because Work entities are not yet
-- created by the ingestion pipeline (pre-existing Phase 7 gap).
-- Composite PK prevents duplicate links for the same (asset, person, role).
CREATE TABLE IF NOT EXISTS person_media_links (
    media_asset_id  TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    person_id       TEXT NOT NULL REFERENCES persons(id)       ON DELETE CASCADE,
    role            TEXT NOT NULL,
    PRIMARY KEY (media_asset_id, person_id, role)
);

-- M-057: Pending person signals.
-- Stores unverified person names extracted during ingestion, between inline
-- extraction and deferred batch Wikidata verification.
CREATE TABLE IF NOT EXISTS pending_person_signals (
    id          TEXT NOT NULL PRIMARY KEY,
    entity_id   TEXT NOT NULL,
    name        TEXT NOT NULL,
    role        TEXT NOT NULL,
    source      TEXT NOT NULL,
    pattern     TEXT,
    media_type  TEXT NOT NULL,
    created_at  TEXT NOT NULL
);


-- =============================================================================
-- INDICES
-- Spec: O(log n) lookup on content_hash, entity_id (claims), collection_id (works)
-- =============================================================================

CREATE INDEX IF NOT EXISTS idx_media_assets_content_hash
    ON media_assets (content_hash);

CREATE INDEX IF NOT EXISTS idx_metadata_claims_entity_id
    ON metadata_claims (entity_id);

CREATE INDEX IF NOT EXISTS idx_works_collection_id
    ON works (collection_id);

CREATE INDEX IF NOT EXISTS idx_api_keys_hashed_key
    ON api_keys (hashed_key);

CREATE INDEX IF NOT EXISTS idx_persons_name
    ON persons (name);

CREATE UNIQUE INDEX IF NOT EXISTS idx_persons_wikidata_qid
    ON persons (wikidata_qid) WHERE wikidata_qid IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_person_media_links_asset
    ON person_media_links (media_asset_id);

CREATE INDEX IF NOT EXISTS idx_pending_person_signals_name_role
    ON pending_person_signals (name, role);

CREATE INDEX IF NOT EXISTS idx_collection_items_collection
    ON hub_items (collection_id);

CREATE INDEX IF NOT EXISTS idx_hubs_collection_type
    ON hubs (collection_type);


-- ── FTS5 Search Index ──────────────────────────────────────────────────────────
-- Multi-language full-text search over work titles, original titles, alternate
-- titles (Wikidata aliases), authors, and descriptions. Populated by the
-- SearchIndexService after scoring completes. Supports cross-language search
-- for items like "Amélie" and "Le Fabuleux Destin d'Amélie Poulain".
--
CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
    entity_id UNINDEXED,
    title,
    original_title,
    alternate_titles,
    author,
    description,
    tokenize = 'trigram'
);
