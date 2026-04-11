namespace MediaEngine.Domain.Enums;

/// <summary>
/// Classifies a provider's primary contribution to the pipeline.
/// Used by the decision service to determine whether a retail match
/// produced a usable bridge ID or only assets (cover art, descriptions).
/// </summary>
public enum ProviderRole
{
    /// <summary>Primarily provides cover art, descriptions, ratings (e.g. Google Books covers).</summary>
    Asset,

    /// <summary>Primarily provides bridge IDs for Wikidata lookup (e.g. ISBN, TMDB ID).</summary>
    Identifier,

    /// <summary>Both asset and identifier (e.g. Apple API, TMDB).</summary>
    Mixed,

    /// <summary>Wikidata — the authority for canonical identity.</summary>
    Canonical
}
