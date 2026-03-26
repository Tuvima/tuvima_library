using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Parses natural language search queries into structured filters.
/// Enhances the existing Command Palette search.
/// </summary>
public interface IIntentSearchParser
{
    /// <summary>
    /// Parse a natural language query into structured search filters.
    /// Uses text_fast model (~200ms). Falls back to keyword extraction on failure.
    /// </summary>
    Task<IntentSearchResult> ParseAsync(string naturalLanguageQuery, CancellationToken ct = default);
}
