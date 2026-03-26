using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

/// <summary>
/// Audio similarity analysis using FFmpeg's built-in Chromaprint support.
/// Generates acoustic fingerprints and computes Hamming distance for similarity.
/// </summary>
public sealed class AudioSimilarityService : IAudioSimilarityService
{
    private readonly IAudioFingerprintRepository _repo;
    private readonly IFFmpegService _ffmpeg;
    private readonly ILogger<AudioSimilarityService> _logger;

    public AudioSimilarityService(
        IAudioFingerprintRepository repo,
        IFFmpegService ffmpeg,
        ILogger<AudioSimilarityService> logger)
    {
        _repo = repo;
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    public async Task<bool> FingerprintAsync(Guid assetId, string filePath, CancellationToken ct = default)
    {
        if (!_ffmpeg.IsAvailable)
        {
            _logger.LogWarning("FFmpeg not available — cannot generate audio fingerprint");
            return false;
        }

        if (await _repo.ExistsAsync(assetId, ct))
        {
            _logger.LogDebug("Fingerprint already exists for asset {Id}", assetId);
            return true;
        }

        try
        {
            // Use FFmpeg to generate a raw Chromaprint fingerprint.
            // -t 120 limits analysis to the first 2 minutes (sufficient for matching).
            // -f chromaprint -fp_format raw writes raw binary fingerprint data.
            var tempFile = Path.Combine(Path.GetTempPath(), $"chromaprint_{Guid.NewGuid():N}.raw");

            var args = $"-i \"{filePath}\" -t 120 -f chromaprint -fp_format raw \"{tempFile}\" -y";
            var (exitCode, _, error) = await _ffmpeg.RunAsync(args, ct);

            if (exitCode != 0)
            {
                _logger.LogWarning("FFmpeg chromaprint failed for {Path}: {Error}", filePath, error);
                CleanupTemp(tempFile);
                return false;
            }

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
            {
                _logger.LogWarning("FFmpeg chromaprint produced no output for {Path}", filePath);
                CleanupTemp(tempFile);
                return false;
            }

            var fingerprint = await File.ReadAllBytesAsync(tempFile, ct);
            CleanupTemp(tempFile);

            // Get duration for normalization.
            var probe = await _ffmpeg.ProbeAsync(filePath, ct);
            var duration = probe?.Duration.TotalSeconds ?? 0;

            await _repo.UpsertAsync(assetId, fingerprint, duration, ct);

            _logger.LogInformation(
                "Audio fingerprint generated for asset {Id}: {Bytes} bytes, {Duration:F1}s",
                assetId, fingerprint.Length, duration);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate audio fingerprint for asset {Id}", assetId);
            return false;
        }
    }

    public async Task<IReadOnlyList<SimilarityMatch>> FindSimilarAsync(
        Guid assetId, int limit = 10, CancellationToken ct = default)
    {
        var target = await _repo.GetAsync(assetId, ct);
        if (target is null || target.Value.Fingerprint is null)
        {
            _logger.LogDebug("No fingerprint found for asset {Id}", assetId);
            return [];
        }

        var targetFp = target.Value.Fingerprint;
        var allFingerprints = await _repo.GetAllAsync(ct);

        var matches = new List<SimilarityMatch>();

        foreach (var (otherId, otherFp) in allFingerprints)
        {
            if (otherId == assetId) continue;

            var similarity = ComputeHammingSimilarity(targetFp, otherFp);
            if (similarity > 0.3) // Minimum similarity threshold
            {
                matches.Add(new SimilarityMatch
                {
                    AssetId = otherId,
                    Score = similarity,
                    MatchType = "acoustic",
                });
            }
        }

        return matches
            .OrderByDescending(m => m.Score)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Compute similarity between two fingerprints using normalized Hamming distance.
    /// Returns 0.0 (completely different) to 1.0 (identical).
    /// Compares over the shorter fingerprint's length to handle different durations.
    /// </summary>
    private static double ComputeHammingSimilarity(byte[] a, byte[] b)
    {
        int minLen = Math.Min(a.Length, b.Length);
        if (minLen == 0) return 0.0;

        int totalBits = minLen * 8;
        int differingBits = 0;

        for (int i = 0; i < minLen; i++)
        {
            // Count differing bits using XOR + popcount
            differingBits += PopCount((byte)(a[i] ^ b[i]));
        }

        return 1.0 - (double)differingBits / totalBits;
    }

    private static int PopCount(byte value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }

    private static void CleanupTemp(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
