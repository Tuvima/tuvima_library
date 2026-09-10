using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Helpers;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Downloads TMDB movie and television artwork into the managed central asset
/// store. Retail matching owns first-poster and episode-still acquisition;
/// this stage adds ranked backdrops, title logos, poster variants, and season art.
/// </summary>
public sealed class ImageEnrichmentService : IImageEnrichmentService
{
    private const string TmdbProviderName = "tmdb";
    private const string TmdbApiBaseUrl = "https://api.themoviedb.org/3";
    private const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p/original";
    private const int MaxVariantsPerAssetType = 3;

    private readonly IEntityAssetRepository _assetRepo;
    private readonly IMediaAssetRepository _mediaAssetRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IWorkRepository _workRepo;
    private readonly IProviderConfigurationRepository _providerConfigRepo;
    private readonly IConfigurationLoader _configLoader;
    private readonly IImageCacheRepository _imageCache;
    private readonly AssetPathService _assetPaths;
    private readonly IAssetExportService? _assetExportService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ImageDownloadCoordinator _imageDownloadCoordinator;
    private readonly ILogger<ImageEnrichmentService> _logger;

    // Keep the established constructor shape so composition and existing callers stay stable.
    public ImageEnrichmentService(
        IEntityAssetRepository assetRepo, IMediaAssetRepository mediaAssetRepo,
        ICharacterPortraitRepository portraitRepo, ICanonicalValueRepository canonicalRepo,
        IWorkRepository workRepo, IFictionalEntityRepository entityRepo, IPersonRepository personRepo,
        IProviderConfigurationRepository providerConfigRepo, IConfigurationLoader configLoader,
        IImageCacheRepository imageCache, AssetPathService assetPaths, IAssetExportService? assetExportService,
        IHttpClientFactory httpFactory, IFuzzyMatchingService fuzzy, ILogger<ImageEnrichmentService> logger,
        ImageDownloadCoordinator? imageDownloadCoordinator = null)
    {
        _assetRepo = assetRepo;
        _mediaAssetRepo = mediaAssetRepo;
        _canonicalRepo = canonicalRepo;
        _workRepo = workRepo;
        _providerConfigRepo = providerConfigRepo;
        _configLoader = configLoader;
        _imageCache = imageCache;
        _assetPaths = assetPaths;
        _assetExportService = assetExportService;
        _httpFactory = httpFactory;
        _imageDownloadCoordinator = imageDownloadCoordinator ?? ImageDownloadCoordinator.Shared;
        _logger = logger;
    }

