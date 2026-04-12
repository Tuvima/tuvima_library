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

    /// <summary>
    /// Optional legacy Collection. Phase 4 collapses this onto
    /// <see cref="ParentWorkId"/>; the column is expected to disappear.
    /// </summary>
    Guid? CollectionId { get; }

    MediaType MediaType { get; }

    /// <summary>Role in the parent/child hierarchy (M-081).</summary>
    WorkKind WorkKind { get; }

    /// <summary>Parent Work in the hierarchy, or null for standalone/root.</summary>
    Guid? ParentWorkId { get; }

    /// <summary>Position within parent (track, episode, issue, volume).</summary>
    int? Ordinal { get; }

    /// <summary>True when this Work has no file in the library yet.</summary>
    bool IsCatalogOnly { get; }

    IReadOnlyList<IEdition> Editions { get; }

    /// <summary>Provider-asserted key-value claims about this Work (property bag).</summary>
    IReadOnlyList<MetadataClaim> MetadataClaims { get; }

    /// <summary>Scored authoritative values resolved from <see cref="MetadataClaims"/>.</summary>
    IReadOnlyList<CanonicalValue> CanonicalValues { get; }
}
