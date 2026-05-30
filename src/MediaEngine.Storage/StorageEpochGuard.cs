using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

internal static class StorageEpochGuard
{
    public const string CurrentEpoch = "guid-blob-v1";
    public const string ResetEnvironmentVariable = "TUVIMA_STORAGE_RESET";

    public static void EnsureCompatibleOrReset(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            return;

        var state = Inspect(fullPath);
        if (state is StorageEpochState.Empty or StorageEpochState.Current)
            return;

        if (!IsDestructiveResetRequested())
        {
            throw new InvalidOperationException(
                $"The SQLite database at '{fullPath}' uses a legacy storage epoch. " +
                $"This build requires '{CurrentEpoch}'. Set {ResetEnvironmentVariable}=1 to rename the old database " +
                "and start a clean database for reingestion.");
        }

        RenameLegacyDatabase(fullPath);
    }

    private static StorageEpochState Inspect(string fullPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={fullPath};Mode=ReadOnly;Pooling=False");
            conn.Open();

            if (!HasAnyTable(conn))
                return StorageEpochState.Empty;

            var epoch = TryGetEpoch(conn);
            if (string.Equals(epoch, CurrentEpoch, StringComparison.Ordinal))
                return StorageEpochState.Current;

            var providerIdType = TryGetColumnType(conn, "metadata_providers", "id");
            if (string.Equals(providerIdType, "BLOB", StringComparison.OrdinalIgnoreCase))
                return StorageEpochState.Current;

            return StorageEpochState.Legacy;
        }
        catch (SqliteException)
        {
            return StorageEpochState.Legacy;
        }
    }

    private static bool HasAnyTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string? TryGetEpoch(SqliteConnection conn)
    {
        if (!TableExists(conn, "storage_metadata"))
            return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM storage_metadata WHERE key = 'storage_epoch' LIMIT 1;";
        return Convert.ToString(cmd.ExecuteScalar());
    }

    private static bool TableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string? TryGetColumnType(SqliteConnection conn, string tableName, string columnName)
    {
        if (!TableExists(conn, tableName))
            return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{tableName}]);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return reader.GetString(2);
        }

        return null;
    }

    private static bool IsDestructiveResetRequested()
    {
        var value = Environment.GetEnvironmentVariable(ResetEnvironmentVariable);
        return value is not null
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("destructive-reingest", StringComparison.OrdinalIgnoreCase));
    }

    private static void RenameLegacyDatabase(string fullPath)
    {
        SqliteConnection.ClearAllPools();

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        MoveIfExists(fullPath, $"{fullPath}.legacy-text-guid.{stamp}.bak");
        MoveIfExists($"{fullPath}-wal", $"{fullPath}-wal.legacy-text-guid.{stamp}.bak");
        MoveIfExists($"{fullPath}-shm", $"{fullPath}-shm.legacy-text-guid.{stamp}.bak");
    }

    private static void MoveIfExists(string source, string destination)
    {
        if (!File.Exists(source))
            return;

        File.Move(source, destination, overwrite: false);
    }

    private enum StorageEpochState
    {
        Empty,
        Current,
        Legacy,
    }
}
