namespace MediaEngine.Processors.Models;

/// <summary>
/// Technical metadata extracted from a video container.
///
/// Produced by <see cref="Contracts.IVideoMetadataExtractor"/> and consumed by
/// <see cref="Processors.VideoProcessor"/> to generate
/// <see cref="ExtractedClaim"/> records for the ingestion pipeline.
///
/// When a field could not be determined (stub implementation or corrupt file),
/// the corresponding property is <see langword="null"/> and no claim is emitted
/// for that field — preserving the principle that unconfirmed data is better
/// omitted than guessed.
///
/// Spec: Phase 5 – Content Extraction; stub-pattern for FFmpeg integration.
/// </summary>
public sealed class VideoMetadata
{
    /// <summary>Frame width in pixels.  Null when unknown.</summary>
    public int? WidthPx { get; init; }

    /// <summary>Frame height in pixels.  Null when unknown.</summary>
    public int? HeightPx { get; init; }

    /// <summary>
    /// Total playback duration.  Null when unknown.
    /// Stored as a claim in fractional seconds (e.g. <c>"5423.8"</c>).
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Short codec identifier as reported by the container
    /// (e.g. <c>"h264"</c>, <c>"hevc"</c>, <c>"av1"</c>).
    /// Null when the container does not expose codec info or extraction failed.
    /// </summary>
    public string? VideoCodec { get; init; }

    /// <summary>
    /// Frames per second as a decimal value (e.g. <c>23.976</c>).
    /// Null when unknown.
    /// </summary>
    public double? FrameRate { get; init; }
}
