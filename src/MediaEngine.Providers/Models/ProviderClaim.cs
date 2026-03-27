namespace MediaEngine.Providers.Models;

/// <summary>
/// A single metadata claim returned by an external provider adapter.
///
/// This type mirrors <c>MediaEngine.Processors.Models.ExtractedClaim</c> in shape
/// but is defined here to keep <c>MediaEngine.Providers</c> free of any dependency
/// on <c>MediaEngine.Processors</c>.  The harvesting service converts
/// <see cref="ProviderClaim"/> instances into
/// <see cref="Domain.Entities.MetadataClaim"/> rows before persisting them.
///
/// Spec: Phase 9 – External Metadata Adapters § Claim Shape.
/// </summary>
/// <param name="Key">
/// The metadata field name, e.g. <c>"cover"</c>, <c>"narrator"</c>,
/// <c>"series"</c>, <c>"wikidata_qid"</c>.
/// </param>
/// <param name="Value">
/// The provider's asserted value for <paramref name="Key"/>.
/// Always a string; the scoring engine interprets the type.
/// </param>
/// <param name="Confidence">
/// The adapter's confidence in this claim.  Range: 0.0–1.0.
/// Used by the scoring engine's conflict resolver.
/// </param>
/// <param name="SourceLanguage">
/// The BCP-47 language code of the API response that produced this claim.
/// <c>null</c> when the language is unknown or matches the user's metadata language.
/// Set to <c>"en"</c> when a provider fell back to English after a localized query returned no results.
/// </param>
public sealed record ProviderClaim(string Key, string Value, double Confidence, string? SourceLanguage = null);
