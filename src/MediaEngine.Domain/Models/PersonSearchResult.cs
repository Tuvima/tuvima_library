namespace MediaEngine.Domain.Models;

/// <summary>
/// Result of a standalone person search against Wikidata.
/// Returned only when confidence meets the auto-accept threshold.
/// </summary>
/// <param name="WikidataQid">The resolved Wikidata QID (e.g. "Q5360976").</param>
/// <param name="Name">The person's display name from Wikidata.</param>
/// <param name="Score">Composite score (0.0–1.0) combining name similarity, occupation match, and notable work match.</param>
public sealed record PersonSearchResult(
    string WikidataQid,
    string Name,
    double Score);
