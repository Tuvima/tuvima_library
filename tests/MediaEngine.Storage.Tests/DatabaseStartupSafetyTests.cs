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
            Assert.True(TableExists(conn, table), $"Required table '{table}' was not created.");

        string[] requiredIndexes =
        [
            "idx_review_queue_status",
            "idx_review_queue_entity_id",
            "idx_media_assets_content_hash",
            "idx_ingestion_batches_status",
        ];

        foreach (var index in requiredIndexes)
            Assert.True(IndexExists(conn, index), $"Required index '{index}' was not created.");

        Assert.Equal("wal", Scalar(conn, "PRAGMA journal_mode;").ToLowerInvariant());
        Assert.Equal("1", Scalar(conn, "PRAGMA foreign_keys;"));
        Assert.Equal("5000", Scalar(conn, "PRAGMA busy_timeout;"));
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
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
            TryDelete(Path);
            TryDelete($"{Path}-wal");
            TryDelete($"{Path}-shm");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Test cleanup is best effort; failure should not hide the test assertion result.
            }
        }
    }
}
