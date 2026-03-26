namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Chromaprint-based audio fingerprinting for music similarity analysis.
/// Groups tracks that "sound good together" based on acoustic similarity
/// combined with album-level Wikipedia context.
/// </summary>
public interface IAudioSimilarityService
{
    /// <summary>Generate and store an audio fingerprint for a music file.</summary>
    Task<bool> FingerprintAsync(Guid assetId, string filePath, CancellationToken ct = default);

    /// <summary>Find similar tracks/albums based on acoustic and contextual similarity.</summary>
    Task<IReadOnlyList<SimilarityMatch>> FindSimilarAsync(Guid assetId, int limit = 10, CancellationToken ct = default);
}

/// <summary>A similarity match result.</summary>
public sealed class SimilarityMatch
{
    /// <summary>The matched asset ID.</summary>
    public Guid AssetId { get; init; }

    /// <summary>Similarity score (0.0-1.0, higher is more similar).</summary>
    public double Score { get; init; }

    /// <summary>Whether similarity is acoustic, contextual, or both.</summary>
    public required string MatchType { get; init; }
}
