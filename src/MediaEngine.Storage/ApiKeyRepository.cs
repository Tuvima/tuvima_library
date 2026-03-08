using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IApiKeyRepository"/>.
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO api_keys (id, label, hashed_key, role, created_at)
            VALUES (@id, @label, @hashed_key, @role, @created_at);
            """;
        cmd.Parameters.AddWithValue("@id",         key.Id.ToString());
        cmd.Parameters.AddWithValue("@label",      key.Label);
        cmd.Parameters.AddWithValue("@hashed_key", key.HashedKey);
        cmd.Parameters.AddWithValue("@role",       key.Role);
        cmd.Parameters.AddWithValue("@created_at", key.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(hashedKey);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, label, hashed_key, role, created_at
            FROM   api_keys
            WHERE  hashed_key = @hashed_key
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@hashed_key", hashedKey);

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ApiKey>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, label, role, created_at
            FROM   api_keys
            ORDER  BY created_at DESC;
            """;

        var results = new List<ApiKey>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ApiKey
            {
                Id         = Guid.Parse(reader.GetString(0)),
                Label      = reader.GetString(1),
                HashedKey  = string.Empty,  // intentionally omitted for listing
                Role       = reader.GetString(2),
                CreatedAt  = DateTimeOffset.Parse(reader.GetString(3)),
            });
        }

        IReadOnlyList<ApiKey> result = results;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM api_keys WHERE id = @id;";
        deleteCmd.Parameters.AddWithValue("@id", id.ToString());
        deleteCmd.ExecuteNonQuery();

        using var changesCmd = conn.CreateCommand();
        changesCmd.CommandText = "SELECT changes();";
        var rows = Convert.ToInt64(changesCmd.ExecuteScalar()!);

        return Task.FromResult(rows > 0);
    }

    /// <inheritdoc/>
    public Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM api_keys;";
        var deleted = cmd.ExecuteNonQuery();

        return Task.FromResult(deleted);
    }

    private static ApiKey MapRow(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id        = Guid.Parse(r.GetString(0)),
        Label     = r.GetString(1),
        HashedKey = r.GetString(2),
        Role      = r.GetString(3),
        CreatedAt = DateTimeOffset.Parse(r.GetString(4)),
    };
}
