using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Promotes a staged media asset from <c>.staging/</c> into the organised library
/// after hydration has improved its metadata confidence above the auto-organize
/// threshold. This is the SOLE path from staging to the Library — files never
/// reach the Library directly from the Watch Folder.
///
/// After promotion: moves companion files (cover.jpg), generates the cinematic
/// hero banner, and publishes the completion event.
/// </summary>
public sealed class AutoOrganizeService : IAutoOrganizeService
{
    private readonly IMediaAssetRepository    _assetRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IFileOrganizer           _organizer;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IReviewQueueRepository   _reviewRepo;
    private readonly IEventPublisher          _publisher;
    private readonly IHeroBannerGenerator     _heroGenerator;
    private readonly IOrganizationGate        _gate;
    private readonly IngestionOptions         _options;
    private readonly ILogger<AutoOrganizeService> _logger;

    public AutoOrganizeService(
        IMediaAssetRepository      assetRepo,
        ICanonicalValueRepository  canonicalRepo,
        IFileOrganizer             organizer,
        ISystemActivityRepository  activityRepo,
        IReviewQueueRepository     reviewRepo,
        IEventPublisher            publisher,
        IHeroBannerGenerator       heroGenerator,
        IOrganizationGate          gate,
        IOptions<IngestionOptions> options,
        ILogger<AutoOrganizeService> logger)
    {
        _assetRepo     = assetRepo;
        _canonicalRepo = canonicalRepo;
        _organizer     = organizer;
        _activityRepo  = activityRepo;
        _reviewRepo    = reviewRepo;
        _publisher     = publisher;
        _heroGenerator = heroGenerator;
        _gate          = gate;
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

        // Determine where the file currently is.
        bool isInStaging = !string.IsNullOrWhiteSpace(_options.StagingPath)
            && asset.FilePathRoot.StartsWith(
                _options.StagingPath, StringComparison.OrdinalIgnoreCase);

        bool alreadyOrganized = !isInStaging
            && asset.FilePathRoot.StartsWith(
                _options.LibraryRoot, StringComparison.OrdinalIgnoreCase);

        if (alreadyOrganized)
        {
            await HandleAlreadyOrganizedAsync(asset, assetId, metadata, mediaType, ct)
                .ConfigureAwait(false);
            return;
        }

        // ── Promote from staging to library ──────────────────────────────

        var synth = new IngestionCandidate
        {
            Path              = asset.FilePathRoot,
            EventType         = FileEventType.Created,
            DetectedAt        = DateTimeOffset.UtcNow,
            ReadyAt           = DateTimeOffset.UtcNow,
            Metadata          = metadata,
            DetectedMediaType = mediaType,
        };

        var template = _options.ResolveTemplate(mediaType?.ToString());
        var relative = _organizer.CalculatePath(synth, template);

        // Evaluate all promotion guards through the centralized gate.
        // AutoOrganizeService runs post-hydration so media type is always resolved;
        // pass mediaTypeNeedsReview: false.
        var gateResult = _gate.Evaluate(
            overallConfidence: 1.0, // post-hydration: confidence gate already passed at ingestion time
            canonicalValues: metadata,
            hasUserLock: false,     // user-lock check is not needed here — gate is used for path/title guards only
            mediaTypeNeedsReview: false,
            resolvedRelativePath: relative);

        if (!gateResult.CanOrganize)
        {
            _logger.LogDebug(
                "Auto-organize blocked for {Id}: {Reason}", assetId, gateResult.BlockReason);
            return;
        }

        var destPath = Path.Combine(_options.LibraryRoot, relative);
        var stagingFolder = Path.GetDirectoryName(asset.FilePathRoot) ?? string.Empty;

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

        string editionFolder = Path.GetDirectoryName(destPath) ?? string.Empty;

        // Move companion files from staging (cover.jpg written during ingestion;
        // hero.jpg may have been generated by the hydration pipeline while the file
        // was still in staging — move it now so it isn't orphaned).
        MoveCompanionFiles(stagingFolder, editionFolder, "cover.jpg", "hero.jpg");

        // Generate cinematic hero banner from cover art.
        await GenerateHeroBannerAsync(assetId, editionFolder, ct).ConfigureAwait(false);

        // Clean empty staging subdirectories left behind.
        if (!string.IsNullOrWhiteSpace(_options.StagingPath))
            CleanEmptyParents(stagingFolder, _options.StagingPath);

        _logger.LogInformation(
            "Promoted asset {Id} from staging to library: {Source} → {Dest}",
            assetId, asset.FilePathRoot, destPath);

        try
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.PathUpdated,
                EntityId   = assetId,
                EntityType = "MediaAsset",
                HubName    = metadata.GetValueOrDefault("title", "Unknown"),
                Detail     = $"Promoted from staging: {Path.GetFileName(asset.FilePathRoot)} → {Path.GetRelativePath(_options.LibraryRoot, destPath)}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Activity log failed for auto-organize — continuing");
        }

        // Auto-resolve any pending review items for this entity — the file passed
        // the organization gate, so review items (LowConfidence, ArtworkUnconfirmed, etc.)
        // created during initial ingestion are now moot.
        try
        {
            var resolved = await _reviewRepo.ResolveAllByEntityAsync(assetId, "system:auto-organize", ct)
                .ConfigureAwait(false);
            if (resolved > 0)
                _logger.LogInformation(
                    "Auto-resolved {Count} review items for {Id} after successful promotion", resolved, assetId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Review auto-resolve failed for {Id} — continuing", assetId);
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
    // Already-organized path (QID updated after hydration)
    // -------------------------------------------------------------------------

    private async Task HandleAlreadyOrganizedAsync(
        Domain.Aggregates.MediaAsset asset, Guid assetId,
        Dictionary<string, string> metadata, MediaType? mediaType,
        CancellationToken ct)
    {
        var checkSynth = new IngestionCandidate
        {
            Path              = asset.FilePathRoot,
            EventType         = FileEventType.Created,
            DetectedAt        = DateTimeOffset.UtcNow,
            ReadyAt           = DateTimeOffset.UtcNow,
            Metadata          = metadata,
            DetectedMediaType = mediaType,
        };
        var checkTemplate = _options.ResolveTemplate(mediaType?.ToString());
        var checkRelative = _organizer.CalculatePath(checkSynth, checkTemplate);
        var newDest = Path.Combine(_options.LibraryRoot, checkRelative);

        if (string.Equals(asset.FilePathRoot, newDest, StringComparison.OrdinalIgnoreCase))
        {
            // Path unchanged — nothing to do.
            _logger.LogDebug(
                "Already organized at {Path} for {Id}",
                asset.FilePathRoot, assetId);
            return;
        }

        // Path changed (QID now available) — move file to new location.
        var oldFolder = Path.GetDirectoryName(asset.FilePathRoot) ?? string.Empty;
        bool relocated = await _organizer.ExecuteMoveAsync(asset.FilePathRoot, newDest, ct)
            .ConfigureAwait(false);

        if (relocated)
        {
            await _assetRepo.UpdateFilePathAsync(assetId, newDest, ct).ConfigureAwait(false);

            var newFolder = Path.GetDirectoryName(newDest) ?? string.Empty;
            MoveCompanionFiles(oldFolder, newFolder, "cover.jpg", "hero.jpg");

            CleanEmptyParents(oldFolder, _options.LibraryRoot);

            _logger.LogInformation(
                "Re-organized asset {Id} after hydration (QID in path): {Old} → {New}",
                assetId, asset.FilePathRoot, newDest);

            try
            {
                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.PathUpdated,
                    EntityId   = assetId,
                    EntityType = mediaType?.ToString() ?? "Unknown",
                    Detail     = $"Re-organized after hydration: {Path.GetFileName(newDest)}",
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to log re-organization activity for {Id}", assetId);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a cinematic hero banner from cover art after promotion to the library.
    /// </summary>
    private async Task GenerateHeroBannerAsync(
        Guid assetId, string editionFolder, CancellationToken ct)
    {
        var coverPath = Path.Combine(editionFolder, "cover.jpg");
        if (!File.Exists(coverPath))
            return;

        try
        {
            var heroResult = await _heroGenerator.GenerateAsync(coverPath, editionFolder, ct)
                                                  .ConfigureAwait(false);

            var heroCanonicals = new List<CanonicalValue>();
            if (!string.IsNullOrEmpty(heroResult.DominantHexColor))
            {
                heroCanonicals.Add(new CanonicalValue
                {
                    EntityId = assetId, Key = "dominant_color",
                    Value = heroResult.DominantHexColor,
                    LastScoredAt = DateTimeOffset.UtcNow,
                });
            }
            heroCanonicals.Add(new CanonicalValue
            {
                EntityId = assetId, Key = "hero",
                Value = $"/stream/{assetId}/hero",
                LastScoredAt = DateTimeOffset.UtcNow,
            });
            await _canonicalRepo.UpsertBatchAsync(heroCanonicals, ct)
                .ConfigureAwait(false);

            try
            {
                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.HeroBannerGenerated,
                    EntityId   = assetId,
                    EntityType = "MediaAsset",
                    ChangesJson = JsonSerializer.Serialize(new
                    {
                        dominant_color = heroResult.DominantHexColor,
                        hero_url       = $"/stream/{assetId}/hero",
                    }),
                    Detail = $"Hero banner generated (dominant color: {heroResult.DominantHexColor ?? "n/a"})",
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Activity log failed for hero banner — continuing");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hero banner generation failed for {Path}", coverPath);
        }
    }

    private static void MoveCompanionFiles(string oldFolder, string newFolder, params string[] fileNames)
    {
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(newFolder);

        foreach (var fileName in fileNames)
        {
            var src = Path.Combine(oldFolder, fileName);
            var dst = Path.Combine(newFolder, fileName);
            if (File.Exists(src) && !File.Exists(dst))
            {
                try { File.Move(src, dst); }
                catch { /* best-effort */ }
            }
        }
    }

    private static void CleanEmptyParents(string folder, string stopAt)
    {
        try
        {
            var dir = new DirectoryInfo(folder);
            while (dir is not null &&
                   dir.Exists &&
                   !string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar),
                       stopAt.TrimEnd(Path.DirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase))
            {
                if (dir.EnumerateFileSystemInfos().Any())
                    break;

                var parent = dir.Parent;
                dir.Delete();
                dir = parent;
            }
        }
        catch { /* best-effort cleanup */ }
    }
}
