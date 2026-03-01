using Microsoft.Data.Sqlite;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Storage.Contracts;

namespace Tanaste.Storage;

/// <summary>
/// SQLite implementation of <see cref="ISystemActivityRepository"/>.
///
/// ORM-less: all SQL is executed via <see cref="SqliteCommand"/>.
/// All methods are async-safe and non-blocking.
/// </summary>
public sealed class SystemActivityRepository : ISystemActivityRepository
{
    private readonly IDatabaseConnection _db;

    public SystemActivityRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task LogAsync(SystemActivityEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO system_activity
                (occurred_at, action_type, hub_name, entity_id, entity_type, profile_id, changes_json, detail)
            VALUES
                (@occurred, @action, @hub, @entity, @entityType, @profile, @changes, @detail);
            """;

        cmd.Parameters.AddWithValue("@occurred",   entry.OccurredAt.ToString("O"));
        cmd.Parameters.AddWithValue("@action",     entry.ActionType);
        cmd.Parameters.AddWithValue("@hub",        (object?)entry.HubName      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@entity",     entry.EntityId.HasValue
                                                        ? entry.EntityId.Value.ToString()
                                                        : DBNull.Value);
        cmd.Parameters.AddWithValue("@entityType", (object?)entry.EntityType   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@profile",    entry.ProfileId.HasValue
                                                        ? entry.ProfileId.Value.ToString()
                                                        : DBNull.Value);
        cmd.Parameters.AddWithValue("@changes",    (object?)entry.ChangesJson  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@detail",     (object?)entry.Detail       ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SystemActivityEntry>> GetRecentAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, occurred_at, action_type, hub_name, entity_id, entity_type,
                   profile_id, changes_json, detail
            FROM   system_activity
            ORDER BY id DESC
            LIMIT  @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SystemActivityEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<SystemActivityEntry>>(results);
    }

    /// <inheritdoc/>
    public Task<int> PruneOlderThanAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O");

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM system_activity
            WHERE occurred_at < @cutoff;
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        var deleted = cmd.ExecuteNonQuery();
        return Task.FromResult(deleted);
    }

    /// <inheritdoc/>
    public Task<long> CountAsync(CancellationToken ct = default)
    {
        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM system_activity;";
        var count = (long)(cmd.ExecuteScalar() ?? 0L);
        return Task.FromResult(count);
    }

    // ── Row mapping ─────────────────────────────────────────────────────────────

    private static SystemActivityEntry MapRow(SqliteDataReader reader)
    {
        var entityIdText  = reader.IsDBNull(4) ? null : reader.GetString(4);
        var profileIdText = reader.IsDBNull(6) ? null : reader.GetString(6);

        return new SystemActivityEntry
        {
            Id          = reader.GetInt64(0),
            OccurredAt  = DateTimeOffset.TryParse(reader.GetString(1), out var ts)
                              ? ts : DateTimeOffset.UtcNow,
            ActionType  = reader.GetString(2),
            HubName     = reader.IsDBNull(3) ? null : reader.GetString(3),
            EntityId    = entityIdText is not null && Guid.TryParse(entityIdText, out var eid)
                              ? eid : null,
            EntityType  = reader.IsDBNull(5) ? null : reader.GetString(5),
            ProfileId   = profileIdText is not null && Guid.TryParse(profileIdText, out var pid)
                              ? pid : null,
            ChangesJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            Detail      = reader.IsDBNull(8) ? null : reader.GetString(8),
        };
    }
}
