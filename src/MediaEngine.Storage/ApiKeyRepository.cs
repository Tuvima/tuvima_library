using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IApiKeyRepository"/>.
///
/// SECURITY rules:
/// • <c>hashed_key</c> is the SHA-256 hex of the plaintext key; plaintext is never stored.
/// • <see cref="GetAllAsync"/> deliberately omits <c>HashedKey</c> to prevent accidental
///   exposure in logs or serialised responses.
/// • The hash is never logged — only a boolean "found / not found" result is exposed.
/// </summary>
public sealed class ApiKeyRepository : IApiKeyRepository
{
    private readonly IDatabaseConnection _db;

    public ApiKeyRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task InsertAsync(ApiKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO api_keys (id, label, hashed_key, role, created_at)
            VALUES (@Id, @Label, @HashedKey, @Role, @CreatedAt);
            """,
            new
            {
                Id        = key.Id,
                key.Label,
                key.HashedKey,
                key.Role,
                CreatedAt = key.CreatedAt,
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(hashedKey);

        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<ApiKey>("""
            SELECT id         AS Id,
                   label      AS Label,
                   hashed_key AS HashedKey,
                   role       AS Role,
                   created_at AS CreatedAt
            FROM   api_keys
            WHERE  hashed_key = @hashedKey
            LIMIT  1;
            """, new { hashedKey });
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        // HashedKey is intentionally omitted from this query to prevent accidental exposure.
        var rows = conn.Query<ApiKeyListRow>("""
            SELECT id         AS Id,
                   label      AS Label,
                   role       AS Role,
                   created_at AS CreatedAt
            FROM   api_keys
            ORDER  BY created_at DESC;
            """).AsList();

        var results = rows.ConvertAll(r => new ApiKey
        {
            Id        = r.Id,
            Label     = r.Label,
            HashedKey = string.Empty,   // intentionally omitted for listing
            Role      = r.Role,
            CreatedAt = r.CreatedAt,
        });

        return Task.FromResult<IReadOnlyList<ApiKey>>(results);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Execute("DELETE FROM api_keys WHERE id = @id;", new { id });
        return Task.FromResult(rows > 0);
    }

    /// <inheritdoc/>
    public Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var deleted = conn.Execute("DELETE FROM api_keys;");
        return Task.FromResult(deleted);
    }

    // ── Private row type for GetAllAsync (no HashedKey) ───────────────────────

    private sealed class ApiKeyListRow
    {
        public Guid           Id        { get; set; }
        public string         Label     { get; set; } = string.Empty;
        public string         Role      { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
