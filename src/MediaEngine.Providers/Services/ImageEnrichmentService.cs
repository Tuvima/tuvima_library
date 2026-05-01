using System.Globalization;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Helpers;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Orchestrates Stage 3 image enrichment for a work — fetches rich imagery
/// from Fanart.tv (backgrounds, logos, banners, character art) and stores typed
/// assets. Uses direct HTTP calls (not ConfigDrivenAdapter) for full control
/// over the multi-image array response and character art name fields.
/// </summary>
public sealed class ImageEnrichmentService : IImageEnrichmentService
{
    private readonly IEntityAssetRepository _assetRepo;
    private readonly IMediaAssetRepository _mediaAssetRepo;
    private readonly ICharacterPortraitRepository _portraitRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IWorkRepository _workRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IProviderConfigurationRepository _providerConfigRepo;
    private readonly IConfigurationLoader _configLoader;
    private readonly IImageCacheRepository _imageCache;
    private readonly ImagePathService _imagePaths;
    private readonly AssetPathService _assetPaths;
    private readonly IAssetExportService? _assetExportService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly ILogger<ImageEnrichmentService> _logger;

    private const string FanartBaseUrl = "https://webservice.fanart.tv/v3";
    private const string FanartProviderConfigName = "fanart_tv";
    private const int MaxFanartVariantsPerAssetType = 3;
    private const double CharacterMatchThreshold = 0.70;

    private sealed record ImageFieldMapping(AssetType Type, bool UpdatePreferred, params string[] JsonFields);

