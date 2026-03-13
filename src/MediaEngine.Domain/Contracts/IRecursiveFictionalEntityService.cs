using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Discovers and enriches fictional entities (characters, locations, organizations)
/// extracted from a work's Wikidata claims. Mirrors the pattern of
/// <see cref="IRecursiveIdentityService"/> for persons.
///
/// For each <see cref="FictionalEntityReference"/>: find-or-create by QID,
/// link to the work, set the fictional universe QID, and enqueue a harvest
/// request if the entity has not yet been enriched.
/// </summary>
public interface IRecursiveFictionalEntityService
{
    /// <summary>
    /// Process a batch of fictional entity references extracted from a work's claims.
    /// </summary>
    /// <param name="workQid">The Wikidata QID of the source work.</param>
    /// <param name="workLabel">Human-readable work label.</param>
    /// <param name="narrativeRootQid">The resolved narrative root QID for this work.</param>
    /// <param name="narrativeRootLabel">Human-readable narrative root label.</param>
    /// <param name="references">Fictional entity references to process.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnrichAsync(
        string workQid,
        string? workLabel,
        string narrativeRootQid,
        string? narrativeRootLabel,
        IReadOnlyList<FictionalEntityReference> references,
        CancellationToken ct = default);
}
