using MediaEngine.Domain.Contracts;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Processors.Extractors;

/// <summary>
/// Extracts video metadata by delegating to <see cref="IFFmpegService.ProbeAsync"/>.
/// Falls back gracefully when FFmpeg is not installed — returns an empty
/// <see cref="VideoMetadata"/> (same behaviour as the former stub).
/// </summary>
public sealed class FFmpegVideoMetadataExtractor : IVideoMetadataExtractor
{
    private readonly IFFmpegService _ffmpeg;
    private readonly ILogger<FFmpegVideoMetadataExtractor> _logger;

    public FFmpegVideoMetadataExtractor(IFFmpegService ffmpeg, ILogger<FFmpegVideoMetadataExtractor> logger)
    {
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    public async Task<VideoMetadata?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        if (!_ffmpeg.IsAvailable)
        {
            _logger.LogDebug("FFmpeg not available — returning empty VideoMetadata for {Path}", filePath);
            return new VideoMetadata();
        }

        var probe = await _ffmpeg.ProbeAsync(filePath, ct);
        if (probe is null)
        {
            _logger.LogWarning("FFprobe returned null for {Path} — file may be corrupt", filePath);
            return null;
        }

        _logger.LogDebug(
            "FFprobe extracted: {Width}x{Height}, {Duration}, codec={Codec}, fps={Fps} for {Path}",
            probe.Width, probe.Height, probe.Duration, probe.VideoCodec, probe.FrameRate, filePath);

        return new VideoMetadata
        {
            WidthPx           = probe.Width,
            HeightPx          = probe.Height,
            Duration          = probe.Duration,
            VideoCodec        = probe.VideoCodec,
            AudioLanguage     = probe.AudioLanguage,
            AudioCodec        = probe.AudioCodec,
            AudioChannels     = probe.Channels,
            FrameRate         = probe.FrameRate,
            SubtitleLanguages = probe.SubtitleLanguages,
        };
    }
}
