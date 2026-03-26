namespace MediaEngine.Domain.Models;

/// <summary>
/// Parsed result of a natural language search query.
/// The LLM translates user intent into structured filters.
/// </summary>
public sealed class IntentSearchResult
{
    /// <summary>Genre filters extracted from the query.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Mood/vibe filters extracted from the query.</summary>
    public IReadOnlyList<string> Moods { get; init; } = [];

    /// <summary>Year range filter (inclusive).</summary>
    public int? YearFrom { get; init; }

    /// <summary>Year range filter (inclusive).</summary>
    public int? YearTo { get; init; }

    /// <summary>Media type filters.</summary>
    public IReadOnlyList<Enums.MediaType> MediaTypes { get; init; } = [];

    /// <summary>Keyword filters for FTS5 search.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>LLM confidence in the intent parsing (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>The original natural language query.</summary>
    public required string OriginalQuery { get; init; }
}
