using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IProviderConfigurationRepository"/>.
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
        var rows = conn.Query<(string Key, int IsSecret)>("""
            SELECT key       AS Key,
                   is_secret AS IsSecret
            FROM   provider_config
            WHERE  provider_id = @providerId
            ORDER  BY key;
            """, new { providerId }).AsList();

        var results = rows.ConvertAll(r => new ProviderConfiguration
        {
            ProviderId = providerId,
            Key        = r.Key,
            Value      = r.IsSecret == 1 ? Mask : string.Empty,
            IsSecret   = r.IsSecret == 1,
        });

        return Task.FromResult<IReadOnlyList<ProviderConfiguration>>(results);
    }

    /// <inheritdoc/>
    public Task<string?> GetDecryptedValueAsync(
        string providerId, string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<(string Value, int IsSecret)>("""
            SELECT value     AS Value,
                   is_secret AS IsSecret
            FROM   provider_config
            WHERE  provider_id = @providerId
              AND  key         = @key
            LIMIT  1;
            """, new { providerId, key });

        if (row == default)
            return Task.FromResult<string?>(null);

        // Decrypt if secret; return raw value otherwise.
        // SECURITY: result is never logged — callers must observe the same rule.
        var plaintext = row.IsSecret == 1
            ? _secrets.Decrypt(row.Value)
            : row.Value;

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
        conn.Execute("""
            INSERT INTO provider_config (provider_id, key, value, is_secret)
            VALUES (@providerId, @key, @value, @isSecret)
            ON CONFLICT(provider_id, key) DO UPDATE SET
                value     = excluded.value,
                is_secret = excluded.is_secret;
            """, new
        {
            providerId,
            key,
            value    = storedValue,
            isSecret = isSecret ? 1 : 0,
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string providerId, string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            DELETE FROM provider_config
            WHERE provider_id = @providerId AND key = @key;
            """, new { providerId, key });

        return Task.CompletedTask;
    }
}
