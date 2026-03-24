namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies the stage within the hydration pipeline.
///
/// The pipeline processes metadata enrichment in two stages:
/// <list type="number">
///   <item><see cref="RetailIdentification"/> — searches retail providers (Apple Books,
///     TMDB, MusicBrainz) using file metadata to identify the work and select cover art.
///     Runs per-file during ingestion. Deposits cover URL, description, bridge IDs.</item>
///   <item><see cref="WikidataBridge"/> — uses bridge IDs from Stage 1 to resolve
///     Wikidata edition and work QIDs. Runs as a deduplicated batch after all files
///     in the ingestion batch complete Stage 1. Links editions to works, works to
///     universes.</item>
/// </list>
///
/// Providers declare which stages they participate in via <c>hydration_stages</c>
/// in their configuration file.
/// </summary>
public enum HydrationStage
{
    /// <summary>
    /// Stage 1: Retail Identification.
    /// Searches retail providers using file metadata (ISBN, ASIN, title+author).
    /// Scores results against file metadata for auto-accept or review queue routing.
    /// Deposits cover art, description, and bridge IDs (Apple Books ID, ISBN, etc.).
    /// </summary>
    RetailIdentification = 1,

    /// <summary>
    /// Stage 2: Wikidata Bridge Resolution.
    /// Uses bridge IDs from Stage 1 to resolve Wikidata edition QID (for edition-aware
    /// media types) or work QID (for TV/Podcasts). Runs as a deduplicated batch.
    /// Collects all platform IDs from the entity into the bridge_ids table.
    /// </summary>
    WikidataBridge = 2,
}
