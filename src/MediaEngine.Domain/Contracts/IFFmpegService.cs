using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Provides access to FFmpeg binaries for media probing and processing.
/// The implementation auto-detects binaries at startup in priority order:
///   1. tools/ffmpeg/ relative to the app base directory (self-contained)
///   2. System PATH
///   3. Explicit path from config/transcoding.json
/// </summary>
public interface IFFmpegService
{
    /// <summary>Absolute path to ffmpeg.exe; null when not available.</summary>
    string? FfmpegPath { get; }

    /// <summary>Absolute path to ffprobe.exe; null when not available.</summary>
    string? FfprobePath { get; }

    /// <summary>True when both binaries are located and executable.</summary>
    bool IsAvailable { get; }

    /// <summary>Hardware acceleration capabilities detected at startup.</summary>
    HardwareCapabilities HardwareCapabilities { get; }

    /// <summary>
    /// Run ffprobe on the given file and return structured media metadata.
    /// Returns null if ffprobe is unavailable or the file cannot be probed.
    /// </summary>
    Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Execute ffmpeg with the given argument string.
    /// Returns (exit code, stdout, stderr).
    /// Throws <see cref="InvalidOperationException"/> if ffmpeg is unavailable.
    /// </summary>
    Task<(int ExitCode, string Output, string Error)> RunAsync(
        string arguments, CancellationToken ct = default);
}
