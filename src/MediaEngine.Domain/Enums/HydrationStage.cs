namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies the stage within the hydration pipeline.
///
/// The pipeline processes metadata enrichment in two stages:
/// <list type="number">
///   <item><see cref="Reconciliation"/> — resolves the work's identity via Wikidata
///     reconciliation, deposits bridge IDs, and performs deep enrichment. This is the
///     first stage and establishes the canonical identity of the media item.</item>
///   <item><see cref="Enrichment"/> — post-confirmation parallel enrichment. Runs
///     retail providers (Apple API, TMDB, etc.) and deep universe lookup after
///     identity is confirmed. Uses bridge IDs from Reconciliation for precise lookups.</item>
/// </list>
///
/// Providers declare which stages they participate in via <c>hydration_stages</c>
/// in their configuration file.
///
/// TODO: Phase 3 - Full pipeline stages will be expanded when ReconciliationAdapter
/// (dotNetRDF-based SPARQL) is implemented.
/// </summary>
public enum HydrationStage
{
    /// <summary>
    /// Stage 1: Reconciliation.
    /// Wikidata reconciliation resolves the work's identity via bridge IDs or title search,
    /// then performs deep enrichment for structured properties. Hub Intelligence
    /// and Person Enrichment run as sub-steps.
    ///
    /// Replaces former AuthorityMatch (1) + ContextMatch (2) stages.
    /// </summary>
    Reconciliation = 1,

    /// <summary>
    /// Stage 2: Enrichment.
    /// Post-confirmation parallel enrichment. Runs retail providers in waterfall
    /// order (primary → secondary → tertiary). Uses bridge IDs from Reconciliation
    /// for precise lookups. Falls back to title search if no bridge IDs available.
    ///
    /// Replaces former RetailMatch (3) stage.
    /// </summary>
    Enrichment = 2,
}
