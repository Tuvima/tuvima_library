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
    private readonly ICharacterPortraitRepository _portraitRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IWorkRepository _workRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IProviderConfigurationRepository _providerConfigRepo;
    private readonly IConfigurationLoader _configLoader;
    private readonly IHeroBannerGenerator _heroGenerator;
    private readonly IImageCacheRepository _imageCache;
    private readonly ImagePathService _imagePaths;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly ILogger<ImageEnrichmentService> _logger;

    private const string FanartBaseUrl = "https://webservice.fanart.tv/v3";
    private const string FanartProviderConfigName = "fanart_tv";
    private const double CharacterMatchThreshold = 0.70;

    /// <summary>
    /// Maps media type → list of (Fanart JSON field, AssetType to store).
    /// CoverArt is excluded — CoverArtWorker handles that separately.
    /// </summary>
    private static readonly Dictionary<string, (string JsonField, AssetType Type)[]> FieldMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Movies"] = [
            ("moviebackground", AssetType.Background),
            ("hdmovielogo",     AssetType.Logo),
            ("moviebanner",     AssetType.Banner),
        ],
        ["TV"] = [
            ("showbackground", AssetType.Background),
            ("hdtvlogo",       AssetType.Logo),
            ("tvbanner",       AssetType.Banner),
        ],
        ["Music"] = [
            ("artistbackground", AssetType.Background),
            ("musiclogo",        AssetType.Logo),
            ("albumcover",       AssetType.SquareArt),
            ("cdart",            AssetType.Logo),
        ],
    };

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
        ICharacterPortraitRepository portraitRepo,
        ICanonicalValueRepository canonicalRepo,
        IWorkRepository workRepo,
        IFictionalEntityRepository entityRepo,
        IPersonRepository personRepo,
        IProviderConfigurationRepository providerConfigRepo,
        IConfigurationLoader configLoader,
        IHeroBannerGenerator heroGenerator,
        IImageCacheRepository imageCache,
        ImagePathService imagePaths,
        IHttpClientFactory httpFactory,
        IFuzzyMatchingService fuzzy,
        ILogger<ImageEnrichmentService> logger)
    {
        ArgumentNullException.ThrowIfNull(assetRepo);
        ArgumentNullException.ThrowIfNull(portraitRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(workRepo);
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(providerConfigRepo);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(heroGenerator);
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(imagePaths);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(fuzzy);
        ArgumentNullException.ThrowIfNull(logger);

        _assetRepo           = assetRepo;
        _portraitRepo        = portraitRepo;
        _canonicalRepo       = canonicalRepo;
        _workRepo            = workRepo;
        _entityRepo          = entityRepo;
        _personRepo          = personRepo;
        _providerConfigRepo  = providerConfigRepo;
        _configLoader        = configLoader;
        _heroGenerator       = heroGenerator;
        _imageCache          = imageCache;
        _imagePaths          = imagePaths;
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
        string? backgroundPath = null;
        if (FieldMappings.TryGetValue(resolvedMediaType, out var mappings))
        {
            foreach (var (jsonField, assetType) in mappings)
            {
                var localPath = await ProcessImageArrayAsync(
                    json, jsonField, assetType, assetId, workQid, resolvedMediaType, ct);

                if (assetType == AssetType.Background && localPath is not null)
                    backgroundPath = localPath;
            }
        }

        // ── Step 5: Regenerate hero from the higher-resolution background art ──
        if (backgroundPath is not null && File.Exists(backgroundPath))
        {
            await RegenerateHeroFromBackgroundAsync(assetId, workQid, backgroundPath, ct);
        }

        // ── Steps 6–7: Character art matching ──
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
    /// Processes a Fanart.tv image array (e.g. "moviebackground"), downloads
    /// the best image, stores it on disk, and creates an EntityAsset record.
    /// Returns the local file path if successful, null otherwise.
    /// </summary>
    private async Task<string?> ProcessImageArrayAsync(
        JsonNode json, string jsonField, AssetType assetType,
        Guid assetId, string workQid, string mediaType,
        CancellationToken ct)
    {
        var imageArray = ResolveImageArray(json, jsonField);
        if (imageArray is null || imageArray.Count == 0)
        {
            _logger.LogDebug("[IMAGE-ENRICH] No {JsonField} images returned for {WorkQid}", jsonField, workQid);
            return null;
        }

        // Pick best image: prefer English, then highest likes
        var best = imageArray
            .Where(n => n is not null)
            .OrderByDescending(n =>
                string.Equals(n!["lang"]?.GetValue<string>(), "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(GetLikes)
            .FirstOrDefault();

        var imageUrl = best?["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(imageUrl)) return null;

        // Resolve local path based on asset type
        var localPath = assetType switch
        {
            AssetType.Background => _imagePaths.GetWorkBackgroundPath(workQid, assetId),
            AssetType.SquareArt  => _imagePaths.GetWorkSquareArtPath(workQid, assetId),
            AssetType.Logo       => _imagePaths.GetWorkLogoPath(workQid, assetId),
            AssetType.Banner     => _imagePaths.GetWorkBannerPath(workQid, assetId),
            _                  => null,
        };

        if (localPath is null) return null;

        // Skip if already downloaded
        if (File.Exists(localPath))
        {
            _logger.LogDebug("[IMAGE-ENRICH] {AssetType} already exists at {Path}", assetType, localPath);
            return localPath;
        }

        // Download with hash-dedup
        var bytes = await DownloadImageBytesAsync(imageUrl, ct);
        if (bytes is null || bytes.Length == 0) return null;

        await PersistImageAsync(bytes, localPath, imageUrl, ct);

        // Create EntityAsset record
        await _assetRepo.UpsertAsync(new EntityAsset
        {
            Id             = Guid.NewGuid(),
            EntityId       = assetId.ToString(),
            EntityType     = "Work",
            AssetTypeValue = assetType.ToString(),
            ImageUrl       = imageUrl,
            LocalImagePath = localPath,
            SourceProvider = "fanart_tv",
            IsPreferred    = true,
            IsUserOverride = false,
            CreatedAt      = DateTimeOffset.UtcNow,
        }, ct);

        _logger.LogInformation(
            "[IMAGE-ENRICH] Downloaded {AssetType} for {WorkQid} ({Bytes} bytes)",
            assetType, workQid, bytes.Length);

        return localPath;
    }

    /// <summary>
    /// Regenerates the hero banner from a high-res background image,
    /// which produces better results than the cover-art-derived hero.
    /// </summary>
    private async Task RegenerateHeroFromBackgroundAsync(
        Guid assetId, string workQid, string backgroundPath, CancellationToken ct)
    {
        try
        {
            var imageDir = _imagePaths.GetWorkImageDir(workQid, assetId);
            var heroResult = await _heroGenerator.GenerateAsync(backgroundPath, imageDir, ct);

            var heroCanonicals = new List<CanonicalValue>
            {
                new()
                {
                    EntityId     = assetId,
                    Key          = "hero",
                    Value        = $"/stream/{assetId}/hero",
                    LastScoredAt = DateTimeOffset.UtcNow,
                },
            };
            heroCanonicals.AddRange(ArtworkCanonicalHelper.CreateFlags(
                assetId,
                coverState: "present",
                coverSource: null,
                heroState: "present",
                lastScoredAt: DateTimeOffset.UtcNow,
                settled: true));

            if (!string.IsNullOrEmpty(heroResult.DominantHexColor))
            {
                heroCanonicals.Add(new CanonicalValue
                {
                    EntityId     = assetId,
                    Key          = "dominant_color",
                    Value        = heroResult.DominantHexColor,
                    LastScoredAt = DateTimeOffset.UtcNow,
                });
            }

            await _canonicalRepo.UpsertBatchAsync(heroCanonicals, ct);

            _logger.LogInformation(
                "[IMAGE-ENRICH] Hero banner regenerated from background art for {WorkQid}", workQid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _canonicalRepo.UpsertBatchAsync(
                ArtworkCanonicalHelper.CreateFlags(
                    assetId,
                    coverState: "present",
                    coverSource: null,
                    heroState: "missing",
                    lastScoredAt: DateTimeOffset.UtcNow,
                    settled: true),
                ct);
            _logger.LogWarning(ex, "[IMAGE-ENRICH] Hero regeneration from background art failed for {WorkQid}", workQid);
        }
    }

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
        var lineage = await _workRepo.GetLineageByAssetAsync(entityId, ct);
        return lineage is null
            ? new ImageWorkContext(entityId, entityId)
            : new ImageWorkContext(entityId, lineage.TargetForParentScope);
    }

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

            // Create CharacterPortrait record
            await _portraitRepo.UpsertAsync(new CharacterPortrait
            {
                Id                = Guid.NewGuid(),
                PersonId          = personId,
                FictionalEntityId = entity.Id,
                ImageUrl          = bestMatch.Art.Url,
                SourceProvider    = "fanart_tv",
                IsDefault         = true,
                CreatedAt         = DateTimeOffset.UtcNow,
            }, ct);

            // Download portrait image
            var bytes = await DownloadImageBytesAsync(bestMatch.Art.Url, ct);
            if (bytes is not null && bytes.Length > 0)
            {
                var portraitDir = Path.Combine(
                    _imagePaths.GetPersonImageDir(entity.WikidataQid),
                    "characters");
                var portraitPath = Path.Combine(portraitDir, "portrait.jpg");
                await PersistImageAsync(bytes, portraitPath, bestMatch.Art.Url, ct);

                // Update local path on the portrait record
                await _portraitRepo.UpsertAsync(new CharacterPortrait
                {
                    Id                = Guid.NewGuid(),
                    PersonId          = personId,
                    FictionalEntityId = entity.Id,
                    ImageUrl          = bestMatch.Art.Url,
                    LocalImagePath    = portraitPath,
                    SourceProvider    = "fanart_tv",
                    IsDefault         = true,
                    CreatedAt         = DateTimeOffset.UtcNow,
                }, ct);
            }

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

    private static JsonArray? ResolveImageArray(JsonNode json, string jsonField)
    {
        if (json[jsonField] is JsonArray directArray && directArray.Count > 0)
            return directArray;

        if (json["albums"] is not JsonObject albums)
            return null;

        foreach (var (_, albumNode) in albums)
        {
            if (albumNode?[jsonField] is JsonArray nestedArray && nestedArray.Count > 0)
                return nestedArray;
        }

        return null;
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

    private sealed record ImageWorkContext(Guid AssetId, Guid CanonicalEntityId);
}
