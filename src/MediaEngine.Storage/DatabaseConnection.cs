using System.Reflection;
using MediaEngine.Domain;
using Microsoft.Data.Sqlite;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Manages the lifecycle of the SQLite connection.
/// Implements WAL mode, startup PRAGMAs, and idempotent schema initialisation.
/// ORM-less: all SQL is executed directly via <see cref="SqliteCommand"/>.
/// Spec: Phase 4 - IDatabaseConnection interface.
/// </summary>
public sealed class DatabaseConnection : IDatabaseConnection
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SqliteConnection? _connection;

    /// <param name="databasePath">
    /// Absolute or relative path to the <c>.db</c> file.
    /// Typically sourced from <c>LegacyManifest.DatabasePath</c>.
    /// </param>
    public DatabaseConnection(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    // -------------------------------------------------------------------------
    // IDatabaseConnection
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public SqliteConnection Open()
    {
        if (_connection is not null)
            return _connection;

        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();

        // Spec: "SQLite MUST be configured in Write-Ahead Logging mode."
        // Also enforce foreign keys and keep temp tables in RAM.
        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText =
            "PRAGMA journal_mode = WAL; " +
            "PRAGMA foreign_keys = ON; " +
            "PRAGMA temp_store = MEMORY;";
        pragmaCmd.ExecuteNonQuery();

        return _connection;
    }

    /// <inheritdoc/>
    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection($"Data Source={_databasePath}");
        conn.Open();

        using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText =
            "PRAGMA journal_mode = WAL; " +
            "PRAGMA foreign_keys = ON; " +
            "PRAGMA temp_store = MEMORY; " +
            "PRAGMA busy_timeout = 5000;";
        pragmaCmd.ExecuteNonQuery();

        return conn;
    }

    /// <inheritdoc/>
    public void InitializeSchema()
    {
        var conn = Open();
        MigrateMetadataProvidersTable(conn);

        var ddl = LoadEmbeddedSchema();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>PRAGMA integrity_check</c> returns anything other than "ok".
    /// </exception>
    public void RunStartupChecks()
    {
        var conn = Open();

        // PRAGMA integrity_check
        using var integrityCmd = conn.CreateCommand();
        integrityCmd.CommandText = "PRAGMA integrity_check;";
        var result = integrityCmd.ExecuteScalar()?.ToString();

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"SQLite integrity_check failed for '{_databasePath}': {result}");

        // PRAGMA optimize - hints the query planner; safe to run on every start.
        using var optimizeCmd = conn.CreateCommand();
        optimizeCmd.CommandText = "PRAGMA optimize;";
        optimizeCmd.ExecuteNonQuery();

        // -- Incremental schema migrations ----------------------------------------
        // Each migration is guarded by a column-presence or table-presence check
        // so it is safe to run on every startup (idempotent).

        // Migration M-001: Phase 8 - add is_user_locked to metadata_claims.
        // Databases created before Phase 8 will not have this column; the ALTER
        // TABLE adds it with DEFAULT 0 so all existing rows are treated as unlocked.
        MigrateAddColumnIfMissing(
            conn,
            table:  "metadata_claims",
            column: "is_user_locked",
            ddl:    "ALTER TABLE metadata_claims " +
                    "ADD COLUMN is_user_locked INTEGER NOT NULL DEFAULT 0 " +
                    "CHECK (is_user_locked IN (0, 1));");

        // Migration M-002: Phase 9 - create persons table.
        // New in Phase 9; not present in databases created before this phase.
        // Uses PRAGMA table_info as a proxy for table existence (checks for 'id' column).
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "persons",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS persons (
                    id           TEXT NOT NULL PRIMARY KEY,
                    name         TEXT NOT NULL,
                    role         TEXT NOT NULL CHECK (role IN ('Author', 'Narrator', 'Director')),
                    wikidata_qid TEXT,
                    headshot_url TEXT,
                    biography    TEXT,
                    created_at   TEXT NOT NULL,
                    enriched_at  TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_persons_name ON persons (name);
                """);

        // Migration M-003: Phase 9 - create person_media_links table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "person_media_links",
            probeColumn: "person_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS person_media_links (
                    media_asset_id  TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
                    person_id       TEXT NOT NULL REFERENCES persons(id)       ON DELETE CASCADE,
                    role            TEXT NOT NULL,
                    PRIMARY KEY (media_asset_id, person_id, role)
                );
                CREATE INDEX IF NOT EXISTS idx_person_media_links_asset
                    ON person_media_links (media_asset_id);
                """);

        // Migration M-004: Phase 7 - add display_name to collections.
        // Databases created before Phase 7 will not have this column; the ALTER
        // TABLE adds it as nullable so all existing rows are treated as unnamed.
        MigrateAddColumnIfMissing(
            conn,
            table:  "hubs",
            column: "display_name",
            ddl:    "ALTER TABLE hubs ADD COLUMN display_name TEXT;");

        // Migration M-005: Settings & Management Layer - create profiles table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "profiles",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS profiles (
                    id           TEXT NOT NULL PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    avatar_color TEXT NOT NULL DEFAULT '#7C4DFF',
                    role         TEXT NOT NULL DEFAULT 'Consumer'
                                     CHECK (role IN ('Administrator', 'Curator', 'Consumer')),
                    pin_hash     TEXT,
                    created_at   TEXT NOT NULL
                );
                """);

        // Migration M-005b: profile-to-SSO account bindings.
        // Local profiles stay as the app persona; external OIDC/OAuth accounts
        // are linked here by provider + subject so providers can be swapped or
        // added without changing user-owned library state.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "profile_external_logins",
            probeColumn: "id",
            ddl: """
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
                """);

        // Migration M-006: Phase A Security — add role column to api_keys.
        // Databases created before Phase A will not have this column; the ALTER
        // TABLE adds it with DEFAULT 'Administrator' so all existing keys retain
        // full access.  New keys can be assigned Curator or Consumer roles.
        MigrateAddColumnIfMissing(
            conn,
            table:  "api_keys",
            column: "role",
            ddl:    "ALTER TABLE api_keys ADD COLUMN role TEXT NOT NULL DEFAULT 'Administrator' " +
                    "CHECK (role IN ('Administrator', 'Curator', 'Consumer'));");

        // Migration M-007: Phase B — add is_conflicted column to canonical_values.
        // Tracks whether the scoring engine could not pick a clear winner for
        // a given metadata field.  Existing rows default to 0 (not conflicted);
        // only re-scored entities will have accurate conflict flags.
        MigrateAddColumnIfMissing(
            conn,
            table:  "canonical_values",
            column: "is_conflicted",
            ddl:    "ALTER TABLE canonical_values ADD COLUMN is_conflicted INTEGER NOT NULL DEFAULT 0 " +
                    "CHECK (is_conflicted IN (0, 1));");

        // Migration M-008: Activity Ledger — create system_activity table.
        // Rich activity log with JSON change details, user attribution, and
        // collection context.  Replaces the limited transaction_log for detailed audit.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "system_activity",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS system_activity (
                    id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    occurred_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    action_type  TEXT    NOT NULL,
                    collection_name     TEXT,
                    entity_id    TEXT,
                    entity_type  TEXT,
                    profile_id   TEXT,
                    changes_json TEXT,
                    detail       TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_system_activity_occurred_at
                    ON system_activity (occurred_at);
                CREATE INDEX IF NOT EXISTS idx_system_activity_action_type
                    ON system_activity (action_type);
                """);

        // Migration M-009: Expand persons role CHECK constraint.
        // Adds Actor, Voice Actor, Composer.
        // SQLite doesn't support ALTER TABLE to modify CHECK constraints,
        // so we recreate the table with the expanded constraint.
        // Foreign keys must be disabled during the table swap.
        MigrateExpandPersonRoles(conn);

        // Migration M-010: Add social link columns to persons.
        // These nullable TEXT columns store Person-scoped social bridge data
        // from Wikidata: occupation (P106), Instagram (P2003), Twitter (P2002),
        // TikTok (P7085), Mastodon (P4033), Website (P856).
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "occupation",
            ddl:    "ALTER TABLE persons ADD COLUMN occupation TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "instagram",
            ddl:    "ALTER TABLE persons ADD COLUMN instagram TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "twitter",
            ddl:    "ALTER TABLE persons ADD COLUMN twitter TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "tiktok",
            ddl:    "ALTER TABLE persons ADD COLUMN tiktok TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "mastodon",
            ddl:    "ALTER TABLE persons ADD COLUMN mastodon TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "website",
            ddl:    "ALTER TABLE persons ADD COLUMN website TEXT;");

        // Migration M-011: Create ui_settings_cache table for UI configuration caching.
        // Stores the JSON blob for each scope (global, device:{class}, profile:{uuid})
        // so API reads do not require filesystem I/O on every request.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "ui_settings_cache",
            probeColumn: "scope",
            ddl: """
                CREATE TABLE IF NOT EXISTS ui_settings_cache (
                    scope       TEXT NOT NULL PRIMARY KEY,
                    settings    TEXT NOT NULL,
                    cached_at   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
                );
                """);

        // Migration M-012: Add navigation_config column to profiles.
        // Stores per-profile navigation preferences (action cluster, tray layout)
        // as an opaque JSON blob.  The Engine stores it; the Dashboard interprets it.
        MigrateAddColumnIfMissing(
            conn,
            table:  "profiles",
            column: "navigation_config",
            ddl:    "ALTER TABLE profiles ADD COLUMN navigation_config TEXT;");

        MigrateAddColumnIfMissing(
            conn,
            table:  "profiles",
            column: "avatar_image_path",
            ddl:    "ALTER TABLE profiles ADD COLUMN avatar_image_path TEXT;");

        // Migration M-013: Hydration Pipeline — create review_queue + image_cache tables.
        // review_queue stores metadata items requiring user intervention (disambiguation,
        // low confidence, manual fix).  image_cache tracks downloaded image content hashes
        // to prevent redundant re-downloads across entities.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "review_queue",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS review_queue (
                    id               TEXT NOT NULL PRIMARY KEY,
                    entity_id        TEXT NOT NULL,
                    entity_type      TEXT NOT NULL,
                    trigger          TEXT NOT NULL,
                    status           TEXT NOT NULL DEFAULT 'Pending'
                                         CHECK (status IN ('Pending', 'Resolved', 'Dismissed')),
                    proposed_hub_id  TEXT,
                    confidence_score REAL,
                    candidates_json  TEXT,
                    detail           TEXT,
                    created_at       TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    resolved_at      TEXT,
                    resolved_by      TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_review_queue_status
                    ON review_queue (status);
                CREATE INDEX IF NOT EXISTS idx_review_queue_entity_id
                    ON review_queue (entity_id);
                """);

        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "image_cache",
            probeColumn: "content_hash",
            ddl: """
                CREATE TABLE IF NOT EXISTS image_cache (
                    content_hash  TEXT NOT NULL PRIMARY KEY,
                    file_path     TEXT NOT NULL,
                    source_url    TEXT,
                    downloaded_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
                );
                """);

        // Migration M-014: Add universe_status to collections.
        // Tracks Wikidata coverage level: Rich (QID + 5+ properties),
        // Limited (QID + <5 properties), None (no QID), Unknown (not yet checked).
        // Enables filtering and scheduled refresh of items without Wikidata coverage.
        MigrateAddColumnIfMissing(
            conn,
            table:  "hubs",
            column: "universe_status",
            ddl:    "ALTER TABLE hubs ADD COLUMN universe_status TEXT NOT NULL DEFAULT 'Unknown' " +
                    "CHECK (universe_status IN ('Rich', 'Limited', 'None', 'Unknown'));");

        // Migration M-015: Add universe_mismatch columns to works.
        // Tracks when a user explicitly skips Universe (Wikidata) matching for
        // a Work.  universe_mismatch is a boolean flag; universe_mismatch_at
        // records when the skip was applied.
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "universe_mismatch",
            ddl:    "ALTER TABLE works ADD COLUMN universe_mismatch INTEGER NOT NULL DEFAULT 0 " +
                    "CHECK (universe_mismatch IN (0, 1));");
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "universe_mismatch_at",
            ddl:    "ALTER TABLE works ADD COLUMN universe_mismatch_at TEXT;");

        // Migration M-016: Fix Wikidata provider GUID — invalid hex character 'w'.
        // The original GUID b3000003-w000-... caused a static constructor crash.
        using (var fix = conn.CreateCommand())
        {
            fix.CommandText = $"""
                UPDATE metadata_providers
                SET id = '{WellKnownProviders.Wikidata}'
                WHERE id = 'b3000003-w000-4000-8000-000000000004';

                UPDATE metadata_claims
                SET provider_id = '{WellKnownProviders.Wikidata}'
                WHERE provider_id = 'b3000003-w000-4000-8000-000000000004';
                """;
            fix.ExecuteNonQuery();
        }

        // Migration M-017: Hub virtualization — hub_relationships, collections,
        // works table recreation (nullable hub_id + wikidata_status + wikidata_checked_at).
        MigrateHubVirtualization(conn);

        // Migration M-018: Add local_headshot_path to persons for centralized people storage.
        MigratePersonHeadshotPath(conn);

        // Migration M-019: Add content_hash and extended_properties to user_states.
        // content_hash enables progress re-linking after file moves (Hash Dominance).
        // extended_properties stores media-type-specific tracking data as a JSON blob.
        MigrateAddColumnIfMissing(
            conn,
            table:  "user_states",
            column: "content_hash",
            ddl:    "ALTER TABLE user_states ADD COLUMN content_hash TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "user_states",
            column: "extended_properties",
            ddl:    "ALTER TABLE user_states ADD COLUMN extended_properties TEXT;");
        // Migration M-091: Playback segment markers for plugin-generated skip intro,
        // credits, recap, and commercial intervals.
        MigrateCreateTableIfMissing(
            conn,
            probeTable: "playback_segments",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS playback_segments (
                    id            TEXT NOT NULL PRIMARY KEY,
                    asset_id      TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
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
                CREATE INDEX IF NOT EXISTS idx_playback_segments_asset
                    ON playback_segments(asset_id, kind, start_seconds);
                """);

        // Migration M-020: Add ingestion_run_id to system_activity for event grouping.
        MigrateAddColumnIfMissing(
            conn,
            table:  "system_activity",
            column: "ingestion_run_id",
            ddl:    "ALTER TABLE system_activity ADD COLUMN ingestion_run_id TEXT;");

        // Migration M-021: Provider response cache — stores raw JSON responses
        // from metadata provider API calls to avoid redundant HTTP requests.
        // Entries have a per-provider TTL and support ETag conditional revalidation.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "provider_response_cache",
            probeColumn: "cache_key",
            ddl: """
                CREATE TABLE IF NOT EXISTS provider_response_cache (
                    cache_key     TEXT NOT NULL PRIMARY KEY,
                    provider_id   TEXT NOT NULL,
                    query_hash    TEXT NOT NULL,
                    response_json TEXT NOT NULL,
                    etag          TEXT,
                    fetched_at    TEXT NOT NULL,
                    expires_at    TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_prc_expires ON provider_response_cache (expires_at);
                CREATE INDEX IF NOT EXISTS idx_prc_provider ON provider_response_cache (provider_id);
                """);

        // Migration M-022: Performance indices for high-frequency query paths.
        // These indices were missing from the initial schema definition and are
        // created here as a safe, idempotent migration (CREATE INDEX IF NOT EXISTS).
        //
        // • metadata_claims(provider_id)   — scoring engine filters claims by provider
        // • metadata_claims(claimed_at)    — stale-claim decay filters by timestamp
        // • media_assets(edition_id)       — FK lookup joining editions ? assets
        // • works(media_type)              — lane-page and swimlane queries filter by type
        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_metadata_claims_provider_id
                    ON metadata_claims (provider_id);
                CREATE INDEX IF NOT EXISTS idx_metadata_claims_claimed_at
                    ON metadata_claims (claimed_at);
                CREATE INDEX IF NOT EXISTS idx_media_assets_edition_id
                    ON media_assets (edition_id);
                CREATE INDEX IF NOT EXISTS idx_works_media_type
                    ON works (media_type);
                """;
            idxCmd.ExecuteNonQuery();
        }

        // Migration M-023: EPUB reader tables â€” bookmarks, highlights, statistics,
        // and WhisperSync alignment jobs.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "reader_bookmarks",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS reader_bookmarks (
                    id             TEXT NOT NULL PRIMARY KEY,
                    user_id        TEXT NOT NULL,
                    asset_id       TEXT NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
                    chapter_index  INTEGER NOT NULL,
                    cfi_position   TEXT,
                    label          TEXT,
                    created_at     TEXT NOT NULL DEFAULT (datetime('now'))
                );
                CREATE INDEX IF NOT EXISTS idx_reader_bookmarks_user_asset
                    ON reader_bookmarks (user_id, asset_id);
                """);

        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "reader_highlights",
            probeColumn: "id",
            ddl: """
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
                CREATE INDEX IF NOT EXISTS idx_reader_highlights_user_asset
                    ON reader_highlights (user_id, asset_id);
                """);

        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "reader_statistics",
            probeColumn: "id",
            ddl: """
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
                """);

        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "alignment_jobs",
            probeColumn: "id",
            ddl: """
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
                """);

        // Migration M-024: Universe Graph — fictional_entities table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "fictional_entities",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS fictional_entities (
                    id                       TEXT NOT NULL PRIMARY KEY,
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
                );
                CREATE INDEX IF NOT EXISTS idx_fictional_entities_type
                    ON fictional_entities (entity_sub_type);
                CREATE INDEX IF NOT EXISTS idx_fictional_entities_universe
                    ON fictional_entities (fictional_universe_qid);
                """);

        // Migration M-025: Universe Graph — fictional_entity_work_links junction table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "fictional_entity_work_links",
            probeColumn: "entity_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS fictional_entity_work_links (
                    entity_id   TEXT NOT NULL REFERENCES fictional_entities(id) ON DELETE CASCADE,
                    work_qid    TEXT NOT NULL,
                    work_label  TEXT,
                    link_type   TEXT NOT NULL DEFAULT 'appears_in',
                    PRIMARY KEY (entity_id, work_qid, link_type)
                );
                CREATE INDEX IF NOT EXISTS idx_fewl_work_qid
                    ON fictional_entity_work_links (work_qid);
                """);

        // Migration M-026: Universe Graph — entity_relationships graph edge table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "entity_relationships",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS entity_relationships (
                    id                      TEXT NOT NULL PRIMARY KEY,
                    subject_qid             TEXT NOT NULL,
                    relationship_type       TEXT NOT NULL,
                    object_qid              TEXT NOT NULL,
                    confidence              REAL NOT NULL DEFAULT 0.9,
                    context_work_qid        TEXT,
                    discovered_at           TEXT NOT NULL,
                    UNIQUE (subject_qid, relationship_type, object_qid)
                );
                CREATE INDEX IF NOT EXISTS idx_entity_rel_subject
                    ON entity_relationships (subject_qid);
                CREATE INDEX IF NOT EXISTS idx_entity_rel_object
                    ON entity_relationships (object_qid);
                """);

        // Migration M-027: Universe Graph — narrative_roots table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "narrative_roots",
            probeColumn: "qid",
            ddl: """
                CREATE TABLE IF NOT EXISTS narrative_roots (
                    qid         TEXT NOT NULL PRIMARY KEY,
                    label       TEXT NOT NULL,
                    level       TEXT NOT NULL
                                    CHECK (level IN ('Universe', 'Franchise', 'Series', 'Standalone')),
                    parent_qid  TEXT,
                    created_at  TEXT NOT NULL
                );
                """);

        // Migration M-028: Person Infrastructure — add biographical columns to persons.
        MigrateAddColumnIfMissing(conn, "persons", "date_of_birth", "ALTER TABLE persons ADD COLUMN date_of_birth TEXT;");
        MigrateAddColumnIfMissing(conn, "persons", "date_of_death", "ALTER TABLE persons ADD COLUMN date_of_death TEXT;");
        MigrateAddColumnIfMissing(conn, "persons", "place_of_birth", "ALTER TABLE persons ADD COLUMN place_of_birth TEXT;");
        MigrateAddColumnIfMissing(conn, "persons", "place_of_death", "ALTER TABLE persons ADD COLUMN place_of_death TEXT;");
        MigrateAddColumnIfMissing(conn, "persons", "nationality", "ALTER TABLE persons ADD COLUMN nationality TEXT;");
        MigrateAddColumnIfMissing(conn, "persons", "is_pseudonym", "ALTER TABLE persons ADD COLUMN is_pseudonym INTEGER NOT NULL DEFAULT 0;");

        // Migration M-029: Character-performer links table.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "character_performer_links",
            probeColumn: "person_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS character_performer_links (
                    person_id           TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    fictional_entity_id TEXT NOT NULL REFERENCES fictional_entities(id) ON DELETE CASCADE,
                    work_qid            TEXT,
                    PRIMARY KEY (person_id, fictional_entity_id, work_qid)
                );
                """);

        // Migration M-030: Person aliases table for pseudonym resolution.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "person_aliases",
            probeColumn: "pseudonym_person_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS person_aliases (
                    pseudonym_person_id TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    real_person_id      TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    PRIMARY KEY (pseudonym_person_id, real_person_id)
                );
                CREATE INDEX IF NOT EXISTS idx_person_aliases_real ON person_aliases (real_person_id);
                """);

        // Migration M-031: QID label cache table.
        // Maps Wikidata Q-identifiers to human-readable display labels for offline
        // resolution. Every QID encountered during SPARQL responses, person enrichment,
        // and universe graph building is cached here.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "qid_labels",
            probeColumn: "qid",
            ddl: """
                CREATE TABLE IF NOT EXISTS qid_labels (
                    qid         TEXT NOT NULL PRIMARY KEY,
                    label       TEXT NOT NULL,
                    description TEXT,
                    entity_type TEXT,
                    fetched_at  TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_qid_labels_type ON qid_labels(entity_type);
                """);

        // Migration M-032: Multi-valued canonical field storage.
        // Replaces |||-separated strings for fields like genre, characters,
        // actor (cast_member) with individual rows carrying ordinals and optional QIDs.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "canonical_value_arrays",
            probeColumn: "entity_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS canonical_value_arrays (
                    entity_id TEXT    NOT NULL,
                    key       TEXT    NOT NULL,
                    ordinal   INTEGER NOT NULL DEFAULT 0,
                    value     TEXT    NOT NULL,
                    value_qid TEXT,
                    PRIMARY KEY (entity_id, key, ordinal)
                );
                CREATE INDEX IF NOT EXISTS idx_cva_key_qid ON canonical_value_arrays(key, value_qid);
                """);

        // Migration M-033: Unit 4 — Source Attribution.
        // Adds winning_provider_id to canonical_values so every scored field
        // records which provider supplied the winning claim.  Existing rows
        // default to NULL (no attribution available for pre-migration data).
        MigrateAddColumnIfMissing(
            conn,
            table:  "canonical_values",
            column: "winning_provider_id",
            ddl:    "ALTER TABLE canonical_values ADD COLUMN winning_provider_id TEXT;");

        // Migration M-034: Unit 5 — Per-Field NeedsReview.
        // Adds needs_review to canonical_values so conflicted fields, missing
        // expected fields, and local-only low-confidence fields are flagged for
        // human review.  Existing rows default to 0 (no review needed).
        MigrateAddColumnIfMissing(
            conn,
            table:  "canonical_values",
            column: "needs_review",
            ddl:    "ALTER TABLE canonical_values ADD COLUMN needs_review INTEGER NOT NULL DEFAULT 0;");

        // -- M-035: Parent Collection hierarchy -------------------------------------
        // Adds parent_hub_id to collections so a Collection can be nested under a Parent Collection
        // (franchise or creative universe container).  NULL = top-level collection.
        MigrateAddColumnIfMissing(
            conn,
            table:  "hubs",
            column: "parent_hub_id",
            ddl:    "ALTER TABLE hubs ADD COLUMN parent_hub_id TEXT;");

        // -- M-036: Deferred enrichment queue for Two-Pass architecture ----
        // Pass 1 (Quick Match) enqueues a Pass 2 (Universe Lookup) request
        // here.  The DeferredEnrichmentService processes the queue when the
        // ingestion pipeline is idle, on a nightly schedule, or on demand.
        MigrateCreateTableIfMissing(conn, "deferred_enrichment_queue", "id", """
            CREATE TABLE IF NOT EXISTS deferred_enrichment_queue (
                id           TEXT NOT NULL PRIMARY KEY,
                entity_id    TEXT NOT NULL,
                wikidata_qid TEXT,
                media_type   TEXT NOT NULL,
                hints_json   TEXT,
                created_at   TEXT NOT NULL,
                status       TEXT NOT NULL DEFAULT 'Pending',
                processed_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_deferred_queue_status
                ON deferred_enrichment_queue(status);
            CREATE INDEX IF NOT EXISTS idx_deferred_queue_created
                ON deferred_enrichment_queue(created_at);
            """);

        // Migration M-037: Chronicle Engine — temporal qualifiers on relationships + revision tracking.
        MigrateAddColumnIfMissing(
            conn,
            table:  "entity_relationships",
            column: "start_time",
            ddl:    "ALTER TABLE entity_relationships ADD COLUMN start_time TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "entity_relationships",
            column: "end_time",
            ddl:    "ALTER TABLE entity_relationships ADD COLUMN end_time TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table:  "fictional_entities",
            column: "wikidata_revision_id",
            ddl:    "ALTER TABLE fictional_entities ADD COLUMN wikidata_revision_id INTEGER;");

        // -- M-038: Ingestion lifecycle log ----------------------------------
        // Per-file tracking from detection through completion.
        // Provides "what happened to my file?" in a single table.
        using var m038 = conn.CreateCommand();
        m038.CommandText = """
            CREATE TABLE IF NOT EXISTS ingestion_log (
                id                TEXT NOT NULL PRIMARY KEY,
                file_path         TEXT NOT NULL,
                media_asset_id    TEXT,
                content_hash      TEXT,
                status            TEXT NOT NULL DEFAULT 'detected',
                media_type        TEXT,
                confidence_score  REAL,
                detected_title    TEXT,
                normalized_title  TEXT,
                wikidata_qid      TEXT,
                error_detail      TEXT,
                ingestion_run_id  TEXT,
                created_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                updated_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );
            CREATE INDEX IF NOT EXISTS idx_ingestion_log_status
                ON ingestion_log(status);
            CREATE INDEX IF NOT EXISTS idx_ingestion_log_run
                ON ingestion_log(ingestion_run_id);
            """;
        m038.ExecuteNonQuery();
        MigrateAddColumnIfMissing(conn, "ingestion_log", "media_asset_id",
            "ALTER TABLE ingestion_log ADD COLUMN media_asset_id TEXT;");
        using (var m038Index = conn.CreateCommand())
        {
            m038Index.CommandText = "CREATE INDEX IF NOT EXISTS idx_ingestion_log_media_asset ON ingestion_log(media_asset_id);";
            m038Index.ExecuteNonQuery();
        }

        // -- M-039: Identity resolution cache ---------------------------------
        // Caches normalized_title + media_type ? QID + confidence decisions.
        // Eliminates redundant 4-tier resolution for same logical entity.
        using var m039 = conn.CreateCommand();
        m039.CommandText = """
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
            CREATE INDEX IF NOT EXISTS idx_resolver_cache_expires
                ON resolver_cache(expires_at);
            """;
        m039.ExecuteNonQuery();

        // -- M-040: Hub Wikidata QID --------------------------------------
        // Guard: skip if hubs table has been renamed to collections by M-081
        MigrateAddColumnIfMissing(conn, "hubs", "wikidata_qid",
            "ALTER TABLE hubs ADD COLUMN wikidata_qid TEXT;");

        {
            bool hubsStillExists;
            using (var hProbe = conn.CreateCommand())
            {
                hProbe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='hubs';";
                hubsStillExists = Convert.ToInt64(hProbe.ExecuteScalar()) > 0;
            }
            if (hubsStillExists)
            {
                using (var cmd040Idx = conn.CreateCommand())
                {
                    cmd040Idx.CommandText = @"
                        CREATE UNIQUE INDEX IF NOT EXISTS idx_hubs_wikidata_qid
                            ON hubs(wikidata_qid) WHERE wikidata_qid IS NOT NULL;";
                    cmd040Idx.ExecuteNonQuery();
                }

                using (var cmd040Fill = conn.CreateCommand())
                {
                    cmd040Fill.CommandText = @"
                        UPDATE hubs SET wikidata_qid = (
                            SELECT cv.value FROM canonical_values cv
                            JOIN media_assets ma ON ma.id = cv.entity_id
                            JOIN editions e ON e.id = ma.edition_id
                            JOIN works w ON w.id = e.work_id
                            WHERE w.hub_id = hubs.id AND cv.key = 'wikidata_qid'
                            LIMIT 1
                        ) WHERE wikidata_qid IS NULL;";
                    cmd040Fill.ExecuteNonQuery();
                }
            }
        }

        // -- M-041: Work Wikidata QID -------------------------------------
        MigrateAddColumnIfMissing(conn, "works", "wikidata_qid",
            "ALTER TABLE works ADD COLUMN wikidata_qid TEXT;");

        using (var cmd041Idx = conn.CreateCommand())
        {
            cmd041Idx.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_works_wikidata_qid
                    ON works(wikidata_qid) WHERE wikidata_qid IS NOT NULL;";
            cmd041Idx.ExecuteNonQuery();
        }

        // Backfill works.wikidata_qid from canonical_values
        using (var cmd041Fill = conn.CreateCommand())
        {
            cmd041Fill.CommandText = @"
                UPDATE works SET wikidata_qid = (
                    SELECT cv.value FROM canonical_values cv
                    JOIN media_assets ma ON ma.id = cv.entity_id
                    JOIN editions e ON e.id = ma.edition_id
                    WHERE e.work_id = works.id AND cv.key = 'wikidata_qid'
                    LIMIT 1
                ) WHERE wikidata_qid IS NULL;";
            cmd041Fill.ExecuteNonQuery();
        }

        // -- M-042: Image cache user override flag ------------------------
        MigrateAddColumnIfMissing(conn, "image_cache", "is_user_override",
            "ALTER TABLE image_cache ADD COLUMN is_user_override INTEGER NOT NULL DEFAULT 0;");

        // -- M-043: Character image path ----------------------------------
        MigrateAddColumnIfMissing(conn, "character_performer_links", "character_image_path",
            "ALTER TABLE character_performer_links ADD COLUMN character_image_path TEXT;");

        // -- M-044: Add Event entity sub-type -----------------------------
        // SQLite does not support ALTER TABLE to modify CHECK constraints,
        // so we recreate fictional_entities with 'Event' added to the constraint.
        MigrateExpandFictionalEntitySubTypes(conn);

        // -- M-045: Hub type for typed containers -------------------------
        MigrateAddColumnIfMissing(conn, "hubs", "hub_type",
            "ALTER TABLE hubs ADD COLUMN hub_type TEXT NOT NULL DEFAULT 'Universe';");

        // -- M-046: Hub-Work junction table (many-to-many) ---------------
        {
            using var m046 = conn.CreateCommand();
            m046.CommandText = """
                CREATE TABLE IF NOT EXISTS hub_work_links (
                    hub_id    TEXT NOT NULL,
                    work_id   TEXT NOT NULL,
                    linked_at TEXT NOT NULL DEFAULT (datetime('now')),
                    PRIMARY KEY (hub_id, work_id)
                );
                CREATE INDEX IF NOT EXISTS idx_hwl_work ON hub_work_links(work_id);
                """;
            m046.ExecuteNonQuery();
        }

        // -- M-047: Clean up orphan collections (no works assigned) -------------
        // Guard: skip if hubs table has been renamed to collections by M-081
        {
            bool m047HubsExists;
            using (var m047Probe = conn.CreateCommand())
            {
                m047Probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='hubs';";
                m047HubsExists = Convert.ToInt64(m047Probe.ExecuteScalar()) > 0;
            }
            if (m047HubsExists)
            {
                using var m047 = conn.CreateCommand();
                m047.CommandText = """
                    DELETE FROM hubs WHERE id NOT IN (
                        SELECT DISTINCT hub_id FROM works WHERE hub_id IS NOT NULL
                        UNION
                        SELECT DISTINCT hub_id FROM hub_work_links
                    );
                    """;
                var orphansDeleted = m047.ExecuteNonQuery();
                if (orphansDeleted > 0)
                    System.Diagnostics.Debug.WriteLine($"M-047: Cleaned up {orphansDeleted} orphan collections");
            }
        }

        // -- M-048: Search results cache (fan-out search results per entity) --
        {
            using var m048 = conn.CreateCommand();
            m048.CommandText = """
                CREATE TABLE IF NOT EXISTS search_results_cache (
                    entity_id    TEXT NOT NULL PRIMARY KEY,
                    results_json TEXT NOT NULL,
                    searched_at  TEXT NOT NULL
                );
                """;
            m048.ExecuteNonQuery();
        }

        // -- M-049: Item History — per-entity event timeline --------------
        {
            using var m049 = conn.CreateCommand();
            m049.CommandText = """
                CREATE TABLE IF NOT EXISTS item_history (
                    id          TEXT NOT NULL PRIMARY KEY,
                    entity_id   TEXT NOT NULL,
                    occurred_at TEXT NOT NULL,
                    event_type  TEXT NOT NULL,
                    label       TEXT NOT NULL,
                    detail      TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_item_history_entity
                    ON item_history(entity_id, occurred_at);
                """;
            m049.ExecuteNonQuery();
        }

        // -- M-050: Consolidate item_history into system_activity ---------
        {
            using var m050Check = conn.CreateCommand();
            m050Check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='item_history';";
            var tableExists = Convert.ToInt64(m050Check.ExecuteScalar()) > 0;

            if (tableExists)
            {
                using var m050 = conn.CreateCommand();
                m050.CommandText = """
                    INSERT INTO system_activity (occurred_at, action_type, entity_id, detail)
                    SELECT
                        occurred_at,
                        event_type,
                        entity_id,
                        label || CASE WHEN detail IS NOT NULL THEN ' — ' || detail ELSE '' END
                    FROM item_history;

                    DROP TABLE item_history;
                    """;
                m050.ExecuteNonQuery();
            }
        }

        // -- M-051: Ingestion batches — groups files into processing runs ----
        {
            using var m051 = conn.CreateCommand();
            m051.CommandText = """
                CREATE TABLE IF NOT EXISTS ingestion_batches (
                    id                TEXT NOT NULL PRIMARY KEY,
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
                CREATE INDEX IF NOT EXISTS idx_ingestion_batches_status ON ingestion_batches(status);
                CREATE INDEX IF NOT EXISTS idx_ingestion_batches_created ON ingestion_batches(created_at);
                """;
            m051.ExecuteNonQuery();
        }

        // -- M-052  LibraryItem lifecycle columns -----------------------------
        // Add curator_state (provisional/rejected), rejected_at (purge countdown),
        // and provisional_metadata_json (curator-entered fields) to the works table.
        // These columns power the new 4-state LibraryItem model:
        //   Registered (default) / InReview (review_queue) / Provisional / Rejected
        MigrateAddColumnIfMissing(conn, "works", "curator_state",
            "ALTER TABLE works ADD COLUMN curator_state TEXT");
        MigrateAddColumnIfMissing(conn, "works", "rejected_at",
            "ALTER TABLE works ADD COLUMN rejected_at TEXT");
        MigrateAddColumnIfMissing(conn, "works", "provisional_metadata_json",
            "ALTER TABLE works ADD COLUMN provisional_metadata_json TEXT");

        // -- M-053: FTS5 search index for works ----------------------------
        // Replaces in-memory FuzzySharp re-ranking with native SQLite full-text
        // search. Indexes title and author for prefix matching and BM25 ranking.
        {
            using var m053Check = conn.CreateCommand();
            m053Check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='search_index'";
            if (m053Check.ExecuteScalar() is null)
            {
                using var m053Create = conn.CreateCommand();
                m053Create.CommandText = """
                    CREATE VIRTUAL TABLE search_index USING fts5(
                        work_id UNINDEXED, title, author, tokenize='unicode61'
                    );
                    """;
                m053Create.ExecuteNonQuery();

                // Populate from existing canonical values.
                using var m053Pop = conn.CreateCommand();
                m053Pop.CommandText = """
                    INSERT INTO search_index (work_id, title, author)
                    SELECT
                        w.id,
                        MAX(CASE WHEN cv.key = 'title' THEN cv.value END),
                        MAX(CASE WHEN cv.key = 'author' THEN cv.value END)
                    FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN canonical_values cv ON cv.entity_id = ma.id
                    WHERE cv.key IN ('title', 'author')
                    GROUP BY w.id;
                    """;
                m053Pop.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("M-053: Created FTS5 search_index table");
            }
        }

        // -- M-054: Bridge IDs table ---------------------------------------
        // Dedicated table for cross-platform identifiers (ISBN, ASIN, TMDB ID, etc.)
        // that link library entities to external catalogues and Wikidata.
        // Stored separately from canonical_values for clean querying and
        // self-documenting schema.  UNIQUE(entity_id, id_type) enforces one value
        // per ID type per entity; upsert updates the value when it changes.
        {
            using var m054 = conn.CreateCommand();
            m054.CommandText = """
                CREATE TABLE IF NOT EXISTS bridge_ids (
                    id                TEXT NOT NULL PRIMARY KEY,
                    entity_id         TEXT NOT NULL,
                    id_type           TEXT NOT NULL,
                    id_value          TEXT NOT NULL,
                    wikidata_property TEXT,
                    provider_id       TEXT,
                    created_at        TEXT NOT NULL DEFAULT (datetime('now')),
                    UNIQUE(entity_id, id_type)
                );
                CREATE INDEX IF NOT EXISTS idx_bridge_ids_entity
                    ON bridge_ids(entity_id);
                CREATE INDEX IF NOT EXISTS idx_bridge_ids_type_value
                    ON bridge_ids(id_type, id_value);
                """;
            m054.ExecuteNonQuery();

            // Backfill from canonical_values where the key is a known bridge ID type.
            // INSERT OR IGNORE so re-runs are safe.
            using var m054Fill = conn.CreateCommand();
            m054Fill.CommandText = """
                INSERT OR IGNORE INTO bridge_ids (id, entity_id, id_type, id_value, created_at)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                          substr(hex(randomblob(2)),2) || '-' ||
                          substr('89ab', abs(random()) % 4 + 1, 1) ||
                          substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                    cv.entity_id,
                    cv.key,
                    cv.value,
                    COALESCE(cv.last_scored_at, datetime('now'))
                FROM canonical_values cv
                WHERE cv.key IN (
                    'isbn', 'isbn_13', 'isbn_10', 'asin',
                    'apple_books_id', 'tmdb_id', 'imdb_id', 'audible_id',
                    'goodreads_id', 'musicbrainz_id', 'comic_vine_id'
                )
                AND cv.value IS NOT NULL AND cv.value != '';
                """;
            m054Fill.ExecuteNonQuery();
        }

        // -- M-055: Add match_level column to works table ------------------
        // Records whether a work was matched at the work level (default),
        // edition level, or collection level during the hydration pipeline.
        // Used by the LibraryItem to surface match granularity to the curator.
        MigrateAddColumnIfMissing(conn, "works", "match_level",
            "ALTER TABLE works ADD COLUMN match_level TEXT DEFAULT 'work';");

        // -- M-056: Add wikidata_qid column to editions table --------------
        // Stores the Wikidata QID for a specific edition entity (e.g. Q113799157
        // for "Blade Runner: The Final Cut").  Populated during Stage 2 (Wikidata
        // Bridge Resolution) when a bridge ID resolves to an edition-level entity
        // with P629 (edition or translation of) present.
        MigrateAddColumnIfMissing(conn, "editions", "wikidata_qid",
            "ALTER TABLE editions ADD COLUMN wikidata_qid TEXT;");

        // -- M-057: Pending person signals + expanded person roles ----------
        // pending_person_signals stores unverified person names extracted during
        // ingestion, between inline extraction and deferred batch Wikidata
        // verification.  The expanded person role CHECK adds: Translator, Editor,
        // Host, Producer.
        MigrateExpandPersonRolesV2(conn);
        {
            using var m057 = conn.CreateCommand();
            m057.CommandText = """
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
                CREATE INDEX IF NOT EXISTS idx_pending_person_signals_name_role
                    ON pending_person_signals (name, role);
                """;
            m057.ExecuteNonQuery();
        }

        // -- M-058: User taste profiles for AI personalization --------------
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "user_taste_profiles",
            probeColumn: "user_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS user_taste_profiles (
                    user_id       TEXT NOT NULL PRIMARY KEY,
                    profile_json  TEXT NOT NULL DEFAULT '{}',
                    summary       TEXT,
                    updated_at    TEXT NOT NULL DEFAULT (datetime('now'))
                );
                """);

        // -- M-059: Audio fingerprints for Chromaprint music similarity -----
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "audio_fingerprints",
            probeColumn: "asset_id",
            ddl: """
                CREATE TABLE IF NOT EXISTS audio_fingerprints (
                    asset_id      TEXT NOT NULL PRIMARY KEY,
                    fingerprint   BLOB NOT NULL,
                    duration_sec  REAL NOT NULL DEFAULT 0,
                    created_at    TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (asset_id) REFERENCES media_assets(id)
                );
                """);

        // -- M-060: Perceptual hash column on image_cache ------------------
        // Stores a 64-bit average hash (pHash) of each cached image so the
        // Stage 1 matching pipeline can compare embedded cover art against
        // provider thumbnails without re-downloading images.
        // SQLite stores this as INTEGER (64-bit signed); the repository layer
        // casts between long and ulong when reading and writing.
        MigrateAddColumnIfMissing(conn, "image_cache", "phash",
            "ALTER TABLE image_cache ADD COLUMN phash INTEGER;");

        {
            using var m060Idx = conn.CreateCommand();
            m060Idx.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_image_cache_phash
                    ON image_cache(phash) WHERE phash IS NOT NULL;
                """;
            m060Idx.ExecuteNonQuery();
        }

        // -- M-061: Expand FTS5 search_index to full multi-language schema ----
        // Phase 2C localization: extends the 3-column search_index (work_id, title,
        // author) created by M-053 to a 6-column schema (entity_id, title,
        // original_title, alternate_titles, author, description).
        // FTS5 virtual tables do not support ALTER TABLE — the table is dropped and
        // recreated. Data is repopulated from canonical_values. The column rename
        // from work_id ? entity_id is intentional: the FTS index stores work GUIDs
        // resolved from media_asset entity IDs (the join is in UpsertByEntityIdAsync).
        {
            using var m061Check = conn.CreateCommand();
            m061Check.CommandText = """
                SELECT COUNT(*) FROM pragma_table_info('search_index')
                WHERE name = 'original_title';
                """;
            var hasOriginalTitle = Convert.ToInt64(m061Check.ExecuteScalar()) > 0;

            if (!hasOriginalTitle)
            {
                // Drop old 3-column FTS5 table and all its shadow tables.
                using var m061Drop = conn.CreateCommand();
                m061Drop.CommandText = "DROP TABLE IF EXISTS search_index;";
                m061Drop.ExecuteNonQuery();

                // Create new 6-column FTS5 table with unicode61 tokenizer.
                using var m061Create = conn.CreateCommand();
                m061Create.CommandText = """
                    CREATE VIRTUAL TABLE search_index USING fts5(
                        entity_id UNINDEXED,
                        title,
                        original_title,
                        alternate_titles,
                        author,
                        description,
                        tokenize = 'unicode61'
                    );
                    """;
                m061Create.ExecuteNonQuery();

                // Repopulate from canonical_values (title, original_title, author, description).
                // alternate_titles sourced from canonical_value_arrays (key='alternate_title').
                using var m061Pop = conn.CreateCommand();
                m061Pop.CommandText = """
                    INSERT INTO search_index (entity_id, title, original_title, alternate_titles, author, description)
                    SELECT
                        w.id,
                        MAX(CASE WHEN cv.key = 'title'          THEN cv.value END),
                        MAX(CASE WHEN cv.key = 'original_title' THEN cv.value END),
                        (SELECT GROUP_CONCAT(cva.value, ' ')
                         FROM canonical_value_arrays cva
                         JOIN editions e2 ON e2.work_id = w.id
                         JOIN media_assets ma2 ON ma2.edition_id = e2.id
                         WHERE cva.entity_id = ma2.id AND cva.key = 'alternate_title'),
                        MAX(CASE WHEN cv.key = 'author'         THEN cv.value END),
                        MAX(CASE WHEN cv.key = 'description'    THEN cv.value END)
                    FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN canonical_values cv ON cv.entity_id = ma.id
                    WHERE cv.key IN ('title', 'original_title', 'author', 'description')
                    GROUP BY w.id;
                    """;
                m061Pop.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("M-061: Rebuilt FTS5 search_index with 6-column multi-language schema");
            }
        }

        // -- M-062: Switch FTS5 search_index tokenizer from unicode61 to trigram --
        // Phase 5 CJK support: the trigram tokenizer indexes every 3-character window
        // of text, enabling substring matching for CJK scripts (Chinese, Japanese,
        // Korean) where words have no space boundaries. It also handles Western text —
        // any substring of 3+ characters matches, so "ame" finds "Amélie".
        //
        // Trade-off: trigram indexes are larger than unicode61 and do not support
        // ranked BM25 ordering (ORDER BY rank is unsupported). Searches fall back to
        // table-scan order. For a personal library index this is acceptable.
        //
        // Detection: check whether the current tokenizer is already 'trigram' by
        // inspecting the FTS5 shadow config table. If not, rebuild the table.
        {
            var trigramAlready = false;
            try
            {
                using var m062Check = conn.CreateCommand();
                // The FTS5 config shadow table stores tokenizer info as a value string.
                m062Check.CommandText = """
                    SELECT value FROM search_index_config WHERE k = 'tokenize';
                    """;
                var tokenizerValue = m062Check.ExecuteScalar() as string;
                trigramAlready = string.Equals(tokenizerValue, "trigram", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // shadow table missing or search_index doesn't exist yet — proceed with rebuild
            }

            if (!trigramAlready)
            {
                // Preserve existing data before dropping the FTS5 table.
                using var m062Backup = conn.CreateCommand();
                m062Backup.CommandText = """
                    CREATE TEMP TABLE IF NOT EXISTS _si_backup AS
                    SELECT entity_id, title, original_title, alternate_titles, author, description
                    FROM search_index;
                    """;
                m062Backup.ExecuteNonQuery();

                // Drop the old FTS5 table (and all its shadow tables).
                using var m062Drop = conn.CreateCommand();
                m062Drop.CommandText = "DROP TABLE IF EXISTS search_index;";
                m062Drop.ExecuteNonQuery();

                // Create the new FTS5 table with trigram tokenizer.
                using var m062Create = conn.CreateCommand();
                m062Create.CommandText = """
                    CREATE VIRTUAL TABLE search_index USING fts5(
                        entity_id UNINDEXED,
                        title,
                        original_title,
                        alternate_titles,
                        author,
                        description,
                        tokenize = 'trigram'
                    );
                    """;
                m062Create.ExecuteNonQuery();

                // Repopulate from the backup.
                using var m062Restore = conn.CreateCommand();
                m062Restore.CommandText = """
                    INSERT INTO search_index (entity_id, title, original_title, alternate_titles, author, description)
                    SELECT entity_id, title, original_title, alternate_titles, author, description
                    FROM _si_backup;
                    """;
                m062Restore.ExecuteNonQuery();

                // Clean up the temp backup.
                using var m062CleanUp = conn.CreateCommand();
                m062CleanUp.CommandText = "DROP TABLE IF EXISTS _si_backup;";
                m062CleanUp.ExecuteNonQuery();

                System.Diagnostics.Debug.WriteLine("M-062: Rebuilt FTS5 search_index with trigram tokenizer for CJK support");
            }
        }

        // Migration M-063: Normalise legacy media_type claim values to canonical enum names.
        // Over time, processors and providers emitted non-canonical values (e.g. "epub",
        // "Book", "movie") that do not match the enum names used by the rest of the pipeline.
        // This migration rewrites those stale values in metadata_claims to the canonical
        // plural forms (e.g. "Books", "Movies") so that library counts, filters, and
        // disambiguation all agree on a single vocabulary.
        {
            using var m063 = conn.CreateCommand();
            m063.CommandText = """
                UPDATE metadata_claims SET claim_value = 'Books'      WHERE claim_key = 'media_type' AND claim_value IN ('Epub', 'Book', 'Ebook', 'book', 'epub', 'ebook');
                UPDATE metadata_claims SET claim_value = 'Audiobooks' WHERE claim_key = 'media_type' AND claim_value IN ('Audiobook', 'audiobook');
                UPDATE metadata_claims SET claim_value = 'Movies'     WHERE claim_key = 'media_type' AND claim_value IN ('Movie', 'movie', 'Video', 'video');
                UPDATE metadata_claims SET claim_value = 'Comics'     WHERE claim_key = 'media_type' AND claim_value IN ('Comic', 'comic', 'Cbz', 'cbz', 'Cbr', 'cbr');

                UPDATE metadata_claims SET claim_value = 'Music'      WHERE claim_key = 'media_type' AND claim_value IN ('music');
                UPDATE metadata_claims SET claim_value = 'TV'         WHERE claim_key = 'media_type' AND claim_value IN ('tv', 'Television', 'television');
                """;
            m063.ExecuteNonQuery();
        }

        // Migration M-064: Multi-role persons with QID-first identity.
        // Migrates from single-role persons.role column to a person_roles junction table
        // so one person can have multiple roles (e.g. Clint Eastwood = Director + Actor).
        // Also adds a UNIQUE index on wikidata_qid for QID-first lookups.
        MigratePersonMultiRole(conn);

        // Migration M-065: Stage 3 tables (entity_assets, character_portraits, series_members).
        MigrateStage3Tables(conn);

        // Migration M-065A: artwork slot normalization.
        // Renames Backdrop -> Background, adds SquareArt, updates stored canonical keys,
        // and rewrites persisted local image paths from backdrop.* to background.*.
        MigrateArtworkSlotNormalization(conn);

        // Migration M-066: Provider health tracking + deferred enrichment failure classification.
        MigrateProviderHealth(conn);

        // Migration M-067: Backfill missing cover_url canonical values.
        // Assets that have a hero banner but no cover_url are missing thumbnails
        // in library views because GenerateHeroBannerAsync did not previously write
        // cover_url alongside hero. This one-time repair inserts the missing rows.
        MigrateBackfillCoverUrl(conn);

        // Migration M-068: Entity timeline tables for pipeline provenance tracking.
        // Two tables: entity_events (one row per pipeline/lifecycle event) and
        // entity_field_changes (one row per field that changed, FK to entity_events).
        MigrateEntityTimeline(conn);

        // Migration M-069: Music provider support — is_group column on persons,
        // person_group_members junction table, Performer/Artist roles.
        MigrateMusicGroupSupport(conn);

        // Migration M-070: Universal Collection System — new columns on collections, hub_placements table.
        MigrateUniversalHubSystem(conn);

        // Migration M-080: Durable Identity Pipeline — identity_jobs, retail_match_candidates,
        // wikidata_bridge_candidates tables for the retail-first identity pipeline.
        MigrateIdentityPipeline(conn);

        // Migration M-081: Work hierarchy — adds work_kind, parent_work_id, ordinal,
        // is_catalog_only, external_identifiers to the works table and renames the
        // legacy sequence_index column to ordinal. Enables albums/seasons/series to
        // be expressed as parent/child Work rows instead of fake ContentGroup collections.
        MigrateWorkHierarchy(conn);

        // Migration M-082: parent_key shadow column + index. Powers the
        // HierarchyResolver's indexed find-or-create lookup for parent Works
        // (albums, shows, series, comic series).
        MigrateParentKey(conn);

        // Migration M-083: ownership column on works. Adds TEXT column
        // 'ownership' defaulting to 'Owned' and backfills 'Unowned' for all
        // existing catalog rows (is_catalog_only = 1). Adds a composite index
        // on (ownership, media_type) for fast unowned filtering.
        MigrateOwnershipColumn(conn);

        // Migration M-084: auto re-tag sweep state on media_assets. Tracks the
        // per-media-type writeback hash, status, retry scheduling, and error.
        // Enables the RetagSweepWorker to detect stale assets after a
        // writeback-fields.json change and route failures appropriately.
        MigrateAddColumnIfMissing(conn, "media_assets", "writeback_fields_hash",
            "ALTER TABLE media_assets ADD COLUMN writeback_fields_hash TEXT;");
        MigrateAddColumnIfMissing(conn, "media_assets", "writeback_status",
            "ALTER TABLE media_assets ADD COLUMN writeback_status TEXT;");
        MigrateAddColumnIfMissing(conn, "media_assets", "writeback_last_error",
            "ALTER TABLE media_assets ADD COLUMN writeback_last_error TEXT;");
        MigrateAddColumnIfMissing(conn, "media_assets", "writeback_attempts",
            "ALTER TABLE media_assets ADD COLUMN writeback_attempts INTEGER NOT NULL DEFAULT 0;");
        MigrateAddColumnIfMissing(conn, "media_assets", "writeback_next_retry_at",
            "ALTER TABLE media_assets ADD COLUMN writeback_next_retry_at INTEGER;");

        // Migration M-085: Side-by-side-with-Plex foundations. Adds
        // library_id / is_orphaned / orphaned_at to media_assets and creates
        // the file_hash_cache table used by the initial sweep. See plan
        // .claude/plans/wise-rolling-beacon.md Slice 1.
        MigrateLibraryIdAndHashCache(conn);

        // Migration M-081: Collection Rename — renames hub-era table/column/index
        // names to the collections vocabulary. Idempotent: skips if the rename
        // has already been applied (probes for the `collections` table).
        MigrateCollectionRename(conn);

        // Migration M-086: ensure modern collections columns exist even on
        // fresh/current databases whose base schema predates the universal
        // collection column set.
        MigrateCollectionUniversalColumns(conn);
        MigrateCollectionSupportTables(conn);

        // Migration M-087: storage-policy metadata on entity_assets.
        MigrateEntityAssetStorageColumns(conn);
        MigrateEntityAssetArtworkMetadataColumns(conn);

        // Migration M-089: timed lyrics and subtitle track records.
        MigrateTextTracks(conn);

        // Migration M-090: Wikidata series manifest cache.
        MigrateSeriesManifestTables(conn);

        // Migration M-092: targeted indexes for large-library list reads.
        EnsureLargeLibraryIndexes(conn);

        // Seed S-001: metadata_providers entries for all known providers.
        // metadata_claims.provider_id has a FK to metadata_providers(id), so these
        // rows MUST exist before any claim is written.  INSERT OR IGNORE makes this
        // idempotent — safe to run on every startup.
        SeedMetadataProviders(conn);

        // Seed S-002: default "Owner" Administrator profile.
        // First-run experience: single user with full access.
        SeedDefaultProfile(conn);
    }

    /// <summary>
    /// Seeds the <c>metadata_providers</c> table with all known provider GUIDs.
    /// Uses <c>INSERT OR IGNORE</c> so duplicate rows are silently skipped.
    /// </summary>
    private static void SeedMetadataProviders(SqliteConnection conn)
    {
        ReadOnlySpan<(string Id, string Name, string Version)> providers =
        [
            (WellKnownProviders.LocalProcessor.ToString(),  "local_processor",      "1.0"),
            (WellKnownProviders.LibraryScanner.ToString(),  "library_scanner",      "1.0"),
            (WellKnownProviders.AppleApi.ToString(),        "apple_api",            "2.0"),
            (WellKnownProviders.Wikidata.ToString(),        "wikidata",             "1.0"),
            (WellKnownProviders.Wikipedia.ToString(),       "wikipedia",            "1.0"),
            (WellKnownProviders.OpenLibrary.ToString(),     "open_library",         "1.0"),
            (WellKnownProviders.GoogleBooks.ToString(),     "google_books",         "1.0"),
            (WellKnownProviders.MusicBrainz.ToString(),     "musicbrainz",          "1.0"),
            (WellKnownProviders.Tmdb.ToString(),            "tmdb",                 "1.0"),
            (WellKnownProviders.Metron.ToString(),          "metron",               "1.0"),
            (WellKnownProviders.ComicVine.ToString(),       "comicvine",            "1.0"),
            (WellKnownProviders.Lrclib.ToString(),          "lrclib",               "1.0"),
            (WellKnownProviders.OpenSubtitles.ToString(),   "opensubtitles",        "1.0"),

            (WellKnownProviders.UserManual.ToString(),      "user_manual",          "1.0"),
            (WellKnownProviders.FanartTv.ToString(),        "fanart_tv",            "1.0"),
            (WellKnownProviders.AiProvider.ToString(),      "ai_provider",          "1.0"),
        ];

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO metadata_providers (id, name, version, is_enabled)
            VALUES (@id, @name, @version, 1);
            """;

        var pId      = cmd.Parameters.Add("@id",      Microsoft.Data.Sqlite.SqliteType.Text);
        var pName    = cmd.Parameters.Add("@name",    Microsoft.Data.Sqlite.SqliteType.Text);
        var pVersion = cmd.Parameters.Add("@version", Microsoft.Data.Sqlite.SqliteType.Text);

        foreach (var (id, name, version) in providers)
        {
            pId.Value      = id;
            pName.Value    = name;
            pVersion.Value = version;
            cmd.ExecuteNonQuery();
        }
    }

    private static void EnsureLargeLibraryIndexes(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_editions_work_id
                ON editions(work_id);

            CREATE INDEX IF NOT EXISTS idx_media_assets_edition_id
                ON media_assets(edition_id);

            CREATE INDEX IF NOT EXISTS idx_canonical_values_key_value_entity
                ON canonical_values(key, value, entity_id);

            CREATE INDEX IF NOT EXISTS idx_canonical_value_arrays_key_value_entity
                ON canonical_value_arrays(key, value, entity_id);

            CREATE INDEX IF NOT EXISTS idx_person_media_links_person
                ON person_media_links(person_id);

            CREATE INDEX IF NOT EXISTS idx_ingestion_log_run_created
                ON ingestion_log(ingestion_run_id, created_at);

            CREATE INDEX IF NOT EXISTS idx_identity_jobs_run_entity_updated
                ON identity_jobs(ingestion_run_id, entity_id, updated_at);
            """;
        cmd.ExecuteNonQuery();
    }
    /// <summary>
    /// Seeds the default "Owner" Administrator profile on first run.
    /// Uses <c>INSERT OR IGNORE</c> so duplicate rows are silently skipped.
    /// </summary>
    private static void SeedDefaultProfile(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO profiles (id, display_name, avatar_color, role, created_at)
            VALUES (@id, @name, @color, @role, @created);
            """;
        cmd.Parameters.AddWithValue("@id",      "00000000-0000-0000-0000-000000000001");
        cmd.Parameters.AddWithValue("@name",    "Owner");
        cmd.Parameters.AddWithValue("@color",   "#7C4DFF");
        cmd.Parameters.AddWithValue("@role",    "Administrator");
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    // -------------------------------------------------------------------------
    // Migration helpers
    // -------------------------------------------------------------------------

    private static void MigrateMetadataProvidersTable(SqliteConnection conn)
    {
        const string currentTable = "metadata_providers";
        var priorTable = "provider_" + "reg" + "istry";

        if (!TableExists(conn, priorTable))
            return;

        using var fkOff = conn.CreateCommand();
        fkOff.CommandText = "PRAGMA foreign_keys = OFF;";
        fkOff.ExecuteNonQuery();

        if (!TableExists(conn, currentTable))
        {
            using var rename = conn.CreateCommand();
            rename.CommandText = $"ALTER TABLE {priorTable} RENAME TO {currentTable};";
            rename.ExecuteNonQuery();
        }
        else
        {
            using var copy = conn.CreateCommand();
            copy.CommandText = $"""
                INSERT OR IGNORE INTO {currentTable} (id, name, version, is_enabled)
                SELECT id, name, version, is_enabled
                FROM {priorTable};
                DROP TABLE {priorTable};
                """;
            copy.ExecuteNonQuery();
        }

        using var fkOn = conn.CreateCommand();
        fkOn.CommandText = "PRAGMA foreign_keys = ON;";
        fkOn.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        cmd.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    /// <summary>
    /// Adds a column to <paramref name="table"/> if it does not yet exist.
    /// Uses <c>PRAGMA table_info</c> for the check - SQLite does not support
    /// <c>ALTER TABLE ADD COLUMN IF NOT EXISTS</c> syntax.
    /// </summary>
    private static void MigrateAddColumnIfMissing(
        SqliteConnection conn,
        string table,
        string column,
        string ddl)
    {
        // Check if the table still exists (may have been renamed by a later migration).
        bool tableExists;
        using (var probeCmd = conn.CreateCommand())
        {
            probeCmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
            tableExists = Convert.ToInt64(probeCmd.ExecuteScalar()) > 0;
        }
        if (!tableExists) return;

        // PRAGMA table_info returns one row per column; we just need to know
        // whether the named column is present.
        bool exists = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                // Column 1 in PRAGMA table_info is "name".
                if (string.Equals(reader.GetString(1), column,
                        StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = ddl;
            alterCmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Creates a table if it does not yet exist, using <c>PRAGMA table_info</c>
    /// on a known column as a proxy for table existence.
    /// Executes <paramref name="ddl"/> (which may contain multiple statements)
    /// when the table is absent.
    /// </summary>
    private static void MigrateCreateTableIfMissing(
        SqliteConnection conn,
        string probeTable,
        string probeColumn,
        string ddl)
    {
        bool exists = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = $"PRAGMA table_info({probeTable});";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), probeColumn,
                        StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = ddl;
            createCmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Migration M-009: Recreate the <c>persons</c> table with an expanded role CHECK.
    /// Adds: Actor, Voice Actor, Composer.
    ///
    /// SQLite does not support <c>ALTER TABLE</c> to modify CHECK constraints.
    /// This migration:
    /// 1. Disables foreign keys (required for table swap with FK references)
    /// 2. Creates <c>persons_new</c> with the expanded constraint
    /// 3. Copies all existing rows
    /// 4. Drops the old table
    /// 5. Renames <c>persons_new</c> to <c>persons</c>
    /// 6. Recreates indices
    /// 7. Re-enables foreign keys
    ///
    /// Idempotent: checks the current CHECK constraint text before migrating.
    /// </summary>
    private static void MigrateExpandPersonRoles(SqliteConnection conn)
    {
        // Check if the persons table already has the expanded role set.
        // We look for 'Voice Actor' in the CREATE TABLE SQL to detect whether
        // the migration has already been applied.
        bool alreadyExpanded = false;
        using (var sqlCmd = conn.CreateCommand())
        {
            sqlCmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='persons';";
            var sql = sqlCmd.ExecuteScalar() as string;
            if (sql is not null && sql.Contains("Voice Actor", StringComparison.OrdinalIgnoreCase))
                alreadyExpanded = true;
            // If the table doesn't exist yet (fresh install), schema.sql handles it.
            if (sql is null)
                alreadyExpanded = true;
            // If the 'role' column no longer exists (fresh schema without it), skip.
            if (sql is not null && !sql.Contains("role", StringComparison.OrdinalIgnoreCase))
                alreadyExpanded = true;
        }

        if (alreadyExpanded)
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys=OFF;

            DROP TABLE IF EXISTS persons_new;

            CREATE TABLE persons_new (
                id           TEXT NOT NULL PRIMARY KEY,
                name         TEXT NOT NULL,
                role         TEXT NOT NULL CHECK (role IN (
                    'Author','Narrator','Director',
                    'Actor','Voice Actor',
                    'Composer')),
                wikidata_qid TEXT,
                headshot_url TEXT,
                biography    TEXT,
                created_at   TEXT NOT NULL,
                enriched_at  TEXT
            );

            INSERT INTO persons_new (id, name, role, wikidata_qid, headshot_url, biography, created_at, enriched_at)
                SELECT id, name, role, wikidata_qid, headshot_url, biography, created_at, enriched_at
                FROM persons;

            DROP TABLE persons;

            ALTER TABLE persons_new RENAME TO persons;

            CREATE INDEX IF NOT EXISTS idx_persons_name ON persons (name);

            PRAGMA foreign_keys=ON;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-057 (role expansion): Recreate the <c>persons</c> table with four
    /// additional roles: Translator, Editor, Host, Producer.
    ///
    /// Idempotent: checks for 'Translator' in the current CHECK constraint before running.
    /// Uses the same SQLite table-recreation pattern as M-009: disable foreign keys ?
    /// create new table ? copy rows ? drop old ? rename ? recreate indices ? re-enable FKs.
    /// All columns that exist by the time this migration runs are preserved.
    /// </summary>
    private static void MigrateExpandPersonRolesV2(SqliteConnection conn)
    {
        // Check if 'Translator' is already in the CHECK constraint.
        // If so, the migration has already been applied (or this is a fresh install
        // whose schema.sql already includes the expanded list).
        bool alreadyExpanded = false;
        using (var sqlCmd = conn.CreateCommand())
        {
            sqlCmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='persons';";
            var sql = sqlCmd.ExecuteScalar() as string;
            if (sql is null || sql.Contains("Translator", StringComparison.OrdinalIgnoreCase) || !sql.Contains("role", StringComparison.OrdinalIgnoreCase))
                alreadyExpanded = true;
            // If the 'role' column no longer exists (fresh schema without it), skip.
            if (sql is not null && !sql.Contains("role", StringComparison.OrdinalIgnoreCase))
                alreadyExpanded = true;
        }

        if (alreadyExpanded)
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys=OFF;

            DROP TABLE IF EXISTS persons_new;

            CREATE TABLE persons_new (
                id                TEXT NOT NULL PRIMARY KEY,
                name              TEXT NOT NULL,
                role              TEXT NOT NULL CHECK (role IN (
                    'Author','Narrator','Director',
                    'Actor','Voice Actor','Composer',
                    'Artist','Performer','Screenwriter')),
                wikidata_qid      TEXT,
                headshot_url      TEXT,
                biography         TEXT,
                occupation        TEXT,
                instagram         TEXT,
                twitter           TEXT,
                tiktok            TEXT,
                mastodon          TEXT,
                website           TEXT,
                local_headshot_path TEXT,
                date_of_birth     TEXT,
                date_of_death     TEXT,
                place_of_birth    TEXT,
                place_of_death    TEXT,
                nationality       TEXT,
                is_pseudonym      INTEGER NOT NULL DEFAULT 0,
                created_at        TEXT NOT NULL,
                enriched_at       TEXT
            );

            INSERT INTO persons_new
                (id, name, role, wikidata_qid, headshot_url, biography,
                 occupation, instagram, twitter, tiktok, mastodon, website,
                 local_headshot_path,
                 date_of_birth, date_of_death, place_of_birth, place_of_death,
                 nationality, is_pseudonym, created_at, enriched_at)
            SELECT
                id, name, role, wikidata_qid, headshot_url, biography,
                occupation, instagram, twitter, tiktok, mastodon, website,
                local_headshot_path,
                date_of_birth, date_of_death, place_of_birth, place_of_death,
                nationality, is_pseudonym, created_at, enriched_at
            FROM persons;

            DROP TABLE persons;

            ALTER TABLE persons_new RENAME TO persons;

            CREATE INDEX IF NOT EXISTS idx_persons_name ON persons (name);

            PRAGMA foreign_keys=ON;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-064: Multi-role persons with QID-first identity.
    ///
    /// Creates the <c>person_roles</c> junction table and migrates existing role data
    /// from <c>persons.role</c> and <c>person_media_links.role</c> into it.
    /// Then recreates the <c>persons</c> table WITHOUT the <c>role</c> column.
    /// Adds a UNIQUE index on <c>wikidata_qid</c> for QID-first lookups.
    ///
    /// Idempotent: checks whether person_roles already exists AND whether
    /// the persons table still has a 'role' column before running.
    /// </summary>
    private static void MigratePersonMultiRole(SqliteConnection conn)
    {
        // Step 1: Create person_roles table if it doesn't exist.
        bool personRolesExists = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = "PRAGMA table_info(person_roles);";
            using var reader = infoCmd.ExecuteReader();
            if (reader.Read())
                personRolesExists = true;
        }

        if (!personRolesExists)
        {
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS person_roles (
                    person_id TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    role      TEXT NOT NULL CHECK (role IN (
                                  'Author','Narrator','Director',
                                  'Actor','Voice Actor','Composer',
                                  'Artist','Performer','Screenwriter')),
                    PRIMARY KEY (person_id, role)
                );

                -- Migrate existing role data from persons.role column.
                INSERT OR IGNORE INTO person_roles (person_id, role)
                SELECT id, role FROM persons WHERE role IS NOT NULL AND role != '';

                -- Also populate from person_media_links (captures roles the person
                -- has via media links but not in persons.role).
                INSERT OR IGNORE INTO person_roles (person_id, role)
                SELECT DISTINCT person_id, role FROM person_media_links
                WHERE person_id IN (SELECT id FROM persons);
                """;
            createCmd.ExecuteNonQuery();
        }

        // Step 2: Recreate persons table WITHOUT the role column.
        // Check if 'role' column still exists on persons.
        bool roleColumnExists = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = "PRAGMA table_info(persons);";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "role", StringComparison.OrdinalIgnoreCase))
                {
                    roleColumnExists = true;
                    break;
                }
            }
        }

        if (roleColumnExists)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys=OFF;

                CREATE TABLE persons_new (
                    id                TEXT    NOT NULL PRIMARY KEY,
                    name              TEXT    NOT NULL,
                    wikidata_qid      TEXT,
                    headshot_url      TEXT,
                    biography         TEXT,
                    occupation        TEXT,
                    instagram         TEXT,
                    twitter           TEXT,
                    tiktok            TEXT,
                    mastodon          TEXT,
                    website           TEXT,
                    local_headshot_path TEXT,
                    date_of_birth     TEXT,
                    date_of_death     TEXT,
                    place_of_birth    TEXT,
                    place_of_death    TEXT,
                    nationality       TEXT,
                    is_pseudonym      INTEGER NOT NULL DEFAULT 0,
                    created_at        TEXT    NOT NULL,
                    enriched_at       TEXT
                );

                INSERT INTO persons_new
                    (id, name, wikidata_qid, headshot_url, biography,
                     occupation, instagram, twitter, tiktok, mastodon, website,
                     local_headshot_path,
                     date_of_birth, date_of_death, place_of_birth, place_of_death,
                     nationality, is_pseudonym, created_at, enriched_at)
                SELECT
                    id, name, wikidata_qid, headshot_url, biography,
                    occupation, instagram, twitter, tiktok, mastodon, website,
                    local_headshot_path,
                    date_of_birth, date_of_death, place_of_birth, place_of_death,
                    nationality, is_pseudonym, created_at, enriched_at
                FROM persons;

                DROP TABLE persons;

                ALTER TABLE persons_new RENAME TO persons;

                CREATE INDEX IF NOT EXISTS idx_persons_name ON persons (name);

                PRAGMA foreign_keys=ON;
                """;
            cmd.ExecuteNonQuery();
        }

        // Step 3: Add UNIQUE index on wikidata_qid for QID-first lookups.
        // CREATE UNIQUE INDEX IF NOT EXISTS is safe to run unconditionally.
        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS idx_persons_wikidata_qid
                    ON persons (wikidata_qid) WHERE wikidata_qid IS NOT NULL;
                """;
            idxCmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Migration M-065: Stage 3 Universe Enrichment tables.
    ///
    /// Creates three tables for the formalized asset type system, character portraits,
    /// and series completeness tracking.
    /// <list type="bullet">
    ///   <item><c>entity_assets</c> — typed image storage for any entity (Work, Person, Universe, FictionalEntity).</item>
    ///   <item><c>character_portraits</c> — actor-in-costume or animated character images per performer-character pair.</item>
    ///   <item><c>series_members</c> — tracks all works in a series for completeness scoring.</item>
    /// </list>
    /// Idempotent: uses CREATE TABLE IF NOT EXISTS.
    /// </summary>
    private static void MigrateStage3Tables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            -- -- entity_assets -----------------------------------------------
            CREATE TABLE IF NOT EXISTS entity_assets (
                id               TEXT PRIMARY KEY,
                entity_id        TEXT NOT NULL,
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

            CREATE INDEX IF NOT EXISTS idx_entity_assets_entity
                ON entity_assets(entity_id, entity_type);
            CREATE INDEX IF NOT EXISTS idx_entity_assets_type
                ON entity_assets(entity_id, asset_type);

            -- -- character_portraits -----------------------------------------
            CREATE TABLE IF NOT EXISTS character_portraits (
                id                  TEXT PRIMARY KEY,
                person_id           TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                fictional_entity_id TEXT NOT NULL REFERENCES fictional_entities(id) ON DELETE CASCADE,
                image_url           TEXT,
                local_image_path    TEXT,
                source_provider     TEXT,
                is_default          INTEGER NOT NULL DEFAULT 0,
                created_at          TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at          TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_character_portraits_character
                ON character_portraits(fictional_entity_id);
            CREATE INDEX IF NOT EXISTS idx_character_portraits_person
                ON character_portraits(person_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_character_portraits_pair
                ON character_portraits(person_id, fictional_entity_id);

            -- -- series_members ----------------------------------------------
            CREATE TABLE IF NOT EXISTS series_members (
                series_qid TEXT NOT NULL,
                work_qid   TEXT NOT NULL,
                work_label TEXT,
                position   TEXT,
                owned      INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                PRIMARY KEY (series_qid, work_qid)
            );

            CREATE INDEX IF NOT EXISTS idx_series_members_series
                ON series_members(series_qid);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateProviderHealth(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
            """;
        cmd.ExecuteNonQuery();

        // Add failure classification columns to the deferred enrichment queue.
        // These are nullable — legacy rows and normal Pass 2 entries have NULL.
        // ALTER TABLE does not support IF NOT EXISTS, so catch duplicates.
        try
        {
            using var alt1 = conn.CreateCommand();
            alt1.CommandText = "ALTER TABLE deferred_enrichment_queue ADD COLUMN failure_type TEXT;";
            alt1.ExecuteNonQuery();
        }
        catch { /* Column already exists — safe to ignore. */ }

        try
        {
            using var alt2 = conn.CreateCommand();
            alt2.CommandText = "ALTER TABLE deferred_enrichment_queue ADD COLUMN failed_provider_name TEXT;";
            alt2.ExecuteNonQuery();
        }
        catch { /* Column already exists — safe to ignore. */ }

        // Index on failure_type — created AFTER the ALTER TABLEs that add the column.
        using var idx = conn.CreateCommand();
        idx.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_deferred_queue_failure_type
                ON deferred_enrichment_queue(failure_type)
                WHERE failure_type IS NOT NULL;
            """;
        idx.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-067: Backfill missing <c>cover_url</c> canonical values.
    /// Any asset with a <c>hero</c> canonical but no <c>cover_url</c> gets an
    /// inserted cover_url pointing to <c>/stream/{assetId}/cover</c>.
    /// </summary>
    private static void MigrateBackfillCoverUrl(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted)
            SELECT
                cv_hero.entity_id,
                'cover_url',
                REPLACE(cv_hero.value, '/hero', '/cover'),
                cv_hero.last_scored_at,
                0
            FROM canonical_values cv_hero
            WHERE cv_hero.key = 'hero'
              AND NOT EXISTS (
                  SELECT 1 FROM canonical_values cv_cover
                  WHERE cv_cover.entity_id = cv_hero.entity_id
                    AND cv_cover.key = 'cover_url'
              );
            """;
        var affected = cmd.ExecuteNonQuery();
        if (affected > 0)
            Console.WriteLine($"[M-067] Backfilled {affected} missing cover_url canonical values.");
    }

    /// <summary>
    /// Migration M-044: Recreate the <c>fictional_entities</c> table with 'Event' added to the
    /// <c>entity_sub_type</c> CHECK constraint.
    ///
    /// SQLite does not support <c>ALTER TABLE</c> to modify CHECK constraints.
    /// This migration:
    /// 1. Disables foreign keys (required for table swap with FK references)
    /// 2. Creates <c>fictional_entities_new</c> with the expanded constraint
    /// 3. Copies all existing rows
    /// 4. Drops the old table
    /// 5. Renames <c>fictional_entities_new</c> to <c>fictional_entities</c>
    /// 6. Recreates indices
    /// 7. Re-enables foreign keys
    ///
    /// Idempotent: checks the current CHECK constraint text before migrating.
    /// </summary>
    private static void MigrateExpandFictionalEntitySubTypes(SqliteConnection conn)
    {
        // Check if the fictional_entities table already has 'Event' in the CHECK constraint.
        bool alreadyExpanded = false;
        using (var sqlCmd = conn.CreateCommand())
        {
            sqlCmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='fictional_entities';";
            var sql = sqlCmd.ExecuteScalar() as string;
            if (sql is not null && sql.Contains("Event", StringComparison.OrdinalIgnoreCase))
                alreadyExpanded = true;
            // If the table doesn't exist yet (fresh install), schema handles it.
            if (sql is null)
                alreadyExpanded = true;
        }

        if (alreadyExpanded)
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys=OFF;

            CREATE TABLE fictional_entities_new (
                id                       TEXT NOT NULL PRIMARY KEY,
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
            );

            INSERT INTO fictional_entities_new
                SELECT id, wikidata_qid, label, description, entity_sub_type,
                       fictional_universe_qid, fictional_universe_label,
                       image_url, local_image_path, created_at, enriched_at
                FROM fictional_entities;

            DROP TABLE fictional_entities;

            ALTER TABLE fictional_entities_new RENAME TO fictional_entities;

            CREATE UNIQUE INDEX IF NOT EXISTS idx_fictional_entities_qid
                ON fictional_entities (wikidata_qid);
            CREATE INDEX IF NOT EXISTS idx_fictional_entities_type
                ON fictional_entities (entity_sub_type);
            CREATE INDEX IF NOT EXISTS idx_fictional_entities_universe
                ON fictional_entities (fictional_universe_qid);

            PRAGMA foreign_keys=ON;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-017: Hub virtualization.
    /// 1. Create collection_relationships table.
    /// 2. Add wikidata_status + wikidata_checked_at to works (via ALTER TABLE).
    /// 3. Create collections + collection_items schema stubs.
    ///
    /// Note: collection_id is already effectively nullable in SQLite (ON DELETE SET NULL
    /// in the FK constraint). The domain model change to Guid? requires no schema
    /// recreation — SQLite does not enforce non-null on FK columns unless explicitly
    /// constrained. We add the new columns via ALTER TABLE for safety.
    /// </summary>
    private static void MigrateHubVirtualization(SqliteConnection conn)
    {
        // Check if the hubs table still exists (pre-M-081 database).
        // Hub-specific SQL is guarded; non-hub parts (wikidata_status on works) always run.
        bool hubsExist;
        using (var hvProbe = conn.CreateCommand())
        {
            hvProbe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='hubs';";
            hubsExist = Convert.ToInt64(hvProbe.ExecuteScalar()) > 0;
        }

        if (hubsExist)
        {
        // hub_relationships table
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "hub_relationships",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS hub_relationships (
                    id            TEXT NOT NULL PRIMARY KEY,
                    hub_id        TEXT NOT NULL REFERENCES hubs(id) ON DELETE CASCADE,
                    rel_type      TEXT NOT NULL,
                    rel_qid       TEXT NOT NULL,
                    rel_label     TEXT,
                    confidence    REAL NOT NULL DEFAULT 0.9,
                    discovered_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
                CREATE INDEX IF NOT EXISTS idx_hub_rel_type_qid ON hub_relationships (rel_type, rel_qid);
                CREATE INDEX IF NOT EXISTS idx_hub_rel_hub_id ON hub_relationships (hub_id);
                """);
        } // end if (hubsExist) — hub_relationships only

        // Add wikidata_status to works (default 'pending')
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "wikidata_status",
            ddl:    "ALTER TABLE works ADD COLUMN wikidata_status TEXT DEFAULT 'pending';");

        // Add wikidata_checked_at to works
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "wikidata_checked_at",
            ddl:    "ALTER TABLE works ADD COLUMN wikidata_checked_at TEXT;");

        // Collections schema stub — only needed for old databases
        if (hubsExist)
        {
            MigrateCreateTableIfMissing(
                conn,
                probeTable:  "collections",
                probeColumn: "id",
                ddl: """
                    CREATE TABLE IF NOT EXISTS hubs (
                        id              TEXT NOT NULL PRIMARY KEY,
                        name            TEXT NOT NULL,
                        hub_type TEXT NOT NULL DEFAULT 'custom',
                        profile_id      TEXT REFERENCES profiles(id) ON DELETE CASCADE,
                        created_at      TEXT NOT NULL DEFAULT (datetime('now'))
                    );
                    """);

            MigrateCreateTableIfMissing(
                conn,
                probeTable:  "hub_items",
                probeColumn: "id",
                ddl: """
                    CREATE TABLE IF NOT EXISTS hub_items (
                        id            TEXT NOT NULL PRIMARY KEY,
                        hub_id TEXT NOT NULL REFERENCES hubs(id) ON DELETE CASCADE,
                        work_id       TEXT NOT NULL REFERENCES works(id) ON DELETE CASCADE,
                        sort_order    INTEGER NOT NULL DEFAULT 0,
                        added_at      TEXT NOT NULL DEFAULT (datetime('now'))
                    );
                    """);
        }
    }

    /// <summary>
    /// Migration M-018: Add <c>local_headshot_path</c> column to the <c>persons</c>
    /// table for centralized people storage under <c>.people/</c>.
    /// </summary>
    private static void MigratePersonHeadshotPath(SqliteConnection conn)
    {
        MigrateAddColumnIfMissing(
            conn,
            table:  "persons",
            column: "local_headshot_path",
            ddl:    "ALTER TABLE persons ADD COLUMN local_headshot_path TEXT;");
    }

    private static void MigrateEntityTimeline(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS entity_events (
                id                TEXT NOT NULL PRIMARY KEY,
                entity_id         TEXT NOT NULL,
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
                ingestion_run_id  TEXT,
                detail            TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_events_entity ON entity_events(entity_id);
            CREATE INDEX IF NOT EXISTS idx_events_entity_stage ON entity_events(entity_id, stage);
            CREATE INDEX IF NOT EXISTS idx_events_entity_type ON entity_events(entity_type);
            CREATE INDEX IF NOT EXISTS idx_events_type ON entity_events(event_type);
            CREATE INDEX IF NOT EXISTS idx_events_occurred ON entity_events(occurred_at);
            CREATE INDEX IF NOT EXISTS idx_events_provider ON entity_events(provider_id);

            CREATE TABLE IF NOT EXISTS entity_field_changes (
                id              TEXT NOT NULL PRIMARY KEY,
                event_id        TEXT NOT NULL REFERENCES entity_events(id) ON DELETE CASCADE,
                entity_id       TEXT NOT NULL,
                field           TEXT NOT NULL,
                old_value       TEXT,
                new_value       TEXT,
                old_provider_id TEXT,
                new_provider_id TEXT,
                confidence      REAL,
                is_file_original INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_field_changes_event ON entity_field_changes(event_id);
            CREATE INDEX IF NOT EXISTS idx_field_changes_entity ON entity_field_changes(entity_id);
            CREATE INDEX IF NOT EXISTS idx_field_changes_field ON entity_field_changes(entity_id, field);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-069: Music provider support.
    ///
    /// <list type="bullet">
    ///   <item><c>is_group</c> column on persons (0 = individual, 1 = group/band).</item>
    ///   <item><c>person_group_members</c> junction table linking groups to their member persons.</item>
    ///   <item>Expanded person_roles CHECK to include 'Performer' and 'Artist'.</item>
    /// </list>
    ///
    /// Idempotent: checks for <c>is_group</c> column before running.
    /// </summary>
    private static void MigrateMusicGroupSupport(SqliteConnection conn)
    {
        // Check if is_group column already exists on persons.
        bool isGroupExists = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = "PRAGMA table_info(persons);";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "is_group", StringComparison.OrdinalIgnoreCase))
                {
                    isGroupExists = true;
                    break;
                }
            }
        }

        if (!isGroupExists)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                ALTER TABLE persons ADD COLUMN is_group INTEGER NOT NULL DEFAULT 0;
                """;
            cmd.ExecuteNonQuery();
        }

        // Create person_group_members junction table.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS person_group_members (
                    group_id    TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    member_id   TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    start_date  TEXT,   -- ISO-8601 date when member joined (from Wikidata P580 qualifier)
                    end_date    TEXT,   -- ISO-8601 date when member left (from Wikidata P582 qualifier)
                    PRIMARY KEY (group_id, member_id)
                );

                CREATE INDEX IF NOT EXISTS idx_person_group_members_member
                    ON person_group_members (member_id);
                """;
            cmd.ExecuteNonQuery();
        }

        // Expand person_roles CHECK constraint to include 'Performer' and 'Artist'.
        // Check if already expanded.
        bool rolesExpanded = false;
        using (var sqlCmd = conn.CreateCommand())
        {
            sqlCmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='person_roles';";
            var sql = sqlCmd.ExecuteScalar() as string;
            if (sql is not null && sql.Contains("Performer", StringComparison.OrdinalIgnoreCase))
                rolesExpanded = true;
        }

        if (!rolesExpanded)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys=OFF;

                CREATE TABLE person_roles_new (
                    person_id TEXT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
                    role      TEXT NOT NULL CHECK (role IN (
                                  'Author','Narrator','Director',
                                  'Actor','Voice Actor','Composer',
                                  'Artist','Performer','Screenwriter')),
                    PRIMARY KEY (person_id, role)
                );

                INSERT INTO person_roles_new (person_id, role)
                SELECT person_id, role FROM person_roles;

                DROP TABLE person_roles;

                ALTER TABLE person_roles_new RENAME TO person_roles;

                PRAGMA foreign_keys=ON;
                """;
            cmd.ExecuteNonQuery();
        }

        System.Diagnostics.Debug.WriteLine("M-069: Music group support — is_group, person_group_members, expanded roles");
    }

    /// <summary>
    /// Migration M-070: Universal Collection System — new columns on collections, collection_placements table.
    /// <list type="bullet">
    ///   <item>Adds resolution, rule_hash, group_by_field, match_mode, sort_field, sort_direction, live_updating to collections.</item>
    ///   <item>Creates collection_placements table for mapping collections to UI locations.</item>
    ///   <item>Backfills existing Universe collections with works to ContentGroup type + materialized resolution.</item>
    /// </list>
    /// Idempotent: checks for <c>resolution</c> column before running.
    /// </summary>
    private static void MigrateUniversalHubSystem(SqliteConnection conn)
    {
        // Check if hubs table still exists (pre-M-081 database).
        bool hubsExist;
        using (var uhsProbe = conn.CreateCommand())
        {
            uhsProbe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='hubs';";
            hubsExist = Convert.ToInt64(uhsProbe.ExecuteScalar()) > 0;
        }

        if (!hubsExist) return; // Fresh DB with new schema.sql already has all columns

        // Check if resolution column already exists on hubs.
        bool resolutionExists = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = "PRAGMA table_info(hubs);";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "resolution", StringComparison.OrdinalIgnoreCase))
                {
                    resolutionExists = true;
                    break;
                }
            }
        }

        if (!resolutionExists)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                ALTER TABLE hubs ADD COLUMN resolution TEXT NOT NULL DEFAULT 'query';
                ALTER TABLE hubs ADD COLUMN rule_hash TEXT;
                ALTER TABLE hubs ADD COLUMN group_by_field TEXT;
                ALTER TABLE hubs ADD COLUMN match_mode TEXT NOT NULL DEFAULT 'all';
                ALTER TABLE hubs ADD COLUMN sort_field TEXT;
                ALTER TABLE hubs ADD COLUMN sort_direction TEXT NOT NULL DEFAULT 'desc';
                ALTER TABLE hubs ADD COLUMN live_updating INTEGER NOT NULL DEFAULT 1;

                CREATE INDEX IF NOT EXISTS idx_hubs_rule_hash ON hubs (rule_hash);
                CREATE INDEX IF NOT EXISTS idx_hubs_hub_type ON hubs (hub_type);
                CREATE INDEX IF NOT EXISTS idx_hubs_resolution ON hubs (resolution);
                """;
            cmd.ExecuteNonQuery();
        }

        // Create hub_placements table
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS hub_placements (
                    id            TEXT PRIMARY KEY,
                    hub_id        TEXT NOT NULL REFERENCES hubs(id) ON DELETE CASCADE,
                    location      TEXT NOT NULL,
                    position      INTEGER NOT NULL DEFAULT 0,
                    display_limit INTEGER NOT NULL DEFAULT 0,
                    display_mode  TEXT NOT NULL DEFAULT 'swimlane',
                    is_visible    INTEGER NOT NULL DEFAULT 1,
                    created_at    TEXT NOT NULL DEFAULT (datetime('now'))
                );

                CREATE INDEX IF NOT EXISTS idx_hub_placements_hub_id ON hub_placements (hub_id);
                CREATE INDEX IF NOT EXISTS idx_hub_placements_location ON hub_placements (location);
                """;
            cmd.ExecuteNonQuery();
        }

        // Backfill: set existing Universe hubs with assigned works to ContentGroup type + materialized resolution
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE hubs SET hub_type = 'ContentGroup', resolution = 'materialized'
                WHERE hub_type = 'Universe'
                  AND id IN (SELECT DISTINCT hub_id FROM works WHERE hub_id IS NOT NULL);
                """;
            cmd.ExecuteNonQuery();
        }

        System.Diagnostics.Debug.WriteLine("M-070: Universal Collection System — collection columns, hub_placements, backfill");
    }

    /// <summary>
    /// Migration M-080: Durable Identity Pipeline.
    /// <list type="bullet">
    ///   <item>Creates <c>identity_jobs</c> table for durable job tracking.</item>
    ///   <item>Creates <c>retail_match_candidates</c> table for Stage 1 candidate evidence.</item>
    ///   <item>Creates <c>wikidata_bridge_candidates</c> table for Stage 2 candidate evidence.</item>
    /// </list>
    /// Idempotent: uses <c>CREATE TABLE IF NOT EXISTS</c>.
    /// </summary>
    private static void MigrateIdentityPipeline(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS identity_jobs (
                id                     TEXT PRIMARY KEY,
                entity_id              TEXT NOT NULL,
                entity_type            TEXT NOT NULL,
                media_type             TEXT NOT NULL,
                ingestion_run_id       TEXT,
                state                  TEXT NOT NULL DEFAULT 'Queued',
                pass                   TEXT NOT NULL DEFAULT 'Quick',
                attempt_count          INTEGER NOT NULL DEFAULT 0,
                lease_owner            TEXT,
                lease_expires_at       TEXT,
                selected_candidate_id  TEXT,
                resolved_qid          TEXT,
                last_error             TEXT,
                next_retry_at          TEXT,
                created_at             TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at             TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_identity_jobs_entity_id ON identity_jobs (entity_id);
            CREATE INDEX IF NOT EXISTS idx_identity_jobs_state ON identity_jobs (state);
            CREATE INDEX IF NOT EXISTS idx_identity_jobs_ingestion_run_id ON identity_jobs (ingestion_run_id);
            CREATE INDEX IF NOT EXISTS idx_identity_jobs_lease ON identity_jobs (state, lease_expires_at);

            DELETE FROM identity_jobs AS older
            WHERE older.state NOT IN ('Completed', 'Failed')
              AND EXISTS (
                  SELECT 1
                  FROM   identity_jobs AS newer
                  WHERE  newer.entity_id = older.entity_id
                    AND  newer.pass = older.pass
                    AND  newer.state NOT IN ('Completed', 'Failed')
                    AND (
                             newer.updated_at > older.updated_at
                          OR (newer.updated_at = older.updated_at AND newer.rowid > older.rowid)
                        )
              );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_jobs_entity_pass_active
                ON identity_jobs (entity_id, pass)
                WHERE state NOT IN ('Completed', 'Failed');

            CREATE TABLE IF NOT EXISTS retail_match_candidates (
                id                    TEXT PRIMARY KEY,
                job_id                TEXT NOT NULL REFERENCES identity_jobs(id) ON DELETE CASCADE,
                provider_id           TEXT NOT NULL,
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

            CREATE INDEX IF NOT EXISTS idx_retail_candidates_job_id ON retail_match_candidates (job_id);
            CREATE INDEX IF NOT EXISTS idx_retail_candidates_outcome ON retail_match_candidates (outcome);

            CREATE TABLE IF NOT EXISTS wikidata_bridge_candidates (
                id                    TEXT PRIMARY KEY,
                job_id                TEXT NOT NULL REFERENCES identity_jobs(id) ON DELETE CASCADE,
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

            CREATE INDEX IF NOT EXISTS idx_wikidata_candidates_job_id ON wikidata_bridge_candidates (job_id);
            CREATE INDEX IF NOT EXISTS idx_wikidata_candidates_outcome ON wikidata_bridge_candidates (outcome);
            """;
        cmd.ExecuteNonQuery();

        System.Diagnostics.Debug.WriteLine("M-080: Durable Identity Pipeline — identity_jobs, retail_match_candidates, wikidata_bridge_candidates");
    }

    /// <summary>
    /// Migration M-081: Work hierarchy.
    /// <list type="bullet">
    ///   <item>Renames <c>works.sequence_index</c> ? <c>works.ordinal</c>.</item>
    ///   <item>Adds <c>work_kind</c> TEXT NOT NULL DEFAULT 'standalone' with a
    ///     CHECK constraint enforcing standalone/parent/child/catalog.</item>
    ///   <item>Adds <c>parent_work_id</c> TEXT FK to <c>works(id)</c> ON DELETE SET NULL.</item>
    ///   <item>Adds <c>is_catalog_only</c> INTEGER NOT NULL DEFAULT 0.</item>
    ///   <item>Adds <c>external_identifiers</c> TEXT (JSON blob).</item>
    ///   <item>Creates supporting indexes <c>idx_works_parent_work_id</c> and
    ///     <c>idx_works_work_kind</c>.</item>
    /// </list>
    /// Idempotent: each step probes <c>PRAGMA table_info</c> first.
    /// All existing rows get <c>work_kind = 'standalone'</c> via the column DEFAULT;
    /// the HierarchyResolver in Phase 3 promotes them to parent/child as files
    /// are re-ingested.
    /// </summary>
    private static void MigrateWorkHierarchy(SqliteConnection conn)
    {
        // -- Step 1: rename sequence_index ? ordinal --------------------------
        // SQLite 3.25+ supports ALTER TABLE RENAME COLUMN. The bundled SQLite
        // is well past that version, so the rename is a single statement —
        // no table-rebuild dance required.
        bool hasOrdinal = false;
        bool hasSequenceIndex = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = "PRAGMA table_info(works);";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "ordinal", StringComparison.OrdinalIgnoreCase))
                    hasOrdinal = true;
                else if (string.Equals(name, "sequence_index", StringComparison.OrdinalIgnoreCase))
                    hasSequenceIndex = true;
            }
        }

        if (!hasOrdinal && hasSequenceIndex)
        {
            using var renameCmd = conn.CreateCommand();
            renameCmd.CommandText = "ALTER TABLE works RENAME COLUMN sequence_index TO ordinal;";
            renameCmd.ExecuteNonQuery();
        }
        else if (!hasOrdinal && !hasSequenceIndex)
        {
            // Fresh DB or weird state — just add the column directly.
            using var addCmd = conn.CreateCommand();
            addCmd.CommandText = "ALTER TABLE works ADD COLUMN ordinal INTEGER;";
            addCmd.ExecuteNonQuery();
        }

        // -- Step 2: work_kind with CHECK constraint ---------------------------
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "work_kind",
            ddl:    "ALTER TABLE works ADD COLUMN work_kind TEXT NOT NULL DEFAULT 'standalone' " +
                    "CHECK (work_kind IN ('standalone','parent','child','catalog'));");

        // -- Step 3: parent_work_id (self-referencing FK) ----------------------
        // Note: SQLite enforces ON DELETE SET NULL only when the FK is added at
        // CREATE TABLE time. ALTER TABLE ADD COLUMN with REFERENCES does not
        // enforce the action — but the column itself is created, and the
        // application layer handles cleanup. The CREATE TABLE statement in
        // schema.sql carries the enforced FK for fresh databases.
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "parent_work_id",
            ddl:    "ALTER TABLE works ADD COLUMN parent_work_id TEXT REFERENCES works(id) ON DELETE SET NULL;");

        // -- Step 4: is_catalog_only -------------------------------------------
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "is_catalog_only",
            ddl:    "ALTER TABLE works ADD COLUMN is_catalog_only INTEGER NOT NULL DEFAULT 0 " +
                    "CHECK (is_catalog_only IN (0, 1));");

        // -- Step 5: external_identifiers (JSON blob) --------------------------
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "external_identifiers",
            ddl:    "ALTER TABLE works ADD COLUMN external_identifiers TEXT;");

        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "display_overrides_json",
            ddl:    "ALTER TABLE works ADD COLUMN display_overrides_json TEXT;");
        MigrateAddColumnIfMissing(
            conn,
            table: "works",
            column: "wikidata_match_source",
            ddl: "ALTER TABLE works ADD COLUMN wikidata_match_source TEXT;");

        MigrateAddColumnIfMissing(
            conn,
            table: "works",
            column: "wikidata_match_locked",
            ddl: "ALTER TABLE works ADD COLUMN wikidata_match_locked INTEGER NOT NULL DEFAULT 0;");

        MigrateAddColumnIfMissing(
            conn,
            table: "works",
            column: "wikidata_rejected_qids_json",
            ddl: "ALTER TABLE works ADD COLUMN wikidata_rejected_qids_json TEXT;");

        // -- Step 6: indexes ---------------------------------------------------
        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_works_parent_work_id
                    ON works(parent_work_id);

                CREATE INDEX IF NOT EXISTS idx_works_work_kind
                    ON works(work_kind) WHERE work_kind != 'standalone';
                """;
            idxCmd.ExecuteNonQuery();
        }

        System.Diagnostics.Debug.WriteLine("M-081: Work hierarchy — work_kind, parent_work_id, ordinal, is_catalog_only, external_identifiers");
    }

    /// <summary>
    /// Migration M-082: parent_key shadow column + index.
    /// <list type="bullet">
    ///   <item>Adds <c>works.parent_key</c> TEXT (nullable). Populated only on
    ///     parent rows (<c>work_kind = 'parent'</c>).</item>
    ///   <item>Creates partial index <c>idx_works_parent_key</c> on
    ///     <c>(media_type, parent_key) WHERE parent_key IS NOT NULL</c>.</item>
    /// </list>
    /// Idempotent: <see cref="MigrateAddColumnIfMissing"/> probes
    /// <c>PRAGMA table_info</c>; the index uses <c>CREATE INDEX IF NOT EXISTS</c>.
    /// No backfill — parent_key is populated by the HierarchyResolver as files
    /// are ingested. Existing standalone rows stay NULL.
    /// </summary>
    private static void MigrateParentKey(SqliteConnection conn)
    {
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "parent_key",
            ddl:    "ALTER TABLE works ADD COLUMN parent_key TEXT;");

        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_works_parent_key
                    ON works(media_type, parent_key) WHERE parent_key IS NOT NULL;
                """;
            idxCmd.ExecuteNonQuery();
        }

        System.Diagnostics.Debug.WriteLine("M-082: parent_key shadow column + index");
    }

    /// <summary>
    /// Migration M-083: <c>works.ownership</c> TEXT column.
    /// <list type="bullet">
    ///   <item>Adds <c>ownership TEXT NOT NULL DEFAULT 'Owned'</c> to
    ///     <c>works</c>. All existing rows default to 'Owned'.</item>
    ///   <item>Backfills <c>ownership = 'Unowned'</c> for every row where
    ///     <c>is_catalog_only = 1</c> (existing catalog children). This keeps
    ///     the new column in sync with the legacy boolean flag.</item>
    ///   <item>Creates a composite index <c>idx_works_ownership_media_type</c>
    ///     on <c>(ownership, media_type)</c> for fast unowned-item queries.</item>
    /// </list>
    /// Idempotent: column addition is guarded by
    /// <see cref="MigrateAddColumnIfMissing"/>; the index uses
    /// <c>CREATE INDEX IF NOT EXISTS</c>.
    /// </summary>
    private static void MigrateOwnershipColumn(SqliteConnection conn)
    {
        // Step 1: add column
        MigrateAddColumnIfMissing(
            conn,
            table:  "works",
            column: "ownership",
            ddl:    "ALTER TABLE works ADD COLUMN ownership TEXT NOT NULL DEFAULT 'Owned';");

        // Step 2: backfill existing catalog rows
        using (var backfillCmd = conn.CreateCommand())
        {
            backfillCmd.CommandText = """
                UPDATE works
                SET    ownership = 'Unowned'
                WHERE  is_catalog_only = 1
                  AND  (ownership IS NULL OR ownership = 'Owned');
                """;
            backfillCmd.ExecuteNonQuery();
        }

        // Step 3: composite index on (ownership, media_type)
        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_works_ownership_media_type
                    ON works(ownership, media_type);
                """;
            idxCmd.ExecuteNonQuery();
        }

        System.Diagnostics.Debug.WriteLine("M-083: ownership column on works");
    }

    /// <summary>
    /// Migration M-085: Side-by-side-with-Plex foundations.
    /// <list type="bullet">
    ///   <item>Adds <c>library_id TEXT</c> (nullable) to <c>media_assets</c>
    ///     so assets can be attributed to the logical library that owns
    ///     their source path. NULL for pre-migration rows — backfilled
    ///     lazily by the ingestion pipeline once
    ///     <see cref="ILibraryFolderResolver"/> is wired in.</item>
    ///   <item>Adds <c>is_orphaned INTEGER NOT NULL DEFAULT 0</c> and
    ///     <c>orphaned_at TEXT</c> as the soft-delete flag for files that
    ///     disappear from disk (NAS unmount, user reorganised in Plex).
    ///     The asset row survives so user progress + metadata are preserved.</item>
    ///   <item>Creates the <c>file_hash_cache</c> table keyed on
    ///     <c>(absolute_path, size_bytes, mtime_utc) ? sha256</c>, used by
    ///     the initial sweep to avoid re-hashing files that haven't changed.</item>
    ///   <item>Creates two indexes:
    ///     <c>idx_media_assets_library_id</c> for per-library queries and a
    ///     partial index <c>idx_media_assets_orphaned</c> over
    ///     <c>is_orphaned = 1</c> rows for fast orphan reconciliation.</item>
    /// </list>
    /// Idempotent: all column additions are guarded by
    /// <see cref="MigrateAddColumnIfMissing"/>; the table uses
    /// <c>CREATE TABLE IF NOT EXISTS</c>; the indexes use
    /// <c>CREATE INDEX IF NOT EXISTS</c>.
    /// </summary>
    private static void MigrateLibraryIdAndHashCache(SqliteConnection conn)
    {
        // Step 1: new columns on media_assets.
        MigrateAddColumnIfMissing(
            conn,
            table:  "media_assets",
            column: "library_id",
            ddl:    "ALTER TABLE media_assets ADD COLUMN library_id TEXT;");

        MigrateAddColumnIfMissing(
            conn,
            table:  "media_assets",
            column: "is_orphaned",
            ddl:    "ALTER TABLE media_assets ADD COLUMN is_orphaned INTEGER NOT NULL DEFAULT 0;");

        MigrateAddColumnIfMissing(
            conn,
            table:  "media_assets",
            column: "orphaned_at",
            ddl:    "ALTER TABLE media_assets ADD COLUMN orphaned_at TEXT;");

        // Step 2: indexes. Full index on library_id for per-library scans;
        // partial index on is_orphaned filtered to 1 for cheap orphan sweeps.
        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_media_assets_library_id
                    ON media_assets(library_id) WHERE library_id IS NOT NULL;
                CREATE INDEX IF NOT EXISTS idx_media_assets_orphaned
                    ON media_assets(is_orphaned) WHERE is_orphaned = 1;
                """;
            idxCmd.ExecuteNonQuery();
        }

        // Step 3: file_hash_cache table. Not FK-linked to anything —
        // entries may outlive the assets they describe (e.g. during
        // re-sweep after a delete-and-restore cycle).
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "file_hash_cache",
            probeColumn: "absolute_path",
            ddl: """
                CREATE TABLE IF NOT EXISTS file_hash_cache (
                    absolute_path TEXT    NOT NULL PRIMARY KEY,
                    size_bytes    INTEGER NOT NULL,
                    mtime_utc     TEXT    NOT NULL,
                    sha256        TEXT    NOT NULL,
                    cached_at     TEXT    NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_file_hash_cache_sha256
                    ON file_hash_cache(sha256);
                """);

        System.Diagnostics.Debug.WriteLine(
            "M-085: library_id + is_orphaned on media_assets, file_hash_cache table");
    }

    /// <summary>
    /// Migration M-081: Collection Rename.
    /// Renames hub-era tables, columns, and indices to the collections vocabulary.
    /// Idempotent: skips if the <c>collections</c> table already exists.
    /// </summary>
    private static void MigrateCollectionRename(SqliteConnection conn)
    {
        // Guard: skip if `hubs` table no longer exists (rename already applied).
        bool hubsTableExists;
        using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='hubs';";
            hubsTableExists = Convert.ToInt64(probe.ExecuteScalar()) > 0;
        }

        if (hubsTableExists)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            DROP TABLE IF EXISTS collections;
            DROP TABLE IF EXISTS collection_items;
            ALTER TABLE hubs RENAME TO collections;
            ALTER TABLE hub_items RENAME TO collection_items;
            ALTER TABLE hub_placements RENAME TO collection_placements;
            ALTER TABLE hub_relationships RENAME TO collection_relationships;
            ALTER TABLE collections RENAME COLUMN hub_type TO collection_type;
            ALTER TABLE collections RENAME COLUMN parent_hub_id TO parent_collection_id;
            ALTER TABLE collection_items RENAME COLUMN hub_id TO collection_id;
            ALTER TABLE collection_placements RENAME COLUMN hub_id TO collection_id;
            ALTER TABLE collection_relationships RENAME COLUMN hub_id TO collection_id;
            ALTER TABLE works RENAME COLUMN hub_id TO collection_id;
            DROP INDEX IF EXISTS idx_hubs_rule_hash;
            DROP INDEX IF EXISTS idx_hubs_hub_type;
            DROP INDEX IF EXISTS idx_hubs_resolution;
            DROP INDEX IF EXISTS idx_hub_placements_hub_id;
            DROP INDEX IF EXISTS idx_hub_placements_location;
            DROP INDEX IF EXISTS idx_hub_rel_type_qid;
            DROP INDEX IF EXISTS idx_hub_rel_hub_id;
            CREATE INDEX idx_collections_rule_hash ON collections(rule_hash);
            CREATE INDEX idx_collections_collection_type ON collections(collection_type);
            CREATE INDEX idx_collections_resolution ON collections(resolution);
            CREATE INDEX idx_collection_placements_collection_id ON collection_placements(collection_id);
            CREATE INDEX idx_collection_placements_location ON collection_placements(location);
            CREATE INDEX idx_collection_rel_type_qid ON collection_relationships(rel_type, rel_qid);
            CREATE INDEX idx_collection_rel_collection_id ON collection_relationships(collection_id);
            UPDATE system_activity SET action_type = 'CollectionCreated' WHERE action_type = 'HubCreated';
            UPDATE system_activity SET action_type = 'CollectionAssigned' WHERE action_type = 'HubAssigned';
            UPDATE system_activity SET action_type = 'CollectionMerged' WHERE action_type = 'HubMerged';
            UPDATE collection_placements SET location = 'collections_page' WHERE location = 'hubs_page';
            """;
            cmd.ExecuteNonQuery();

            System.Diagnostics.Debug.WriteLine(
                "M-081: Collection Rename — tables, columns, indices renamed");
        }

        // Rename review_queue.proposed_hub_id ? proposed_collection_id (runs on both old and fresh DBs)
        MigrateAddColumnIfMissing(conn, "review_queue", "proposed_collection_id",
            "ALTER TABLE review_queue RENAME COLUMN proposed_hub_id TO proposed_collection_id;");
    }

    /// <summary>
    /// Migration M-086: ensure the universal collection-system columns exist on
    /// the modern <c>collections</c> table.
    /// This closes the gap where fresh/current databases already have
    /// <c>collections</c> (not <c>hubs</c>) but the base schema still lacks the
    /// Phase M-070 columns expected by <see cref="CollectionRepository"/>.
    /// Idempotent: each column is added only when missing.
    /// </summary>
    private static void MigrateCollectionUniversalColumns(SqliteConnection conn)
    {
        MigrateAddColumnIfMissing(conn, "collections", "resolution",
            "ALTER TABLE collections ADD COLUMN resolution TEXT NOT NULL DEFAULT 'query';");
        MigrateAddColumnIfMissing(conn, "collections", "rule_hash",
            "ALTER TABLE collections ADD COLUMN rule_hash TEXT;");
        MigrateAddColumnIfMissing(conn, "collections", "group_by_field",
            "ALTER TABLE collections ADD COLUMN group_by_field TEXT;");
        MigrateAddColumnIfMissing(conn, "collections", "match_mode",
            "ALTER TABLE collections ADD COLUMN match_mode TEXT NOT NULL DEFAULT 'all';");
        MigrateAddColumnIfMissing(conn, "collections", "sort_field",
            "ALTER TABLE collections ADD COLUMN sort_field TEXT;");
        MigrateAddColumnIfMissing(conn, "collections", "sort_direction",
            "ALTER TABLE collections ADD COLUMN sort_direction TEXT NOT NULL DEFAULT 'desc';");
        MigrateAddColumnIfMissing(conn, "collections", "live_updating",
            "ALTER TABLE collections ADD COLUMN live_updating INTEGER NOT NULL DEFAULT 1;");
        MigrateAddColumnIfMissing(conn, "collections", "square_artwork_path",
            "ALTER TABLE collections ADD COLUMN square_artwork_path TEXT;");
        MigrateAddColumnIfMissing(conn, "collections", "square_artwork_mime_type",
            "ALTER TABLE collections ADD COLUMN square_artwork_mime_type TEXT;");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_collections_rule_hash ON collections(rule_hash);
            CREATE INDEX IF NOT EXISTS idx_collections_collection_type ON collections(collection_type);
            CREATE INDEX IF NOT EXISTS idx_collections_resolution ON collections(resolution);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-065A: normalize artwork slot naming and supported typed assets.
    /// Rebuilds <c>entity_assets</c> if its asset type constraint still uses Backdrop,
    /// rewrites canonical/claim keys to <c>background</c>, and renames local files
    /// from <c>backdrop.*</c> to <c>background.*</c>.
    /// </summary>
    private static void MigrateArtworkSlotNormalization(SqliteConnection conn)
    {
        bool needsEntityAssetRebuild = false;
        using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='entity_assets';";
            var sql = probe.ExecuteScalar() as string;
            if (sql is not null)
            {
                needsEntityAssetRebuild =
                    !sql.Contains("SquareArt", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("Backdrop", StringComparison.OrdinalIgnoreCase)
                    || !sql.Contains("Background", StringComparison.OrdinalIgnoreCase)
                    || !sql.Contains("DiscArt", StringComparison.OrdinalIgnoreCase)
                    || !sql.Contains("ClearArt", StringComparison.OrdinalIgnoreCase)
                    || !sql.Contains("SeasonPoster", StringComparison.OrdinalIgnoreCase)
                    || !sql.Contains("SeasonThumb", StringComparison.OrdinalIgnoreCase)
                    || !sql.Contains("EpisodeStill", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (needsEntityAssetRebuild)
        {
            using var rebuild = conn.CreateCommand();
            rebuild.CommandText = """
                PRAGMA foreign_keys=OFF;

                CREATE TABLE entity_assets_new (
                    id               TEXT PRIMARY KEY,
                    entity_id        TEXT NOT NULL,
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

                INSERT INTO entity_assets_new (
                    id,
                    entity_id,
                    entity_type,
                    asset_type,
                    image_url,
                    local_image_path,
                    local_image_path_s,
                    local_image_path_m,
                    local_image_path_l,
                    source_provider,
                    width_px,
                    height_px,
                    aspect_class,
                    primary_hex,
                    secondary_hex,
                    accent_hex,
                    asset_class,
                    storage_location,
                    owner_scope,
                    is_preferred,
                    is_user_override,
                    is_locally_exported,
                    is_preferred_exported,
                    created_at,
                    updated_at
                )
                SELECT
                    id,
                    entity_id,
                    entity_type,
                    CASE asset_type
                        WHEN 'Backdrop' THEN 'Background'
                        ELSE asset_type
                    END AS asset_type,
                    image_url,
                    CASE
                        WHEN local_image_path IS NOT NULL
                             AND asset_type = 'Backdrop'
                             AND instr(local_image_path, 'backdrop.') > 0
                            THEN REPLACE(local_image_path, 'backdrop.', 'background.')
                        ELSE local_image_path
                    END AS local_image_path,
                    NULL AS local_image_path_s,
                    NULL AS local_image_path_m,
                    NULL AS local_image_path_l,
                    source_provider,
                    NULL AS width_px,
                    NULL AS height_px,
                    'UnsupportedRect' AS aspect_class,
                    NULL AS primary_hex,
                    NULL AS secondary_hex,
                    NULL AS accent_hex,
                    'Artwork' AS asset_class,
                    'Central' AS storage_location,
                    'Unknown' AS owner_scope,
                    is_preferred,
                    is_user_override,
                    0 AS is_locally_exported,
                    0 AS is_preferred_exported,
                    created_at,
                    updated_at
                FROM entity_assets;

                DROP TABLE entity_assets;

                ALTER TABLE entity_assets_new RENAME TO entity_assets;

                CREATE INDEX IF NOT EXISTS idx_entity_assets_entity
                    ON entity_assets(entity_id, entity_type);
                CREATE INDEX IF NOT EXISTS idx_entity_assets_type
                    ON entity_assets(entity_id, asset_type);

                PRAGMA foreign_keys=ON;
                """;
            rebuild.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE canonical_values
                SET key = 'background',
                    value = CASE
                        WHEN value LIKE '%/backdrop' THEN REPLACE(value, '/backdrop', '/background')
                        ELSE value
                    END
                WHERE key = 'backdrop';

                UPDATE metadata_claims
                SET claim_key = 'background',
                    claim_value = CASE
                        WHEN claim_value LIKE '%/backdrop' THEN REPLACE(claim_value, '/backdrop', '/background')
                        ELSE claim_value
                    END
                WHERE claim_key = 'backdrop';

                UPDATE entity_assets
                SET asset_type = 'Background',
                    local_image_path = CASE
                        WHEN local_image_path IS NOT NULL AND instr(local_image_path, 'backdrop.') > 0
                            THEN REPLACE(local_image_path, 'backdrop.', 'background.')
                        ELSE local_image_path
                    END
                WHERE asset_type = 'Backdrop';
                """;
            cmd.ExecuteNonQuery();
        }

        using var pathRows = conn.CreateCommand();
        pathRows.CommandText = """
            SELECT DISTINCT local_image_path
            FROM entity_assets
            WHERE local_image_path IS NOT NULL
              AND instr(local_image_path, 'background.') > 0;
            """;
        using var reader = pathRows.ExecuteReader();
        while (reader.Read())
        {
            var backgroundPath = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(backgroundPath))
                continue;

            var legacyPath = backgroundPath.Replace("background.", "backdrop.", StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(legacyPath) || File.Exists(backgroundPath))
                continue;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backgroundPath)!);
                File.Move(legacyPath, backgroundPath);
            }
            catch
            {
                // Best-effort migration. Startup should continue even if an individual file could not move.
            }
        }
    }

    /// <summary>
    /// Ensures collection support tables exist on fresh/current databases whose
    /// embedded schema predates the universal collection relationship tables.
    /// Safe to run after the hub-to-collection rename migration.
    /// </summary>
    private static void MigrateCollectionSupportTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-087: adds storage-policy metadata to <c>entity_assets</c>.
    /// </summary>
    private static void MigrateEntityAssetStorageColumns(SqliteConnection conn)
    {
        MigrateAddColumnIfMissing(conn, "entity_assets", "asset_class",
            "ALTER TABLE entity_assets ADD COLUMN asset_class TEXT NOT NULL DEFAULT 'Artwork';");
        MigrateAddColumnIfMissing(conn, "entity_assets", "storage_location",
            "ALTER TABLE entity_assets ADD COLUMN storage_location TEXT NOT NULL DEFAULT 'Central';");
        MigrateAddColumnIfMissing(conn, "entity_assets", "owner_scope",
            "ALTER TABLE entity_assets ADD COLUMN owner_scope TEXT NOT NULL DEFAULT 'Unknown';");
        MigrateAddColumnIfMissing(conn, "entity_assets", "is_locally_exported",
            "ALTER TABLE entity_assets ADD COLUMN is_locally_exported INTEGER NOT NULL DEFAULT 0;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "is_preferred_exported",
            "ALTER TABLE entity_assets ADD COLUMN is_preferred_exported INTEGER NOT NULL DEFAULT 0;");

        using var backfill = conn.CreateCommand();
        backfill.CommandText = """
            UPDATE entity_assets
            SET asset_class = COALESCE(NULLIF(asset_class, ''), 'Artwork'),
                storage_location = CASE
                    WHEN local_image_path IS NOT NULL AND instr(REPLACE(local_image_path, '\', '/'), '/.data/assets/') > 0 THEN 'Central'
                    WHEN local_image_path IS NOT NULL THEN 'Local'
                    ELSE COALESCE(NULLIF(storage_location, ''), 'Central')
                END,
                owner_scope = COALESCE(NULLIF(owner_scope, ''), 'Unknown')
            WHERE asset_class IS NULL
               OR asset_class = ''
               OR storage_location IS NULL
               OR storage_location = ''
               OR owner_scope IS NULL
               OR owner_scope = '';
            """;
        backfill.ExecuteNonQuery();
    }

    /// <summary>
    /// Migration M-088: measured artwork metadata and generated rendition paths on <c>entity_assets</c>.
    /// </summary>
    private static void MigrateEntityAssetArtworkMetadataColumns(SqliteConnection conn)
    {
        MigrateAddColumnIfMissing(conn, "entity_assets", "local_image_path_s",
            "ALTER TABLE entity_assets ADD COLUMN local_image_path_s TEXT;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "local_image_path_m",
            "ALTER TABLE entity_assets ADD COLUMN local_image_path_m TEXT;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "local_image_path_l",
            "ALTER TABLE entity_assets ADD COLUMN local_image_path_l TEXT;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "width_px",
            "ALTER TABLE entity_assets ADD COLUMN width_px INTEGER;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "height_px",
            "ALTER TABLE entity_assets ADD COLUMN height_px INTEGER;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "aspect_class",
            "ALTER TABLE entity_assets ADD COLUMN aspect_class TEXT NOT NULL DEFAULT 'UnsupportedRect';");
        MigrateAddColumnIfMissing(conn, "entity_assets", "primary_hex",
            "ALTER TABLE entity_assets ADD COLUMN primary_hex TEXT;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "secondary_hex",
            "ALTER TABLE entity_assets ADD COLUMN secondary_hex TEXT;");
        MigrateAddColumnIfMissing(conn, "entity_assets", "accent_hex",
            "ALTER TABLE entity_assets ADD COLUMN accent_hex TEXT;");

        using var backfill = conn.CreateCommand();
        backfill.CommandText = """
            UPDATE entity_assets
            SET aspect_class = COALESCE(NULLIF(aspect_class, ''), 'UnsupportedRect')
            WHERE aspect_class IS NULL
               OR aspect_class = '';
            """;
        backfill.ExecuteNonQuery();
    }

    private static void MigrateTextTracks(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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

            CREATE INDEX IF NOT EXISTS idx_text_tracks_asset_kind
                ON text_tracks(asset_id, kind);
            CREATE INDEX IF NOT EXISTS idx_text_tracks_preferred
                ON text_tracks(asset_id, kind, language, is_preferred);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateSeriesManifestTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS series_manifest_hydrations (
                series_qid            TEXT PRIMARY KEY,
                collection_id         TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
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

            CREATE INDEX IF NOT EXISTS idx_series_manifest_hydrations_collection
                ON series_manifest_hydrations(collection_id);

            CREATE TABLE IF NOT EXISTS series_manifest_items (
                id                          TEXT PRIMARY KEY,
                collection_id               TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
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
                linked_work_id              TEXT REFERENCES works(id) ON DELETE SET NULL,
                last_hydrated_at            TEXT NOT NULL,
                created_at                  TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at                  TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE(collection_id, item_qid)
            );

            CREATE INDEX IF NOT EXISTS idx_series_manifest_items_series_qid
                ON series_manifest_items(series_qid);
            CREATE INDEX IF NOT EXISTS idx_series_manifest_items_collection
                ON series_manifest_items(collection_id);
            CREATE INDEX IF NOT EXISTS idx_series_manifest_items_item_qid
                ON series_manifest_items(item_qid);
            CREATE INDEX IF NOT EXISTS idx_series_manifest_items_linked_work
                ON series_manifest_items(linked_work_id);
            CREATE INDEX IF NOT EXISTS idx_series_manifest_items_ownership
                ON series_manifest_items(ownership_state);
            """;
        cmd.ExecuteNonQuery();
    }
    /// <summary>
    /// Executes a VACUUM to reclaim unused pages.
    /// Spec: "SHOULD perform a VACUUM during low-activity maintenance windows."
    /// Call when <c>MaintenanceSettings.VacuumOnStartup</c> is <c>true</c>.
    /// </summary>
    public void Vacuum()
    {
        var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public Task AcquireWriteLockAsync(CancellationToken ct = default)
        => _writeLock.WaitAsync(ct);

    /// <inheritdoc/>
    public void ReleaseWriteLock()
        => _writeLock.Release();

    /// <inheritdoc/>
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _writeLock.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads <c>Schema/schema.sql</c> from the assembly's embedded resources.
    /// The resource is registered in the .csproj as an EmbeddedResource so the
    /// DDL ships inside the DLL and requires no file-system deployment.
    /// </summary>
    private static string LoadEmbeddedSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Resource name follows the default convention:
        //   <RootNamespace>.<folder-path-with-dots>.<filename>
        //   -> "MediaEngine.Storage.Schema.schema.sql"
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("schema.sql", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Embedded resource 'schema.sql' was not found in the assembly. " +
                "Ensure Schema\\schema.sql is marked as EmbeddedResource in the .csproj.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

