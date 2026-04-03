using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Orchestrates Stage 3 image enrichment for a work — fetches rich imagery
/// from Fanart.tv (backdrops, logos, banners, character art) and stores typed
/// assets. Uses direct HTTP calls (not ConfigDrivenAdapter) for full control
/// over the multi-image array response and character art name fields.
/// </summary>
public sealed class ImageEnrichmentService : IImageEnrichmentService
{
    private readonly IEntityAssetRepository _assetRepo;
    private readonly ICharacterPortraitRepository _portraitRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IProviderConfigurationRepository _providerConfigRepo;
    private readonly IHeroBannerGenerator _heroGenerator;
    private readonly IImageCacheRepository _imageCache;
    private readonly ImagePathService _imagePaths;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly ILogger<ImageEnrichmentService> _logger;

    private const string FanartBaseUrl = "https://webservice.fanart.tv/v3";
    private const double CharacterMatchThreshold = 0.70;

    /// <summary>
    /// Maps media type → list of (Fanart JSON field, AssetType to store).
    /// CoverArt is excluded — CoverArtWorker handles that separately.
    /// </summary>
    private static readonly Dictionary<string, (string JsonField, AssetType Type)[]> FieldMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Movies"] = [
            ("moviebackground", AssetType.Backdrop),
            ("hdmovielogo",     AssetType.Logo),
            ("moviebanner",     AssetType.Banner),
        ],
        ["TV"] = [
            ("showbackground", AssetType.Backdrop),
            ("hdtvlogo",       AssetType.Logo),
            ("tvbanner",       AssetType.Banner),
        ],
        ["Music"] = [
            ("artistbackground", AssetType.Backdrop),
            ("musiclogo",        AssetType.Logo),
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
        IFictionalEntityRepository entityRepo,
        IPersonRepository personRepo,
        IProviderConfigurationRepository providerConfigRepo,
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
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(providerConfigRepo);
        ArgumentNullException.ThrowIfNull(heroGenerator);
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(imagePaths);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(fuzzy);
        ArgumentNullException.ThrowIfNull(logger);

        _assetRepo           = assetRepo;
        _portraitRepo        = portraitRepo;
        _canonicalRepo       = canonicalRepo;
        _entityRepo          = entityRepo;
        _personRepo          = personRepo;
        _providerConfigRepo  = providerConfigRepo;
        _heroGenerator       = heroGenerator;
        _imageCache          = imageCache;
        _imagePaths          = imagePaths;
        _httpFactory          = httpFactory;
        _fuzzy               = fuzzy;
        _logger              = logger;
    }

    /// <inheritdoc/>
    public async Task EnrichWorkImagesAsync(Guid workId, string workQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workQid);

        _logger.LogInformation("[IMAGE-ENRICH] Starting image enrichment for work {WorkId} ({WorkQid})", workId, workQid);

        // ── Step 1: Read bridge IDs from canonical values ──
        var canonicals = await _canonicalRepo.GetByEntityAsync(workId, ct);
        var tmdbId = GetCanonical(canonicals, BridgeIdKeys.TmdbId);
        var tvdbId = GetCanonical(canonicals, BridgeIdKeys.TvdbId);
        var mbId   = GetCanonical(canonicals, BridgeIdKeys.MusicBrainzId);
        var mediaTypeStr = GetCanonical(canonicals, MetadataFieldConstants.MediaTypeField);

        if (tmdbId is null && tvdbId is null && mbId is null)
        {
            _logger.LogDebug("[IMAGE-ENRICH] No bridge IDs for Fanart.tv — skipping work {WorkQid}", workQid);
            return;
        }

        // ── Step 2: Resolve API key ──
        var apiKey = await _providerConfigRepo.GetDecryptedValueAsync(
            WellKnownProviders.FanartTv.ToString(), "api_key", ct);

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[IMAGE-ENRICH] No Fanart.tv API key configured — skipping");
            return;
        }

        // ── Step 3: Call Fanart.tv API ──
        var (json, resolvedMediaType) = await CallFanartApiAsync(tmdbId, tvdbId, mbId, mediaTypeStr, apiKey, ct);
        if (json is null) return;

        // ── Step 4: Parse response and download work-level assets ──
        string? backdropPath = null;
        if (FieldMappings.TryGetValue(resolvedMediaType, out var mappings))
        {
            foreach (var (jsonField, assetType) in mappings)
            {
                var localPath = await ProcessImageArrayAsync(
                    json, jsonField, assetType, workId, workQid, resolvedMediaType, ct);

                if (assetType == AssetType.Backdrop && localPath is not null)
                    backdropPath = localPath;
            }
        }

        // ── Step 5: Regenerate hero from backdrop (higher quality than cover) ──
        if (backdropPath is not null && File.Exists(backdropPath))
        {
            await RegenerateHeroFromBackdropAsync(workId, workQid, backdropPath, ct);
        }

        // ── Steps 6–7: Character art matching ──
        if (CharacterArtFields.TryGetValue(resolvedMediaType, out var charField))
        {
            await MatchCharacterArtAsync(json, charField, workId, workQid, ct);
        }

        _logger.LogInformation("[IMAGE-ENRICH] Image enrichment complete for work {WorkId} ({WorkQid})", workId, workQid);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the appropriate Fanart.tv endpoint based on available bridge IDs.
    /// Returns the parsed JSON response and the resolved media type string.
    /// </summary>
    private async Task<(JsonNode? Json, string MediaType)> CallFanartApiAsync(
        string? tmdbId, string? tvdbId, string? mbId,
        string? mediaTypeStr, string apiKey, CancellationToken ct)
    {
        // Determine URL and media type from available bridge IDs
        string url;
        string resolvedMediaType;

        if (tmdbId is not null && IsMovieType(mediaTypeStr))
        {
            url = $"{FanartBaseUrl}/movies/{tmdbId}?api_key={apiKey}";
            resolvedMediaType = "Movies";
        }
        else if (tvdbId is not null)
        {
            url = $"{FanartBaseUrl}/tv/{tvdbId}?api_key={apiKey}";
            resolvedMediaType = "TV";
        }
        else if (tmdbId is not null)
        {
            // TMDB ID without explicit movie type — try TV (TMDB IDs work for both)
            url = $"{FanartBaseUrl}/movies/{tmdbId}?api_key={apiKey}";
            resolvedMediaType = "Movies";
        }
        else if (mbId is not null)
        {
            url = $"{FanartBaseUrl}/music/{mbId}?api_key={apiKey}";
            resolvedMediaType = "Music";
        }
        else
        {
            return (null, string.Empty);
        }

        try
        {
            using var client = _httpFactory.CreateClient("fanart_tv");
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
        Guid workId, string workQid, string mediaType,
        CancellationToken ct)
    {
        var imageArray = json[jsonField]?.AsArray();
        if (imageArray is null || imageArray.Count == 0) return null;

        // Pick best image: prefer English, then highest likes
        var best = imageArray
            .Where(n => n is not null)
            .OrderByDescending(n =>
                string.Equals(n!["lang"]?.GetValue<string>(), "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(n => n!["likes"]?.GetValue<int>() ?? 0)
            .FirstOrDefault();

        var imageUrl = best?["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(imageUrl)) return null;

        // Resolve local path based on asset type
        var localPath = assetType switch
        {
            AssetType.Backdrop => _imagePaths.GetWorkBackdropPath(workQid, workId),
            AssetType.Logo     => _imagePaths.GetWorkLogoPath(workQid, workId),
            AssetType.Banner   => _imagePaths.GetWorkBannerPath(workQid, workId),
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
            EntityId       = workId.ToString(),
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
    /// Regenerates the hero banner from a high-res backdrop image,
    /// which produces better results than the cover-art-derived hero.
    /// </summary>
    private async Task RegenerateHeroFromBackdropAsync(
        Guid workId, string workQid, string backdropPath, CancellationToken ct)
    {
        try
        {
            var imageDir = _imagePaths.GetWorkImageDir(workQid, workId);
            var heroResult = await _heroGenerator.GenerateAsync(backdropPath, imageDir, ct);

            var heroCanonicals = new List<CanonicalValue>
            {
                new()
                {
                    EntityId     = workId,
                    Key          = "hero",
                    Value        = $"/stream/{workId}/hero",
                    LastScoredAt = DateTimeOffset.UtcNow,
                },
            };

            if (!string.IsNullOrEmpty(heroResult.DominantHexColor))
            {
                heroCanonicals.Add(new CanonicalValue
                {
                    EntityId     = workId,
                    Key          = "dominant_color",
                    Value        = heroResult.DominantHexColor,
                    LastScoredAt = DateTimeOffset.UtcNow,
                });
            }

            await _canonicalRepo.UpsertBatchAsync(heroCanonicals, ct);

            _logger.LogInformation(
                "[IMAGE-ENRICH] Hero banner regenerated from backdrop for {WorkQid}", workQid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[IMAGE-ENRICH] Hero regeneration from backdrop failed for {WorkQid}", workQid);
        }
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

    /// <summary>Gets a canonical value by key, or null if missing.</summary>
    private static string? GetCanonical(IReadOnlyList<CanonicalValue> canonicals, string key) =>
        canonicals.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Checks if the media type string indicates a movie (not TV).</summary>
    private static bool IsMovieType(string? mediaType) =>
        string.Equals(mediaType, "Movies", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase);
}
