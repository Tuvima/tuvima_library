namespace MediaEngine.Ingestion.Models;

/// <summary>
/// The output of a single <see cref="Contracts.IAssetHasher.ComputeAsync"/> call.
/// Contains all information needed to decide whether a file is new to the library
/// and to construct a <see cref="Domain.Aggregates.MediaAsset"/> for storage.
/// </summary>
public sealed class HashResult
{
    /// <summary>The file that was hashed.</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Lowercase hexadecimal SHA-256 digest (64 characters).
    /// This value maps directly to <c>media_assets.content_hash</c>.
    /// Example: <c>"e3b0c44298fc1c149afb4c8996fb92427ae41e4649b934ca495991b7852b855"</c>
    /// </summary>
    public required string Hex { get; init; }

    /// <summary>Total number of bytes read from the file during hashing.</summary>
    public required long FileSize { get; init; }

    /// <summary>Wall-clock time taken to read and hash the file. Useful for perf diagnostics.</summary>
    public required TimeSpan Elapsed { get; init; }
}
