using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// CRUD operations for <see cref="EntityRelationship"/> graph edges.
/// </summary>
public interface IEntityRelationshipRepository
{
    /// <summary>
    /// Insert a relationship edge. Idempotent — duplicate edges
    /// (same subject, type, object) are silently ignored.
    /// </summary>
    Task CreateAsync(EntityRelationship edge, CancellationToken ct = default);

    /// <summary>
    /// Return all outgoing edges from a given subject QID.
    /// </summary>
    Task<IReadOnlyList<EntityRelationship>> GetBySubjectAsync(
        string subjectQid, CancellationToken ct = default);

    /// <summary>
    /// Return all incoming edges pointing to a given object QID.
    /// </summary>
    Task<IReadOnlyList<EntityRelationship>> GetByObjectAsync(
        string objectQid, CancellationToken ct = default);

    /// <summary>
    /// Return all edges (both directions) involving a given QID.
    /// </summary>
    Task<IReadOnlyList<EntityRelationship>> GetByEntityAsync(
        string qid, CancellationToken ct = default);

    /// <summary>
    /// Return all edges where both subject and object belong to the given universe.
    /// Used when building the full universe graph.
    /// </summary>
    Task<IReadOnlyList<EntityRelationship>> GetByUniverseAsync(
        IReadOnlyCollection<string> entityQids, CancellationToken ct = default);

    /// <summary>Return total edge count (for stats).</summary>
    Task<int> CountAsync(CancellationToken ct = default);
}
