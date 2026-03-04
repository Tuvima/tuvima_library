using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the bridge between a Work and its physical manifestations.
/// Implemented by <see cref="Aggregates.Edition"/>.
/// Spec: Phase 2 – Interfaces § IEdition.
/// </summary>
public interface IEdition
{
    Guid Id { get; }
    Guid WorkId { get; }

    /// <summary>Optional label describing this physical form, e.g. "4K Bluray".</summary>
    string? FormatLabel { get; }

    IReadOnlyList<IMediaAsset> MediaAssets { get; }

    /// <summary>Provider-asserted key-value claims scoped to this Edition.</summary>
    IReadOnlyList<MetadataClaim> MetadataClaims { get; }

    /// <summary>Scored authoritative values resolved from <see cref="MetadataClaims"/>.</summary>
    IReadOnlyList<CanonicalValue> CanonicalValues { get; }
}
