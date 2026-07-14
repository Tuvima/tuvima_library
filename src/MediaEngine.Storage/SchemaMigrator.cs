using MediaEngine.Domain;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

internal sealed class SchemaMigrator
{
    public void RunStartupTasks(SqliteConnection conn)
    {
        EnsureCurrentColumns(conn);
        EnsureCurrentIndexes(conn);
        SeedMetadataProviders(conn);
        SeedDefaultProfile(conn);
    }

    private static void EnsureCurrentColumns(SqliteConnection conn)
    {
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
            "metadata_claims",
            "decision_source_provider_id",
            "ALTER TABLE metadata_claims ADD COLUMN decision_source_provider_id BLOB REFERENCES metadata_providers(id);");
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

            CREATE INDEX IF NOT EXISTS idx_works_collection_ordinal_sort
                ON works(collection_id, ordinal_sort);

            CREATE INDEX IF NOT EXISTS idx_canonical_values_key_value_entity
                ON canonical_values(key, value, entity_id);

            CREATE INDEX IF NOT EXISTS idx_canonical_values_entity_key
                ON canonical_values(entity_id, key);

            CREATE INDEX IF NOT EXISTS idx_canonical_value_arrays_key_value_entity
                ON canonical_value_arrays(key, value, entity_id);

            CREATE INDEX IF NOT EXISTS idx_person_media_links_person
                ON person_media_links(person_id);

            CREATE INDEX IF NOT EXISTS idx_person_media_links_asset_role_person
                ON person_media_links(media_asset_id, role, person_id);

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
