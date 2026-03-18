using System.Text.Json;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IUserStateStore"/>.
///
/// Tracks user progress (reading position, playback timestamp, completion %)
/// for each media asset.  Extended properties are serialised as a JSON blob.
///
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
        var row = conn.QueryFirstOrDefault<UserStateRow>("""
            SELECT user_id AS UserId, asset_id AS AssetId, content_hash AS ContentHash,
                   progress_pct AS ProgressPct, last_accessed AS LastAccessed,
                   extended_properties AS ExtendedProperties
            FROM   user_states
            WHERE  user_id  = @userId
              AND  asset_id = @assetId
            """, new { userId = userId.ToString(), assetId = assetId.ToString() });

        return Task.FromResult(row is null ? null : (UserState?)MapRow(row));
    }

    /// <inheritdoc/>
    public Task SaveAsync(UserState state, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT OR REPLACE INTO user_states
                (user_id, asset_id, content_hash, progress_pct,
                 last_accessed, extended_properties)
            VALUES
                (@userId, @assetId, @contentHash, @progressPct,
                 @lastAccessed, @extendedProperties)
            """, new
        {
            userId             = state.UserId.ToString(),
            assetId            = state.AssetId.ToString(),
            contentHash        = string.IsNullOrEmpty(state.ContentHash) ? null : state.ContentHash,
            progressPct        = state.ProgressPct,
            lastAccessed       = state.LastAccessed.ToString("O"),
            extendedProperties = state.ExtendedProperties.Count > 0
                ? JsonSerializer.Serialize(state.ExtendedProperties)
                : null,
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserState>> FindByContentHashAsync(
        string contentHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using var conn = _db.CreateConnection();
        var rows = conn.Query<UserStateRow>("""
            SELECT user_id AS UserId, asset_id AS AssetId, content_hash AS ContentHash,
                   progress_pct AS ProgressPct, last_accessed AS LastAccessed,
                   extended_properties AS ExtendedProperties
            FROM   user_states
            WHERE  content_hash = @contentHash
            """, new { contentHash }).AsList();

        return Task.FromResult<IReadOnlyList<UserState>>(rows.Select(MapRow).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserState>> GetRecentAsync(
        Guid userId, int limit = 50, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<UserStateRow>("""
            SELECT user_id AS UserId, asset_id AS AssetId, content_hash AS ContentHash,
                   progress_pct AS ProgressPct, last_accessed AS LastAccessed,
                   extended_properties AS ExtendedProperties
            FROM   user_states
            WHERE  user_id = @userId
            ORDER BY last_accessed DESC
            LIMIT @limit
            """, new { userId = userId.ToString(), limit }).AsList();

        return Task.FromResult<IReadOnlyList<UserState>>(rows.Select(MapRow).ToList());
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private sealed class UserStateRow
    {
        public string UserId             { get; set; } = "";
        public string AssetId            { get; set; } = "";
        public string? ContentHash       { get; set; }
        public double ProgressPct        { get; set; }
        public string LastAccessed       { get; set; } = "";
        public string? ExtendedProperties { get; set; }
    }

    private static UserState MapRow(UserStateRow r) => new()
    {
        UserId       = Guid.Parse(r.UserId),
        AssetId      = Guid.Parse(r.AssetId),
        ContentHash  = r.ContentHash ?? string.Empty,
        ProgressPct  = r.ProgressPct,
        LastAccessed = DateTimeOffset.Parse(r.LastAccessed),
        ExtendedProperties = string.IsNullOrEmpty(r.ExtendedProperties)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(r.ExtendedProperties) ?? [],
    };
}
