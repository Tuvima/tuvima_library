using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// CRUD operations for <see cref="FictionalEntity"/> records and their
/// work-link junction table.
/// </summary>
public interface IFictionalEntityRepository
{
    /// <summary>Find a fictional entity by its Wikidata QID.</summary>
    Task<FictionalEntity?> FindByQidAsync(string qid, CancellationToken ct = default);

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
    /// Link a fictional entity to a work. Idempotent — duplicate links are ignored.
    /// </summary>
    /// <param name="entityId">The fictional entity's database ID.</param>
    /// <param name="workQid">The Wikidata QID of the work.</param>
    /// <param name="workLabel">Human-readable work label.</param>
    /// <param name="linkType">
    /// How the entity relates to the work: <c>"appears_in"</c>, <c>"set_in"</c>,
    /// <c>"features"</c>, etc.
    /// </param>
    Task LinkToWorkAsync(
        Guid entityId,
        string workQid,
        string? workLabel,
        string linkType = "appears_in",
        CancellationToken ct = default);

    /// <summary>
    /// Return all work QIDs linked to a fictional entity.
    /// </summary>
    Task<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>>
        GetWorkLinksAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Return total entity count (for stats).</summary>
    Task<int> CountAsync(CancellationToken ct = default);
}
