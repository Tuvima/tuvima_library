using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IProviderConfigurationRepository"/>.
///
/// Secret handling rules (enforced here, not in the caller):
/// • On write  : if <c>is_secret = 1</c>, value is encrypted via <see cref="ISecretStore"/>
///               before being stored. Plaintext NEVER reaches the database for secret entries.
/// • On read   : <see cref="GetAllMaskedAsync"/> always returns "********" for secrets.
///               <see cref="GetDecryptedValueAsync"/> decrypts and returns the plaintext.
///               Neither method logs the value.
/// </summary>
public sealed class ProviderConfigurationRepository : IProviderConfigurationRepository
{
    private const string Mask = "********";

    private readonly IDatabaseConnection _db;
    private readonly ISecretStore        _secrets;

    public ProviderConfigurationRepository(IDatabaseConnection db, ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(secrets);
        _db      = db;
        _secrets = secrets;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProviderConfiguration>> GetAllMaskedAsync(
        string providerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT key, is_secret
            FROM   provider_config
            WHERE  provider_id = @provider_id
            ORDER  BY key;
            """;
        cmd.Parameters.AddWithValue("@provider_id", providerId);

        var results = new List<ProviderConfiguration>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var isSecret = reader.GetInt32(1) == 1;
            results.Add(new ProviderConfiguration
            {
                ProviderId = providerId,
                Key        = reader.GetString(0),
                Value      = isSecret ? Mask : string.Empty, // non-secret value not needed for listing
                IsSecret   = isSecret,
            });
        }

        IReadOnlyList<ProviderConfiguration> result = results;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<string?> GetDecryptedValueAsync(
        string providerId, string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT value, is_secret
            FROM   provider_config
            WHERE  provider_id = @provider_id
              AND  key         = @key
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@provider_id", providerId);
        cmd.Parameters.AddWithValue("@key",         key);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<string?>(null);

        var storedValue = reader.GetString(0);
        var isSecret    = reader.GetInt32(1) == 1;

        // Decrypt if secret; return raw value otherwise.
        // SECURITY: result is never logged — callers must observe the same rule.
        var plaintext = isSecret
            ? _secrets.Decrypt(storedValue)
            : storedValue;

        return Task.FromResult<string?>(plaintext);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(
        string providerId,
        string key,
        string plaintextValue,
        bool isSecret,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextValue);

        // SECURITY: encrypt before the value ever touches storage.
        var storedValue = isSecret
            ? _secrets.Encrypt(plaintextValue)
            : plaintextValue;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO provider_config (provider_id, key, value, is_secret)
            VALUES (@provider_id, @key, @value, @is_secret)
            ON CONFLICT(provider_id, key) DO UPDATE SET
                value     = excluded.value,
                is_secret = excluded.is_secret;
            """;
        cmd.Parameters.AddWithValue("@provider_id", providerId);
        cmd.Parameters.AddWithValue("@key",         key);
        cmd.Parameters.AddWithValue("@value",       storedValue);
        cmd.Parameters.AddWithValue("@is_secret",   isSecret ? 1 : 0);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string providerId, string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM provider_config
            WHERE provider_id = @provider_id AND key = @key;
            """;
        cmd.Parameters.AddWithValue("@provider_id", providerId);
        cmd.Parameters.AddWithValue("@key",         key);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }
}
