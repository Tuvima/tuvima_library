using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Contract for the recursive person enrichment service.
///
/// After a media asset is ingested, the ingestion engine passes the extracted
/// author and narrator references to this service. It ensures each person has
/// a <see cref="Entities.Person"/> record, links them to the media asset, and
/// returns any pending <see cref="HarvestRequest"/> items for persons not yet
/// enriched. The caller decides whether to enqueue them for background
/// processing or process them synchronously.
///
/// Implementations live in <c>MediaEngine.Providers</c>.
/// Spec: Phase 9 – Recursive Person Enrichment.
/// </summary>
public interface IRecursiveIdentityService
{
    /// <summary>
    /// Processes a list of person references for a newly ingested media asset.
    ///
    /// For each reference:
    /// 1. Looks up or creates a <see cref="Entities.Person"/> record.
    /// 2. Creates a <c>person_media_links</c> row linking the person to the asset.
    /// 3. If the person has not yet been enriched (<c>EnrichedAt</c> is <c>null</c>),
    ///    creates a <see cref="HarvestRequest"/> with <c>EntityType.Person</c>.
    ///
    /// Returns the list of pending person harvest requests. The caller is
    /// responsible for either enqueuing them (background) or processing them
    /// synchronously (e.g. during review resolution).
    /// </summary>
    /// <param name="mediaAssetId">The media asset the persons are associated with.</param>
    /// <param name="persons">
    /// The person references extracted from the asset's metadata.
    /// May be empty; no-op if so.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Harvest requests for persons that need enrichment. Empty if all
    /// persons are already enriched or if the input list is empty.
    /// </returns>
    Task<IReadOnlyList<HarvestRequest>> EnrichAsync(
        Guid mediaAssetId,
        IReadOnlyList<PersonReference> persons,
        CancellationToken ct = default);
}
