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
    public async Task TryAutoOrganizeAsync(Guid assetId, CancellationToken ct = default)
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

        // Only organize files that are currently in staging or the watch folder —
        // not files already in the library.
        if (asset.FilePathRoot.StartsWith(
                _options.LibraryRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Auto-organize skipped for {Id}: already in library at {Path}",
                assetId, asset.FilePathRoot);
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

        await _sidecar.WriteEditionSidecarAsync(editionFolder, new EditionSidecarData
        {
            Title         = metadata.GetValueOrDefault("title"),
            Author        = metadata.GetValueOrDefault("author"),
            MediaType     = mediaType?.ToString(),
            Isbn          = metadata.GetValueOrDefault("isbn"),
            Asin          = metadata.GetValueOrDefault("asin"),
            ContentHash   = asset.ContentHash,
            CoverPath     = "cover.jpg",
            UserLocks     = [],
            LastOrganized = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        // Guard: only write the hub-level sidecar when the hub folder is at least one level
        // below a category folder (i.e., the relative path contains a separator).
        // With flat templates like "{Category}/{Author}/{Title}{Ext}", two calls to
        // GetDirectoryName land on the category root (e.g., "Books/") rather than a real
        // hub subfolder — writing library.xml there would create a stray file.
        var relHubPath = Path.GetRelativePath(_options.LibraryRoot, hubFolder);
        bool hubHasDepth = relHubPath.Contains(Path.DirectorySeparatorChar)
                        || relHubPath.Contains('/');

        if (hubHasDepth)
        {
            await _sidecar.WriteHubSidecarAsync(hubFolder, new HubSidecarData
            {
                DisplayName   = metadata.GetValueOrDefault("title", "Unknown"),
                Year          = metadata.GetValueOrDefault("year"),
                WikidataQid   = metadata.GetValueOrDefault("wikidata_qid"),
                Franchise     = metadata.GetValueOrDefault("franchise"),
                LastOrganized = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug(
                "Hub sidecar skipped for {Id}: hubFolder '{HubFolder}' is a category root " +
                "(template too shallow — hub sidecar requires a dedicated hub subfolder).",
                assetId, hubFolder);
        }

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
}
