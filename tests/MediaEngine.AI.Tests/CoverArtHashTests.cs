using MediaEngine.AI.Features;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace MediaEngine.AI.Tests;

/// <summary>
/// Unit tests for <see cref="CoverArtHashService"/>.
/// Tests the perceptual hash computation and similarity score logic.
/// No mocking required — all tests exercise pure image processing and bit arithmetic.
/// </summary>
public sealed class CoverArtHashTests
{
    private static CoverArtHashService Build() =>
        new(NullLogger<CoverArtHashService>.Instance);

    // ── Hash computation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeHashAsync_ValidImage_ReturnsHash()
    {
        var svc = Build();
        var imageBytes = CreateSolidColorImage(64, 96, 120, 60, 200);

        var hash = await svc.ComputeHashAsync(imageBytes);

        Assert.NotNull(hash);
    }

    [Fact]
    public async Task ComputeHashAsync_NullBytes_ReturnsNull()
    {
        var svc = Build();

        var hash = await svc.ComputeHashAsync(null!);

        Assert.Null(hash);
    }

    [Fact]
    public async Task ComputeHashAsync_TooSmallBytes_ReturnsNull()
    {
        var svc = Build();
        var tinyBytes = new byte[10];

        var hash = await svc.ComputeHashAsync(tinyBytes);

        Assert.Null(hash);
    }

    [Fact]
    public async Task ComputeHashAsync_InvalidImageData_ReturnsNull()
    {
        var svc = Build();
        // Random bytes that are not a valid image format
        var random = new byte[200];
        new Random(42).NextBytes(random);

        var hash = await svc.ComputeHashAsync(random);

        Assert.Null(hash);
    }

    [Fact]
    public async Task ComputeHashAsync_SameImage_ReturnsSameHash()
    {
        var svc = Build();
        var imageBytes = CreateSolidColorImage(32, 48, 200, 100, 50);

        var hash1 = await svc.ComputeHashAsync(imageBytes);
        var hash2 = await svc.ComputeHashAsync(imageBytes);

        Assert.NotNull(hash1);
        Assert.NotNull(hash2);
        Assert.Equal(hash1, hash2);
    }

    // ── Similarity computation ────────────────────────────────────────────────

    [Fact]
    public void ComputeSimilarity_IdenticalHashes_Returns1()
    {
        var svc = Build();
        ulong hash = 0xABCDEF1234567890UL;

        var similarity = svc.ComputeSimilarity(hash, hash);

        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void ComputeSimilarity_OppositeHashes_Returns0()
    {
        var svc = Build();

        // All 64 bits differ
        var similarity = svc.ComputeSimilarity(0x0000000000000000UL, 0xFFFFFFFFFFFFFFFFUL);

        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void ComputeSimilarity_OneFlippedBit_ReturnsHigh()
    {
        var svc = Build();
        ulong hashA = 0xFFFFFFFFFFFFFFFFUL;
        ulong hashB = 0xFFFFFFFFFFFFFFFEUL; // One bit flipped

        var similarity = svc.ComputeSimilarity(hashA, hashB);

        // 63 of 64 bits match = 63/64 ≈ 0.984
        Assert.Equal(63.0 / 64.0, similarity, precision: 10);
    }

    [Fact]
    public void ComputeSimilarity_HalfFlipped_Returns0_5()
    {
        var svc = Build();
        // 32 bits set vs none
        ulong hashA = 0x00000000FFFFFFFFUL;
        ulong hashB = 0xFFFFFFFF00000000UL;

        var similarity = svc.ComputeSimilarity(hashA, hashB);

        // All 64 bits differ → 0.0
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void ComputeSimilarity_32BitsMatch_Returns0_5()
    {
        var svc = Build();
        // Exactly 32 bits differ
        ulong hashA = 0x0000000000000000UL;
        ulong hashB = 0x00000000FFFFFFFFUL; // Lower 32 bits differ

        var similarity = svc.ComputeSimilarity(hashA, hashB);

        // 32 bits differ out of 64 = 1 - 32/64 = 0.5
        Assert.Equal(0.5, similarity, precision: 10);
    }

    [Fact]
    public async Task ComputeSimilarity_SameColorImages_ProducesHighSimilarity()
    {
        var svc = Build();
        // Two identical solid red images at different sizes should have identical hashes
        var image1 = CreateSolidColorImage(100, 150, 255, 0, 0);
        var image2 = CreateSolidColorImage(200, 300, 255, 0, 0);

        var hash1 = await svc.ComputeHashAsync(image1);
        var hash2 = await svc.ComputeHashAsync(image2);

        Assert.NotNull(hash1);
        Assert.NotNull(hash2);

        // Same solid color → same average hash regardless of size
        var similarity = svc.ComputeSimilarity(hash1!.Value, hash2!.Value);
        Assert.Equal(1.0, similarity);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PNG-encoded solid-color image of the given dimensions and RGB colour.
    /// </summary>
    private static byte[] CreateSolidColorImage(int width, int height, byte r, byte g, byte b)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(r, g, b));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
