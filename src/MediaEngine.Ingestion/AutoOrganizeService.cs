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
/// After promotion: writes the edition sidecar, moves companion files (cover.jpg),
/// generates the cinematic hero banner, and publishes the completion event.
/// </summary>
public sealed class AutoOrganizeService : IAutoOrganizeService
{
    private readonly IMediaAssetRepository    _assetRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IFileOrganizer           _organizer;
    private readonly ISidecarWriter           _sidecar;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IEventPublisher          _publisher;
    private readonly IHeroBannerGenerator     _heroGenerator;
    private readonly IngestionOptions         _options;
    private readonly ILogger<AutoOrganizeService> _logger;

    public AutoOrganizeService(
        IMediaAssetRepository      assetRepo,
        ICanonicalValueRepository  canonicalRepo,
        IFileOrganizer             organizer,
        ISidecarWriter             sidecar,
        ISystemActivityRepository  activityRepo,
        IEventPublisher            publisher,
        IHeroBannerGenerator       heroGenerator,
        IOptions<IngestionOptions> options,
        ILogger<AutoOrganizeService> logger)
    {
        _assetRepo     = assetRepo;
        _canonicalRepo = canonicalRepo;
        _organizer     = organizer;
        _sidecar       = sidecar;
        _activityRepo  = activityRepo;
        _publisher     = publisher;
        _heroGenerator = heroGenerator;
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

        // Block promotion into "Other" category.
        if (relative.StartsWith("Other", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Auto-organize blocked for {Id}: resolved category is 'Other'", assetId);
            return;
        }

        // Placeholder title guard: block promotion when title is a
        // well-known placeholder and no bridge ID confirms identity.
        string? orgTitle = metadata.GetValueOrDefault("title");
        if (MetadataGuards.IsPlaceholderTitle(orgTitle) && !MetadataGuards.HasBridgeId(metadata))
        {
            _logger.LogWarning(
                "Auto-organize blocked for {Id}: placeholder title \"{Title}\" with no bridge IDs",
                assetId, orgTitle ?? "(blank)");
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

        // Move companion files from staging (cover.jpg written during ingestion).
        MoveCompanionFiles(stagingFolder, editionFolder, "cover.jpg");

        // Write edition-level sidecar XML now that the file is in the Library.
        await WriteEditionSidecarAsync(asset.ContentHash, metadata, mediaType,
            editionFolder, ct).ConfigureAwait(false);

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
            // Path unchanged — just refresh sidecar.
            string existingEditionFolder = Path.GetDirectoryName(asset.FilePathRoot) ?? string.Empty;
            await WriteEditionSidecarAsync(asset.ContentHash, metadata, mediaType,
                existingEditionFolder, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Sidecar refreshed for {Id} (already organized at {Path})",
                assetId, asset.FilePathRoot);
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
            MoveCompanionFiles(oldFolder, newFolder, "cover.jpg", "hero.jpg", "library.xml");

            await WriteEditionSidecarAsync(asset.ContentHash, metadata, mediaType,
                newFolder, ct).ConfigureAwait(false);

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
    /// Writes (or refreshes) the edition-level sidecar XML file at the given
    /// folder path using the full canonical metadata dictionary.
    /// </summary>
    private async Task WriteEditionSidecarAsync(
        string                       contentHash,
        Dictionary<string, string>   metadata,
        MediaType?                   mediaType,
        string                       editionFolder,
        CancellationToken            ct)
    {
        var canonicalValues = metadata
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);

        var wikidataQid = metadata.GetValueOrDefault("wikidata_qid");
        var authorQid   = metadata.GetValueOrDefault("author_qid");
        var multiValued = BuildMultiValuedCanonicals(metadata);

        await _sidecar.WriteEditionSidecarAsync(editionFolder, new EditionSidecarData
        {
            Title                 = metadata.GetValueOrDefault("title"),
            TitleQid              = wikidataQid,
            Author                = metadata.GetValueOrDefault("author"),
            AuthorQid             = authorQid,
            MediaType             = mediaType?.ToString(),
            Isbn                  = metadata.GetValueOrDefault("isbn"),
            Asin                  = metadata.GetValueOrDefault("asin"),
            WikidataQid           = wikidataQid,
            ContentHash           = contentHash,
            CoverPath             = "cover.jpg",
            UserLocks             = [],
            CanonicalValues       = canonicalValues,
            MultiValuedCanonicals = multiValued,
            LastOrganized         = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);
    }

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

    private static IReadOnlyDictionary<string, MultiValuedCanonical> BuildMultiValuedCanonicals(
        Dictionary<string, string> metadata)
    {
        var result = new Dictionary<string, MultiValuedCanonical>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in metadata)
        {
            if (!kv.Value.Contains("|||", StringComparison.Ordinal))
                continue;

            var values = kv.Value.Split("|||",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var qids = Array.Empty<string>();
            if (metadata.TryGetValue(kv.Key + "_qid", out var qidValue) &&
                qidValue.Contains("|||", StringComparison.Ordinal))
            {
                qids = qidValue.Split("|||",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            result[kv.Key] = new MultiValuedCanonical { Values = values, Qids = qids };
        }

        return result;
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
