using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class AudioSimilarityService : IAudioSimilarityService
{
    private readonly ILogger<AudioSimilarityService> _logger;

    public AudioSimilarityService(ILogger<AudioSimilarityService> logger)
    {
        _logger = logger;
    }

    public Task<bool> FingerprintAsync(Guid assetId, string filePath, CancellationToken ct = default)
    {
        // Full Chromaprint integration requires native library + FFmpeg audio extraction.
        // This will be implemented when the Chromaprint NuGet is added and the
        // audio_fingerprints migration (M-059) is created.
        _logger.LogDebug("AudioSimilarityService.FingerprintAsync: awaiting Chromaprint integration for asset {Id}", assetId);
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<SimilarityMatch>> FindSimilarAsync(Guid assetId, int limit = 10, CancellationToken ct = default)
    {
        _logger.LogDebug("AudioSimilarityService.FindSimilarAsync: awaiting Chromaprint integration for asset {Id}", assetId);
        return Task.FromResult<IReadOnlyList<SimilarityMatch>>([]);
    }
}
