using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the intellectual properties of a specific title.
/// Implemented by <see cref="Aggregates.Work"/>.
/// Spec: Phase 2 – Interfaces § IWork.
/// </summary>
public interface IWork
{
    Guid Id { get; }

    /// <summary>Non-nullable: a Work MUST NOT exist without a parent Hub.</summary>
    Guid HubId { get; }

    MediaType MediaType { get; }

    /// <summary>Required when the Work belongs to an ordered series; otherwise null.</summary>
    int? SequenceIndex { get; }

    IReadOnlyList<IEdition> Editions { get; }

    /// <summary>Provider-asserted key-value claims about this Work (property bag).</summary>
    IReadOnlyList<MetadataClaim> MetadataClaims { get; }

    /// <summary>Scored authoritative values resolved from <see cref="MetadataClaims"/>.</summary>
    IReadOnlyList<CanonicalValue> CanonicalValues { get; }
}
