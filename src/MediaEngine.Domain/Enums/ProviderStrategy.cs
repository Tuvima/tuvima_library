namespace MediaEngine.Domain.Enums;

/// <summary>
/// Defines how multiple retail providers collaborate during Stage 1 (Retail Identification)
/// of the hydration pipeline.
/// </summary>
public enum ProviderStrategy
{
    /// <summary>
    /// Providers run in rank order. First provider that returns claims wins; subsequent
    /// providers are skipped. Used when a single provider is sufficient (e.g. TMDB for Movies).
    /// </summary>
    Waterfall,

    /// <summary>
    /// All providers run independently with the same file metadata as input. Claims from
    /// all providers are merged and scored using per-field priority configuration.
    /// Used when providers complement each other (e.g. Apple Podcasts + Podcast Index).
    /// </summary>
    Cascade,

    /// <summary>
    /// Providers run in rank order, each receiving the previous provider's bridge IDs
    /// and metadata as additional input. Used when one provider identifies the item
    /// and a subsequent provider enriches it (e.g. MusicBrainz → Apple API for audiobooks).
    /// </summary>
    Sequential,
}
