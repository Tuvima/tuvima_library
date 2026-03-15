using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>Checks Wikidata revision IDs for a universe's entities to detect changes.</summary>
public interface ILoreDeltaService
{
    /// <summary>
    /// Compare cached Wikidata revision IDs against current revisions for all entities
    /// in the given universe that have a stored revision ID.
    /// </summary>
    Task<IReadOnlyList<LoreDeltaResult>> CheckForUpdatesAsync(
        string universeQid, CancellationToken ct = default);
}
