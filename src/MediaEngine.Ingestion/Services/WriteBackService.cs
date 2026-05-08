using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Ingestion.Services;

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
    private readonly IWorkRepository               _workRepo;
    private readonly IConfigurationLoader          _configLoader;
    private readonly IEnumerable<IMetadataTagger>  _taggers;
    private readonly ISystemActivityRepository     _activityRepo;
    private readonly WritebackConfigState?         _hashState;
    private readonly IEnrichmentConcurrencyLimiter _concurrency;
    private readonly ILogger<WriteBackService>     _logger;

    public WriteBackService(
        IMediaAssetRepository         assetRepo,
        ICanonicalValueRepository     canonicalRepo,
        IWorkRepository               workRepo,
        IConfigurationLoader          configLoader,
        IEnumerable<IMetadataTagger>  taggers,
        ISystemActivityRepository     activityRepo,
        ILogger<WriteBackService>     logger,
        WritebackConfigState?         hashState = null,
        IEnrichmentConcurrencyLimiter? concurrencyLimiter = null)
    {
        _assetRepo     = assetRepo;
        _canonicalRepo = canonicalRepo;
        _workRepo      = workRepo;
        _configLoader  = configLoader;
        _taggers       = taggers;
        _activityRepo  = activityRepo;
        _hashState     = hashState;
        _concurrency   = concurrencyLimiter ?? NoopEnrichmentConcurrencyLimiter.Instance;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public Task WriteMetadataAsync(Guid assetId, string trigger, CancellationToken ct = default, Guid? ingestionRunId = null) =>
        _concurrency.RunAsync(
            EnrichmentWorkKind.WriteBack,
            token => WriteMetadataCoreAsync(assetId, trigger, token, ingestionRunId),
            ct);

    private async Task WriteMetadataCoreAsync(Guid assetId, string trigger, CancellationToken ct, Guid? ingestionRunId)
    {
        // Load write-back configuration.
        var config = _configLoader.LoadConfig<WriteBackConfiguration>("", "writeback")
                     ?? new WriteBackConfiguration();

        if (!config.Enabled)
        {
            _logger.LogDebug("WriteBack: disabled — skipping for asset {AssetId}", assetId);
            return;
        }

        // Check trigger-specific flags. The "config_change" trigger is the
        // auto re-tag sweep — defaults to enabled (mirrors WriteOnAutoMatch)
        // because the user has just edited writeback-fields.json and clicked Apply.
        var allowed = trigger switch
        {
            "auto_match"           => config.WriteOnAutoMatch,
            "manual_override"      => config.WriteOnManualOverride,
            "universe_enrichment"  => config.WriteOnUniverseEnrichment,
            "config_change"        => config.WriteOnAutoMatch,
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

        // Guard: only write back to files that are already in the Library Root.
        // Writing metadata back to a file still in the Watch Folder modifies its
        // content hash, which causes the watcher to re-detect it as a new file,
        // triggering an infinite ingestion loop.
        var core = _configLoader.LoadCore();
        if (!string.IsNullOrWhiteSpace(core.LibraryRoot)
            && !asset.FilePathRoot.StartsWith(core.LibraryRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("WriteBack: skipping — file is not yet in Library Root (still in watcher/staging)");
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

        // Resolve the asset's media type via its Work lineage so we can look up
        // the per-media-type writable field list from writeback-fields.json.
        var lineage = await _workRepo.GetLineageByAssetAsync(assetId, ct);
        var mediaType = lineage?.MediaType.ToString();

        // Load the per-media-type field catalogue (single source of truth for
        // both display in the library detail drawer and file write-back).
        var fieldsConfig = _configLoader.LoadConfig<WritebackFieldsConfiguration>("", "writeback-fields")
                           ?? new WritebackFieldsConfiguration();
        var allowedFields = new HashSet<string>(
            fieldsConfig.GetFieldsFor(mediaType),
            StringComparer.OrdinalIgnoreCase);

        if (allowedFields.Count == 0)
        {
            _logger.LogDebug("WriteBack: no writable fields configured for media type {MediaType} — skipping {AssetId}",
                mediaType ?? "(unknown)", assetId);
            return;
        }

        // Build the tag dictionary, applying field filtering.
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var excludeFields = new HashSet<string>(config.ExcludeFields, StringComparer.OrdinalIgnoreCase);

        foreach (var cv in canonicals)
        {
            if (excludeFields.Contains(cv.Key)) continue;
            if (!allowedFields.Contains(cv.Key)) continue;
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

            // Stamp the per-media-type writeback hash so the auto re-tag sweep
            // skips this asset on the next pass. The 30-day refresh path also
            // funnels through here, which is how it stays in sync without a
            // separate touchpoint.
            if (_hashState is not null && !string.IsNullOrEmpty(mediaType))
            {
                var hash = _hashState.ComputeHashFor(mediaType);
                if (!string.IsNullOrEmpty(hash))
                {
                    await _assetRepo.UpdateWritebackHashAsync(assetId, hash, ct);
                }
            }

            // Log to activity ledger.
            await _activityRepo.LogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.MetadataWrittenToFile,
                EntityId   = assetId,
                Detail     = $"Write-back ({trigger}): {tags.Count} field(s) written to {Path.GetFileName(asset.FilePathRoot)}.",
                IngestionRunId = ingestionRunId,
            }, ct);
        }
        catch (Exception ex) when (trigger == "config_change")
        {
            // For sweep-driven writes, the caller (RetagSweepWorker) is
            // responsible for failure classification and retry routing.
            // Re-throw so the worker sees the original exception.
            _logger.LogWarning(ex, "WriteBack: sweep failed for {Path} — caller will classify",
                asset.FilePathRoot);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteBack: failed to write metadata to {Path}", asset.FilePathRoot);
            // Non-fatal — write-back failure should not break the pipeline.
        }
    }
}
