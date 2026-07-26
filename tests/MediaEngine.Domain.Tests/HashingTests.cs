using System.Security.Cryptography;
using System.Text;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class HashingTests
{
    // NIST FIPS 180-4 published test vector for SHA-256("abc").
    private const string KnownAbcSha256Hex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void Sha256Hex_String_MatchesKnownVector()
    {
        Assert.Equal(KnownAbcSha256Hex, Hashing.Sha256Hex("abc"));
    }

    [Fact]
    public void Sha256Hex_String_MatchesRawBclComputation()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hello world")));

        Assert.Equal(expected, Hashing.Sha256Hex("hello world"));
    }

    [Fact]
    public void Sha256Hex_IsLowercase()
    {
        var hash = Hashing.Sha256Hex("Some Mixed CASE Input");

        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Sha256Hex_Bytes_MatchesStringOverload()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");

        Assert.Equal(Hashing.Sha256Hex("abc"), Hashing.Sha256Hex((ReadOnlySpan<byte>)bytes));
    }

    [Fact]
    public void Sha256Hex_Stream_MatchesStringOverload()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abc"));

        Assert.Equal(Hashing.Sha256Hex("abc"), Hashing.Sha256Hex(stream));
    }

    [Fact]
    public void DeterministicGuid_IsStable_ForSameInput()
    {
        var first = Hashing.DeterministicGuid("Dune Novels");
        var second = Hashing.DeterministicGuid("Dune Novels");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DeterministicGuid_Differs_ForDifferentInput()
    {
        var first = Hashing.DeterministicGuid("Dune Novels");
        var second = Hashing.DeterministicGuid("Dune Films");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeterministicGuid_NeverReturnsEmptyGuid_ForNonEmptyInput()
    {
        Assert.NotEqual(Guid.Empty, Hashing.DeterministicGuid("anything"));
    }
}
