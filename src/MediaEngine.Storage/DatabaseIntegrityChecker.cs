using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

internal sealed class DatabaseIntegrityChecker
{
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>PRAGMA integrity_check</c> returns anything other than "ok".
    /// </exception>
    public void RunStartupChecks(SqliteConnection conn, string databasePath)
    {
        using var integrityCmd = conn.CreateCommand();
        integrityCmd.CommandText = "PRAGMA integrity_check;";
        var result = integrityCmd.ExecuteScalar()?.ToString();

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite integrity_check failed for '{databasePath}': {result}");
        }

        using var optimizeCmd = conn.CreateCommand();
        optimizeCmd.CommandText = "PRAGMA optimize;";
        optimizeCmd.ExecuteNonQuery();
    }
}
