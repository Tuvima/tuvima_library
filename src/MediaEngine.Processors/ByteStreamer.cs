using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors;

/// <summary>
/// File-backed implementation of <see cref="IByteStreamer"/>.
///
/// Opens a <see cref="FileStream"/> per request, seeks to the requested offset,
/// and wraps it in a <see cref="LengthLimitedStream"/> that prevents reads
/// beyond the range boundary.  The <see cref="ByteRangeResult"/> owns the stream
/// and disposes it when the caller is done.
///
/// ──────────────────────────────────────────────────────────────────
/// Blazor / ASP.NET Core usage pattern
/// ──────────────────────────────────────────────────────────────────
/// <code>
/// // In a minimal-API endpoint:
/// app.MapGet("/stream/{id}", async (Guid id, HttpContext ctx, IByteStreamer streamer) =>
/// {
///     string path = library.ResolveAssetPath(id);
///
///     // Parse the Range header (if any).
///     if (ctx.Request.Headers.TryGetValue("Range", out var rangeHeader))
///     {
///         // e.g. "bytes=0-1048575"
///         ParseRange(rangeHeader!, out long start, out long? length);
///         using var result = await streamer.GetRangeAsync(path, start, length, ctx.RequestAborted);
///         ctx.Response.StatusCode  = 206;
///         ctx.Response.ContentType = "video/mp4";
///         ctx.Response.Headers.ContentRange  = result.ContentRangeHeader;
///         ctx.Response.Headers.ContentLength = result.ContentLength;
///         await result.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
///     }
///     else
///     {
///         // Full file response.
///         using var result = await streamer.GetRangeAsync(path, 0, null, ctx.RequestAborted);
///         ctx.Response.ContentType = "video/mp4";
///         await result.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
///     }
/// });
/// </code>
///
/// Spec: Phase 5 – Streaming Delivery; Invariants § Byte-Range Support.
/// </summary>
public sealed class ByteStreamer : IByteStreamer
{
    // 64 KB FileStream buffer — tuned for sequential reads of large media files.
    private const int FileStreamBufferSize = 65_536;

    // -------------------------------------------------------------------------
    // IByteStreamer
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<long> GetFileSizeAsync(string assetPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);

        var info = new FileInfo(assetPath);
        if (!info.Exists)
            throw new FileNotFoundException($"Media asset not found: {assetPath}", assetPath);

        return Task.FromResult(info.Length);
    }

    /// <inheritdoc/>
    public Task<ByteRangeResult> GetRangeAsync(
        string assetPath,
        long offset,
        long? length = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);

        var info = new FileInfo(assetPath);
        if (!info.Exists)
            throw new FileNotFoundException($"Media asset not found: {assetPath}", assetPath);

        long totalLength = info.Length;

        // Clamp offset to valid range.
        long rangeStart = Math.Clamp(offset, 0, totalLength > 0 ? totalLength - 1 : 0);

        // Compute inclusive range end.
        long rangeEnd = length.HasValue
            ? Math.Min(rangeStart + length.Value - 1, totalLength - 1)
            : totalLength - 1;

        // Guard against empty or inverted ranges (e.g. empty file).
        if (totalLength == 0 || rangeStart > rangeEnd)
        {
            return Task.FromResult(new ByteRangeResult
            {
                Content     = Stream.Null,
                RangeStart  = 0,
                RangeEnd    = 0,
                TotalLength = totalLength,
            });
        }

        long contentLength = rangeEnd - rangeStart + 1;

        // Open the file and seek to the start of the requested range.
        // Guard: if Seek or LengthLimitedStream construction throws, dispose the
        // FileStream immediately so we don't leak a file handle.
        FileStream? fs = null;
        try
        {
            fs = new FileStream(
                assetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: FileStreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            fs.Seek(rangeStart, SeekOrigin.Begin);

            // Wrap the stream so callers cannot read beyond rangeEnd.
            // Ownership of fs transfers to LengthLimitedStream — it disposes fs.
            var limitedStream = new LengthLimitedStream(fs, contentLength);
            fs = null; // prevent disposal in catch block

            return Task.FromResult(new ByteRangeResult
            {
                Content     = limitedStream,
                RangeStart  = rangeStart,
                RangeEnd    = rangeEnd,
                TotalLength = totalLength,
            });
        }
        catch
        {
            fs?.Dispose();
            throw;
        }
    }

    // =========================================================================
    // Internal: stream wrapper that enforces the range boundary
    // =========================================================================

    /// <summary>
    /// Wraps an inner stream and prevents reads beyond a fixed byte-count limit.
    /// Disposing this stream also disposes the inner stream.
    /// </summary>
    private sealed class LengthLimitedStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LengthLimitedStream(Stream inner, long maxBytes)
        {
            _inner     = inner;
            _remaining = maxBytes;
        }

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException("LengthLimitedStream does not support Length.");

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        // ── Synchronous read ────────────────────────────────────────────────

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            int toRead = (int)Math.Min(count, _remaining);
            int read   = _inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0) return 0;
            int toRead = (int)Math.Min(buffer.Length, _remaining);
            int read   = _inner.Read(buffer[..toRead]);
            _remaining -= read;
            return read;
        }

        // ── Asynchronous read ───────────────────────────────────────────────

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_remaining <= 0) return 0;
            int toRead = (int)Math.Min(count, _remaining);
            int read   = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), ct)
                                     .ConfigureAwait(false);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_remaining <= 0) return 0;
            int toRead = (int)Math.Min(buffer.Length, _remaining);
            int read   = await _inner.ReadAsync(buffer[..toRead], ct).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }

        // ── Unsupported operations ──────────────────────────────────────────

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("Range streams are forward-only.");

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Range streams are read-only.");

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
