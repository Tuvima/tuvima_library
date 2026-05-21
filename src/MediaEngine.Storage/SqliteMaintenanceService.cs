using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

internal sealed class SqliteMaintenanceService
{
    public void Vacuum(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }
}
