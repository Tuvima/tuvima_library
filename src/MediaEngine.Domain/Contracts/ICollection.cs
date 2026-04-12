using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the root of the Collection Aggregate.
/// Implemented by <see cref="Collection"/>.
/// Spec: Phase 2 – Interfaces § ICollection.
/// </summary>
public interface ICollection
{
    Guid Id { get; }
    Guid? UniverseId { get; }
    DateTimeOffset CreatedAt { get; }

    /// <summary>Works that belong to this Collection.</summary>
    IReadOnlyList<IWork> Works { get; }
}