    /// <summary>
    /// Maps media type → list of (Fanart JSON field, AssetType to store).
    /// CoverArt is excluded — CoverArtWorker handles that separately.
    /// </summary>
    private static readonly Dictionary<string, ImageFieldMapping[]> FieldMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Movies"] = [
            new(AssetType.Background, true, "moviebackground"),
            new(AssetType.Logo, true, "hdmovielogo", "movielogo"),
            new(AssetType.ClearArt, true, "hdmovieclearart", "movieclearart"),
            new(AssetType.Banner, true, "moviebanner"),
            new(AssetType.CoverArt, false, "movieposter"),
            new(AssetType.DiscArt, true, "moviedisc"),
        ],
        ["TV"] = [
            new(AssetType.Background, true, "showbackground"),
            new(AssetType.Logo, true, "hdtvlogo", "clearlogo"),
            new(AssetType.ClearArt, true, "hdclearart", "clearart"),
            new(AssetType.Banner, true, "tvbanner"),
            new(AssetType.CoverArt, false, "tvposter"),
        ],
        ["Music"] = [
            new(AssetType.Background, true, "artistbackground"),
            new(AssetType.Logo, true, "musiclogo"),
            new(AssetType.SquareArt, true, "albumcover"),
            new(AssetType.DiscArt, true, "cdart"),
        ],
    };

    private static readonly string[] TvSeasonPosterFields = ["seasonposter"];
    private static readonly string[] TvSeasonThumbFields = ["seasonthumb"];
    private static readonly string[] TvEpisodeStillFields = ["tvthumb"];

    /// <summary>
    /// Maps media type → Fanart JSON field containing character art.
    /// </summary>
    private static readonly Dictionary<string, string> CharacterArtFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Movies"] = "characterart",
        ["TV"] = "characterart",
    };

    public ImageEnrichmentService(
        IEntityAssetRepository assetRepo,
        IMediaAssetRepository mediaAssetRepo,
        ICharacterPortraitRepository portraitRepo,
        ICanonicalValueRepository canonicalRepo,
        IWorkRepository workRepo,
        IFictionalEntityRepository entityRepo,
        IPersonRepository personRepo,
        IProviderConfigurationRepository providerConfigRepo,
        IConfigurationLoader configLoader,
        IImageCacheRepository imageCache,
        ImagePathService imagePaths,
        AssetPathService assetPaths,
        IAssetExportService? assetExportService,
        IHttpClientFactory httpFactory,
        IFuzzyMatchingService fuzzy,
        ILogger<ImageEnrichmentService> logger)
    {
        ArgumentNullException.ThrowIfNull(assetRepo);
        ArgumentNullException.ThrowIfNull(mediaAssetRepo);
        ArgumentNullException.ThrowIfNull(portraitRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(workRepo);
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(providerConfigRepo);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(imagePaths);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(fuzzy);
        ArgumentNullException.ThrowIfNull(logger);

        _assetRepo           = assetRepo;
        _mediaAssetRepo      = mediaAssetRepo;
        _portraitRepo        = portraitRepo;
        _canonicalRepo       = canonicalRepo;
        _workRepo            = workRepo;
        _entityRepo          = entityRepo;
        _personRepo          = personRepo;
        _providerConfigRepo  = providerConfigRepo;
        _configLoader        = configLoader;
        _imageCache          = imageCache;
        _imagePaths          = imagePaths;
        _assetPaths          = assetPaths;
        _assetExportService  = assetExportService;
        _httpFactory          = httpFactory;
        _fuzzy               = fuzzy;
        _logger              = logger;
    }

    /// <inheritdoc/>
    public async Task EnrichWorkImagesAsync(Guid assetId, string workQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workQid);

        var context = await ResolveImageWorkContextAsync(assetId, ct);
        if (string.IsNullOrWhiteSpace(context.MediaFilePath))
        {
            _logger.LogWarning(
                "[IMAGE-ENRICH] Asset {AssetId} has no media file path — skipping sidecar artwork enrichment",
                assetId);
            return;
        }

        _logger.LogInformation(
            "[IMAGE-ENRICH] Starting image enrichment for asset {AssetId} using canonical entity {CanonicalEntityId} ({WorkQid})",
            assetId,
            context.CanonicalEntityId,
            workQid);

        // ── Step 1: Read bridge IDs from canonical values ──
        var canonicalLookup = await LoadEffectiveCanonicalLookupAsync(
            context.CanonicalEntityId,
            assetId,
            ct);
        var tmdbId = GetCanonical(canonicalLookup, BridgeIdKeys.TmdbId, "tmdb_movie_id", "tmdb_tv_id");
        var tvdbId = GetCanonical(canonicalLookup, BridgeIdKeys.TvdbId);
        var musicBrainzArtistId = GetCanonical(canonicalLookup, BridgeIdKeys.MusicBrainzId, "musicbrainz_artist_id");
        var musicBrainzReleaseGroupId = GetCanonical(canonicalLookup, BridgeIdKeys.MusicBrainzReleaseGroupId);
        var mediaTypeStr = GetCanonical(canonicalLookup, MetadataFieldConstants.MediaTypeField);

        if (tmdbId is null
            && tvdbId is null
            && musicBrainzArtistId is null
            && musicBrainzReleaseGroupId is null)
        {
            _logger.LogDebug("[IMAGE-ENRICH] No bridge IDs for Fanart.tv — skipping work {WorkQid}", workQid);
            return;
        }

        // ── Step 2: Resolve API key ──
        var fanartConfig = _configLoader.LoadProvider(FanartProviderConfigName);
        var apiKey = await ResolveFanartApiKeyAsync(fanartConfig, ct);

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[IMAGE-ENRICH] No Fanart.tv API key configured — skipping");
            return;
        }

        // ── Step 3: Call Fanart.tv API ──
        var (json, resolvedMediaType) = await CallFanartApiAsync(
            tmdbId,
            tvdbId,
            musicBrainzArtistId,
            musicBrainzReleaseGroupId,
            mediaTypeStr,
            apiKey,
            fanartConfig?.Name,
            ct);
        if (json is null) return;

        // ── Step 4: Parse response and download work-level assets ──
        if (FieldMappings.TryGetValue(resolvedMediaType, out var mappings))
        {
            foreach (var mapping in mappings)
            {
                await ProcessImageArrayAsync(
                    json,
                    mapping.JsonFields,
                    mapping.Type,
                    context.CanonicalEntityId,
                    workQid,
                    mapping.UpdatePreferred,
                    ct);
            }
        }

        if (string.Equals(resolvedMediaType, "TV", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessSeasonScopedImageArrayAsync(
                json,
                TvSeasonPosterFields,
                AssetType.SeasonPoster,
                context.CanonicalEntityId,
                workQid,
                ct);
            await ProcessSeasonScopedImageArrayAsync(
                json,
                TvSeasonThumbFields,
                AssetType.SeasonThumb,
                context.CanonicalEntityId,
                workQid,
                ct);
            await ProcessEpisodeScopedImageArrayAsync(
                json,
                TvEpisodeStillFields,
                AssetType.EpisodeStill,
                context.CanonicalEntityId,
                workQid,
                ct);
        }

        // ── Steps 5–6: Character art matching ──
        if (CharacterArtFields.TryGetValue(resolvedMediaType, out var charField))
        {
            await MatchCharacterArtAsync(json, charField, assetId, workQid, ct);
        }

        _logger.LogInformation("[IMAGE-ENRICH] Image enrichment complete for asset {AssetId} ({WorkQid})", assetId, workQid);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the appropriate Fanart.tv endpoint based on available bridge IDs.
    /// Returns the parsed JSON response and the resolved media type string.
    /// </summary>
    private async Task<(JsonNode? Json, string MediaType)> CallFanartApiAsync(
        string? tmdbId,
        string? tvdbId,
        string? musicBrainzArtistId,
        string? musicBrainzReleaseGroupId,
        string? mediaTypeStr,
        string apiKey,
        string? clientName,
        CancellationToken ct)
    {
        string url;
        string resolvedMediaType;

        if (IsMovieType(mediaTypeStr))
        {
            if (string.IsNullOrWhiteSpace(tmdbId))
                return (null, "Movies");

            url = $"{FanartBaseUrl}/movies/{tmdbId}?api_key={apiKey}";
            resolvedMediaType = "Movies";
        }
        else if (IsTvType(mediaTypeStr))
        {
            if (string.IsNullOrWhiteSpace(tvdbId))
            {
                _logger.LogDebug("[IMAGE-ENRICH] TV work has no TVDB id — skipping Fanart.tv lookup");
                return (null, "TV");
            }

            url = $"{FanartBaseUrl}/tv/{tvdbId}?api_key={apiKey}";
            resolvedMediaType = "TV";
        }
        else if (IsMusicType(mediaTypeStr))
        {
            if (!string.IsNullOrWhiteSpace(musicBrainzArtistId))
            {
                url = $"{FanartBaseUrl}/music/{musicBrainzArtistId}?api_key={apiKey}";
                resolvedMediaType = "Music";
            }
            else if (!string.IsNullOrWhiteSpace(musicBrainzReleaseGroupId))
            {
                url = $"{FanartBaseUrl}/music/albums/{musicBrainzReleaseGroupId}?api_key={apiKey}";
                resolvedMediaType = "Music";
            }
            else
            {
                _logger.LogDebug("[IMAGE-ENRICH] Music work has no MusicBrainz artist or release-group id — skipping Fanart.tv lookup");
                return (null, "Music");
            }
        }
        else if (!string.IsNullOrWhiteSpace(tvdbId))
        {
            url = $"{FanartBaseUrl}/tv/{tvdbId}?api_key={apiKey}";
            resolvedMediaType = "TV";
        }
        else if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            url = $"{FanartBaseUrl}/movies/{tmdbId}?api_key={apiKey}";
            resolvedMediaType = "Movies";
        }
        else if (!string.IsNullOrWhiteSpace(musicBrainzArtistId))
        {
            url = $"{FanartBaseUrl}/music/{musicBrainzArtistId}?api_key={apiKey}";
            resolvedMediaType = "Music";
        }
        else if (!string.IsNullOrWhiteSpace(musicBrainzReleaseGroupId))
        {
            url = $"{FanartBaseUrl}/music/albums/{musicBrainzReleaseGroupId}?api_key={apiKey}";
            resolvedMediaType = "Music";
        }
        else
        {
            return (null, string.Empty);
        }

        try
        {
            using var client = _httpFactory.CreateClient(
                string.IsNullOrWhiteSpace(clientName) ? FanartProviderConfigName : clientName);
            var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[IMAGE-ENRICH] Fanart.tv returned {Status} for {Url}",
                    response.StatusCode, url.Replace(apiKey, "***"));
                return (null, resolvedMediaType);
            }

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            return (json, resolvedMediaType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[IMAGE-ENRICH] Fanart.tv call failed");
            return (null, resolvedMediaType);
        }
    }

    /// <summary>
    /// Processes a Fanart.tv image array (e.g. "moviebackground"), stores all
    /// distinct variants for the role, and marks the highest-ranked image as
    /// preferred. Returns the preferred local file path if successful.
    /// </summary>
    private async Task<string?> ProcessImageArrayAsync(
        JsonNode json,
        IReadOnlyList<string> jsonFields,
        AssetType assetType,
        Guid ownerEntityId,
        string workQid,
        bool updatePreferred,
        CancellationToken ct)
    {
        var imageNodes = ResolveImageNodes(json, jsonFields);
        if (imageNodes.Count == 0)
        {
            _logger.LogDebug(
                "[IMAGE-ENRICH] No {JsonFields} images returned for {WorkQid}",
                string.Join(", ", jsonFields),
                workQid);
            return null;
        }

        return await ProcessRankedImagesAsync(imageNodes, assetType, ownerEntityId, workQid, updatePreferred, ct);
    }

    private async Task ProcessSeasonScopedImageArrayAsync(
        JsonNode json,
        IReadOnlyList<string> jsonFields,
        AssetType assetType,
        Guid showWorkId,
        string workQid,
        CancellationToken ct)
    {
        var imageNodes = ResolveImageNodes(json, jsonFields);
        if (imageNodes.Count == 0)
            return;

        foreach (var seasonGroup in imageNodes
                     .Select(node => new
                     {
                         Node = node,
                         HasSeason = TryGetOrdinal(node, out var seasonOrdinal, "season", "season_number"),
                         SeasonOrdinal = seasonOrdinal,
                     })
                     .Where(entry => entry.HasSeason)
                     .GroupBy(entry => entry.SeasonOrdinal))
        {
            var seasonWorkId = await _workRepo.FindChildByOrdinalAsync(showWorkId, seasonGroup.Key, ct);
            if (!seasonWorkId.HasValue)
            {
                _logger.LogDebug(
                    "[IMAGE-ENRICH] No season work found for show {ShowWorkId} season {Season} while processing {AssetType}",
                    showWorkId,
                    seasonGroup.Key,
                    assetType);
                continue;
            }

            await ProcessRankedImagesAsync(
                seasonGroup.Select(entry => entry.Node),
                assetType,
                seasonWorkId.Value,
                workQid,
                updatePreferred: true,
                ct);
        }
    }

    private async Task ProcessEpisodeScopedImageArrayAsync(
        JsonNode json,
        IReadOnlyList<string> jsonFields,
        AssetType assetType,
        Guid showWorkId,
        string workQid,
        CancellationToken ct)
    {
        var imageNodes = ResolveImageNodes(json, jsonFields);
        if (imageNodes.Count == 0)
            return;

        var seasonCache = new Dictionary<int, Guid?>();
        foreach (var episodeGroup in imageNodes
                     .Select(node => new
                     {
                         Node = node,
                         HasSeason = TryGetOrdinal(node, out var seasonOrdinal, "season", "season_number"),
                         SeasonOrdinal = seasonOrdinal,
                         HasEpisode = TryGetOrdinal(node, out var episodeOrdinal, "episode", "episode_number"),
                         EpisodeOrdinal = episodeOrdinal,
                     })
                     .Where(entry => entry.HasSeason && entry.HasEpisode)
                     .GroupBy(entry => (entry.SeasonOrdinal, entry.EpisodeOrdinal)))
        {
            if (!seasonCache.TryGetValue(episodeGroup.Key.SeasonOrdinal, out var seasonWorkId))
            {
                seasonWorkId = await _workRepo.FindChildByOrdinalAsync(showWorkId, episodeGroup.Key.SeasonOrdinal, ct);
                seasonCache[episodeGroup.Key.SeasonOrdinal] = seasonWorkId;
            }

            Guid? episodeWorkId = null;
            if (seasonWorkId.HasValue)
            {
                episodeWorkId = await _workRepo.FindChildByOrdinalAsync(seasonWorkId.Value, episodeGroup.Key.EpisodeOrdinal, ct);
            }
            else
            {
                _logger.LogDebug(
                    "[IMAGE-ENRICH] No season work found for show {ShowWorkId} season {Season}; trying direct episode child lookup",
                    showWorkId,
                    episodeGroup.Key.SeasonOrdinal);
            }

            episodeWorkId ??= await _workRepo.FindChildByOrdinalAsync(showWorkId, episodeGroup.Key.EpisodeOrdinal, ct);
            if (!episodeWorkId.HasValue)
            {
                _logger.LogDebug(
                    "[IMAGE-ENRICH] No episode work found for season {SeasonWorkId} or show {ShowWorkId} episode {Episode} while processing {AssetType}",
                    seasonWorkId?.ToString() ?? "(none)",
                    showWorkId,
                    episodeGroup.Key.EpisodeOrdinal,
                    assetType);
                continue;
            }

            await ProcessRankedImagesAsync(
                episodeGroup.Select(entry => entry.Node),
                assetType,
                episodeWorkId.Value,
                workQid,
                updatePreferred: true,
                ct);
        }
    }

    private async Task<string?> ProcessRankedImagesAsync(
        IEnumerable<JsonNode?> imageNodes,
        AssetType assetType,
        Guid ownerEntityId,
        string workQid,
        bool updatePreferred,
        CancellationToken ct)
    {
        var rankedImages = imageNodes
            .Where(n => n is not null)
            .Where(n => IsAllowedArtworkLanguage(n, assetType))
            .OrderByDescending(GetArtworkLanguageRank)
            .ThenByDescending(GetLikes)
            .Take(MaxFanartVariantsPerAssetType)
            .ToList();

        if (rankedImages.Count == 0)
            return null;

        var existingVariants = (await _assetRepo.GetByEntityAsync(ownerEntityId.ToString(), assetType.ToString(), ct)).ToList();
        EntityAsset? preferredVariant = updatePreferred
            ? existingVariants.FirstOrDefault(asset => asset.IsPreferred && asset.IsUserOverride)
            : null;

        foreach (var imageNode in rankedImages)
        {
            var imageUrl = imageNode?["url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(imageUrl))
                continue;

            var existing = existingVariants.FirstOrDefault(asset =>
                string.Equals(asset.ImageUrl, imageUrl, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (updatePreferred && preferredVariant is null && !existing.IsUserOverride)
                    preferredVariant = existing;

                continue;
            }

            var bytes = await DownloadImageBytesAsync(imageUrl, ct);
            if (bytes is null || bytes.Length == 0)
                continue;

            var variant = new EntityAsset
            {
                Id = Guid.NewGuid(),
                EntityId = ownerEntityId.ToString(),
                EntityType = "Work",
                AssetTypeValue = assetType.ToString(),
                ImageUrl = imageUrl,
                LocalImagePath = string.Empty,
                SourceProvider = "fanart_tv",
                AssetClassValue = "Artwork",
                StorageLocationValue = "Central",
                OwnerScope = InferOwnerScope(assetType),
                IsPreferred = false,
                IsUserOverride = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            variant.LocalImagePath = _assetPaths.GetCentralAssetPath(
                "Work",
                ownerEntityId,
                assetType.ToString(),
                variant.Id,
                InferVariantExtension(assetType, imageUrl));

            await PersistImageAsync(bytes, variant.LocalImagePath, imageUrl, ct);
            ArtworkVariantHelper.StampMetadataAndRenditions(variant, _assetPaths);
            await _assetRepo.UpsertAsync(variant, ct);
            existingVariants.Add(variant);

            if (updatePreferred && preferredVariant is null)
                preferredVariant = variant;

            _logger.LogInformation(
                "[IMAGE-ENRICH] Downloaded {AssetType} variant for {WorkQid} ({Bytes} bytes)",
                assetType,
                workQid,
                bytes.Length);
        }

        if (!updatePreferred)
        {
            return existingVariants
                .FirstOrDefault(asset => asset.IsPreferred)
                ?.LocalImagePath;
        }

        preferredVariant ??= existingVariants
            .OrderByDescending(asset => asset.IsPreferred && asset.IsUserOverride)
            .ThenByDescending(asset => asset.IsPreferred)
            .ThenByDescending(asset => asset.CreatedAt)
            .FirstOrDefault();

        if (preferredVariant is null)
            return null;

        await _assetRepo.SetPreferredAsync(preferredVariant.Id, ct);
        await UpsertPreferredArtworkCanonicalAsync(ownerEntityId, preferredVariant, ct);
        if (_assetExportService is not null)
            await _assetExportService.ReconcileArtworkAsync(
                preferredVariant.EntityId,
                preferredVariant.EntityType,
                preferredVariant.AssetTypeValue,
                ct);

        return preferredVariant.LocalImagePath;
    }

    private async Task UpsertPreferredArtworkCanonicalAsync(
        Guid assetId,
        EntityAsset preferredVariant,
        CancellationToken ct)
    {
        await _canonicalRepo.UpsertBatchAsync(
            ArtworkCanonicalHelper.CreatePreferredAssetCanonicals(
                assetId,
                preferredVariant,
                DateTimeOffset.UtcNow),
            ct);
    }

    private static string InferVariantExtension(AssetType assetType, string imageUrl)
    {
        if (assetType is AssetType.Logo or AssetType.DiscArt or AssetType.ClearArt)
            return ".png";

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            var extension = Path.GetExtension(imageUri.AbsolutePath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                return ".png";
        }

        return ".jpg";
    }

    private static string InferPortraitExtension(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            var extension = Path.GetExtension(imageUri.AbsolutePath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                return ".png";
        }

        return ".jpg";
    }

    private static string InferOwnerScope(AssetType assetType) =>
        assetType switch
        {
            AssetType.SeasonPoster or AssetType.SeasonThumb => "Season",
            AssetType.EpisodeStill => "Episode",
            _ => "Work",
        };

    private async Task<string?> ResolveFanartApiKeyAsync(
        MediaEngine.Storage.Models.ProviderConfiguration? fanartConfig,
        CancellationToken ct)
    {
        var apiKey = fanartConfig?.HttpClient?.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        apiKey = await _providerConfigRepo.GetDecryptedValueAsync(
            WellKnownProviders.FanartTv.ToString(),
            "api_key",
            ct);

        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    private async Task<ImageWorkContext> ResolveImageWorkContextAsync(Guid entityId, CancellationToken ct)
    {
        var asset = await _mediaAssetRepo.FindByIdAsync(entityId, ct);
        var lineage = await _workRepo.GetLineageByAssetAsync(entityId, ct);
        var mediaFilePath = asset?.FilePathRoot;

        return lineage is null
            ? new ImageWorkContext(entityId, entityId, mediaFilePath, GetContainerFolder(mediaFilePath))
            : new ImageWorkContext(
                entityId,
                lineage.TargetForParentScope,
                mediaFilePath,
                ResolveArtworkFolderPath(lineage.MediaType, mediaFilePath));
    }

    private static string? ResolveArtworkFolderPath(MediaEngine.Domain.Enums.MediaType mediaType, string? mediaFilePath) =>
        mediaType switch
        {
            MediaEngine.Domain.Enums.MediaType.TV => GetSeriesFolder(mediaFilePath),
            MediaEngine.Domain.Enums.MediaType.Music => GetArtistFolder(mediaFilePath) ?? GetContainerFolder(mediaFilePath),
            _ => GetContainerFolder(mediaFilePath),
        };

    private static string? GetSeriesFolder(string? mediaFilePath)
    {
        var seasonFolder = GetContainerFolder(mediaFilePath);
        return string.IsNullOrWhiteSpace(seasonFolder)
            ? null
            : Path.GetDirectoryName(seasonFolder);
    }

    private static string? GetArtistFolder(string? mediaFilePath)
    {
        var albumFolder = GetContainerFolder(mediaFilePath);
        return string.IsNullOrWhiteSpace(albumFolder)
            ? null
            : Path.GetDirectoryName(albumFolder);
    }

    private static string? GetContainerFolder(string? mediaFilePath) =>
        string.IsNullOrWhiteSpace(mediaFilePath)
            ? null
            : Path.GetDirectoryName(mediaFilePath);

    /// <summary>
    /// Matches Fanart.tv character art images against fictional entities
    /// linked to this work, using fuzzy name matching, then creates
    /// CharacterPortrait records for matched pairs.
    /// </summary>
    private async Task MatchCharacterArtAsync(
        JsonNode json, string charField, Guid workId, string workQid, CancellationToken ct)
    {
        var charArray = json[charField]?.AsArray();
        if (charArray is null || charArray.Count == 0) return;

        // Step 6a: Get fictional entities linked to this work
        var entities = await _entityRepo.GetByWorkQidAsync(workQid, ct);
        if (entities.Count == 0)
        {
            _logger.LogDebug("[IMAGE-ENRICH] No fictional entities for {WorkQid} — skipping character art", workQid);
            return;
        }

        // Step 6b: Get character-performer links for this work
        var performerLinks = await _personRepo.GetCharacterLinksByWorkAsync(workQid, ct);
        var linkLookup = performerLinks.ToDictionary(l => l.FictionalEntityId, l => l.PersonId);

        // Step 6c: Parse character art entries
        var charArts = charArray
            .Where(n => n is not null)
            .Select(n => new
            {
                Name = n!["name"]?.GetValue<string>() ?? string.Empty,
                Url  = n!["url"]?.GetValue<string>() ?? string.Empty,
            })
            .Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.Url))
            .ToList();

        if (charArts.Count == 0) return;

        // Step 6d: Fuzzy-match character art names against entity labels
        var matched = 0;
        foreach (var entity in entities)
        {
            if (string.IsNullOrEmpty(entity.Label)) continue;

            // Find best-matching character art for this entity
            var bestMatch = charArts
                .Select(art => new
                {
                    Art   = art,
                    Score = _fuzzy.ComputeTokenSetRatio(entity.Label, art.Name),
                })
                .Where(m => m.Score >= CharacterMatchThreshold)
                .OrderByDescending(m => m.Score)
                .FirstOrDefault();

            if (bestMatch is null) continue;

            // Need a performer link to create a CharacterPortrait
            if (!linkLookup.TryGetValue(entity.Id, out var personId)) continue;

            var portrait = new CharacterPortrait
            {
                Id                = Guid.NewGuid(),
                PersonId          = personId,
                FictionalEntityId = entity.Id,
                ImageUrl          = bestMatch.Art.Url,
                SourceProvider    = "fanart_tv",
                IsDefault         = true,
                CreatedAt         = DateTimeOffset.UtcNow,
            };

            // Download portrait image
            var bytes = await DownloadImageBytesAsync(bestMatch.Art.Url, ct);
            if (bytes is not null && bytes.Length > 0)
            {
                var portraitPath = _assetPaths.GetCharacterPortraitPath(
                    personId,
                    entity.Id,
                    InferPortraitExtension(bestMatch.Art.Url));
                await PersistImageAsync(bytes, portraitPath, bestMatch.Art.Url, ct);
                portrait.LocalImagePath = portraitPath;
            }

            await _portraitRepo.UpsertAsync(portrait, ct);

            matched++;
            _logger.LogDebug(
                "[IMAGE-ENRICH] Character art matched: {CharName} → {EntityLabel} (score={Score:F2})",
                bestMatch.Art.Name, entity.Label, bestMatch.Score);
        }

        if (matched > 0)
        {
            _logger.LogInformation(
                "[IMAGE-ENRICH] Matched {Count} character art images for {WorkQid}",
                matched, workQid);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Image download utilities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Downloads image bytes from a URL, returning null on failure.</summary>
    private async Task<byte[]?> DownloadImageBytesAsync(string imageUrl, CancellationToken ct)
    {
        try
        {
            using var client = _httpFactory.CreateClient("fanart_tv");
            return await client.GetByteArrayAsync(imageUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[IMAGE-ENRICH] Failed to download image from {Url}", imageUrl);
            return null;
        }
    }

    /// <summary>
    /// Persists image bytes to disk with SHA-256 hash dedup via image cache.
    /// </summary>
    private async Task PersistImageAsync(
        byte[] bytes, string localPath, string sourceUrl, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // Check cache for identical image already on disk
        var cached = await _imageCache.FindByHashAsync(hash, ct);
        if (cached is not null && File.Exists(cached))
        {
            ImagePathService.EnsureDirectory(localPath);
            File.Copy(cached, localPath, overwrite: true);
        }
        else
        {
            ImagePathService.EnsureDirectory(localPath);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
            await _imageCache.InsertAsync(hash, localPath, sourceUrl, ct);
        }
    }

    private async Task<Dictionary<string, string>> LoadEffectiveCanonicalLookupAsync(
        Guid canonicalEntityId,
        Guid assetId,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var canonical in await _canonicalRepo.GetByEntityAsync(canonicalEntityId, ct))
        {
            if (!string.IsNullOrWhiteSpace(canonical.Key)
                && !string.IsNullOrWhiteSpace(canonical.Value)
                && !lookup.ContainsKey(canonical.Key))
            {
                lookup[canonical.Key] = canonical.Value;
            }
        }

        if (assetId == canonicalEntityId)
            return lookup;

        foreach (var canonical in await _canonicalRepo.GetByEntityAsync(assetId, ct))
        {
            if (!string.IsNullOrWhiteSpace(canonical.Key)
                && !string.IsNullOrWhiteSpace(canonical.Value)
                && !lookup.ContainsKey(canonical.Key))
            {
                lookup[canonical.Key] = canonical.Value;
            }
        }

        return lookup;
    }

    private static List<JsonNode> ResolveImageNodes(JsonNode json, IReadOnlyList<string> jsonFields)
    {
        var results = new List<JsonNode>();

        foreach (var jsonField in jsonFields)
        {
            if (json[jsonField] is JsonArray directArray && directArray.Count > 0)
                results.AddRange(directArray.Where(node => node is not null).Select(node => node!));
        }

        if (json["albums"] is JsonObject albums)
        {
            foreach (var (_, albumNode) in albums)
            {
                foreach (var jsonField in jsonFields)
                {
                    if (albumNode?[jsonField] is JsonArray nestedArray && nestedArray.Count > 0)
                        results.AddRange(nestedArray.Where(node => node is not null).Select(node => node!));
                }
            }
        }

        return results;
    }

    private static int GetLikes(JsonNode? node) =>
        TryGetInt(node?["likes"], out var likes) ? likes : 0;

    private static bool TryGetInt(JsonNode? node, out int value)
    {
        value = 0;

        if (node is not JsonValue jsonValue)
            return false;

        if (jsonValue.TryGetValue<int>(out value))
            return true;

        if (jsonValue.TryGetValue<string>(out var text))
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        return false;
    }

    /// <summary>Gets the first canonical value for the provided keys, or null if missing.</summary>
    private static string? GetCanonical(
        IReadOnlyDictionary<string, string> canonicals,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (canonicals.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>Checks if the media type string indicates a movie (not TV).</summary>
    private static bool IsMovieType(string? mediaType) =>
        string.Equals(mediaType, "Movies", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase);

    private static bool IsTvType(string? mediaType) =>
        string.Equals(mediaType, "TV", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "Television", StringComparison.OrdinalIgnoreCase);

    private static bool IsMusicType(string? mediaType) =>
        string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedArtworkLanguage(JsonNode? imageNode, AssetType assetType)
    {
        if (assetType is not (AssetType.Logo or AssetType.Banner or AssetType.SquareArt or AssetType.ClearArt))
            return true;

        return IsPreferredArtworkLanguage(imageNode?["lang"]?.GetValue<string>());
    }

    private static bool IsPreferredArtworkLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language)
        || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "00", StringComparison.OrdinalIgnoreCase);

    private static int GetArtworkLanguageRank(JsonNode? imageNode)
    {
        var language = imageNode?["lang"]?.GetValue<string>();
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            return 2;

        return IsPreferredArtworkLanguage(language) ? 1 : 0;
    }

    private static bool TryGetOrdinal(JsonNode? imageNode, out int ordinal, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetInt(imageNode?[key], out ordinal))
                return true;
        }

        ordinal = 0;
        return false;
    }

    private sealed record ImageWorkContext(Guid AssetId, Guid CanonicalEntityId, string? MediaFilePath, string? ArtworkFolderPath);
}
