using System.Security.Cryptography;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Downloads cover art from provider URLs, computes perceptual hashes for dedup,
/// generates 200px thumbnails and cinematic hero banners.
///
/// Extracted from <c>HydrationPipelineService.PersistCoverFromUrlAsync</c>.
/// </summary>
public sealed class CoverArtWorker
{
    private readonly IMediaAssetRepository _assetRepo;
    private readonly IEntityAssetRepository? _entityAssetRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IWorkRepository _workRepo;
    private readonly IImageCacheRepository _imageCache;
    private readonly IHeroBannerGenerator _heroGenerator;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ImagePathService? _imagePathService;
    private readonly ICoverArtHashService? _coverArtHash;
    private readonly ILogger<CoverArtWorker> _logger;

    public CoverArtWorker(
        IMediaAssetRepository assetRepo,
        ICanonicalValueRepository canonicalRepo,
        IWorkRepository workRepo,
        IImageCacheRepository imageCache,
        IHeroBannerGenerator heroGenerator,
        IHttpClientFactory httpFactory,
        ILogger<CoverArtWorker> logger,
        ImagePathService? imagePathService = null,
        ICoverArtHashService? coverArtHash = null,
        IEntityAssetRepository? entityAssetRepo = null)
    {
        _assetRepo = assetRepo;
        _entityAssetRepo = entityAssetRepo;
        _canonicalRepo = canonicalRepo;
        _workRepo = workRepo;
        _imageCache = imageCache;
        _heroGenerator = heroGenerator;
        _httpFactory = httpFactory;
        _logger = logger;
        _imagePathService = imagePathService;
        _coverArtHash = coverArtHash;
    }

    /// <summary>
    /// Downloads cover art from the provider URL stored in canonicals,
    /// generates thumbnail and hero banner.
    /// </summary>
    public async Task DownloadAndPersistAsync(Guid entityId, string? wikidataQid, CancellationToken ct)
    {
        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);
        var lineage = await _workRepo.GetLineageByAssetAsync(entityId, ct);

        // Find cover URL from the asset's own canonicals.
        var coverUrl = FindCoverUrl(canonicals);

