using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// A first-class appearance of a fictional entity in a work. It retains work-local
/// anchors, narrative context, spoiler boundaries, and provenance instead of
/// reducing an appearance to a bare entity/work pair.
/// </summary>
public sealed record FictionalEntityWorkLink(
    Guid FictionalEntityId,
    string WorkQid,
    string? WorkLabel,
    string LinkType,
    string? AppearanceRole = null,
    string? WorkContext = null,
    string? AnchorKind = null,
    string? AnchorValue = null,
    string? NarrativeTimeIndex = null,
    string? StartTime = null,
    string? EndTime = null,
    string? SpoilerForWorkQid = null,
    string? SourceProvider = null,
    string Provenance = "Wikidata",
    bool IsSupplemental = false,
    double? Confidence = null,
    string? AppearanceKey = null);

/// <summary>
/// CRUD operations for <see cref="FictionalEntity"/> records and their
/// work-link junction table.
/// </summary>
public interface IFictionalEntityRepository
{
    /// <summary>Find a fictional entity by its Wikidata QID.</summary>
    Task<FictionalEntity?> FindByQidAsync(string qid, CancellationToken ct = default);

    /// <summary>Find fictional entities by Wikidata QID in bounded batches.</summary>
    Task<IReadOnlyList<FictionalEntity>> FindByQidsAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default);

    /// <summary>Find a fictional entity by its database ID.</summary>
    Task<FictionalEntity?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Return all fictional entities belonging to a given narrative universe.
    /// </summary>
    Task<IReadOnlyList<FictionalEntity>> GetByUniverseAsync(
        string universeQid, CancellationToken ct = default);

    /// <summary>
    /// Return all fictional entities of a given sub-type within a universe.
    /// </summary>
    Task<IReadOnlyList<FictionalEntity>> GetByUniverseAndTypeAsync(
        string universeQid, string entitySubType, CancellationToken ct = default);

    /// <summary>
    /// Returns all fictional entities linked to a given work QID
    /// via the <c>fictional_entity_work_links</c> junction table.
    /// </summary>
    Task<IReadOnlyList<FictionalEntity>> GetByWorkQidAsync(
        string workQid, CancellationToken ct = default);

    /// <summary>Create a new fictional entity record.</summary>
    Task CreateAsync(FictionalEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Update enrichment data (description, image, enriched timestamp) after
    /// a successful SPARQL deep-hydration query.
    /// </summary>
    Task UpdateEnrichmentAsync(
        Guid entityId,
        string? description,
        string? imageUrl,
        DateTimeOffset enrichedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the authoritative narrative-universe identity discovered from Wikidata
    /// P1080. This is separate from enrichment fields so an absent P1080 never
    /// clears the universe resolved from an owned work.
    /// </summary>
    Task UpdateUniverseAsync(
        Guid entityId,
        string universeQid,
        string? universeLabel,
        CancellationToken ct = default);

    /// <summary>
    /// Persist a fictional-entity appearance. Idempotency is scoped to the complete
    /// appearance statement, including its work-local and spoiler context.
    /// </summary>
    Task LinkToWorkAsync(FictionalEntityWorkLink appearance, CancellationToken ct = default);

    /// <summary>
    /// Return all work QIDs linked to a fictional entity.
    /// </summary>
    Task<IReadOnlyList<FictionalEntityWorkLink>> GetWorkLinksAsync(
        Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// Return all work links for the supplied fictional entities in bounded batches.
    /// </summary>
    Task<IReadOnlyList<FictionalEntityWorkLink>> GetWorkLinksAsync(
        IEnumerable<Guid> entityIds,
        CancellationToken ct = default);

    /// <summary>Return total entity count (for stats).</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Update the Wikidata revision ID for Lore Delta tracking.</summary>
    Task UpdateRevisionAsync(Guid entityId, long revisionId, CancellationToken ct = default);

    /// <summary>
    /// Return entities whose enrichment timestamp is older than
    /// <paramref name="staleAfterDays"/> days, ordered by oldest first.
    /// Only entities that have been enriched at least once are returned.
    /// </summary>
    /// <param name="staleAfterDays">Age threshold in days.</param>
    /// <param name="limit">Maximum number of results.</param>
    Task<IReadOnlyList<FictionalEntity>> GetStaleEntitiesAsync(
        int staleAfterDays, int limit, CancellationToken ct = default);
}
