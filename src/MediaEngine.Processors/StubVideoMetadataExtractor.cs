using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors;

/// <summary>
/// Placeholder <see cref="IVideoMetadataExtractor"/> that satisfies the DI
/// contract without requiring FFmpeg to be installed.
///
/// ──────────────────────────────────────────────────────────────────
/// Replacement path (spec: Phase 5 – VideoProcessor stub)
/// ──────────────────────────────────────────────────────────────────
///  When FFmpeg integration is ready:
///  1. Add the <c>FFMpegCore</c> (or <c>Xabe.FFmpeg</c>) NuGet package.
///  2. Create <c>FFmpegVideoMetadataExtractor : IVideoMetadataExtractor</c>
///     that calls <c>FFProbe.AnalyseAsync</c> and maps the result to
///     <see cref="VideoMetadata"/>.
///  3. Register the new implementation in the DI container in place of this class.
///
///  No other file needs to change — <see cref="VideoProcessor"/> is already
///  coded against <see cref="IVideoMetadataExtractor"/>.
/// </summary>
public sealed class StubVideoMetadataExtractor : IVideoMetadataExtractor
{
    /// <inheritdoc/>
    /// <remarks>
    /// Returns a <see cref="VideoMetadata"/> with all nullable fields set to
    /// <see langword="null"/>, causing <see cref="VideoProcessor"/> to emit only
    /// the claims it can derive from the file itself (title from filename,
    /// container format from magic bytes).  No false data is invented.
    /// </remarks>
    public Task<VideoMetadata?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Return an empty-but-valid instance rather than null — the file exists
        // and was detected as video; we just lack FFmpeg to inspect it further.
        return Task.FromResult<VideoMetadata?>(new VideoMetadata());
    }
}
