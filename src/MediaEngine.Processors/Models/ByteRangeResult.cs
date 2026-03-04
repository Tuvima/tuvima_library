namespace MediaEngine.Processors.Models;

/// <summary>
/// Result of a <see cref="Contracts.IByteStreamer.GetRangeAsync"/> call.
///
/// Carries the length-limited content stream and all values required to
/// construct HTTP 206 Partial Content response headers.
///
/// ──────────────────────────────────────────────────────────────────
/// HTTP 206 header mapping
/// ──────────────────────────────────────────────────────────────────
///  Content-Range: bytes {RangeStart}-{RangeEnd}/{TotalLength}
///  Content-Length: {ContentLength}
///  Accept-Ranges: bytes
///
/// The caller MUST dispose this object after copying the content stream
/// to the HTTP response body; disposal closes the underlying FileStream.
///
/// Spec: Phase 5 – Streaming Delivery; Invariants § Byte-Range Support.
/// </summary>
public sealed class ByteRangeResult : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// The content stream, positioned at <see cref="RangeStart"/> and
    /// limited to <see cref="ContentLength"/> bytes.
    /// Owned by this instance; disposed alongside it.
    /// </summary>
    public required Stream Content { get; init; }

    /// <summary>Inclusive start offset within the file (0-based).</summary>
    public required long RangeStart { get; init; }

    /// <summary>Inclusive end offset within the file (0-based).</summary>
    public required long RangeEnd { get; init; }

    /// <summary>Total file length in bytes (denominator in the Content-Range header).</summary>
    public required long TotalLength { get; init; }

    /// <summary>
    /// Number of bytes this range contains.
    /// Equals <c>RangeEnd - RangeStart + 1</c>.
    /// </summary>
    public long ContentLength => RangeEnd - RangeStart + 1;

    /// <summary>
    /// Ready-formatted value for the HTTP <c>Content-Range</c> response header.
    /// Example: <c>"bytes 0-1023/4096"</c>.
    /// </summary>
    public string ContentRangeHeader => $"bytes {RangeStart}-{RangeEnd}/{TotalLength}";

    /// <summary>
    /// <see langword="true"/> when the requested range covers the entire file
    /// (i.e. no partial-content semantics apply and a 200 OK is acceptable).
    /// </summary>
    public bool IsFullContent => RangeStart == 0 && RangeEnd == TotalLength - 1;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Content.Dispose();
    }
}
