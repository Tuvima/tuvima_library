using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// CRUD operations for <see cref="NarrativeRoot"/> records.
/// </summary>
public interface INarrativeRootRepository
{
    /// <summary>Find a narrative root by its Wikidata QID.</summary>
    Task<NarrativeRoot?> FindByQidAsync(string qid, CancellationToken ct = default);

    /// <summary>Return all known narrative roots.</summary>
    Task<IReadOnlyList<NarrativeRoot>> ListAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Insert or update a narrative root. If a row with the same QID exists,
    /// its label, level, and parent are updated.
    /// </summary>
    Task UpsertAsync(NarrativeRoot root, CancellationToken ct = default);

    /// <summary>Persist a user-authored root label/description override without changing authoritative QID identity.</summary>
    Task UpdateUserDetailsAsync(string qid, string label, string? description, CancellationToken ct = default);

    /// <summary>Return work identities whose canonical root/universe provenance resolves to the QID.</summary>
    Task<IReadOnlyList<Guid>> FindWorkIdsByProvenanceQidAsync(string qid, CancellationToken ct = default);

    /// <summary>
    /// Return all children (franchises within a universe, series within a franchise).
    /// </summary>
    Task<IReadOnlyList<NarrativeRoot>> GetChildrenAsync(
        string parentQid, CancellationToken ct = default);

    /// <summary>Return total narrative root count (for stats).</summary>
    Task<int> CountAsync(CancellationToken ct = default);
}
