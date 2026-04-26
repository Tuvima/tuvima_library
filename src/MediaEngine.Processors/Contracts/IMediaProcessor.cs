using MediaEngine.Domain.Enums;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Contracts;

/// <summary>
/// Stateless handler that identifies and extracts metadata from a single file format.
///
/// ──────────────────────────────────────────────────────────────────
/// Contract guarantees (spec: Phase 5 – Format Identification)
/// ──────────────────────────────────────────────────────────────────
///  • Identification MUST be based on magic bytes, not file extension.
///  • Implementations MUST be stateless — no instance fields that vary
///    between calls.
///  • Implementations MUST NOT modify, move, or delete the source file.
///  • A processor that cannot fully parse a file MUST return a
///    <see cref="ProcessorResult"/> with <c>IsCorrupt = true</c> rather
///    than throwing an unhandled exception.
///
/// Spec: Phase 5 – Media Processor Architecture § Format Identification.
/// </summary>
public interface IMediaProcessor
{
    /// <summary>
    /// The <see cref="MediaType"/> this processor handles.
    /// <see cref="MediaType.Unknown"/> is reserved for the generic fallback.
    /// </summary>
    MediaType SupportedType { get; }

    /// <summary>
    /// Relative priority used by <see cref="IProcessorRouter"/> when multiple
    /// processors claim the same file.  Higher value wins.
    /// Spec-assigned values: high-fidelity processors ≥ 100;
    /// generic fallback = <see cref="int.MinValue"/>.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Inspects the leading bytes of <paramref name="filePath"/> (magic-byte check)
    /// and returns <see langword="true"/> when this processor can handle the file.
    ///
    /// This method MUST NOT read more data than necessary for format identification
    /// (~16 bytes is sufficient for all current formats) and MUST be fast —
    /// it is called on every file during ingestion filtering.
    /// </summary>
    /// <param name="filePath">Absolute path to the candidate file.</param>
    bool CanProcess(string filePath);

    /// <summary>
    /// Opens <paramref name="filePath"/>, parses format-specific metadata, and
    /// returns a <see cref="ProcessorResult"/> containing the extracted claims
    /// and optional cover image.
    ///
    /// On partial / corrupt input the result's <see cref="ProcessorResult.IsCorrupt"/>
    /// flag is set to <see langword="true"/> and <see cref="ProcessorResult.CorruptReason"/>
    /// is populated; the method still returns (does not throw).
    /// </summary>
    /// <param name="filePath">Absolute path to the file to process.</param>
    /// <param name="ct">Cancellation token forwarded to async I/O operations.</param>
    Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default);
}
