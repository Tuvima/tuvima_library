namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Repository for audio fingerprints used in music similarity analysis.
/// </summary>
public interface IAudioFingerprintRepository
{
    /// <summary>Store a fingerprint for an asset.</summary>
    Task UpsertAsync(Guid assetId, byte[] fingerprint, double durationSec, CancellationToken ct = default);

    /// <summary>Get a fingerprint for an asset.</summary>
    Task<(byte[]? Fingerprint, double DurationSec)?> GetAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Get all fingerprints for Hamming distance comparison.</summary>
    Task<IReadOnlyList<(Guid AssetId, byte[] Fingerprint)>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Check if a fingerprint exists for an asset.</summary>
    Task<bool> ExistsAsync(Guid assetId, CancellationToken ct = default);
}
