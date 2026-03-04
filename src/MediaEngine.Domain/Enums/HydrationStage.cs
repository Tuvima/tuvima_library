namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies the stage within the two-stage hydration pipeline.
///
/// The pipeline processes metadata enrichment in two sequential stages:
/// <list type="number">
///   <item><see cref="RetailMatch"/> — queries the <b>primary</b> provider (from
///     <c>config/slots.json</c>) for high-fidelity assets and bridge identifiers.</item>
///   <item><see cref="UniversalBridge"/> — maps retail IDs to a Wikidata QID via SPARQL,
///     pulling authoritative series, franchise, and cross-platform identifiers.
///     Person enrichment runs as a sub-step of this stage.</item>
/// </list>
///
/// Providers declare which stages they participate in via <c>hydration_stages</c>
/// in their configuration file.
/// </summary>
public enum HydrationStage
{
    /// <summary>
    /// Stage 1: Content Match.
    /// Runs only the primary provider for the file's media type (from slots.json).
    /// If no match is found, a ContentMatchFailed review item is created.
    /// </summary>
    RetailMatch = 1,

    /// <summary>
    /// Stage 2: Universe Match.
    /// Map bridge identifiers from Stage 1 to a Wikidata QID.
    /// SPARQL deep hydration pulls 50+ structured properties.
    /// Person enrichment runs as part of this stage.
    /// </summary>
    UniversalBridge = 2,

    /// <summary>
    /// Stage 3: Human Hub (deprecated).
    /// Person enrichment is now performed as part of Stage 2.
    /// This value is preserved for backward compatibility with provider configs
    /// that declare <c>hydration_stages: [1, 3]</c>.
    /// </summary>
    [System.Obsolete("Person enrichment is now part of Stage 2. Preserved for config backward compatibility.")]
    HumanHub = 3,
}
