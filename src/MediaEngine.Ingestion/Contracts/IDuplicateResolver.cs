using MediaEngine.Domain.Aggregates;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Determines whether a hashed ingestion candidate is new, a same-path echo,
/// a true duplicate, or a replacement for an orphaned asset row.
/// </summary>
public interface IDuplicateResolver
{
    Task<DuplicateResolution> ResolveAsync(
        IngestionCandidate candidate,
        string contentHash,
        CancellationToken ct = default);
}

public enum DuplicateResolutionKind
{
    NewAsset,
    OrphanedExisting,
    SamePathRedetected,
    DuplicateDifferentPath,
}

public sealed record DuplicateResolution(
    DuplicateResolutionKind Kind,
    MediaAsset? ExistingAsset);
