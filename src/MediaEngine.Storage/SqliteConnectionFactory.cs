using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

internal sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    public SqliteConnection OpenSharedConnection()
    {
        var conn = new SqliteConnection($"Data Source={_databasePath}");
        conn.Open();
        ApplySharedPragmas(conn);
        return conn;
    }

    public SqliteConnection CreateOperationConnection()
    {
        var conn = new SqliteConnection($"Data Source={_databasePath}");
        conn.Open();
        ApplyOperationPragmas(conn);
        return conn;
    }

    private static void ApplySharedPragmas(SqliteConnection conn)
    {
        using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText =
            "PRAGMA journal_mode = WAL; " +
            "PRAGMA synchronous = NORMAL; " +
            "PRAGMA foreign_keys = ON; " +
            "PRAGMA temp_store = MEMORY; " +
            "PRAGMA busy_timeout = 5000; " +
            "PRAGMA cache_size = -16384; " +
            "PRAGMA mmap_size = 268435456;";
        pragmaCmd.ExecuteNonQuery();
    }

    private static void ApplyOperationPragmas(SqliteConnection conn)
    {
        using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText =
            "PRAGMA journal_mode = WAL; " +
            "PRAGMA synchronous = NORMAL; " +
            "PRAGMA foreign_keys = ON; " +
            "PRAGMA temp_store = MEMORY; " +
            "PRAGMA busy_timeout = 5000; " +
            "PRAGMA cache_size = -16384; " +
            "PRAGMA mmap_size = 268435456;";
        pragmaCmd.ExecuteNonQuery();
    }
}
