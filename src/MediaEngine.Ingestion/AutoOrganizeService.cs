using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Moves a staged media asset into the organised library after hydration
/// has improved its metadata confidence above the auto-organize threshold.
///
/// Reuses the same path-calculation and sidecar-writing logic as the primary
/// ingestion pipeline, but operates on canonical values rather than freshly
/// extracted metadata.
/// </summary>
public sealed class AutoOrganizeService : IAutoOrganizeService
{
    private readonly IMediaAssetRepository    _assetRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IFileOrganizer           _organizer;
    private readonly ISidecarWriter           _sidecar;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IEventPublisher          _publisher;
    private readonly IngestionOptions         _options;
    private readonly ILogger<AutoOrganizeService> _logger;

    public AutoOrganizeService(
        IMediaAssetRepository      assetRepo,
        ICanonicalValueRepository  canonicalRepo,
        IFileOrganizer             organizer,
        ISidecarWriter             sidecar,
        ISystemActivityRepository  activityRepo,
        IEventPublisher            publisher,
        IOptions<IngestionOptions> options,
        ILogger<AutoOrganizeService> logger)
    {
        _assetRepo     = assetRepo;
        _canonicalRepo = canonicalRepo;
        _organizer     = organizer;
        _sidecar       = sidecar;
        _activityRepo  = activityRepo;
        _publisher     = publisher;
        _options       = options.Value;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task TryAutoOrganizeAsync(Guid assetId, CancellationToken ct = default, Guid? ingestionRunId = null)
    {
        if (string.IsNullOrWhiteSpace(_options.LibraryRoot))
        {
            _logger.LogDebug(
                "Auto-organize skipped for {Id}: LibraryRoot not configured", assetId);
            return;
        }

        var asset = await _assetRepo.FindByIdAsync(assetId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            _logger.LogDebug("Auto-organize skipped: asset {Id} not found", assetId);
            return;
        }

        if (!File.Exists(asset.FilePathRoot))
        {
            _logger.LogWarning(
                "Auto-organize skipped for {Id}: file missing at {Path}",
                assetId, asset.FilePathRoot);
            return;
        }

        var canonicals = await _canonicalRepo.GetByEntityAsync(assetId, ct)
            .ConfigureAwait(false);
        if (canonicals.Count == 0)
        {
            _logger.LogDebug(
                "Auto-organize skipped for {Id}: no canonical values", assetId);
            return;
        }

        var metadata = canonicals.ToDictionary(
            c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        var mediaType = metadata.TryGetValue("media_type", out var mtStr)
            && Enum.TryParse<MediaType>(mtStr, ignoreCase: true, out var mt)
                ? mt
                : (MediaType?)null;

        // If the file is already in the library, skip the move but still refresh
        // the sidecar with the latest canonical values (enrichment may have added
        // new fields — characters, genre, series, narrator, etc. — since the file
        // was first organized).
        bool alreadyOrganized = asset.FilePathRoot.StartsWith(
            _options.LibraryRoot, StringComparison.OrdinalIgnoreCase);

        if (alreadyOrganized)
        {
            string existingEditionFolder = Path.GetDirectoryName(asset.FilePathRoot) ?? string.Empty;
            string existingHubFolder     = Path.GetDirectoryName(existingEditionFolder) ?? string.Empty;
            await WriteSidecarsAsync(assetId, asset.ContentHash, metadata, mediaType,
                existingEditionFolder, existingHubFolder, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Sidecar refreshed for {Id} (already organized at {Path})",
                assetId, asset.FilePathRoot);
            return;
        }

        var synth = new IngestionCandidate
        {
            Path              = asset.FilePathRoot,
            EventType         = FileEventType.Created,
            DetectedAt        = DateTimeOffset.UtcNow,
            ReadyAt           = DateTimeOffset.UtcNow,
            Metadata          = metadata,
            DetectedMediaType = mediaType,
        };

        var relative = _organizer.CalculatePath(synth, _options.OrganizationTemplate);

        // Block organization into "Other" category.
        if (relative.StartsWith("Other", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Auto-organize blocked for {Id}: resolved category is 'Other'", assetId);
            return;
        }

        var destPath = Path.Combine(_options.LibraryRoot, relative);
        bool moved = await _organizer.ExecuteMoveAsync(asset.FilePathRoot, destPath, ct)
            .ConfigureAwait(false);

        if (!moved)
        {
            _logger.LogWarning(
                "Auto-organize move failed for {Id}: {Source} → {Dest}",
                assetId, asset.FilePathRoot, destPath);
            return;
        }

        await _assetRepo.UpdateFilePathAsync(assetId, destPath, ct).ConfigureAwait(false);

        // Write sidecar XML files.
        string editionFolder = Path.GetDirectoryName(destPath) ?? string.Empty;
        string hubFolder     = Path.GetDirectoryName(editionFolder) ?? string.Empty;

        await WriteSidecarsAsync(assetId, asset.ContentHash, metadata, mediaType,
            editionFolder, hubFolder, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Auto-organized asset {Id} after hydration: {Source} → {Dest}",
            assetId, asset.FilePathRoot, destPath);

        try
        {
            await _activityRepo.LogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = SystemActionType.PathUpdated,
                EntityId   = assetId,
                EntityType = "MediaAsset",
                HubName    = metadata.GetValueOrDefault("title", "Unknown"),
                Detail     = $"Auto-organized after hydration: {Path.GetFileName(asset.FilePathRoot)} → {Path.GetRelativePath(_options.LibraryRoot, destPath)}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Activity log failed for auto-organize — continuing");
        }

        try
        {
            await _publisher.PublishAsync("IngestionCompleted", new
            {
                path       = destPath,
                media_type = mediaType?.ToString() ?? "Unknown",
                timestamp  = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Event publish failed for auto-organize — continuing");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes (or refreshes) the edition and hub sidecar XML files at the given
    /// folder paths using the full canonical metadata dictionary.
    /// All canonical key-value pairs are written to the &lt;canonical-values&gt;
    /// section so the sidecar is a complete, portable metadata snapshot.
    /// </summary>
    private async Task WriteSidecarsAsync(
        Guid                         assetId,
        string                       contentHash,
        Dictionary<string, string>   metadata,
        MediaType?                   mediaType,
        string                       editionFolder,
        string                       hubFolder,
        CancellationToken            ct)
    {
        var canonicalValues = metadata
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);

        await _sidecar.WriteEditionSidecarAsync(editionFolder, new EditionSidecarData
        {
            Title           = metadata.GetValueOrDefault("title"),
            Author          = metadata.GetValueOrDefault("author"),
            MediaType       = mediaType?.ToString(),
            Isbn            = metadata.GetValueOrDefault("isbn"),
            Asin            = metadata.GetValueOrDefault("asin"),
            ContentHash     = contentHash,
            CoverPath       = "cover.jpg",
            UserLocks       = [],
            CanonicalValues = canonicalValues,
            LastOrganized   = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        // Guard: only write the hub-level sidecar when the hub folder is a title-specific
        // folder, not a grouping folder like an author or category root.
        // Require at least two path separators relative to LibraryRoot so only a
        // title-level or deeper folder qualifies.
        var relHubPath  = Path.GetRelativePath(_options.LibraryRoot, hubFolder);
        int hubDepth    = relHubPath.Count(c => c == Path.DirectorySeparatorChar || c == '/');
        bool hubHasDepth = hubDepth >= 2;

        if (hubHasDepth)
        {
            await _sidecar.WriteHubSidecarAsync(hubFolder, new HubSidecarData
            {
                DisplayName     = metadata.GetValueOrDefault("title", "Unknown"),
                Year            = metadata.GetValueOrDefault("year"),
                WikidataQid     = metadata.GetValueOrDefault("wikidata_qid"),
                Franchise       = metadata.GetValueOrDefault("franchise"),
                CanonicalValues = canonicalValues,
                LastOrganized   = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug(
                "Hub sidecar skipped for {Id}: hubFolder '{HubFolder}' is a category root " +
                "(template too shallow — hub sidecar requires a dedicated hub subfolder).",
                assetId, hubFolder);
        }
    }
}
