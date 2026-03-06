using System.Reflection;
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
    public void InitializeSchema()
    {
        var conn = Open();
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

        // Migration M-004: Phase 7 - add display_name to hubs.
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
        // hub context.  Replaces the limited transaction_log for detailed audit.
        MigrateCreateTableIfMissing(
            conn,
            probeTable:  "system_activity",
            probeColumn: "id",
            ddl: """
                CREATE TABLE IF NOT EXISTS system_activity (
                    id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    occurred_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    action_type  TEXT    NOT NULL,
                    hub_name     TEXT,
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
        // Adds Illustrator, Cast Member, Voice Actor, Screenwriter, Composer.
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

        // Migration M-014: Add universe_status to hubs.
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
            fix.CommandText = """
                UPDATE provider_registry
                SET id = 'b3000003-d000-4000-8000-000000000004'
                WHERE id = 'b3000003-w000-4000-8000-000000000004';

                UPDATE metadata_claims
                SET provider_id = 'b3000003-d000-4000-8000-000000000004'
                WHERE provider_id = 'b3000003-w000-4000-8000-000000000004';
                """;
            fix.ExecuteNonQuery();
        }

        // Seed S-001: provider_registry entries for all known providers.
        // metadata_claims.provider_id has a FK to provider_registry(id), so these
        // rows MUST exist before any claim is written.  INSERT OR IGNORE makes this
        // idempotent — safe to run on every startup.
        SeedProviderRegistry(conn);

        // Seed S-002: default "Owner" Administrator profile.
        // First-run experience: single user with full access.
        SeedDefaultProfile(conn);
    }

    /// <summary>
    /// Seeds the <c>provider_registry</c> table with all known provider GUIDs.
    /// Uses <c>INSERT OR IGNORE</c> so duplicate rows are silently skipped.
    /// </summary>
    private static void SeedProviderRegistry(SqliteConnection conn)
    {
        ReadOnlySpan<(string Id, string Name, string Version)> providers =
        [
            ("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f", "local_processor",      "1.0"),
            ("c9d8e7f6-a5b4-4321-fedc-0102030405c9",  "library_scanner",      "1.0"),
            ("b1000001-e000-4000-8000-000000000001",   "apple_books",          "2.0"),
            ("b2000002-a000-4000-8000-000000000003",   "audnexus",            "1.0"),
            ("b3000003-d000-4000-8000-000000000004",   "wikidata",            "1.0"),
            ("b4000004-0000-4000-8000-000000000005",   "open_library",        "1.0"),
            ("b5000005-0000-4000-8000-000000000006",   "google_books",        "1.0"),
            ("d0000000-0000-4000-8000-000000000001",   "user_manual",         "1.0"),
        ];

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO provider_registry (id, name, version, is_enabled)
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
    /// Adds: Illustrator, Cast Member, Voice Actor, Screenwriter, Composer.
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
        // We look for 'Illustrator' in the CREATE TABLE SQL to detect whether
        // the migration has already been applied.
        bool alreadyExpanded = false;
        using (var sqlCmd = conn.CreateCommand())
        {
            sqlCmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='persons';";
            var sql = sqlCmd.ExecuteScalar() as string;
            if (sql is not null && sql.Contains("Illustrator", StringComparison.OrdinalIgnoreCase))
                alreadyExpanded = true;
            // If the table doesn't exist yet (fresh install), schema.sql handles it.
            if (sql is null)
                alreadyExpanded = true;
        }

        if (alreadyExpanded)
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys=OFF;

            CREATE TABLE persons_new (
                id           TEXT NOT NULL PRIMARY KEY,
                name         TEXT NOT NULL,
                role         TEXT NOT NULL CHECK (role IN (
                    'Author','Narrator','Director',
                    'Illustrator','Cast Member','Voice Actor',
                    'Screenwriter','Composer')),
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
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
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
