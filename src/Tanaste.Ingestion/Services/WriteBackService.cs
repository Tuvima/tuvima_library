using Microsoft.Extensions.Logging;
using Tanaste.Domain.Contracts;
using Tanaste.Ingestion.Contracts;
using Tanaste.Storage.Contracts;
using Tanaste.Storage.Models;

namespace Tanaste.Ingestion.Services;

/// <summary>
/// Writes resolved canonical metadata back into physical media files.
///
/// Loads configuration from <c>config/writeback.json</c> via the generic
/// <see cref="IConfigurationLoader.LoadConfig{T}"/> method.
///
/// Selects the correct <see cref="IMetadataTagger"/> for the file format
/// and delegates the write operation. Respects the write-back configuration
/// (enabled toggle, trigger-specific flags, field filtering).
/// </summary>
public sealed class WriteBackService : IWriteBackService
{
    private readonly IMediaAssetRepository         _assetRepo;
    private readonly ICanonicalValueRepository     _canonicalRepo;
    private readonly IConfigurationLoader          _configLoader;
    private readonly IEnumerable<IMetadataTagger>  _taggers;
    private readonly ISystemActivityRepository     _activityRepo;
    private readonly ILogger<WriteBackService>     _logger;

    public WriteBackService(
        IMediaAssetRepository         assetRepo,
        ICanonicalValueRepository     canonicalRepo,
        IConfigurationLoader          configLoader,
        IEnumerable<IMetadataTagger>  taggers,
        ISystemActivityRepository     activityRepo,
        ILogger<WriteBackService>     logger)
    {
        _assetRepo     = assetRepo;
        _canonicalRepo = canonicalRepo;
        _configLoader  = configLoader;
        _taggers       = taggers;
        _activityRepo  = activityRepo;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task WriteMetadataAsync(Guid assetId, string trigger, CancellationToken ct = default)
    {
        // Load write-back configuration.
        var config = _configLoader.LoadConfig<WriteBackConfiguration>("", "writeback")
                     ?? new WriteBackConfiguration();

        if (!config.Enabled)
        {
            _logger.LogDebug("WriteBack: disabled — skipping for asset {AssetId}", assetId);
            return;
        }

        // Check trigger-specific flags.
        var allowed = trigger switch
        {
            "auto_match"           => config.WriteOnAutoMatch,
            "manual_override"      => config.WriteOnManualOverride,
            "universe_enrichment"  => config.WriteOnUniverseEnrichment,
            _                      => config.Enabled,
        };

        if (!allowed)
        {
            _logger.LogDebug("WriteBack: trigger '{Trigger}' disabled — skipping for asset {AssetId}",
                trigger, assetId);
            return;
        }

        // Resolve file path.
        var asset = await _assetRepo.FindByIdAsync(assetId, ct);
        if (asset is null)
        {
            _logger.LogWarning("WriteBack: asset {AssetId} not found in database", assetId);
            return;
        }

        if (string.IsNullOrWhiteSpace(asset.FilePathRoot) || !File.Exists(asset.FilePathRoot))
        {
            _logger.LogWarning("WriteBack: file not found at {Path} for asset {AssetId}",
                asset.FilePathRoot, assetId);
            return;
        }

        // Find a tagger for this file type.
        var tagger = _taggers.FirstOrDefault(t => t.CanHandle(asset.FilePathRoot));
        if (tagger is null)
        {
            _logger.LogDebug("WriteBack: no tagger supports {Path} — skipping", asset.FilePathRoot);
            return;
        }

        // Load canonical values for this entity.
        // The asset's entity is typically the Work or Edition; canonical values
        // are keyed by entity_id which maps to the work the asset belongs to.
        var canonicals = await _canonicalRepo.GetByEntityAsync(assetId, ct);

        // If no canonical values for the asset ID, try the edition's entity chain.
        if (canonicals.Count == 0)
        {
            _logger.LogDebug("WriteBack: no canonical values for asset {AssetId} — skipping", assetId);
            return;
        }

        // Build the tag dictionary, applying field filtering.
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var excludeFields = new HashSet<string>(config.ExcludeFields, StringComparer.OrdinalIgnoreCase);
        var writeAll = string.Equals(config.FieldsToWrite, "all", StringComparison.OrdinalIgnoreCase);

        HashSet<string>? allowedFields = null;
        if (!writeAll)
        {
            allowedFields = new HashSet<string>(
                config.FieldsToWrite.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        foreach (var cv in canonicals)
        {
            if (excludeFields.Contains(cv.Key)) continue;
            if (allowedFields is not null && !allowedFields.Contains(cv.Key)) continue;
            tags[cv.Key] = cv.Value;
        }

        if (tags.Count == 0)
        {
            _logger.LogDebug("WriteBack: no writable fields after filtering — skipping {AssetId}", assetId);
            return;
        }

        // Write tags.
        try
        {
            await tagger.WriteTagsAsync(asset.FilePathRoot, tags, ct);

            _logger.LogInformation("WriteBack: wrote {Count} fields to {Path} (trigger: {Trigger})",
                tags.Count, asset.FilePathRoot, trigger);

            // Log to activity ledger.
            await _activityRepo.LogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.MetadataWrittenToFile,
                EntityId   = assetId,
                Detail     = $"Write-back ({trigger}): {tags.Count} field(s) written to {Path.GetFileName(asset.FilePathRoot)}.",
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteBack: failed to write metadata to {Path}", asset.FilePathRoot);
            // Non-fatal — write-back failure should not break the pipeline.
        }
    }
}