    public async Task<ImageEnrichmentResult> EnrichWorkImagesAsync(Guid assetId, string? workQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var checkedAt = DateTimeOffset.UtcNow;
        var context = await ResolveContextAsync(assetId, ct).ConfigureAwait(false);
        var mediaType = context.MediaType.ToString();
        if (context.MediaType is not (MediaType.Movies or MediaType.TV))
            return await PersistDiagnosticsAsync(context, CreateResult("Skipped", checkedAt, mediaType,
                skippedReason: "unsupported_media_type", message: "TMDB artwork enrichment supports movies and TV only."), ct);

        var canonicals = await LoadCanonicalsAsync(context, ct).ConfigureAwait(false);
        var tmdbId = GetValue(canonicals, BridgeIdKeys.TmdbId);
        if (string.IsNullOrWhiteSpace(tmdbId))
            return await PersistDiagnosticsAsync(context, CreateResult("Skipped", checkedAt, mediaType,
                skippedReason: "missing_bridge_id", message: "This item needs a TMDB ID before artwork can be refreshed."), ct);

        var apiKey = await ResolveTmdbApiKeyAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
            return await PersistDiagnosticsAsync(context, CreateResult("Skipped", checkedAt, mediaType, BridgeIdKeys.TmdbId, tmdbId,
                skippedReason: "missing_api_key", message: "TMDB is not configured."), ct);

        var endpoint = context.MediaType == MediaType.Movies
            ? $"{TmdbApiBaseUrl}/movie/{Uri.EscapeDataString(tmdbId)}/images"
            : $"{TmdbApiBaseUrl}/tv/{Uri.EscapeDataString(tmdbId)}/images";
        var response = await GetImagesAsync(endpoint, apiKey, ct).ConfigureAwait(false);
        if (response.Json is null)
            return await PersistDiagnosticsAsync(context, CreateResult(response.Status, checkedAt, mediaType, BridgeIdKeys.TmdbId, tmdbId,
                endpoint, response.HttpStatusCode, response.SkippedReason, response.Message), ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var preferredUpdates = 0;
        var owner = context.MediaType == MediaType.TV ? context.RootWorkId : context.SelfWorkId;
        foreach (var mapping in RootMappings)
        {
            var processed = await ProcessRankedImagesAsync(response.Json[mapping.JsonField]?.AsArray() ?? [], mapping.AssetType,
                owner, mapping.UpdatePreferred, ct).ConfigureAwait(false);
            AddCount(counts, mapping.AssetType, processed.StoredCount);
            preferredUpdates += processed.UpdatedPreferredCount;
        }

        if (context.MediaType == MediaType.TV && context.SeasonWorkId.HasValue && context.SeasonNumber.HasValue)
        {
            var seasonEndpoint = $"{TmdbApiBaseUrl}/tv/{Uri.EscapeDataString(tmdbId)}/season/{context.SeasonNumber.Value}/images";
            var seasonResponse = await GetImagesAsync(seasonEndpoint, apiKey, ct).ConfigureAwait(false);
            if (seasonResponse.Json is not null)
            {
                foreach (var mapping in SeasonMappings)
                {
                    var processed = await ProcessRankedImagesAsync(seasonResponse.Json[mapping.JsonField]?.AsArray() ?? [], mapping.AssetType,
                        context.SeasonWorkId.Value, updatePreferred: true, ct).ConfigureAwait(false);
                    AddCount(counts, mapping.AssetType, processed.StoredCount);
                    preferredUpdates += processed.UpdatedPreferredCount;
                }
            }
        }

        var downloaded = counts.Values.Sum();
        return await PersistDiagnosticsAsync(context, CreateResult(downloaded > 0 || preferredUpdates > 0 ? "Completed" : "NoImages",
            checkedAt, mediaType, BridgeIdKeys.TmdbId, tmdbId, endpoint, response.HttpStatusCode,
            message: downloaded > 0 ? $"Stored {downloaded} TMDB artwork variant(s)." : "TMDB returned no compatible artwork variants.",
            storedCounts: counts, updatedPreferredCount: preferredUpdates), ct);
    }

    private static readonly ArtworkMapping[] RootMappings =
    [new("backdrops", AssetType.Background, true), new("logos", AssetType.Logo, true), new("posters", AssetType.CoverArt, false)];
    private static readonly ArtworkMapping[] SeasonMappings =
    [new("posters", AssetType.SeasonPoster, true), new("backdrops", AssetType.SeasonThumb, true)];

    private async Task<ImageAssetProcessingResult> ProcessRankedImagesAsync(IEnumerable<JsonNode?> imageNodes, AssetType assetType,
        Guid ownerEntityId, bool updatePreferred, CancellationToken ct)
    {
        var ranked = imageNodes.Where(node => node is not null && !string.IsNullOrWhiteSpace(node!["file_path"]?.GetValue<string>()))
            .Where(node => IsCompatibleImage(node!, assetType))
            .OrderByDescending(node => LanguageRank(node!["iso_639_1"]?.GetValue<string>(), assetType))
            .ThenByDescending(node => node!["vote_average"]?.GetValue<double?>() ?? 0)
            .ThenByDescending(node => node!["vote_count"]?.GetValue<int?>() ?? 0)
            .ThenByDescending(node => (node!["width"]?.GetValue<int?>() ?? 0) * (node!["height"]?.GetValue<int?>() ?? 0))
            .Take(MaxVariantsPerAssetType).ToList();
        if (ranked.Count == 0) return ImageAssetProcessingResult.Empty;

        var variants = (await _assetRepo.GetByEntityAsync(ownerEntityId.ToString(), assetType.ToString(), ct)).ToList();
        var currentPreferred = variants.FirstOrDefault(asset => asset.IsPreferred)?.Id;
        EntityAsset? preferred = updatePreferred ? variants.FirstOrDefault(asset => asset.IsPreferred && asset.IsUserOverride) : null;
        var stored = 0;
        foreach (var node in ranked)
        {
            var url = BuildImageUrl(node!["file_path"]!.GetValue<string>());
            await using var lease = await _imageDownloadCoordinator.AcquireAsync(url, ct).ConfigureAwait(false);
            var existing = variants.FirstOrDefault(asset => string.Equals(asset.ImageUrl, url, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.LocalImagePath) && File.Exists(existing.LocalImagePath))
            {
                if (updatePreferred && preferred is null && !existing.IsUserOverride) preferred = existing;
                continue;
            }
            var bytes = await GetCachedOrDownloadAsync(url, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) continue;
            var variant = existing ?? new EntityAsset
            {
                Id = Guid.NewGuid(), EntityId = ownerEntityId.ToString(), EntityType = "Work", AssetTypeValue = assetType.ToString(),
                ImageUrl = url, SourceProvider = TmdbProviderName, AssetClassValue = "Artwork", StorageLocationValue = "Central",
                OwnerScope = OwnerScope(assetType), CreatedAt = DateTimeOffset.UtcNow,
            };
            variant.LocalImagePath ??= _assetPaths.GetCentralAssetPath("Work", ownerEntityId, assetType.ToString(), variant.Id, InferExtension(url));
            await PersistImageAsync(bytes, variant.LocalImagePath, url, ct).ConfigureAwait(false);
            ArtworkVariantHelper.StampMetadataAndRenditions(variant, _assetPaths);
            await _assetRepo.UpsertAsync(variant, ct).ConfigureAwait(false);
            if (existing is null) variants.Add(variant); else variants[variants.IndexOf(existing)] = variant;
            stored++;
            if (updatePreferred && preferred is null) preferred = variant;
        }
        if (!updatePreferred || preferred is null) return new ImageAssetProcessingResult(preferred?.LocalImagePath, stored, 0);
        await _assetRepo.SetPreferredAsync(preferred.Id, ct).ConfigureAwait(false);
        await _canonicalRepo.UpsertBatchAsync(ArtworkCanonicalHelper.CreatePreferredAssetCanonicals(ownerEntityId, preferred, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (_assetExportService is not null) await _assetExportService.ReconcileArtworkAsync(preferred.EntityId, preferred.EntityType, preferred.AssetTypeValue, ct).ConfigureAwait(false);
        return new ImageAssetProcessingResult(preferred.LocalImagePath, stored, currentPreferred == preferred.Id ? 0 : 1);
    }

    private async Task<(JsonNode? Json, string Status, int? HttpStatusCode, string? SkippedReason, string? Message)> GetImagesAsync(string endpoint, string apiKey, CancellationToken ct)
    {
        var url = $"{endpoint}?include_image_language=en,null&api_key={Uri.EscapeDataString(apiKey)}";
        try
        {
            using var client = _httpFactory.CreateClient(TmdbProviderName);
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode == HttpStatusCode.NotFound ? "NoResult" : "Error", (int)response.StatusCode,
                    response.StatusCode == HttpStatusCode.NotFound ? "provider_no_result" : "provider_request_failed", $"TMDB returned {(int)response.StatusCode}.");
            return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false), "Completed", (int)response.StatusCode, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB artwork request failed for {Endpoint}", endpoint);
            return (null, "Error", null, "provider_call_failed", "TMDB artwork request failed.");
        }
    }

    private async Task<byte[]?> GetCachedOrDownloadAsync(string url, CancellationToken ct)
    {
        var cached = await _imageCache.FindBySourceUrlAsync(url, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached)) return await File.ReadAllBytesAsync(cached, ct).ConfigureAwait(false);
        try
        {
            using var client = _httpFactory.CreateClient(TmdbProviderName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await BoundedHttpContent.ReadImageAsync(response.Content, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB artwork download failed for {Url}", url);
            return null;
        }
    }

    private async Task PersistImageAsync(byte[] bytes, string destination, string sourceUrl, CancellationToken ct)
    {
        AssetPathService.EnsureDirectory(destination);
        var hash = Hashing.Sha256Hex(bytes);
        var cached = await _imageCache.FindByHashAsync(hash, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached) && !string.Equals(cached, destination, StringComparison.OrdinalIgnoreCase)) File.Copy(cached, destination, true);
        else if (string.IsNullOrWhiteSpace(cached) || !File.Exists(cached)) await BoundedHttpContent.WriteFileAtomicallyAsync(destination, bytes, ct).ConfigureAwait(false);
        await _imageCache.InsertAsync(hash, destination, sourceUrl, ct).ConfigureAwait(false);
    }

    private async Task<ArtworkContext> ResolveContextAsync(Guid entityId, CancellationToken ct)
    {
        var asset = await _mediaAssetRepo.FindByIdAsync(entityId, ct).ConfigureAwait(false);
        var lineage = await _workRepo.GetLineageByAssetAsync(asset?.Id ?? entityId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            asset = await _mediaAssetRepo.FindFirstByWorkIdAsync(entityId, ct).ConfigureAwait(false);
            if (asset is not null) lineage = await _workRepo.GetLineageByAssetAsync(asset.Id, ct).ConfigureAwait(false);
        }
        if (lineage is null) return new ArtworkContext(asset?.Id ?? entityId, entityId, entityId, null, null, MediaType.Unknown);
        var own = await _canonicalRepo.GetByEntityAsync(lineage.WorkId, ct).ConfigureAwait(false);
        var seasonValue = own.FirstOrDefault(value => string.Equals(value.Key, MetadataFieldConstants.SeasonNumber, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(seasonValue) && lineage.ParentWorkId is { } parentWorkId)
        {
            var parentCanonicals = await _canonicalRepo.GetByEntityAsync(parentWorkId, ct).ConfigureAwait(false);
            seasonValue = parentCanonicals.FirstOrDefault(value =>
                string.Equals(value.Key, MetadataFieldConstants.SeasonNumber, StringComparison.OrdinalIgnoreCase))?.Value;
        }
        return new ArtworkContext(asset?.Id ?? entityId, lineage.WorkId, lineage.RootParentWorkId, lineage.ParentWorkId,
            int.TryParse(seasonValue, out var season) ? season : null, lineage.MediaType);
    }

    private async Task<Dictionary<string, string>> LoadCanonicalsAsync(ArtworkContext context, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in new[] { context.RootWorkId, context.SelfWorkId, context.AssetId }.Distinct())
            foreach (var canonical in await _canonicalRepo.GetByEntityAsync(id, ct).ConfigureAwait(false))
                if (!string.IsNullOrWhiteSpace(canonical.Key) && !string.IsNullOrWhiteSpace(canonical.Value) && !values.ContainsKey(canonical.Key)) values[canonical.Key] = canonical.Value;
        return values;
    }

    private async Task<string?> ResolveTmdbApiKeyAsync(CancellationToken ct)
    {
        var config = _configLoader.LoadProvider(TmdbProviderName);
        if (!string.IsNullOrWhiteSpace(config?.HttpClient?.ApiKey)) return config.HttpClient.ApiKey;
        return await _providerConfigRepo.GetDecryptedValueAsync(WellKnownProviders.Tmdb.ToString(), "api_key", ct).ConfigureAwait(false);
    }

    private async Task<ImageEnrichmentResult> PersistDiagnosticsAsync(ArtworkContext context, ImageEnrichmentResult result, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var values = new List<CanonicalValue>();
        foreach (var id in new[] { context.RootWorkId, context.SelfWorkId, context.AssetId }.Distinct())
        {
            AddDiagnostic(values, id, "tmdb_artwork_status", result.Status, now);
            AddDiagnostic(values, id, "tmdb_artwork_last_checked_at", result.LastCheckedAt.ToString("O", CultureInfo.InvariantCulture), now);
            AddDiagnostic(values, id, "tmdb_artwork_downloaded_count", result.DownloadedCount.ToString(CultureInfo.InvariantCulture), now);
            AddDiagnostic(values, id, "tmdb_artwork_bridge_id", result.BridgeId, now);
            AddDiagnostic(values, id, "tmdb_artwork_http_status", result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture), now);
            AddDiagnostic(values, id, "tmdb_artwork_skipped_reason", result.SkippedReason, now);
        }
        if (values.Count > 0) await _canonicalRepo.UpsertBatchAsync(values, ct).ConfigureAwait(false);
        return result;
    }

