namespace Tanaste.Domain.Enums;

/// <summary>
/// Identifies the stage within the three-stage hydration pipeline.
///
/// The pipeline processes metadata enrichment in three sequential stages:
/// <list type="number">
///   <item><see cref="RetailMatch"/> — queries commercial providers for high-fidelity
///     assets (cover art, descriptions, narrator credits) and bridge identifiers.</item>
///   <item><see cref="UniversalBridge"/> — maps retail IDs to a Wikidata QID via SPARQL,
///     pulling authoritative series, franchise, and cross-platform identifiers.</item>
///   <item><see cref="HumanHub"/> — enriches every creator (author, narrator, director)
///     with headshots, biographies, and social links.</item>
/// </list>
///
/// Providers declare which stages they participate in via <c>hydration_stages</c>
/// in their configuration file.
/// </summary>
public enum HydrationStage
{
    /// <summary>
    /// Stage 1: Retail Match.
    /// Query commercial providers (Apple Books, Audnexus, Google Books, Open Library).
    /// All matching providers run; the scoring engine resolves field conflicts.
    /// </summary>
    RetailMatch = 1,

    /// <summary>
    /// Stage 2: Universal Bridge.
    /// Map bridge identifiers from Stage 1 to a Wikidata QID.
    /// SPARQL deep hydration pulls 50+ structured properties.
    /// </summary>
    UniversalBridge = 2,

    /// <summary>
    /// Stage 3: Human Hub.
    /// Enrich every creator referenced in canonical values with headshots,
    /// biographies, social links, and detailed role information.
    /// </summary>
    HumanHub = 3,
}
