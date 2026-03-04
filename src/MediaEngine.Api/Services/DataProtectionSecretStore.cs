using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// <see cref="ISecretStore"/> implementation backed by ASP.NET Core Data Protection.
/// Encryption keys are stored in the host machine's default key ring
/// (<c>%APPDATA%\Microsoft\DataProtection\KeyRing</c> on Windows,
/// <c>~/.aspnet/DataProtection-Keys/</c> on Linux) which ties the ciphertext
/// to the machine — it cannot be decrypted on a different host.
///
/// SECURITY:
/// • Plaintext and ciphertext values are NEVER logged.
/// • <see cref="Decrypt"/> returns <see cref="string.Empty"/> on failure rather
///   than throwing — callers must treat an empty result as a misconfigured secret.
/// </summary>
public sealed class DataProtectionSecretStore : ISecretStore
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretStore(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        // Purpose string scopes this protector to provider secrets only.
        // A payload encrypted for this purpose cannot be decrypted by
        // any other IDataProtector instance with a different purpose.
        _protector = provider.CreateProtector("MediaEngine.ProviderSecrets.v1");
    }

    /// <inheritdoc/>
    public string Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc/>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Key has rotated, data was imported from another machine, or
            // the ciphertext is corrupt. Return empty — the caller logs a
            // warning WITHOUT including the ciphertext value.
            return string.Empty;
        }
    }
}
