using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
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
    private readonly ImagePathService?        _imagePathService;
    private readonly ILibraryFolderResolver?  _libraryResolver;
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
        ILogger<AutoOrganizeService> logger,
        ImagePathService?          imagePathService = null,
        ILibraryFolderResolver?    libraryResolver  = null)
    {
        _assetRepo        = assetRepo;
        _canonicalRepo    = canonicalRepo;
        _organizer        = organizer;
        _activityRepo     = activityRepo;
        _reviewRepo       = reviewRepo;
        _publisher        = publisher;
        _heroGenerator    = heroGenerator;
        _gate             = gate;
        _options          = options.Value;
        _imagePathService = imagePathService;
        _libraryResolver  = libraryResolver;
        _logger           = logger;
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

        // ── Per-library ReadOnly gate ──────────────────────────────────────
        // Side-by-side-with-Plex plan §C/§I. When the file belongs to a
        // library marked ReadOnly (the user's "Plex owns this tree" opt-out),
        // we never move, rename, or tag it — we index in place and return.
        var owningLibrary = _libraryResolver?.ResolveForPath(asset.FilePathRoot);
        if (owningLibrary is { ReadOnly: true })
        {
            _logger.LogDebug(
                "Auto-organize skipped for {Id}: owning library is ReadOnly ({Path})",
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

        // ── Blocking review check ──────────────────────────────────────────
        // ANY pending review item blocks promotion. The file stays in staging
        // until the user resolves all review items.
        var pendingReviews = await _reviewRepo.GetPendingByEntityAsync(assetId, ct)
            .ConfigureAwait(false);

        var blockingReviews = pendingReviews
            .Where(r => !string.Equals(r.Trigger, nameof(ReviewTrigger.WritebackFailed), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (blockingReviews.Any())
        {
            _logger.LogInformation(
                "Auto-organize blocked for {Id}: pending review '{Trigger}' must be resolved first",
                assetId, blockingReviews[0].Trigger);
            return;
        }

        // ── QID gate — nothing enters the library without a confirmed identity ──
        // For per-track / per-episode media (music tracks, TV episodes), Wikidata
        // does not assign individual QIDs — the parent album / show / series QID
        // (emitted as series_qid by ResolveMusicAlbumAsync and the TV resolver) is
        // the asset's confirmed identity. Accept any of wikidata_qid / series_qid /
        // edition_qid as proof of identification.
        static bool IsRealQid(string? v) =>
            !string.IsNullOrWhiteSpace(v)
            && !v.StartsWith("NF", StringComparison.OrdinalIgnoreCase);

        // series_qid values are stored as "Qxxx::Label" — strip the label suffix.
        static string? StripLabel(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null
            : (v.IndexOf("::", StringComparison.Ordinal) is var i && i > 0 ? v[..i] : v);

        metadata.TryGetValue("wikidata_qid", out var qidVal);
        metadata.TryGetValue("series_qid", out var seriesQidVal);
        metadata.TryGetValue("edition_qid", out var editionQidVal);

        var hasQid = IsRealQid(qidVal)
            || IsRealQid(StripLabel(seriesQidVal))
            || IsRealQid(StripLabel(editionQidVal));

        if (!hasQid)
        {
            _logger.LogInformation(
                "Auto-organize blocked for {Id}: no confirmed Wikidata QID — file stays in staging",
                assetId);
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

        // Move cover/hero companion files from staging to the library edition folder.
        // CoverArtWorker writes poster.jpg next to the media file (even in staging) via
        // ImagePathService.GetMediaFilePosterPath, so companion files must always be moved
        // on promotion — regardless of whether ImagePathService is active.
        MoveCompanionFiles(stagingFolder, editionFolder,
            "poster.jpg", "poster-thumb.jpg", "cover.jpg", "hero.jpg");

        // Generate cinematic hero banner from cover art.
        await GenerateHeroBannerAsync(assetId, editionFolder, ct).ConfigureAwait(false);

        // Clean up any .tuvima.bak files left behind by metadata taggers.
        // These backups are normally deleted on success, but can persist when the
        // tagger fails partway through (e.g. M4B cover art write on a locked file).
        CleanStagingBakFiles(stagingFolder);

        // Clean empty staging subdirectories left behind.
        if (!string.IsNullOrWhiteSpace(_options.StagingPath))
            CleanEmptyParents(stagingFolder, _options.StagingPath);

        _logger.LogInformation(
            "Promoted asset {Id} from staging to library: {Source} → {Dest}",
            assetId, asset.FilePathRoot, destPath);

        // History: promoted to library.
        try
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = "Promoted",
                EntityId = assetId,
                Detail = "Promoted to library",
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to log item history (Promoted) for {Id}", assetId); }

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

        // All pending review items were cleared before reaching this point
        // (any pending review blocks promotion). Resolve any that may have
        // been created concurrently during promotion.
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
            await _publisher.PublishAsync(SignalREvents.IngestionCompleted, new
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
            MoveCompanionFiles(oldFolder, newFolder,
                "poster.jpg", "poster-thumb.jpg", "cover.jpg", "hero.jpg");

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
    /// When ImagePathService is active, reads cover from .images/ and writes hero there too.
    /// </summary>
    private async Task GenerateHeroBannerAsync(
        Guid assetId, string editionFolder, CancellationToken ct)
    {
        // Resolve paths using .images/ when available, else use legacy edition folder.
        string coverPath;
        string heroOutputDir;
        if (_imagePathService is not null)
        {
            // Look up QID from canonical values for correct .images/ sub-directory.
            var canonicals = await _canonicalRepo.GetByEntityAsync(assetId, ct).ConfigureAwait(false);
            var wikidataQid = canonicals
                .FirstOrDefault(c => c.Key is "wikidata_qid"
                    && !string.IsNullOrEmpty(c.Value)
                    && !c.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            coverPath    = _imagePathService.GetWorkCoverPath(wikidataQid, assetId);
            heroOutputDir = _imagePathService.GetWorkImageDir(wikidataQid, assetId);

            // Fallback: if .images/ cover doesn't exist, try legacy paths.
            // CoverArtWorker writes poster.jpg (via GetMediaFilePosterPath), not cover.jpg.
            if (!File.Exists(coverPath))
                coverPath = Path.Combine(editionFolder, "cover.jpg");
            if (!File.Exists(coverPath))
                coverPath = Path.Combine(editionFolder, "poster.jpg");
        }
        else
        {
            coverPath    = Path.Combine(editionFolder, "cover.jpg");
            heroOutputDir = editionFolder;
        }

        if (!File.Exists(coverPath))
            return;

        try
        {
            var heroResult = await _heroGenerator.GenerateAsync(coverPath, heroOutputDir, ct)
                                                  .ConfigureAwait(false);

            var heroCanonicals = new List<CanonicalValue>();

            // Ensure cover_url canonical is set — it may be missing if the file
            // lacked embedded cover art during ingestion but cover.jpg was later
            // created by a provider download during hydration in staging.
            heroCanonicals.Add(new CanonicalValue
            {
                EntityId = assetId, Key = "cover_url",
                Value = $"/stream/{assetId}/cover",
                LastScoredAt = DateTimeOffset.UtcNow,
            });

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

    private static void CleanStagingBakFiles(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        try
        {
            foreach (var bakFile in Directory.EnumerateFiles(folder, "*.tuvima.bak"))
            {
                try { File.Delete(bakFile); }
                catch { /* best-effort cleanup */ }
            }
        }
        catch { /* best-effort cleanup */ }
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
