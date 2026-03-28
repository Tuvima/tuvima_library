namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Orchestrates Stage 3 image enrichment for a work — fetches rich imagery
/// from Fanart.tv (backdrops, logos, banners, character art) and Wikidata P18
/// (entity images), stores typed assets, and matches character art to
/// performer-character pairs.
/// </summary>
public interface IImageEnrichmentService
{
    /// <summary>
    /// Enrich a work with images from Fanart.tv and Wikidata.
    /// Downloads backdrops, logos, banners for the work; matches character art
    /// to performer-character pairs; upgrades hero images from backdrops.
    /// </summary>
    /// <param name="workId">The work's database ID.</param>
    /// <param name="workQid">The work's confirmed Wikidata QID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnrichWorkImagesAsync(Guid workId, string workQid, CancellationToken ct = default);
}
