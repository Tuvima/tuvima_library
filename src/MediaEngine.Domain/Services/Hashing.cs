using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Shared hashing helpers for content fingerprints and deterministic
/// identifiers. Every hash here is an identifier/fingerprint hash, not a
/// security boundary — MD5 and SHA-256 are used deliberately for speed and
/// for byte-for-byte compatibility with values already stored in the
/// database or on disk, not for attacker resistance.
/// </summary>
public static class Hashing
{
    /// <summary>Computes the lowercase hex-encoded SHA-256 digest of a UTF-8 encoded string.</summary>
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sha256Hex(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>Computes the lowercase hex-encoded SHA-256 digest of raw bytes.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data)
        => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>
    /// Computes the lowercase hex-encoded SHA-256 digest of a stream's remaining
    /// contents (e.g. a model file on disk being checksum-validated).
    /// </summary>
    public static string Sha256Hex(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Computes a stable, deterministic <see cref="Guid"/> from a UTF-8 encoded
    /// string via MD5 — <c>new Guid(MD5.HashData(utf8Bytes))</c>, the construction
    /// used by four of the five audited deterministic-ID call sites. This is an
    /// identifier hash, not a security boundary; MD5 is kept deliberately so that
    /// GUIDs already stored in the database, or compared against previously
    /// computed values, keep resolving identically.
    /// </summary>
#pragma warning disable CA5351 // MD5 used only as a stable identifier hash, not a security control.
    public static Guid DeterministicGuid(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
#pragma warning restore CA5351
}
