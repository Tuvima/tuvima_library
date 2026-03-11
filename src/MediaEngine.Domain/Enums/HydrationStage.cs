namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies the stage within the three-stage hydration pipeline.
///
/// The pipeline processes metadata enrichment in three sequential stages:
/// <list type="number">
///   <item><see cref="AuthorityMatch"/> — queries Wikidata (the authority source) for
///     the work's identity: QID, bridge IDs (ISBN, ASIN, TMDB, etc.), series, franchise,
///     and 50+ structured properties via SPARQL. Hub Intelligence and Person Enrichment
///     run as sub-steps of this stage.</item>
///   <item><see cref="ContextMatch"/> — queries Wikipedia for a human-readable description
///     using the QID from Stage 1. Skipped if no QID was resolved.</item>
///   <item><see cref="RetailMatch"/> — runs retail providers (Apple Books, Audnexus, etc.)
///     in waterfall order from <c>config/slots.json</c>. Uses bridge IDs from Stage 1
///     for precise identifier-based lookups instead of fuzzy title search.</item>
/// </list>
///
/// Providers declare which stages they participate in via <c>hydration_stages</c>
/// in their configuration file.
/// </summary>
public enum HydrationStage
{
    /// <summary>
    /// Stage 1: Authority Match.
    /// Wikidata resolves the work's identity via bridge IDs or title search,
    /// then runs SPARQL deep hydration for 50+ properties. Hub Intelligence
    /// and Person Enrichment run as sub-steps.
    /// </summary>
    AuthorityMatch = 1,

    /// <summary>
    /// Stage 2: Context Match.
    /// Wikipedia provides a human-readable description using the QID from Stage 1.
    /// Skipped if no QID was resolved. Failure is silent (description is optional).
    /// </summary>
    ContextMatch = 2,

    /// <summary>
    /// Stage 3: Retail Match.
    /// Runs retail providers in waterfall order (primary → secondary → tertiary)
    /// from <c>config/slots.json</c>. Uses bridge IDs from Stage 1 for precise
    /// lookups. Falls back to title search if no bridge IDs available.
    /// </summary>
    RetailMatch = 3,

    // ── Legacy values (backward compatibility) ──────────────────────────

    /// <summary>
    /// Legacy Stage 2 name. Mapped to <see cref="AuthorityMatch"/> at runtime.
    /// Preserved for backward compatibility with provider configs that use the old name.
    /// </summary>
    [System.Obsolete("Renamed to AuthorityMatch. Preserved for config backward compatibility.")]
    UniversalBridge = 10,

    /// <summary>
    /// Legacy Stage 3 name. Person enrichment is now part of Stage 1 (AuthorityMatch).
    /// Preserved for backward compatibility with provider configs that declare
    /// <c>hydration_stages: [1, 3]</c>.
    /// </summary>
    [System.Obsolete("Person enrichment is now part of AuthorityMatch. Preserved for config backward compatibility.")]
    HumanHub = 11,
}
