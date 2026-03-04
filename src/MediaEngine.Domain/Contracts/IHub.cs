using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the root of the Hub Aggregate.
/// Implemented by <see cref="Hub"/>.
/// Spec: Phase 2 – Interfaces § IHub.
/// </summary>
public interface IHub
{
    Guid Id { get; }
    Guid? UniverseId { get; }
    DateTimeOffset CreatedAt { get; }

    /// <summary>Works that belong to this Hub.</summary>
    IReadOnlyList<IWork> Works { get; }
}
