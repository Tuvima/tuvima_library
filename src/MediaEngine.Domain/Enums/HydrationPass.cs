namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies whether a hydration request runs the quick first pass (core identity
/// only) or the full universe lookup (deep enrichment).
///
/// <list type="bullet">
///   <item><see cref="Quick"/> — Pass 1: fetches core properties (title, author, year,
///     genre, series, series_position) plus bridge IDs from Wikidata, then Wikipedia
///     description and retail cover art. Skips fictional entity discovery, pseudonym
///     resolution, and deep person enrichment.</item>
///   <item><see cref="Universe"/> — Pass 2: runs the full 50+ property SPARQL deep
///     hydration, fictional entity discovery, character relationships, pseudonym
///     resolution, and universe graph writing.</item>
/// </list>
///
/// Spec: §3.24 — Two-Pass Enrichment Architecture.
/// </summary>
public enum HydrationPass
{
    /// <summary>
    /// Pass 1 — Quick Match. Core identity + bridge IDs + cover art.
    /// Runs immediately after ingestion for fast Dashboard appearance.
    /// </summary>
    Quick = 1,

    /// <summary>
    /// Pass 2 — Universe Lookup. Full deep enrichment.
    /// Runs when the system is idle, on a nightly schedule, or on user demand.
    /// </summary>
    Universe = 2,
}
