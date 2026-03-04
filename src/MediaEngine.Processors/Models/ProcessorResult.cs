using MediaEngine.Domain.Enums;

namespace MediaEngine.Processors.Models;

/// <summary>
/// The output of a single <see cref="Contracts.IMediaProcessor.ProcessAsync"/> call.
///
/// Contains everything the ingestion engine needs to:
///   • Classify the file's <see cref="MediaType"/>
///   • Populate <c>metadata_claims</c> rows
///   • Persist an optional cover image
///   • Decide whether to quarantine a corrupt file
///
/// Spec: Phase 5 – Media Processor Architecture § Processor Output.
/// </summary>
public sealed class ProcessorResult
{
    /// <summary>The absolute path of the file that was processed.</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The <see cref="MediaType"/> determined from magic-byte inspection.
    /// <see cref="MediaType.Unknown"/> when the format could not be identified.
    /// </summary>
    public required MediaType DetectedType { get; init; }

    /// <summary>
    /// Ordered list of metadata claims extracted from the file.
    /// Empty (not null) when no metadata could be read.
    /// </summary>
    public IReadOnlyList<ExtractedClaim> Claims { get; init; } = [];

    /// <summary>
    /// Raw cover-image bytes, or <see langword="null"/> if the format carries
    /// no embedded cover or the cover could not be decoded.
    /// </summary>
    public byte[]? CoverImage { get; init; }

    /// <summary>
    /// IANA MIME type of <see cref="CoverImage"/>
    /// (e.g. <c>"image/jpeg"</c>, <c>"image/png"</c>).
    /// <see langword="null"/> when <see cref="CoverImage"/> is <see langword="null"/>.
    /// </summary>
    public string? CoverImageMimeType { get; init; }

    /// <summary>
    /// <see langword="true"/> when the file failed format validation
    /// (magic bytes present but content is malformed / truncated).
    ///
    /// The ingestion engine uses this flag to quarantine the asset rather
    /// than promoting it to the library.
    /// </summary>
    public bool IsCorrupt { get; init; }

    /// <summary>
    /// Human-readable explanation of why <see cref="IsCorrupt"/> is set.
    /// <see langword="null"/> when <see cref="IsCorrupt"/> is <see langword="false"/>.
    /// </summary>
    public string? CorruptReason { get; init; }
}
