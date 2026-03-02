using Microsoft.Data.Sqlite;
using Tanaste.Storage.Contracts;

namespace Tanaste.Storage;

/// <summary>
/// SQLite cache for UI settings. Stores the JSON blob for each scope
/// (global, device:{class}, profile:{uuid}) so API reads do not require
/// filesystem I/O on every request.
///
/// <para>
/// ORM-less: all SQL is executed via <see cref="SqliteCommand"/>.
/// The cache is rebuilt from config files on Engine startup and updated
/// on every save operation.
/// </para>
/// </summary>
public sealed class UISettingsCacheRepository
{
    private readonly IDatabaseConnection _db;

    public UISettingsCacheRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <summary>
    /// Reads a cached settings JSON blob by scope key.
    /// Returns <c>null</c> if no cache entry exists for the given scope.
    /// </summary>
    /// <param name="scope">
    /// Scope key: <c>"global"</c>, <c>"device:web"</c>, <c>"profile:{uuid}"</c>.
    /// </param>
    public string? Get(string scope)
    {
        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT settings FROM ui_settings_cache WHERE scope = @scope;";
        cmd.Parameters.AddWithValue("@scope", scope);

        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Inserts or updates a cache entry.
    /// </summary>
    public void Upsert(string scope, string settingsJson)
    {
        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ui_settings_cache (scope, settings, cached_at)
            VALUES (@scope, @settings, strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            ON CONFLICT(scope)
            DO UPDATE SET settings = excluded.settings, cached_at = excluded.cached_at;
            """;

        cmd.Parameters.AddWithValue("@scope",    scope);
        cmd.Parameters.AddWithValue("@settings", settingsJson);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes a cache entry by scope.
    /// </summary>
    public void Delete(string scope)
    {
        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ui_settings_cache WHERE scope = @scope;";
        cmd.Parameters.AddWithValue("@scope", scope);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes all cache entries (used before a full rebuild).
    /// </summary>
    public void Clear()
    {
        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ui_settings_cache;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Rebuilds the cache from configuration files.
    /// Called on Engine startup to ensure the cache reflects the current file state.
    /// </summary>
    public void RebuildFromFiles(IConfigurationLoader configLoader)
    {
        ArgumentNullException.ThrowIfNull(configLoader);

        var conn = _db.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            // Clear existing cache
            using (var clearCmd = conn.CreateCommand())
            {
                clearCmd.Transaction = transaction;
                clearCmd.CommandText = "DELETE FROM ui_settings_cache;";
                clearCmd.ExecuteNonQuery();
            }

            // Cache global settings
            var global = configLoader.LoadConfig<Models.UIGlobalSettings>("ui", "global");
            if (global is not null)
                UpsertInTransaction(conn, transaction, "global",
                    System.Text.Json.JsonSerializer.Serialize(global));

            // Cache device profiles
            string[] deviceClasses = ["web", "mobile", "television", "automotive"];
            foreach (var dc in deviceClasses)
            {
                var device = configLoader.LoadConfig<Models.UIDeviceProfile>("ui/devices", dc);
                if (device is not null)
                    UpsertInTransaction(conn, transaction, $"device:{dc}",
                        System.Text.Json.JsonSerializer.Serialize(device));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void UpsertInTransaction(
        SqliteConnection conn, SqliteTransaction transaction, string scope, string settingsJson)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO ui_settings_cache (scope, settings, cached_at)
            VALUES (@scope, @settings, strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            ON CONFLICT(scope)
            DO UPDATE SET settings = excluded.settings, cached_at = excluded.cached_at;
            """;

        cmd.Parameters.AddWithValue("@scope",    scope);
        cmd.Parameters.AddWithValue("@settings", settingsJson);
        cmd.ExecuteNonQuery();
    }
}
