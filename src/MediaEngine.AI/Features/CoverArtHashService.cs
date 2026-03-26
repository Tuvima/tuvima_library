using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MediaEngine.AI.Features;

/// <summary>
/// Perceptual hash (average hash) for cover art comparison.
/// Uses SkiaSharp to resize, grayscale, and compute a 64-bit fingerprint.
/// Two images of the same cover (even at different resolutions) produce similar hashes.
/// </summary>
public sealed class CoverArtHashService : ICoverArtHashService
{
    private const int HashSize = 8; // 8×8 = 64 bits
    private readonly ILogger<CoverArtHashService> _logger;

    public CoverArtHashService(ILogger<CoverArtHashService> logger)
    {
        _logger = logger;
    }

    public Task<ulong?> ComputeHashAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length < 100) // Too small to be a real image
            return Task.FromResult<ulong?>(null);

        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap is null)
            {
                _logger.LogDebug("Failed to decode image ({Bytes} bytes)", imageBytes.Length);
                return Task.FromResult<ulong?>(null);
            }

            // Resize to 8×8
            using var resized = bitmap.Resize(new SKImageInfo(HashSize, HashSize, SKColorType.Gray8), SKFilterQuality.Medium);
            if (resized is null)
            {
                _logger.LogDebug("Failed to resize image for hashing");
                return Task.FromResult<ulong?>(null);
            }

            // Extract grayscale pixel values
            var pixels = new byte[HashSize * HashSize];
            for (int y = 0; y < HashSize; y++)
            {
                for (int x = 0; x < HashSize; x++)
                {
                    var color = resized.GetPixel(x, y);
                    // Gray8 format: R=G=B, so just use Red channel
                    pixels[y * HashSize + x] = color.Red;
                }
            }

            // Compute mean
            double mean = pixels.Average(p => (double)p);

            // Generate hash: bit=1 if pixel >= mean
            ulong hash = 0;
            for (int i = 0; i < 64; i++)
            {
                if (pixels[i] >= mean)
                    hash |= (1UL << i);
            }

            return Task.FromResult<ulong?>(hash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute perceptual hash for image ({Bytes} bytes)", imageBytes.Length);
            return Task.FromResult<ulong?>(null);
        }
    }

    public double ComputeSimilarity(ulong hashA, ulong hashB)
    {
        // XOR gives bits that differ
        ulong xor = hashA ^ hashB;

        // Count differing bits (Hamming distance)
        int distance = System.Numerics.BitOperations.PopCount(xor);

        // Normalize: 0 different bits = 1.0 similarity, 64 different = 0.0
        return 1.0 - (double)distance / 64.0;
    }
}
