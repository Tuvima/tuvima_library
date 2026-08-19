using Dapper;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Services;

internal static class SqliteBackupTarget
{
    public static SqliteConnection Create(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    public static void Verify(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Staged database is missing.", path);
        }

        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        var result = connection.ExecuteScalar<string>("PRAGMA integrity_check;");
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Backup database failed integrity_check: {result ?? "no result"}.");
        }
    }
}
