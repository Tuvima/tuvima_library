using System.Text.Json;
using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IUserStateStore"/>.
///
/// Tracks user progress (reading position, playback timestamp, completion %)
/// for each media asset.  Extended properties are serialised as a JSON blob.
///
/// Thread safety: same serialised-connection model as <see cref="PersonRepository"/>.
/// Spec: Phase 2 – IUserStateStore.
/// </summary>
public sealed class UserStateRepository : IUserStateStore
{
    private readonly IDatabaseConnection _db;

    public UserStateRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<UserState?> GetAsync(Guid userId, Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, asset_id, content_hash, progress_pct,
                   last_accessed, extended_properties
            FROM   user_states
            WHERE  user_id  = @userId
              AND  asset_id = @assetId;
            """;
        cmd.Parameters.AddWithValue("@userId",  userId.ToString());
        cmd.Parameters.AddWithValue("@assetId", assetId.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task SaveAsync(UserState state, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO user_states
                (user_id, asset_id, content_hash, progress_pct,
                 last_accessed, extended_properties)
            VALUES
                (@userId, @assetId, @contentHash, @progressPct,
                 @lastAccessed, @extendedProperties);
            """;
        cmd.Parameters.AddWithValue("@userId",      state.UserId.ToString());
        cmd.Parameters.AddWithValue("@assetId",     state.AssetId.ToString());
        cmd.Parameters.AddWithValue("@contentHash", (object?)state.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@progressPct", state.ProgressPct);
        cmd.Parameters.AddWithValue("@lastAccessed", state.LastAccessed.ToString("O"));
        cmd.Parameters.AddWithValue("@extendedProperties",
            state.ExtendedProperties.Count > 0
                ? JsonSerializer.Serialize(state.ExtendedProperties)
                : DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserState>> FindByContentHashAsync(
        string contentHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, asset_id, content_hash, progress_pct,
                   last_accessed, extended_properties
            FROM   user_states
            WHERE  content_hash = @hash;
            """;
        cmd.Parameters.AddWithValue("@hash", contentHash);

        using var reader = cmd.ExecuteReader();
        var results = new List<UserState>();
        while (reader.Read())
            results.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<UserState>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserState>> GetRecentAsync(
        Guid userId, int limit = 50, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, asset_id, content_hash, progress_pct,
                   last_accessed, extended_properties
            FROM   user_states
            WHERE  user_id = @userId
            ORDER BY last_accessed DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@limit",  limit);

        using var reader = cmd.ExecuteReader();
        var results = new List<UserState>();
        while (reader.Read())
            results.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<UserState>>(results);
    }

    // ── Mapping ─────────────────────────────────────────────────────────────

    private static UserState MapRow(SqliteDataReader reader)
    {
        var extJson = reader.IsDBNull(5) ? null : reader.GetString(5);
        var extProps = string.IsNullOrEmpty(extJson)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(extJson) ?? [];

        return new UserState
        {
            UserId       = Guid.Parse(reader.GetString(0)),
            AssetId      = Guid.Parse(reader.GetString(1)),
            ContentHash  = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            ProgressPct  = reader.GetDouble(3),
            LastAccessed = DateTimeOffset.Parse(reader.GetString(4)),
            ExtendedProperties = extProps,
        };
    }
}
