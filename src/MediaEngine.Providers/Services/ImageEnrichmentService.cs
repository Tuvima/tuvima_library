using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Orchestrates Stage 3 image enrichment for a work — fetches rich imagery
/// from Fanart.tv (backdrops, logos, banners, character art) and Wikidata P18
/// (entity images), stores typed assets, and matches character art to
/// performer-character pairs.
/// </summary>
public sealed class ImageEnrichmentService : IImageEnrichmentService
{
    private readonly IEntityAssetRepository _assetRepo;
    private readonly ICharacterPortraitRepository _portraitRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly ILogger<ImageEnrichmentService> _logger;

    public ImageEnrichmentService(
        IEntityAssetRepository assetRepo,
        ICharacterPortraitRepository portraitRepo,
        ICanonicalValueRepository canonicalRepo,
        IFictionalEntityRepository entityRepo,
        ILogger<ImageEnrichmentService> logger)
    {
        ArgumentNullException.ThrowIfNull(assetRepo);
        ArgumentNullException.ThrowIfNull(portraitRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(logger);

        _assetRepo     = assetRepo;
        _portraitRepo  = portraitRepo;
        _canonicalRepo = canonicalRepo;
        _entityRepo    = entityRepo;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task EnrichWorkImagesAsync(Guid workId, string workQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workQid);

        _logger.LogInformation("[IMAGE-ENRICH] Starting image enrichment for work {WorkId} ({WorkQid})", workId, workQid);

        // Step 1: Read bridge IDs from canonical values (tmdb_id, tvdb_id, musicbrainz_id).
        var canonicals = await _canonicalRepo.GetByEntityAsync(workId, ct);
        var tmdbId = canonicals.FirstOrDefault(c => c.Key == "tmdb_id")?.Value;
        var tvdbId = canonicals.FirstOrDefault(c => c.Key == "tvdb_id")?.Value;
        var musicbrainzId = canonicals.FirstOrDefault(c => c.Key == "musicbrainz_id")?.Value;

        if (string.IsNullOrWhiteSpace(tmdbId) && string.IsNullOrWhiteSpace(tvdbId) && string.IsNullOrWhiteSpace(musicbrainzId))
        {
            _logger.LogDebug("[IMAGE-ENRICH] No bridge IDs for Fanart.tv — skipping work {WorkQid}", workQid);
            return;
        }

        // Step 2: Call Fanart.tv API to get work-level assets.
        // TODO: Call ConfigDrivenAdapter or direct HTTP with the Fanart.tv config.
        // For now, log the intent.
        _logger.LogDebug("[IMAGE-ENRICH] Would call Fanart.tv for work {WorkQid} (tmdb={TmdbId}, tvdb={TvdbId}, mb={MbId})",
            workQid, tmdbId, tvdbId, musicbrainzId);

        // Step 3: Store entity_assets rows for each image type (backdrop, logo, banner).
        // TODO: Parse Fanart.tv response and create EntityAsset records.

        // Step 4: Upgrade hero image from backdrop (SkiaSharp vignette + grain).
        // TODO: If backdrop downloaded, regenerate hero.jpg from backdrop.

        // Step 5: Walk character_performer_links for this work.
        // TODO: Get all (person_id, fictional_entity_id) pairs linked to workQid.

        // Step 6: Match Fanart characterart against fictional entity labels (fuzzy match).
        // TODO: Parse characterart array, fuzzy-match names, create CharacterPortrait records.

        // Step 7: Select default portraits by latest release date.
        // TODO: Query P577 publication dates for works linked to each character.

        // Step 8: Download images to organized filesystem paths.
        // TODO: Download to .universe/{Universe}/characters/{Name (QID)}/

        _logger.LogInformation("[IMAGE-ENRICH] Image enrichment complete for work {WorkId} ({WorkQid})", workId, workQid);
    }
}
