using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Models;

/// <summary>
/// All inputs required by <see cref="Contracts.IScoringEngine.ScoreEntityAsync"/>.
///
/// Bundling inputs into a context object keeps the engine interface stable as
/// new tuning parameters are added, and makes unit testing straightforward.
///
/// Spec: Phase 6 – Provider Agnosticism (the engine sees only claims and weights,
/// never provider-specific logic).
/// </summary>
public sealed class ScoringContext
{
    /// <summary>
    /// The entity being scored — either a <c>Work.Id</c> or an <c>Edition.Id</c>.
    /// Copied verbatim into the returned <see cref="ScoringResult.EntityId"/>.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// All <see cref="MetadataClaim"/> records for <see cref="EntityId"/>.
    /// The engine groups these by <c>ClaimKey</c> internally.
    /// Must not be null; may be empty (yields an empty result with zero confidence).
    /// </summary>
    public required IReadOnlyList<MetadataClaim> Claims { get; init; }

    /// <summary>
    /// Map of <c>ProviderId → weight</c> for every provider whose claims appear
    /// in <see cref="Claims"/>.  Providers absent from this map default to weight 1.0.
    ///
    /// Typically populated from <see cref="MediaEngine.Storage.Models.ProviderBootstrap.Weight"/>
    /// values loaded at startup.
    /// </summary>
    public required IReadOnlyDictionary<Guid, double> ProviderWeights { get; init; }

    /// <summary>
    /// Scoring thresholds and decay parameters for this evaluation.
    /// Use <see cref="ScoringConfiguration"/> defaults when no explicit config is available.
    /// </summary>
    public required ScoringConfiguration Configuration { get; init; }

    /// <summary>
    /// Optional per-provider, per-field weight overrides.
    /// Outer key = <c>ProviderId</c>; inner key = claim key (e.g. <c>"cover"</c>,
    /// <c>"narrator"</c>); value = weight in [0.0, 1.0].
    ///
    /// When the scoring engine resolves a field it consults this map first:
    ///   effective weight = ProviderFieldWeights[providerId][fieldKey]
    ///                      ?? ProviderWeights[providerId]
    ///                      ?? 1.0 (absolute fallback)
    ///
    /// <c>null</c> means "no field-specific overrides — use ProviderWeights for all fields."
    /// Existing callers that do not supply field weights continue to work unchanged.
    ///
    /// Populated from provider config files at scoring time, keyed by the resolved
    /// provider GUID from <c>provider_registry</c>.
    /// Spec: Phase 8 – Field-Level Weight Matrix.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, double>>?
        ProviderFieldWeights { get; init; }

    /// <summary>
    /// The detected media type for this entity. Used by the scoring engine to
    /// apply media-type-aware confidence floors (Principle 6).
    /// Default: <see cref="MediaType.Unknown"/> (no floor applied).
    /// </summary>
    public MediaType DetectedMediaType { get; init; } = MediaType.Unknown;

    /// <summary>
    /// Additive confidence boost from the Library Folder's category configuration.
    /// Category-specific folders contribute +0.10, multi-type folders +0.05,
    /// general catch-all folders 0.00. Applied after computing average field
    /// confidence; result capped at 1.0.
    /// Default: 0.0 (no boost — backward compatible with single-folder setups).
    /// </summary>
    public double CategoryConfidencePrior { get; init; }
}
