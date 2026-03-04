using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Contracts;

/// <summary>
/// Provides byte-range access to media files, enabling HTTP 206 Partial Content
/// responses for seeking and resumable streaming in Blazor and API endpoints.
///
/// ──────────────────────────────────────────────────────────────────
/// Byte-range contract (spec: Phase 5 – Streaming Delivery)
/// ──────────────────────────────────────────────────────────────────
///  Callers supply an <paramref name="offset"/> and an optional
///  <paramref name="length"/>.  When <paramref name="length"/> is
///  <see langword="null"/>, the stream runs to the end of the file.
///
///  The returned <see cref="ByteRangeResult"/> carries:
///   • A <see cref="System.IO.Stream"/> positioned at <paramref name="offset"/>
///     and limited to at most <paramref name="length"/> bytes.
///   • The exact byte range in [RangeStart, RangeEnd] (inclusive).
///   • The file's total byte length for the HTTP Content-Range header.
///
///  The caller MUST dispose the <see cref="ByteRangeResult"/> after use to
///  release the underlying <see cref="System.IO.FileStream"/>.
///
/// Spec: Phase 5 – Interfaces § IByteStreamer; Invariants § Byte-Range Support.
/// </summary>
public interface IByteStreamer
{
    /// <summary>
    /// Opens the file at <paramref name="assetPath"/> and returns a
    /// length-limited stream starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="assetPath">Absolute path to the media file.</param>
    /// <param name="offset">
    /// Start position within the file (0-based, inclusive).
    /// Clamped to [0, fileSize] automatically.
    /// </param>
    /// <param name="length">
    /// Maximum number of bytes to serve.  Pass <see langword="null"/> to
    /// stream from <paramref name="offset"/> to the end of the file.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the file does not exist at <paramref name="assetPath"/>.
    /// </exception>
    Task<ByteRangeResult> GetRangeAsync(
        string assetPath,
        long offset,
        long? length = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the total byte-length of the file at <paramref name="assetPath"/>
    /// without opening a full read stream.
    /// Used by controllers to set <c>Content-Length</c> on un-ranged responses.
    /// </summary>
    Task<long> GetFileSizeAsync(string assetPath, CancellationToken ct = default);
}
