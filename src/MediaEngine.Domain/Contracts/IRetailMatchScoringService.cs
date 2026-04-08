using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Scores retail provider search results against file metadata
/// to determine match confidence for Stage 1 (Retail Identification).
/// </summary>
public interface IRetailMatchScoringService
{
    /// <summary>
    /// Scores a single retail candidate against file metadata hints.
    /// </summary>
    /// <param name="fileHints">File metadata: title, author, year, narrator, isbn, asin, etc.</param>
    /// <param name="candidateTitle">Title from the retail result.</param>
    /// <param name="candidateAuthor">Author from the retail result.</param>
    /// <param name="candidateYear">Year from the retail result.</param>
    /// <param name="mediaType">Media type of the file being scored.</param>
    /// <param name="matchTiers">Optional tier config from the provider. When null, uses fuzzy match only.</param>
    /// <param name="extendedMetadata">Optional extended metadata (description, publisher, duration, genres) for cross-field scoring.</param>
    /// <param name="structuralBonus">Optional additive bonus (e.g. TV S/E exact match) added to the composite after the weighted average and before clamping. Default 0.0 (no effect).</param>
    /// <returns>Composite confidence score (0.0–1.0) and per-field breakdown.</returns>
    FieldMatchScores ScoreCandidate(
        IReadOnlyDictionary<string, string> fileHints,
        string? candidateTitle,
        string? candidateAuthor,
        string? candidateYear,
        MediaType mediaType,
        MatchTierConfig? matchTiers = null,
        CandidateExtendedMetadata? extendedMetadata = null,
        double structuralBonus = 0.0);
}

/// <summary>
/// Match tier configuration for a specific media type within a provider config.
/// Read from the provider's <c>match_tiers</c> JSON section.
/// </summary>
public sealed class MatchTierConfig
{
    /// <summary>Tier 1 IDs: hardware/universal (e.g. ISBN). Exact match = 1.0.</summary>
    public List<string> Tier1 { get; set; } = [];

    /// <summary>Tier 2 IDs: platform (e.g. Apple Books ID, ASIN). Exact match = 1.0.</summary>
    public List<string> Tier2 { get; set; } = [];

    /// <summary>Tier 3 IDs: industry (e.g. IMDb, MusicBrainz). Exact match = 1.0.</summary>
    public List<string> Tier3 { get; set; } = [];

    /// <summary>Tier 4 fields for fuzzy matching (e.g. title, author, year).</summary>
    public List<string> Tier4Fields { get; set; } = ["title", "author", "year"];
}
