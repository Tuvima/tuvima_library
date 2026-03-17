using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Provides access to the Wikidata Wikibase REST API for qualifier extraction
/// and batch entity fetching. Supplements the ReconciliationAdapter's Data Extension
/// queries with capabilities the Extension API does not support.
///
/// <para>
/// This is a supplementary service, not a pipeline provider. It does not implement
/// <c>IExternalMetadataProvider</c>. It is consumed by HydrationPipelineService and
/// RecursiveFictionalEntityService for targeted Wikibase API calls.
/// </para>
/// </summary>
public interface IWikibaseApiService
{
    /// <summary>
    /// Fetches all statements for a specific property on an entity, including qualifiers.
    /// Used for extracting actor→character mappings from P161 (cast_member) + P453 (character).
    /// </summary>
    /// <param name="entityQid">Wikidata QID (e.g. "Q104686073" for Dune Part Two).</param>
    /// <param name="propertyId">Property to fetch (e.g. "P161" for cast_member).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of qualified statements; empty on error or missing property.</returns>
    Task<IReadOnlyList<QualifiedStatement>> GetClaimsAsync(
        string entityQid,
        string propertyId,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches full entity data for multiple entities in one batch call (max 50 per call).
    /// Automatically splits inputs exceeding 50 QIDs into multiple requests.
    /// Returns labels, descriptions, and sitelinks for each entity.
    /// </summary>
    /// <param name="qids">Wikidata QIDs to fetch (e.g. ["Q15072805", "Q3079065"]).</param>
    /// <param name="language">BCP-47 language code for labels and descriptions (default: "en").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of resolved entities; missing QIDs are omitted from the result.</returns>
    Task<IReadOnlyList<WikibaseEntity>> GetEntitiesBatchAsync(
        IReadOnlyList<string> qids,
        string language = "en",
        CancellationToken ct = default);

    /// <summary>
    /// Gets the Wikipedia sitelink title for an entity in the specified language.
    /// Returns <c>null</c> if no sitelink exists for that language.
    /// </summary>
    /// <param name="entityQid">Wikidata QID.</param>
    /// <param name="language">BCP-47 language code (default: "en").</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> GetSitelinkAsync(
        string entityQid,
        string language = "en",
        CancellationToken ct = default);
}
