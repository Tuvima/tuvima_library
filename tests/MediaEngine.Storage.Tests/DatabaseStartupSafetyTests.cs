using System.Data;
using System.Threading.Tasks;
using MediaEngine.Domain;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Tests;

public sealed class DatabaseStartupSafetyTests
{
    [Fact]
    public void FreshDatabase_InitializesRequiredTablesIndexesAndWalSettings()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();

        string[] requiredTables =
        [
            "media_assets",
            "editions",
            "works",
            "collections",
            "review_queue",
            "user_states",
            "profiles",
            "ingestion_batches",
            "search_index",
        ];

        foreach (var table in requiredTables)
        {
            Assert.True(TableExists(conn, table), $"Required table '{table}' was not created.");
        }

        string[] requiredIndexes =
        [
            "idx_review_queue_status",
            "idx_review_queue_entity_id",
            "idx_media_assets_content_hash",
            "idx_ingestion_batches_status",
        ];

        foreach (var index in requiredIndexes)
        {
            Assert.True(IndexExists(conn, index), $"Required index '{index}' was not created.");
        }

        Assert.Equal("wal", Scalar(conn, "PRAGMA journal_mode;").ToLowerInvariant());
        Assert.Equal("1", Scalar(conn, "PRAGMA foreign_keys;"));
        Assert.Equal("5000", Scalar(conn, "PRAGMA busy_timeout;"));
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void FreshDatabase_UsesGuidBlobStorageEpoch()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("guid-blob-v1", Scalar(conn, "SELECT value FROM storage_metadata WHERE key = 'storage_epoch';"));

        (string Table, string Column)[] internalGuidColumns =
        [
            ("metadata_providers", "id"),
            ("provider_config", "provider_id"),
            ("collections", "id"),
            ("works", "id"),
            ("editions", "id"),
            ("media_assets", "id"),
            ("persons", "id"),
            ("metadata_claims", "id"),
            ("metadata_claims", "entity_id"),
            ("metadata_claims", "provider_id"),
            ("canonical_values", "entity_id"),
            ("canonical_values", "winning_provider_id"),
            ("canonical_value_arrays", "entity_id"),
            ("profiles", "id"),
            ("entity_assets", "id"),
            ("entity_assets", "entity_id"),
            ("pending_person_signals", "id"),
            ("pending_person_signals", "entity_id"),
            ("system_activity", "entity_id"),
            ("system_activity", "profile_id"),
            ("system_activity", "ingestion_run_id"),
            ("review_queue", "id"),
            ("review_queue", "entity_id"),
            ("review_queue", "source_operation_id"),
            ("ingestion_batches", "id"),
            ("ingestion_log", "id"),
            ("ingestion_log", "media_asset_id"),
            ("ingestion_log", "ingestion_run_id"),
            ("bridge_ids", "id"),
            ("bridge_ids", "entity_id"),
            ("identity_jobs", "id"),
            ("identity_jobs", "entity_id"),
            ("identity_jobs", "ingestion_run_id"),
            ("identity_jobs", "selected_candidate_id"),
            ("retail_match_candidates", "id"),
            ("retail_match_candidates", "job_id"),
            ("wikidata_bridge_candidates", "id"),
            ("wikidata_bridge_candidates", "job_id"),
            ("person_aliases", "pseudonym_person_id"),
            ("person_aliases", "real_person_id"),
            ("person_media_links", "media_asset_id"),
            ("person_media_links", "person_id"),
            ("person_roles", "person_id"),
            ("person_group_members", "group_id"),
            ("person_group_members", "member_id"),
            ("character_performer_links", "person_id"),
            ("character_performer_links", "fictional_entity_id"),
            ("character_portraits", "id"),
            ("character_portraits", "person_id"),
            ("character_portraits", "fictional_entity_id"),
            ("media_operations", "id"),
            ("media_operations", "entity_id"),
            ("media_operations", "batch_id"),
            ("media_operation_events", "id"),
            ("media_operation_events", "operation_id"),
            ("media_operation_events", "entity_id"),
            ("media_operation_events", "batch_id"),
            ("entity_capability_states", "id"),
            ("entity_capability_states", "entity_id"),
            ("entity_capability_states", "last_operation_id"),
            ("entity_events", "id"),
            ("entity_events", "entity_id"),
            ("entity_events", "ingestion_run_id"),
            ("entity_field_changes", "id"),
            ("entity_field_changes", "event_id"),
            ("entity_field_changes", "entity_id"),
        ];

        foreach (var (table, column) in internalGuidColumns)
        {
            Assert.Equal("BLOB", ColumnType(conn, table, column));
        }