        // Parent-scope fallback. For TV / Movies / Music / Comics,
        // ClaimScopeRegistry routes `cover` and `cover_url` to ClaimScope.Parent,
        // so the retail provider's cover URL lands on the parent Work id
        // (album / show / movie Work) — not the media asset id. Without this
        // fallback, every one of those media types silently returns "No cover
        // URL found" and the Vault shows a placeholder.
        if (string.IsNullOrEmpty(coverUrl))
        {
            try
            {
                if (lineage is not null && lineage.RootParentWorkId != entityId)
                {
                    var parentCanonicals = await _canonicalRepo.GetByEntityAsync(
                        lineage.RootParentWorkId, ct);
                    coverUrl = FindCoverUrl(parentCanonicals);
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        _logger.LogDebug(
                            "Cover art: asset {EntityId} had no cover canonical; using parent Work {ParentId} cover URL",
                            entityId, lineage.RootParentWorkId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex,
                    "Cover art: parent lineage lookup failed for entity {EntityId} — proceeding with self-scope only",
                    entityId);
            }
        }

        if (string.IsNullOrEmpty(coverUrl))
        {
            await _canonicalRepo.UpsertBatchAsync(
                ArtworkCanonicalHelper.CreateFlags(
                    entityId,
                    coverState: "missing",
                    coverSource: "none",
                    heroState: "missing",
                    lastScoredAt: DateTimeOffset.UtcNow,
                    settled: true),
                ct);
            _logger.LogDebug("No cover URL found for entity {EntityId}", entityId);
            return;
        }

        // Resolve output path.
        //
        // Side-by-side-with-Plex plan §D — prefer the per-file location next
        // to the media file (Plex/Jellyfin convention) so the same disk path
        // serves Tuvima, Plex, and Jellyfin without duplication. Fall back to
        // the legacy QID/asset-id-keyed location under .data/images/ when
        // the asset has no resolvable file path (e.g. pre-organize states or
        // entities that aren't backed by a file).
        string coverPath;
        string imageDir;

        // Look up the asset's current file path. The lookup is cheap (single
        // row by id) and is the gate that decides "per-file" vs "legacy".
        var assetForPath = await _assetRepo.FindByIdAsync(entityId, ct);
        var assetFilePath = assetForPath?.FilePathRoot;
        bool hasFilePath = !string.IsNullOrWhiteSpace(assetFilePath) && File.Exists(assetFilePath);
        var ownerEntityId = lineage?.TargetForParentScope ?? entityId;
        var ownerFolderPath = ResolveCoverFolderPath(lineage?.MediaType, assetFilePath);

        if (hasFilePath && !string.IsNullOrWhiteSpace(ownerFolderPath))
        {
            coverPath = Path.Combine(ownerFolderPath!, "poster.jpg");
            imageDir  = Path.GetDirectoryName(coverPath) ?? ".";
            ImagePathService.EnsureDirectory(coverPath);
        }
        else if (_imagePathService is not null)
        {
            if (!string.IsNullOrEmpty(wikidataQid))
                _imagePathService.PromoteToQid(ownerEntityId, wikidataQid);

            coverPath = _imagePathService.GetWorkCoverPath(wikidataQid, ownerEntityId);
            imageDir  = _imagePathService.GetWorkImageDir(wikidataQid, ownerEntityId);
            ImagePathService.EnsureDirectory(coverPath);
        }
        else
        {
            // Last-ditch fallback: write next to whatever path the asset
            // exposes, even if the file no longer exists on disk.
            var fileDir = Path.GetDirectoryName(assetFilePath) ?? ".";
            coverPath = Path.Combine(fileDir, "cover.jpg");
            imageDir  = fileDir;
        }

        // Skip if cover already exists
        if (File.Exists(coverPath))
        {
            var existingVariant = await EnsureCoverVariantAsync(
                ownerEntityId,
                coverUrl,
                coverPath,
                InferCoverSource(canonicals, coverUrl),
                ct);
            await _canonicalRepo.UpsertBatchAsync(
                BuildCoverCanonicals(
                    ownerEntityId,
                    existingVariant?.Id,
                    InferCoverSource(canonicals, coverUrl),
                    heroState: "pending"),
                ct);
            await GenerateHeroBannerAsync(ownerEntityId, coverPath, imageDir, ct);

            var titleForSkip = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value
                ?? $"entity {entityId}";
            _logger.LogInformation(
                "Cover art: skipped '{Title}' — already cached on disk ({Path})",
                titleForSkip, coverPath);
            return;
        }

        {
            var titleForDownload = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value
                ?? $"entity {entityId}";
            _logger.LogInformation(
                "Cover art: downloading '{Title}' from {Url} → {LocalPath}",
                titleForDownload, coverUrl, coverPath);
        }

        // Capture embedded cover pHash before downloading new one
        ulong? embeddedPhash = null;
        if (_coverArtHash is not null && File.Exists(coverPath))
        {
            var existingBytes = await File.ReadAllBytesAsync(coverPath, ct);
            var existingHash = Convert.ToHexStringLower(SHA256.HashData(existingBytes));
            embeddedPhash = await _imageCache.GetPerceptualHashAsync(existingHash, ct);
        }

        // Download provider image
        byte[] bytes;
        try
        {
            using var client = _httpFactory.CreateClient("cover_download");
            bytes = await client.GetByteArrayAsync(coverUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to download cover from {Url} for entity {EntityId}", coverUrl, entityId);
            return;
        }

        if (bytes.Length == 0) return;

        // Content-hash dedup
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var cached = await _imageCache.FindByHashAsync(hash, ct);
        if (cached is not null && File.Exists(cached))
        {
            File.Copy(cached, coverPath, overwrite: true);
        }
        else
        {
            await File.WriteAllBytesAsync(coverPath, bytes, ct);
            await _imageCache.InsertAsync(hash, coverPath, coverUrl, ct);
        }

        // Compute perceptual hash
        if (_coverArtHash is not null && bytes.Length > 100)
        {
            var phash = await _coverArtHash.ComputeHashAsync(bytes, ct);
            if (phash.HasValue)
            {
                await _imageCache.SetPerceptualHashAsync(hash, phash.Value, ct);

                if (embeddedPhash.HasValue)
                {
                    var similarity = _coverArtHash.ComputeSimilarity(embeddedPhash.Value, phash.Value);
                    _logger.LogDebug(
                        "Cover pHash similarity for entity {EntityId}: {Similarity:F2}",
                        entityId, similarity);
                }
            }
        }

        // Generate hero banner
        await GenerateHeroBannerAsync(ownerEntityId, coverPath, imageDir, ct);

        // Generate thumbnail. When we wrote the cover next to the media file,
        // the thumbnail belongs next to it as well so the Dashboard's thumb
        // route can find it without a separate cache lookup.
        if (hasFilePath && !string.IsNullOrWhiteSpace(ownerFolderPath))
        {
            var thumbPath = Path.Combine(ownerFolderPath!, "poster-thumb.jpg");
            GenerateThumbnail(coverPath, thumbPath);
        }
        else if (_imagePathService is not null)
        {
            var thumbPath = _imagePathService.GetWorkCoverThumbPath(wikidataQid, ownerEntityId);
            GenerateThumbnail(coverPath, thumbPath);
        }

        var coverVariant = await EnsureCoverVariantAsync(ownerEntityId, coverUrl, coverPath, "provider", ct);
        await _canonicalRepo.UpsertBatchAsync(
            BuildCoverCanonicals(
                ownerEntityId,
                coverVariant?.Id,
                "provider",
                heroState: "pending"),
            ct);

        {
            var titleForDone = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value
                ?? $"entity {entityId}";
            // NOTE: We deliberately do NOT write a cover_url canonical pointing at
            // /stream/{entityId}/cover here. The /stream URL is a display hint that the
            // Dashboard layer can build on the fly from the entity id; it is not
            // persisted state. The authoritative source URL (the provider URL) lives in
            // the original claims, and the disk location is owned by ImagePathService.
            _logger.LogInformation(
                "Cover art: downloaded poster for '{Title}' ({SizeKB:F1} KB) → {LocalPath}",
                titleForDone, bytes.Length / 1024.0, coverPath);
        }
    }

    /// <summary>
    /// Finds the first HTTP(S) cover URL in a list of canonical values,
    /// checking <c>cover</c> (provider poster) first and falling back to <c>cover_url</c>.
    /// </summary>
    private static string? FindCoverUrl(IReadOnlyList<CanonicalValue> canonicals)
    {
        // Prefer the provider poster URL (cover) over local streaming URLs (cover_url).
        // The "cover" claim comes from retail providers (TMDB, Apple, ComicVine) and
        // contains the actual poster artwork. The "cover_url" claim may contain a
        // /stream/ URL (not downloadable) or a stale supplementary image.
        return canonicals
            .Where(c => string.Equals(c.Key, MetadataFieldConstants.Cover, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v) && v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            ?? canonicals
            .Where(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v) && v.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    private static string InferCoverSource(IReadOnlyList<CanonicalValue> canonicals, string? coverUrl)
    {
        var existing = canonicals.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.CoverSource, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        if (!string.IsNullOrWhiteSpace(coverUrl) && coverUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return "provider";

        return "existing";
    }

    private static string? ResolveCoverFolderPath(MediaType? mediaType, string? mediaFilePath) =>
        mediaType switch
        {
            MediaType.TV => GetSeriesFolder(mediaFilePath),
            MediaType.Music => GetContainerFolder(mediaFilePath),
            _ => GetContainerFolder(mediaFilePath),
        };

    private static string? GetSeriesFolder(string? mediaFilePath)
    {
        var seasonFolder = GetContainerFolder(mediaFilePath);
        return string.IsNullOrWhiteSpace(seasonFolder)
            ? null
            : Path.GetDirectoryName(seasonFolder);
    }

    private static string? GetContainerFolder(string? mediaFilePath) =>
        string.IsNullOrWhiteSpace(mediaFilePath)
            ? null
            : Path.GetDirectoryName(mediaFilePath);

    private async Task GenerateHeroBannerAsync(
        Guid entityId, string coverPath, string outputDir, CancellationToken ct)
    {
        try
        {
            var heroResult = await _heroGenerator.GenerateAsync(coverPath, outputDir, ct);

            var heroCanonicals = new List<CanonicalValue>();
            if (!string.IsNullOrEmpty(heroResult.DominantHexColor))
            {
                heroCanonicals.Add(new CanonicalValue
                {
                    EntityId = entityId,
                    Key = "dominant_color",
                    Value = heroResult.DominantHexColor,
                    LastScoredAt = DateTimeOffset.UtcNow,
                });
            }
            heroCanonicals.Add(new CanonicalValue
            {
                EntityId = entityId,
                Key = "hero",
                Value = $"/stream/{entityId}/hero",
                LastScoredAt = DateTimeOffset.UtcNow,
            });
            heroCanonicals.AddRange(ArtworkCanonicalHelper.CreateFlags(
                entityId,
                coverState: "present",
                coverSource: null,
                heroState: "present",
                lastScoredAt: DateTimeOffset.UtcNow,
                settled: true));
            await _canonicalRepo.UpsertBatchAsync(heroCanonicals, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _canonicalRepo.UpsertBatchAsync(
                ArtworkCanonicalHelper.CreateFlags(
                    entityId,
                    coverState: "present",
                    coverSource: null,
                    heroState: "missing",
                    lastScoredAt: DateTimeOffset.UtcNow,
                    settled: true),
                ct);
            _logger.LogWarning(ex, "Hero banner generation failed for entity {EntityId}", entityId);
        }
    }

    private static void GenerateThumbnail(string coverPath, string thumbPath)
    {
        try
        {
            using var inputStream = File.OpenRead(coverPath);
            using var bitmap = SKBitmap.Decode(inputStream);
            if (bitmap is null) return;

            var targetWidth = 200;
            var targetHeight = (int)(bitmap.Height * (200.0 / bitmap.Width));
            using var resized = bitmap.Resize(
                new SKImageInfo(targetWidth, targetHeight),
                new SKSamplingOptions(SKFilterMode.Linear));
            if (resized is null) return;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 75);

            ImagePathService.EnsureDirectory(thumbPath);
            using var output = File.OpenWrite(thumbPath);
            data.SaveTo(output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-critical: thumbnail generation failure doesn't affect the main cover art.
            _ = ex;
        }
    }

    private async Task<EntityAsset?> EnsureCoverVariantAsync(
        Guid entityId,
        string? providerUrl,
        string coverPath,
        string sourceProvider,
        CancellationToken ct)
    {
        if (_entityAssetRepo is null)
            return null;

        var existing = (await _entityAssetRepo.GetByEntityAsync(entityId.ToString(), "CoverArt", ct))
            .FirstOrDefault(asset =>
                (!string.IsNullOrWhiteSpace(providerUrl)
                 && string.Equals(asset.ImageUrl, providerUrl, StringComparison.OrdinalIgnoreCase))
                || string.Equals(asset.LocalImagePath, coverPath, StringComparison.OrdinalIgnoreCase));

        var variant = existing ?? new EntityAsset
        {
            Id             = Guid.NewGuid(),
            EntityId       = entityId.ToString(),
            EntityType     = "Work",
            AssetTypeValue = "CoverArt",
            CreatedAt      = DateTimeOffset.UtcNow,
        };

        variant.ImageUrl = providerUrl;
        variant.LocalImagePath = coverPath;
        variant.SourceProvider = sourceProvider;
        variant.IsPreferred = true;
        variant.IsUserOverride = false;

        await _entityAssetRepo.UpsertAsync(variant, ct);
        await _entityAssetRepo.SetPreferredAsync(variant.Id, ct);
        return variant;
    }

    private static IReadOnlyList<CanonicalValue> BuildCoverCanonicals(
        Guid entityId,
        Guid? preferredVariantId,
        string? coverSource,
        string heroState)
    {
        var values = new List<CanonicalValue>();

        if (preferredVariantId.HasValue)
        {
            values.Add(new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.CoverUrl,
                Value = $"/stream/artwork/{preferredVariantId.Value}",
                LastScoredAt = DateTimeOffset.UtcNow,
            });
        }

        values.AddRange(ArtworkCanonicalHelper.CreateFlags(
            entityId,
            coverState: "present",
            coverSource: coverSource,
            heroState: heroState,
            lastScoredAt: DateTimeOffset.UtcNow,
            settled: true));

        return values;
    }
}
