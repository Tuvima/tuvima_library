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
-- 2. MEDIA CORE & HUBS
-- =============================================================================

CREATE TABLE IF NOT EXISTS hubs (
    id           TEXT NOT NULL PRIMARY KEY,  -- UUID
    universe_id  TEXT,                       -- NULLABLE: cross-hub grouping
    display_name TEXT,                       -- Phase 7: human-readable hub name
    created_at   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- hub_id is NULLABLE so that ON DELETE SET NULL can satisfy the spec invariant:
--   "A deletion of a Hub MUST trigger a cascade or re-assignment of all
--    associated Works to an 'Unassigned' state."
-- NULL hub_id == Unassigned.
CREATE TABLE IF NOT EXISTS works (
    id             TEXT    NOT NULL PRIMARY KEY,  -- UUID
    hub_id         TEXT    REFERENCES hubs(id) ON DELETE SET NULL,
    media_type     TEXT    NOT NULL,              -- e.g. 'MOVIE', 'EPUB'
    sequence_index INTEGER                        -- NULLABLE: series ordering
);

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
                       CHECK (status IN ('Normal', 'Conflicted', 'Orphaned'))
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
    entity_id      TEXT    NOT NULL,
    key            TEXT    NOT NULL,
    value          TEXT    NOT NULL,
    last_scored_at TEXT    NOT NULL,
    -- Phase B: tracks whether the scoring engine could not pick a clear winner.
    -- 1 = conflicted (runner-up within epsilon of winner); 0 = resolved.
    is_conflicted  INTEGER NOT NULL DEFAULT 0
                       CHECK (is_conflicted IN (0, 1)),
    PRIMARY KEY (entity_id, key)
);


-- =============================================================================
-- 4. USER & OPERATIONS
-- =============================================================================

-- Composite PK (user_id, asset_id) binds progress to a specific media asset.
-- Reconciliation is via media_assets.content_hash, ensuring user_states survive
-- file moves (spec: Hash Dominance invariant â€“ enforced at application layer).
CREATE TABLE IF NOT EXISTS user_states (
    user_id       TEXT NOT NULL,
    asset_id      TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
    progress_pct  REAL NOT NULL DEFAULT 0.0,
    last_accessed TEXT NOT NULL DEFAULT (datetime('now')),
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


-- =============================================================================
-- 7. PERSONS & PERSON-ASSET LINKS
-- Spec: Phase 9 - Recursive Person Enrichment
-- =============================================================================

-- Authors, narrators, and directors linked to media assets.
-- Persons are created when file metadata contains author/narrator fields and
-- enriched asynchronously via the Wikidata adapter.
-- role CHECK mirrors the valid values accepted by IPersonRepository.
CREATE TABLE IF NOT EXISTS persons (
    id           TEXT NOT NULL PRIMARY KEY,  -- UUID
    name         TEXT NOT NULL,
    role         TEXT NOT NULL CHECK (role IN (
                     'Author', 'Narrator', 'Director',
                     'Illustrator', 'Cast Member', 'Voice Actor',
                     'Screenwriter', 'Composer')),
    wikidata_qid TEXT,                       -- e.g. Q42
    headshot_url TEXT,                       -- Wikimedia Commons image URL
    biography    TEXT,                       -- Wikidata entity description
    occupation   TEXT,                       -- Wikidata P106 (e.g. "Writer", "Actor")
    instagram    TEXT,                       -- Wikidata P2003 (Instagram handle)
    twitter      TEXT,                       -- Wikidata P2002 (Twitter/X handle)
    tiktok       TEXT,                       -- Wikidata P7085 (TikTok handle)
    mastodon     TEXT,                       -- Wikidata P4033 (Mastodon address)
    website      TEXT,                       -- Wikidata P856 (Official website URL)
    created_at   TEXT NOT NULL,              -- ISO-8601
    enriched_at  TEXT                        -- NULL = not yet enriched
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


-- =============================================================================
-- INDICES
-- Spec: O(log n) lookup on content_hash, entity_id (claims), hub_id (works)
-- =============================================================================

CREATE INDEX IF NOT EXISTS idx_media_assets_content_hash
    ON media_assets (content_hash);

CREATE INDEX IF NOT EXISTS idx_metadata_claims_entity_id
    ON metadata_claims (entity_id);

CREATE INDEX IF NOT EXISTS idx_works_hub_id
    ON works (hub_id);

CREATE INDEX IF NOT EXISTS idx_api_keys_hashed_key
    ON api_keys (hashed_key);

CREATE INDEX IF NOT EXISTS idx_persons_name
    ON persons (name);

CREATE INDEX IF NOT EXISTS idx_person_media_links_asset
    ON person_media_links (media_asset_id);