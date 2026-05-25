using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Helpers;

namespace MediaEngine.Api.Services;

public sealed class ArtworkRenditionRepairStartupService : BackgroundService
{
    private readonly IEntityAssetRepository _assetRepository;
    private readonly ICanonicalValueRepository _canonicalRepository;
    private readonly AssetPathService _assetPathService;
    private readonly ILogger<ArtworkRenditionRepairStartupService> _logger;

    public ArtworkRenditionRepairStartupService(
        IEntityAssetRepository assetRepository,
        ICanonicalValueRepository canonicalRepository,
        AssetPathService assetPathService,
        ILogger<ArtworkRenditionRepairStartupService> logger)
    {
        _assetRepository = assetRepository;
        _canonicalRepository = canonicalRepository;
        _assetPathService = assetPathService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            await RepairAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Artwork rendition repair failed.");
        }
    }

    private async Task RepairAsync(CancellationToken ct)
    {
        var assets = await _assetRepository.GetPreferredArtworkAsync(ct);
        var repairedRenditions = 0;
        var repairedCanonicals = 0;

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            if (!Guid.TryParse(asset.EntityId, out var entityId))
            {
                continue;
            }

            var repairedAsset = false;
            if (ArtworkVariantHelper.ShouldGenerateRenditions(asset.AssetTypeValue)
                && NeedsRenditionRepair(asset)
                && !string.IsNullOrWhiteSpace(asset.LocalImagePath)
                && File.Exists(asset.LocalImagePath))
            {
                ArtworkVariantHelper.StampMetadataAndRenditions(asset, _assetPathService);
                await _assetRepository.UpsertAsync(asset, ct);
                repairedRenditions++;
                repairedAsset = true;
            }

            var canonicalValues = ArtworkCanonicalHelper.CreatePreferredAssetCanonicals(
                entityId,
                asset,
                DateTimeOffset.UtcNow);

            if (canonicalValues.Count == 0)
            {
                continue;
            }

            if (repairedAsset || await HasMissingOrStaleCanonicalAsync(entityId, canonicalValues, ct))
            {
                await _canonicalRepository.UpsertBatchAsync(canonicalValues, ct);
                repairedCanonicals++;
            }
        }

        if (repairedRenditions > 0 || repairedCanonicals > 0)
        {
            _logger.LogInformation(
                "Artwork rendition repair completed: {RenditionCount} assets repaired, {CanonicalCount} canonical sets refreshed.",
                repairedRenditions,
                repairedCanonicals);
        }
    }

    private static bool NeedsRenditionRepair(EntityAsset asset) =>
        string.IsNullOrWhiteSpace(asset.LocalImagePathSmall)
        || !File.Exists(asset.LocalImagePathSmall)
        || string.IsNullOrWhiteSpace(asset.LocalImagePathMedium)
        || !File.Exists(asset.LocalImagePathMedium)
        || string.IsNullOrWhiteSpace(asset.LocalImagePathLarge)
        || !File.Exists(asset.LocalImagePathLarge);

    private async Task<bool> HasMissingOrStaleCanonicalAsync(
        Guid entityId,
        IReadOnlyList<CanonicalValue> expectedValues,
        CancellationToken ct)
    {
        var existing = await _canonicalRepository.GetByEntityAsync(entityId, ct);
        var valuesByKey = existing.ToDictionary(
            value => value.Key,
            value => value.Value,
            StringComparer.OrdinalIgnoreCase);

        return expectedValues.Any(expected =>
            !valuesByKey.TryGetValue(expected.Key, out var existingValue)
            || !string.Equals(existingValue, expected.Value, StringComparison.Ordinal));
    }
}
