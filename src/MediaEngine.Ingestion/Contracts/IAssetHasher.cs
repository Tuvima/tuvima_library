using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Computes a SHA-256 hash for a media file without loading the file into memory.
/// Spec: Phase 7 – Asset Integrity; Hash-Based Uniqueness.
///
/// The returned <see cref="HashResult.Hex"/> is used as
/// <c>media_assets.content_hash</c>, the Hash Dominance reconciliation key.
/// </summary>
public interface IAssetHasher
{
    /// <summary>
    /// Streams <paramref name="filePath"/> in fixed-size chunks and returns a
    /// <see cref="HashResult"/> containing the lowercase hex digest.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the file does not exist or is not readable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled mid-stream.
    /// </exception>
    Task<HashResult> ComputeAsync(string filePath, CancellationToken ct = default);
}