    private static ImageEnrichmentResult CreateResult(string status, DateTimeOffset checkedAt, string? mediaType, string? bridgeKey = null,
        string? bridgeId = null, string? endpoint = null, int? httpStatus = null, string? skippedReason = null, string? message = null,
        IReadOnlyDictionary<string, int>? storedCounts = null, int updatedPreferredCount = 0) => new()
    {
        Provider = TmdbProviderName, ProviderName = "TMDB", Status = status, MediaType = mediaType, BridgeKey = bridgeKey, BridgeId = bridgeId,
        Endpoint = endpoint, HttpStatusCode = httpStatus, SkippedReason = skippedReason, Message = message,
        StoredVariantCounts = storedCounts ?? new Dictionary<string, int>(), DownloadedCount = storedCounts?.Values.Sum() ?? 0,
        UpdatedPreferredCount = updatedPreferredCount, LastCheckedAt = checkedAt,
    };

    private static bool IsCompatibleImage(JsonNode node, AssetType assetType)
    {
        var width = node["width"]?.GetValue<int?>() ?? 0; var height = node["height"]?.GetValue<int?>() ?? 0;
        if (width <= 0 || height <= 0) return true;
        var ratio = width / (double)height;
        return assetType switch { AssetType.Background or AssetType.SeasonThumb => ratio >= 1.35, AssetType.CoverArt or AssetType.SeasonPoster => ratio <= .9, _ => true };
    }
    private static int LanguageRank(string? language, AssetType type) => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? 3 : string.IsNullOrWhiteSpace(language) ? (type == AssetType.Logo ? 2 : 1) : 0;
    private static string BuildImageUrl(string filePath) => $"{TmdbImageBaseUrl}/{filePath.TrimStart('/')}";
    private static string InferExtension(string url) => url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
    private static string OwnerScope(AssetType type) => type is AssetType.SeasonPoster or AssetType.SeasonThumb ? "Season" : "Work";
    private static string? GetValue(IReadOnlyDictionary<string, string> values, params string[] keys) => keys.Select(key => values.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static void AddCount(Dictionary<string, int> values, AssetType type, int count) { if (count > 0) values[type.ToString()] = values.GetValueOrDefault(type.ToString()) + count; }
    private static void AddDiagnostic(List<CanonicalValue> values, Guid id, string key, string? value, DateTimeOffset now) { if (!string.IsNullOrWhiteSpace(value)) values.Add(new CanonicalValue { EntityId = id, Key = key, Value = value, LastScoredAt = now, WinningProviderId = WellKnownProviders.Tmdb }); }
    private sealed record ArtworkMapping(string JsonField, AssetType AssetType, bool UpdatePreferred);
    private sealed record ImageAssetProcessingResult(string? PreferredLocalPath, int StoredCount, int UpdatedPreferredCount) { public static readonly ImageAssetProcessingResult Empty = new(null, 0, 0); }
    private sealed record ArtworkContext(Guid AssetId, Guid SelfWorkId, Guid RootWorkId, Guid? SeasonWorkId, int? SeasonNumber, MediaType MediaType);
}