        Assert.Equal("TEXT", ColumnType(conn, "works", "wikidata_qid"));
        Assert.Equal("TEXT", ColumnType(conn, "media_assets", "content_hash"));
        Assert.Equal("TEXT", ColumnType(conn, "bridge_ids", "provider_id"));
        Assert.Equal("TEXT", ColumnType(conn, "provider_response_cache", "provider_id"));
    }

    [Fact]
    public async Task CanonicalValueRepository_KeepsMultiValuedKeysOutOfScalarTable()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        var repo = new CanonicalValueRepository(fixture.Database);
        var entityId = Guid.NewGuid();
        await repo.UpsertBatchAsync([
            new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.Title,
                Value = "Dune",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.Author,
                Value = "Frank Herbert",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        ]);

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal(1, Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM canonical_values;")));
        Assert.Equal("Dune", Scalar(conn, "SELECT value FROM canonical_values WHERE key = 'title';"));
        Assert.Equal("0", Scalar(conn, "SELECT COUNT(*) FROM canonical_values WHERE key = 'author';"));
    }

    [Fact]
    public void StartupInitialization_IsIdempotent()
    {
        using var fixture = TempDatabase.Create();

        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();
        Assert.True(TableExists(conn, "review_queue"));
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void StartupMigrations_RecreateReviewQueueFromPreviousSchemaFixture()
    {
        using var fixture = TempDatabase.Create();

        fixture.Database.InitializeSchema();
        using (var conn = fixture.Database.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            // First compatibility fixture: a previous schema state before the review queue table existed.
            cmd.CommandText = "DROP TABLE IF EXISTS review_queue;";
            cmd.ExecuteNonQuery();
        }

        fixture.Database.RunStartupChecks();

        using var upgraded = fixture.Database.CreateConnection();
        Assert.True(TableExists(upgraded, "review_queue"));
        Assert.True(IndexExists(upgraded, "idx_review_queue_status"));
        Assert.True(IndexExists(upgraded, "idx_review_queue_entity_id"));
    }

    [Fact]
    public void ShortLivedConnections_DoNotDisposeSharedStartupConnection()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        var startup = fixture.Database.Open();
        using (var transient = fixture.Database.CreateConnection())
        {
            Assert.NotSame(startup, transient);
            Assert.Equal(System.Data.ConnectionState.Open, transient.State);
        }

        Assert.Equal(System.Data.ConnectionState.Open, startup.State);
        using var cmd = startup.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) > 0);
    }

    [Fact]
    public void Open_ReusesSharedStartupConnection()
    {
        using var fixture = TempDatabase.Create();

        var first = fixture.Database.Open();
        var second = fixture.Database.Open();

        Assert.Same(first, second);
        Assert.Equal(ConnectionState.Open, first.State);
    }

    [Fact]
    public void Dispose_ClosesSharedStartupConnection()
    {
        var fixture = TempDatabase.Create();
        var startup = fixture.Database.Open();

        fixture.Dispose();

        Assert.Equal(ConnectionState.Closed, startup.State);
    }

    [Fact]
    public void Vacuum_RemainsCallableAfterStartup()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        fixture.Database.Vacuum();

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task WriteLock_SerializesAndCanBeReacquired()
    {
        using var fixture = TempDatabase.Create();

        await fixture.Database.AcquireWriteLockAsync();
        var waiterStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiterAcquired = false;

        var waiter = Task.Run(async () =>
        {
            waiterStarted.SetResult();
            await fixture.Database.AcquireWriteLockAsync();
            waiterAcquired = true;
            fixture.Database.ReleaseWriteLock();
        });

        await waiterStarted.Task;
        await Task.Delay(50);
        Assert.False(waiterAcquired);

        fixture.Database.ReleaseWriteLock();
        await waiter;

        await fixture.Database.AcquireWriteLockAsync();
        fixture.Database.ReleaseWriteLock();
    }

    private static bool TableExists(SqliteConnection conn, string name) =>
        Exists(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;", name);

    private static bool IndexExists(SqliteConnection conn, string name) =>
        Exists(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name;", name);

    private static bool Exists(SqliteConnection conn, string sql, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private static string ColumnType(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return reader.GetString(2);
        }

        throw new InvalidOperationException($"Column {table}.{column} was not found.");
    }

    private static void CreateLegacyTextGuidDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE metadata_providers (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                version TEXT NOT NULL
            );
            INSERT INTO metadata_providers (id, name, version)
            VALUES ('00000000-0000-0000-0000-000000000001', 'legacy', '1.0');
            """;
        cmd.ExecuteNonQuery();
    }

    private sealed class TempDatabase : IDisposable
    {
        private TempDatabase(string path)
        {
            Path = path;
            Database = new DatabaseConnection(path);
        }

        public string Path { get; }

        public DatabaseConnection Database { get; }

        public static TempDatabase Create() =>
            new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tuvima_startup_{Guid.NewGuid():N}.db"));

        public void Dispose()
        {
            Database.Dispose();
            DatabaseStartupSafetyTests.TryDelete(Path);
            DatabaseStartupSafetyTests.TryDelete($"{Path}-wal");
            DatabaseStartupSafetyTests.TryDelete($"{Path}-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Test cleanup is best effort; failure should not hide the test assertion result.
        }
    }
}
