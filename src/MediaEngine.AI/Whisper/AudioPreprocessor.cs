using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Whisper;

/// <summary>
/// Converts audio files to 16kHz mono WAV format required by Whisper.
/// Uses FFmpeg via IFFmpegService.
/// </summary>
public sealed class AudioPreprocessor
{
    private readonly IFFmpegService _ffmpeg;
    private readonly ILogger<AudioPreprocessor> _logger;

    public AudioPreprocessor(IFFmpegService ffmpeg, ILogger<AudioPreprocessor> logger)
    {
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    /// <summary>
    /// Convert an audio file to 16kHz mono WAV for Whisper processing.
    /// Returns the path to the temporary WAV file.
    /// </summary>
    public async Task<string?> ConvertToWavAsync(string inputPath, CancellationToken ct = default)
    {
        if (!_ffmpeg.IsAvailable)
        {
            _logger.LogWarning("FFmpeg not available — cannot preprocess audio for Whisper");
            return null;
        }

        var tempWav = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid():N}.wav");

        var args = $"-i \"{inputPath}\" -ar 16000 -ac 1 -f wav \"{tempWav}\" -y";
        var (exitCode, _, error) = await _ffmpeg.RunAsync(args, ct);

        if (exitCode != 0)
        {
            _logger.LogError("FFmpeg audio conversion failed: {Error}", error);
            return null;
        }

        _logger.LogDebug("Audio preprocessed to WAV: {Output}", tempWav);
        return tempWav;
    }

    /// <summary>
    /// Extract only the first N seconds of audio for language detection.
    /// </summary>
    public async Task<string?> ExtractFirstSecondsAsync(string inputPath, int seconds = 30, CancellationToken ct = default)
    {
        if (!_ffmpeg.IsAvailable)
            return null;

        var tempWav = Path.Combine(Path.GetTempPath(), $"whisper_lang_{Guid.NewGuid():N}.wav");

        var args = $"-i \"{inputPath}\" -t {seconds} -ar 16000 -ac 1 -f wav \"{tempWav}\" -y";
        var (exitCode, _, error) = await _ffmpeg.RunAsync(args, ct);

        if (exitCode != 0)
        {
            _logger.LogError("FFmpeg language extraction failed: {Error}", error);
            return null;
        }

        return tempWav;
    }
}
