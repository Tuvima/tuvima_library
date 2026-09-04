using MediaEngine.Domain;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

internal sealed class SchemaMigrator
{
    public void RunStartupTasks(SqliteConnection conn)
    {
        EnsureIdentitySchema(conn);
        EnsureOnboardingSchema(conn);
        EnsureAdaptiveDeliverySchema(conn);
        EnsureCurrentColumns(conn);
        EnsureCurrentIndexes(conn);
        SeedMetadataProviders(conn);
        SeedDefaultProfile(conn);
    }

    private static void EnsureAdaptiveDeliverySchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS adaptive_hls_packages (
                id BLOB NOT NULL PRIMARY KEY,
                asset_id BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
                source_hash TEXT NOT NULL,
                profile_key TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('preparing', 'ready', 'failed', 'deleting')),
                root_path TEXT NOT NULL,
                total_bytes INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                last_accessed TEXT NOT NULL,
                completed_at TEXT,
                last_error TEXT,
                UNIQUE(asset_id, source_hash, profile_key)
            );
            CREATE INDEX IF NOT EXISTS idx_adaptive_hls_packages_eviction
                ON adaptive_hls_packages(status, last_accessed);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureOnboardingSchema(SqliteConnection conn)
    {
        DatabaseConnection.ExecuteStartupTransaction(conn, transaction =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS onboarding_workflows (
                workflow_version INTEGER NOT NULL PRIMARY KEY,
                state TEXT NOT NULL CHECK (state IN ('in_progress', 'complete')),
                current_step TEXT NOT NULL,
                administrator_profile_id BLOB REFERENCES profiles(id) ON DELETE SET NULL,
                revision INTEGER NOT NULL DEFAULT 0,
                completed_at TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS onboarding_steps (
                workflow_version INTEGER NOT NULL REFERENCES onboarding_workflows(workflow_version) ON DELETE CASCADE,
                step_key TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('not_started', 'in_progress', 'passed', 'deferred', 'blocked')),
                detail TEXT,
                repair_target TEXT,
                completed_at TEXT,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (workflow_version, step_key)
            );

            CREATE TABLE IF NOT EXISTS onboarding_sessions (
                id BLOB NOT NULL PRIMARY KEY,
                workflow_version INTEGER NOT NULL REFERENCES onboarding_workflows(workflow_version) ON DELETE CASCADE,
                token_hash TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                last_used_at TEXT NOT NULL,
                revoked_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_onboarding_sessions_active
                ON onboarding_sessions(workflow_version, expires_at, revoked_at);

            CREATE TABLE IF NOT EXISTS onboarding_restore_operations (
                id BLOB NOT NULL PRIMARY KEY,
                workflow_version INTEGER NOT NULL REFERENCES onboarding_workflows(workflow_version) ON DELETE CASCADE,
                archive_path TEXT NOT NULL,
                original_file_name TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('inspected', 'scheduled', 'applied', 'failed', 'cancelled')),
                manifest_version TEXT NOT NULL,
                database_epoch TEXT NOT NULL,
                summary_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            INSERT OR IGNORE INTO onboarding_workflows
                (workflow_version, state, current_step, revision, created_at, updated_at)
            VALUES
                (1, 'in_progress', 'preflight', 0, strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

            INSERT OR IGNORE INTO onboarding_steps (workflow_version, step_key, status, updated_at)
            VALUES
                (1, 'preflight', 'not_started', strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                (1, 'administrator', 'not_started', strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                (1, 'media-locations', 'not_started', strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                (1, 'providers', 'not_started', strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                (1, 'readiness', 'not_started', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            """;
            cmd.ExecuteNonQuery();
        });
    }

    private static void EnsureIdentitySchema(SqliteConnection conn)
    {
        using var legacyIdentity = conn.CreateCommand();
        legacyIdentity.CommandText = """
            SELECT CASE WHEN
                EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'profile_external_logins')
                OR EXISTS (SELECT 1 FROM pragma_table_info('profile_credentials') WHERE name = 'normalized_username')
                OR EXISTS (SELECT 1 FROM pragma_table_info('auth_sessions') WHERE name = 'profile_id')
                OR EXISTS (SELECT 1 FROM pragma_table_info('password_recovery_codes') WHERE name = 'profile_id')
            THEN 1 ELSE 0 END;
            """;
        if (Convert.ToInt32(legacyIdentity.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
        {
            throw new InvalidOperationException(
                "Unsupported pre-beta profile-owned authentication schema was found. Reset the disposable development database and configure the new account/profile identity model.");
        }

        using var legacyPin = conn.CreateCommand();
        legacyPin.CommandText = "SELECT COUNT(1) FROM pragma_table_info('profiles') WHERE name = 'pin_hash';";
        if (Convert.ToInt32(legacyPin.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
        {
            using var populated = conn.CreateCommand();
            populated.CommandText = "SELECT COUNT(1) FROM profiles WHERE pin_hash IS NOT NULL AND trim(pin_hash) <> '';";
            if (Convert.ToInt32(populated.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
            {
                throw new InvalidOperationException(
                    "Unsupported pre-beta SHA-256 profile PIN state was found. Reset the disposable development database and configure a new password or profile PIN.");
            }
        }

        DatabaseConnection.ExecuteStartupTransaction(conn, transaction =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                migration_id TEXT NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations (migration_id, applied_at)
            VALUES ('004_account_profile_identity', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            """;
            cmd.ExecuteNonQuery();
        });
    }

    private static void EnsureCurrentColumns(SqliteConnection conn)
    {
        AddColumnIfMissing(conn, "collections", "background_artwork_path",
            "ALTER TABLE collections ADD COLUMN background_artwork_path TEXT;");
        AddColumnIfMissing(conn, "collections", "background_artwork_mime_type",
            "ALTER TABLE collections ADD COLUMN background_artwork_mime_type TEXT;");
        AddColumnIfMissing(conn, "collections", "logo_artwork_path",
            "ALTER TABLE collections ADD COLUMN logo_artwork_path TEXT;");
        AddColumnIfMissing(conn, "collections", "logo_artwork_mime_type",
            "ALTER TABLE collections ADD COLUMN logo_artwork_mime_type TEXT;");
        AddColumnIfMissing(conn, "collections", "banner_artwork_path",
            "ALTER TABLE collections ADD COLUMN banner_artwork_path TEXT;");
        AddColumnIfMissing(conn, "collections", "banner_artwork_mime_type",
            "ALTER TABLE collections ADD COLUMN banner_artwork_mime_type TEXT;");
        AddColumnIfMissing(conn, "collections", "secondary_sort_field",
            "ALTER TABLE collections ADD COLUMN secondary_sort_field TEXT;");
        AddColumnIfMissing(conn, "collections", "secondary_sort_direction",
            "ALTER TABLE collections ADD COLUMN secondary_sort_direction TEXT;");
        AddColumnIfMissing(
            conn,
            "media_assets",
            "presented_at",
            "ALTER TABLE media_assets ADD COLUMN presented_at TEXT;");

        AddColumnIfMissing(
            conn,
            "works",
            "ordinal_sort",
            "ALTER TABLE works ADD COLUMN ordinal_sort REAL;");

        AddColumnIfMissing(
            conn,
            "series_manifest_items",
            "membership_scope",
            "ALTER TABLE series_manifest_items ADD COLUMN membership_scope TEXT NOT NULL DEFAULT 'MainSequence';");

        AddColumnIfMissing(
            conn,
            "series_manifest_items",
            "ordinal_scope_qid",
            "ALTER TABLE series_manifest_items ADD COLUMN ordinal_scope_qid TEXT;");

        AddColumnIfMissing(
            conn,
            "series_manifest_items",
            "duration",
            "ALTER TABLE series_manifest_items ADD COLUMN duration TEXT;");

        AddColumnIfMissing(
            conn,
            "metadata_claims",
            "decision_source_provider_id",
            "ALTER TABLE metadata_claims ADD COLUMN decision_source_provider_id BLOB REFERENCES metadata_providers(id);");

        AddColumnIfMissing(conn, "metadata_claims", "observation_set_id",
            "ALTER TABLE metadata_claims ADD COLUMN observation_set_id BLOB;");
        AddColumnIfMissing(conn, "metadata_claims", "is_current",
            "ALTER TABLE metadata_claims ADD COLUMN is_current INTEGER NOT NULL DEFAULT 1 CHECK (is_current IN (0, 1));");
        AddColumnIfMissing(conn, "metadata_claims", "superseded_at",
            "ALTER TABLE metadata_claims ADD COLUMN superseded_at TEXT;");

        AddColumnIfMissing(conn, "identity_jobs", "poison_attempt_count",
            "ALTER TABLE identity_jobs ADD COLUMN poison_attempt_count INTEGER NOT NULL DEFAULT 0;");
        AddColumnIfMissing(conn, "identity_jobs", "last_outcome_category",
            "ALTER TABLE identity_jobs ADD COLUMN last_outcome_category TEXT;");
        AddColumnIfMissing(conn, "media_operations", "last_outcome_category",
            "ALTER TABLE media_operations ADD COLUMN last_outcome_category TEXT;");
        AddColumnIfMissing(conn, "media_operations", "poison_attempt_count",
            "ALTER TABLE media_operations ADD COLUMN poison_attempt_count INTEGER NOT NULL DEFAULT 0;");
        AddColumnIfMissing(conn, "entity_capability_states", "last_outcome_category",
            "ALTER TABLE entity_capability_states ADD COLUMN last_outcome_category TEXT;");
        AddColumnIfMissing(conn, "ai_feature_artifacts", "last_outcome_category",
            "ALTER TABLE ai_feature_artifacts ADD COLUMN last_outcome_category TEXT;");

        AddColumnIfMissing(
            conn,
            "player_queue_items",
            "year",
            "ALTER TABLE player_queue_items ADD COLUMN year TEXT;");

        AddColumnIfMissing(
            conn,
            "player_queue_items",
            "content_rating",
            "ALTER TABLE player_queue_items ADD COLUMN content_rating TEXT;");

        AddColumnIfMissing(
            conn,
            "player_queue_items",
            "season_number",
            "ALTER TABLE player_queue_items ADD COLUMN season_number TEXT;");

        AddColumnIfMissing(
            conn,
            "player_queue_items",
            "episode_number",
            "ALTER TABLE player_queue_items ADD COLUMN episode_number TEXT;");

        AddColumnIfMissing(
            conn,
            "player_queue_items",
            "episode_title",
            "ALTER TABLE player_queue_items ADD COLUMN episode_title TEXT;");

        AddColumnIfMissing(
            conn,
            "player_queue_items",
            "quality",
            "ALTER TABLE player_queue_items ADD COLUMN quality TEXT;");
    }

    private static void SeedMetadataProviders(SqliteConnection conn)
    {
        ReadOnlySpan<(Guid Id, string Name, string Version)> providers =
        [
            (WellKnownProviders.LocalProcessor, "local_processor", "1.0"),
            (WellKnownProviders.LibraryScanner, "library_scanner", "1.0"),
            (WellKnownProviders.AppleApi, "apple_api", "2.0"),
            (WellKnownProviders.Wikidata, "wikidata", "1.0"),
            (WellKnownProviders.Wikipedia, "wikipedia", "1.0"),
            (WellKnownProviders.OpenLibrary, "open_library", "1.0"),
            (WellKnownProviders.MusicBrainz, "musicbrainz", "1.0"),
            (WellKnownProviders.Tmdb, "tmdb", "1.0"),
            (WellKnownProviders.ComicVine, "comicvine", "1.0"),
            (WellKnownProviders.Lrclib, "lrclib", "1.0"),
            (WellKnownProviders.OpenSubtitles, "opensubtitles", "1.0"),
            (WellKnownProviders.UserManual, "user_manual", "1.0"),
            (WellKnownProviders.FanartTv, "fanart_tv", "1.0"),
            (WellKnownProviders.AiProvider, "ai_provider", "1.0"),
        ];

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO metadata_providers (id, name, version, is_enabled)
            VALUES (@id, @name, @version, 1);
            """;

        var pId = cmd.Parameters.Add("@id", SqliteType.Blob);
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pVersion = cmd.Parameters.Add("@version", SqliteType.Text);

        foreach (var (id, name, version) in providers)
        {
            pId.Value = GuidSql.ToBlob(id);
            pName.Value = name;
            pVersion.Value = version;
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultProfile(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO profiles (id, display_name, avatar_color, role, created_at)
            VALUES (@id, @name, @color, @role, @created);
            """;
        cmd.Parameters.Add("@id", SqliteType.Blob).Value =
            GuidSql.ToBlob(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        cmd.Parameters.AddWithValue("@name", "Owner");
        cmd.Parameters.AddWithValue("@color", "#7C4DFF");
        cmd.Parameters.AddWithValue("@role", "Administrator");
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void EnsureCurrentIndexes(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH duplicate_pending_reviews AS (
                SELECT rowid,
                       ROW_NUMBER() OVER (
                           PARTITION BY entity_id, trigger
                           ORDER BY created_at ASC, rowid ASC
                       ) AS rn
                FROM review_queue
                WHERE status = 'Pending'
            )
            UPDATE review_queue
            SET status = 'Resolved',
                resolved_at = strftime('%Y-%m-%dT%H:%M:%fZ','now'),
                resolved_by = 'system:review-dedupe'
            WHERE rowid IN (
                SELECT rowid
                FROM duplicate_pending_reviews
                WHERE rn > 1
            );

            CREATE INDEX IF NOT EXISTS idx_editions_work_id
                ON editions(work_id);

            CREATE INDEX IF NOT EXISTS idx_media_assets_edition_id
                ON media_assets(edition_id);

            CREATE INDEX IF NOT EXISTS idx_media_assets_presented
                ON media_assets(presented_at) WHERE presented_at IS NOT NULL;

            CREATE INDEX IF NOT EXISTS idx_media_assets_file_path_root
                ON media_assets(file_path_root COLLATE NOCASE);

            CREATE INDEX IF NOT EXISTS idx_media_assets_status
                ON media_assets(status);

            DELETE FROM file_hash_cache
            WHERE rowid NOT IN (
                SELECT MAX(rowid)
                FROM file_hash_cache
                GROUP BY absolute_path COLLATE NOCASE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_file_hash_cache_path_nocase
                ON file_hash_cache(absolute_path COLLATE NOCASE);

            CREATE INDEX IF NOT EXISTS idx_collection_items_collection_sort
                ON collection_items(collection_id, sort_order);

            CREATE INDEX IF NOT EXISTS idx_collection_items_work
                ON collection_items(work_id);

            CREATE INDEX IF NOT EXISTS idx_works_collection_ordinal_sort
                ON works(collection_id, ordinal_sort);

            CREATE INDEX IF NOT EXISTS idx_canonical_values_key_value_entity
                ON canonical_values(key, value, entity_id);

            CREATE INDEX IF NOT EXISTS idx_canonical_values_entity_key
                ON canonical_values(entity_id, key);

            CREATE INDEX IF NOT EXISTS idx_canonical_value_arrays_key_value_entity
                ON canonical_value_arrays(key, value, entity_id);

            CREATE INDEX IF NOT EXISTS idx_canonical_value_arrays_key_qid_entity
                ON canonical_value_arrays(key, value_qid, entity_id);

            CREATE INDEX IF NOT EXISTS idx_metadata_claims_current_lookup
                ON metadata_claims(entity_id, provider_id, claim_key, is_current);

            CREATE INDEX IF NOT EXISTS idx_person_media_links_person
                ON person_media_links(person_id);

            CREATE INDEX IF NOT EXISTS idx_person_media_links_asset_role_person
                ON person_media_links(media_asset_id, role, person_id);

            CREATE INDEX IF NOT EXISTS idx_persons_name_nocase
                ON persons(name COLLATE NOCASE);

            CREATE INDEX IF NOT EXISTS idx_works_curator_state
                ON works(curator_state) WHERE curator_state IS NOT NULL;

            CREATE INDEX IF NOT EXISTS idx_works_catalog_media_type
                ON works(is_catalog_only, media_type);

            CREATE INDEX IF NOT EXISTS idx_ingestion_log_run_created
                ON ingestion_log(ingestion_run_id, created_at);

            CREATE INDEX IF NOT EXISTS idx_identity_jobs_run_entity_updated
                ON identity_jobs(ingestion_run_id, entity_id, updated_at);

            CREATE INDEX IF NOT EXISTS idx_identity_jobs_activity_latest
                ON identity_jobs(ingestion_run_id, entity_id, updated_at, created_at);

            CREATE INDEX IF NOT EXISTS idx_identity_jobs_next_retry
                ON identity_jobs(next_retry_at) WHERE next_retry_at IS NOT NULL;

            CREATE INDEX IF NOT EXISTS idx_media_operations_source_path
                ON media_operations(operation_type, source_path, status);

            CREATE INDEX IF NOT EXISTS idx_media_operations_batch_entity_type
                ON media_operations(batch_id, entity_id, operation_type);

            CREATE INDEX IF NOT EXISTS idx_media_operation_events_batch_entity
                ON media_operation_events(batch_id, entity_id, occurred_at);

            CREATE INDEX IF NOT EXISTS idx_review_queue_status_entity_ready
                ON review_queue(status, entity_id, review_ready_at);

            CREATE INDEX IF NOT EXISTS idx_system_activity_run_entity_action
                ON system_activity(ingestion_run_id, entity_id, action_type, occurred_at);

            CREATE UNIQUE INDEX IF NOT EXISTS ux_review_queue_pending_entity_trigger
                ON review_queue(entity_id, trigger)
                WHERE status = 'Pending';

            CREATE UNIQUE INDEX IF NOT EXISTS ux_collections_custom_rule_hash
                ON collections(rule_hash)
                WHERE rule_hash IS NOT NULL AND is_enabled = 1 AND collection_type = 'Custom';
            """;
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(
        SqliteConnection conn,
        string table,
        string column,
        string alterSql)
    {
        using var exists = conn.CreateCommand();
        exists.CommandText = $"PRAGMA table_info([{table}]);";
        using var reader = exists.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = alterSql;
        alter.ExecuteNonQuery();
    }
}
