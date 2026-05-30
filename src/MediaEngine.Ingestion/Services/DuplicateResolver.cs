using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Services;

public sealed class DuplicateResolver : IDuplicateResolver
{
    private readonly IMediaAssetRepository _assetRepo;

    public DuplicateResolver(IMediaAssetRepository assetRepo)
        => _assetRepo = assetRepo;

    public async Task<DuplicateResolution> ResolveAsync(
        IngestionCandidate candidate,
        string contentHash,
        CancellationToken ct = default)
    {
        var existing = await _assetRepo.FindByHashAsync(contentHash, ct).ConfigureAwait(false);
        if (existing is null)
            return new DuplicateResolution(DuplicateResolutionKind.NewAsset, null);

        if (!File.Exists(existing.FilePathRoot))
            return new DuplicateResolution(DuplicateResolutionKind.OrphanedExisting, existing);

        var samePath = string.Equals(
            Path.GetFullPath(candidate.Path),
            Path.GetFullPath(existing.FilePathRoot),
            StringComparison.OrdinalIgnoreCase);

        return samePath
            ? new DuplicateResolution(DuplicateResolutionKind.SamePathRedetected, existing)
            : new DuplicateResolution(DuplicateResolutionKind.DuplicateDifferentPath, existing);
    }
}
