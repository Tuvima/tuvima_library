using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Streams a media file in fixed-size chunks and computes a SHA-256 digest
/// without ever loading the full file into memory.
///
/// ──────────────────────────────────────────────────────────────────
/// Memory model
/// ──────────────────────────────────────────────────────────────────
///  • One 80 KB buffer is rented from <see cref="ArrayPool{T}.Shared"/>
///    per call.  It is returned in the <c>finally</c> block regardless
///    of success or failure.
///  • The 32-byte SHA-256 output is stack-allocated via <c>stackalloc</c>.
///  • Total managed heap pressure per call: ~0 bytes (beyond object headers).
///
/// ──────────────────────────────────────────────────────────────────
/// I/O model
/// ──────────────────────────────────────────────────────────────────
///  • FileStream is opened with <see cref="FileOptions.Asynchronous"/>
///    so <c>ReadAsync</c> yields the calling thread to the thread pool
///    while the OS services the read request.
///  • <see cref="FileOptions.SequentialScan"/> signals the OS to maximise
///    read-ahead prefetching and avoid polluting the file-system cache
///    with random-access patterns.
///  • <see cref="FileShare.Read"/> allows concurrent readers but fails if
///    a writer still holds an exclusive lock — consistent with the probe in
///    <see cref="DebounceQueue"/>.
///
/// Spec: Phase 7 – Hash-Based Uniqueness; Asset Integrity.
/// </summary>
public sealed class AssetHasher : IAssetHasher
{
    // 80 KB: large enough to amortize per-syscall overhead across large media files,
    // small enough to fit comfortably in L2 cache on modern hardware.
    private const int ReadBufferSize = 81_920;

    /// <inheritdoc/>
    public async Task<HashResult> ComputeAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var sw = Stopwatch.StartNew();

        // Open the file for sequential async reading.
        // FileShare.Read is intentional — the DebounceQueue already confirmed
        // no exclusive writer is active, but we still want to allow other readers.
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Rent a buffer to avoid a 80 KB heap allocation on every call.
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            // IncrementalHash feeds data in chunks without requiring the full
            // content upfront.  It is the underlying engine of SHA256.HashDataAsync.
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long bytesHashed = 0;
            int bytesRead;

            // Read until the stream is exhausted.  ReadAsync returns 0 at EOF.
            // ct is forwarded so a cancellation mid-file terminates promptly
            // and does not waste I/O on a result that will be discarded.
            while ((bytesRead = await stream.ReadAsync(rentedBuffer, ct)) > 0)
            {
                // Pass only the filled portion; the rented buffer may be larger
                // than ReadBufferSize (ArrayPool rounds up to power-of-two).
                hasher.AppendData(rentedBuffer.AsSpan(0, bytesRead));
                bytesHashed += bytesRead;
            }

            sw.Stop();

            // Stack-allocate the 32-byte output (SHA-256 digest is always 32 bytes).
            // TryGetHashAndReset finalises the hash and resets the internal state.
            Span<byte> digest = stackalloc byte[32];
            if (!hasher.TryGetHashAndReset(digest, out int written) || written != 32)
                throw new InvalidOperationException("SHA-256 finalisation returned unexpected length.");

            return new HashResult
            {
                FilePath = filePath,
                // Lowercase hex matches the format stored in media_assets.content_hash.
                Hex      = Convert.ToHexString(digest).ToLowerInvariant(),
                FileSize = bytesHashed,
                Elapsed  = sw.Elapsed,
            };
        }
        finally
        {
            // Return without clearing: the buffer contains no sensitive data.
            ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: false);
        }
    }
}
