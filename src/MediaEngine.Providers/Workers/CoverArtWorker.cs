using System.Security.Cryptography;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
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
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IImageCacheRepository _imageCache;
    private readonly IHeroBannerGenerator _heroGenerator;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ImagePathService? _imagePathService;
    private readonly ICoverArtHashService? _coverArtHash;
    private readonly ILogger<CoverArtWorker> _logger;

    public CoverArtWorker(
        IMediaAssetRepository assetRepo,
        ICanonicalValueRepository canonicalRepo,
        IImageCacheRepository imageCache,
        IHeroBannerGenerator heroGenerator,
        IHttpClientFactory httpFactory,
        ILogger<CoverArtWorker> logger,
        ImagePathService? imagePathService = null,
        ICoverArtHashService? coverArtHash = null)
    {
        _assetRepo = assetRepo;
        _canonicalRepo = canonicalRepo;
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

        // Find cover URL from canonicals
        var coverUrl = canonicals
            .Where(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .FirstOrDefault(v => v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            ?? canonicals
            .Where(c => string.Equals(c.Key, MetadataFieldConstants.Cover, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .FirstOrDefault(v => v.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(coverUrl))
        {
            _logger.LogDebug("No cover URL found for entity {EntityId}", entityId);
            return;
        }

        // Resolve output path
        string coverPath;
        string imageDir;
        if (_imagePathService is not null)
        {
            if (!string.IsNullOrEmpty(wikidataQid))
                _imagePathService.PromoteToQid(entityId, wikidataQid);

            coverPath = _imagePathService.GetWorkCoverPath(wikidataQid, entityId);
            imageDir = _imagePathService.GetWorkImageDir(wikidataQid, entityId);
            ImagePathService.EnsureDirectory(coverPath);
        }
        else
        {
            var asset = await _assetRepo.FindByIdAsync(entityId, ct);
            var fileDir = asset is not null ? Path.GetDirectoryName(asset.FilePathRoot) ?? "." : ".";
            coverPath = Path.Combine(fileDir, "cover.jpg");
            imageDir = fileDir;
        }

        // Skip if cover already exists
        if (File.Exists(coverPath))
        {
            _logger.LogDebug("Cover already exists at {Path} for entity {EntityId}", coverPath, entityId);
            return;
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

        // Persist cover_url canonical as local streaming path
        await _canonicalRepo.UpsertBatchAsync(
        [
            new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.CoverUrl,
                Value = $"/stream/{entityId}/cover",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        ], ct);

        // Generate hero banner
        await GenerateHeroBannerAsync(entityId, coverPath, imageDir, ct);

        // Generate thumbnail
        if (_imagePathService is not null)
        {
            var thumbPath = _imagePathService.GetWorkCoverThumbPath(wikidataQid, entityId);
            GenerateThumbnail(coverPath, thumbPath);
        }

        _logger.LogInformation(
            "Cover art downloaded and processed for entity {EntityId} ({Bytes} bytes)",
            entityId, bytes.Length);
    }

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
            await _canonicalRepo.UpsertBatchAsync(heroCanonicals, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
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
        catch
        {
            // Thumbnail generation is non-critical
        }
    }
}
