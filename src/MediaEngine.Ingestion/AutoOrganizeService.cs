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

        // If the file is already in the library, check whether the path needs
        // updating (e.g., QID now available after hydration).  If the new path
        // matches the current one, just refresh the sidecar.
        bool isInOrphanage = !string.IsNullOrWhiteSpace(_options.OrphanagePath)
            && asset.FilePathRoot.StartsWith(
                _options.OrphanagePath, StringComparison.OrdinalIgnoreCase);

        bool alreadyOrganized = !isInOrphanage
            && asset.FilePathRoot.StartsWith(
                _options.LibraryRoot, StringComparison.OrdinalIgnoreCase);

        if (alreadyOrganized)
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

                // Move companion files (cover.jpg, hero.jpg, library.xml) to new folder.
                var newFolder = Path.GetDirectoryName(newDest) ?? string.Empty;
                MoveCompanionFiles(oldFolder, newFolder, "cover.jpg", "hero.jpg", "library.xml");

                await WriteEditionSidecarAsync(asset.ContentHash, metadata, mediaType,
                    newFolder, ct).ConfigureAwait(false);

                // Clean up empty parent directories left behind.
                CleanEmptyParents(oldFolder, _options.LibraryRoot);

                _logger.LogInformation(
                    "Re-organized asset {Id} after hydration (QID in path): {Old} → {New}",
                    assetId, asset.FilePathRoot, newDest);

                try
                {
                    await _activityRepo.LogAsync(new Domain.Entities.SystemActivityEntry
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

        var template = _options.ResolveTemplate(mediaType?.ToString());
        var relative = _organizer.CalculatePath(synth, template);

        // Block organization into "Other" category.
        if (relative.StartsWith("Other", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Auto-organize blocked for {Id}: resolved category is 'Other'", assetId);
            return;
        }

        // Placeholder title guard: block organization when title is a
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

        // Write edition-level sidecar XML.
        string editionFolder = Path.GetDirectoryName(destPath) ?? string.Empty;

        await WriteEditionSidecarAsync(asset.ContentHash, metadata, mediaType,
            editionFolder, ct).ConfigureAwait(false);

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
    /// Writes (or refreshes) the edition-level sidecar XML file at the given
    /// folder path using the full canonical metadata dictionary.
    /// All canonical key-value pairs are written to the &lt;canonical-values&gt;
    /// section so the sidecar is a complete, portable metadata snapshot.
    /// Hub-level sidecars are not written — hubs are reconstructed from
    /// edition sidecars during Great Inhale.
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

        // Extract QIDs from canonical values for v2.0 sidecar.
        var wikidataQid = metadata.GetValueOrDefault("wikidata_qid");
        var authorQid   = metadata.GetValueOrDefault("author_qid");

        // Build multi-valued canonical entries from |||-separated values.
        var multiValued = BuildMultiValuedCanonicals(metadata);

        await _sidecar.WriteEditionSidecarAsync(editionFolder, new EditionSidecarData
        {
            Title                 = metadata.GetValueOrDefault("title"),
            TitleQid              = wikidataQid,  // title QID = work QID
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
    /// Identifies <c>|||</c>-separated canonical values and decomposes them into
    /// <see cref="MultiValuedCanonical"/> entries for the v2.0 sidecar format.
    /// Pairs with matching <c>_qid</c> canonical values when available.
    /// </summary>
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

    /// <summary>
    /// Moves companion files (cover.jpg, hero.jpg, library.xml) from the old
    /// edition folder to the new one after a re-organization move.
    /// </summary>
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

    /// <summary>
    /// Recursively deletes empty parent directories up to (but not including)
    /// the library root.
    /// </summary>
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
                    break; // Not empty — stop climbing.

                var parent = dir.Parent;
                dir.Delete();
                dir = parent;
            }
        }
        catch { /* best-effort cleanup */ }
    }
}
