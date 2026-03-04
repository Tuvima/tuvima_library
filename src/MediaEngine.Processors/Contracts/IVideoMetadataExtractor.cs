using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Contracts;

/// <summary>
/// Stub-pattern interface for video metadata extraction.
///
/// ──────────────────────────────────────────────────────────────────
/// Design intent (spec: Phase 5 – VideoProcessor stub)
/// ──────────────────────────────────────────────────────────────────
///  The <see cref="VideoProcessor"/> depends on this interface rather than
///  calling FFmpeg directly.  This keeps the processor testable and decouples
///  the metadata extraction from the processing pipeline.
///
///  Two implementations exist:
///   • <c>StubVideoMetadataExtractor</c> (current) — returns placeholder
///     values; used until FFmpeg is integrated.
///   • <c>FFmpegVideoMetadataExtractor</c> (future) — wraps
///     <c>FFMpegCore</c> or <c>Xabe.FFmpeg</c> to read real container
///     metadata (resolution, duration, codec).
///
///  To switch to the real extractor, register <c>FFmpegVideoMetadataExtractor</c>
///  in the DI container instead of <c>StubVideoMetadataExtractor</c>.
///
/// Spec: Phase 5 – Extension Points § IStreamAdapter (analogous stub pattern).
/// </summary>
public interface IVideoMetadataExtractor
{
    /// <summary>
    /// Attempts to extract basic technical metadata from the video file at
    /// <paramref name="filePath"/>.
    /// </summary>
    /// <returns>
    /// A populated <see cref="VideoMetadata"/> when extraction succeeds, or
    /// <see langword="null"/> when the file is corrupt / unsupported.
    /// </returns>
    Task<VideoMetadata?> ExtractAsync(string filePath, CancellationToken ct = default);
}
