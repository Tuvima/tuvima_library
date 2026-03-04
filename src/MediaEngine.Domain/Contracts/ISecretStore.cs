namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Encrypts and decrypts sensitive configuration values (provider API keys, tokens).
/// Implementations MUST tie encryption to the host machine so that ciphertext
/// cannot be transferred to another host and decrypted.
///
/// SECURITY: Implementations MUST NOT log plaintext or ciphertext values.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns a base-64 ciphertext
    /// safe for storage in SQLite.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a ciphertext produced by <see cref="Encrypt"/>.
    /// Returns <see cref="string.Empty"/> if decryption fails (e.g. key rotation,
    /// cross-machine import) rather than throwing.
    /// </summary>
    string Decrypt(string ciphertext);
}
