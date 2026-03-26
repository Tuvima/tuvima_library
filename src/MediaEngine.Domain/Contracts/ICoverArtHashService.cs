namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Computes perceptual hashes (pHash) for cover art images.
/// Used to compare embedded cover art against provider thumbnails
/// for improved Stage 1 matching accuracy.
/// </summary>
public interface ICoverArtHashService
{
    /// <summary>Compute a 64-bit perceptual hash from image bytes.</summary>
    Task<ulong?> ComputeHashAsync(byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// Compute similarity between two perceptual hashes.
    /// Returns 0.0 (completely different) to 1.0 (identical).
    /// Based on normalized Hamming distance of the 64-bit hashes.
    /// </summary>
    double ComputeSimilarity(ulong hashA, ulong hashB);
}
