namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Fuzzy-matches embedded file metadata against retail provider description text
/// to improve candidate ranking. Config-driven: field definitions, match types,
/// and weights are read from <c>config/description_matching.json</c>.
/// </summary>
public interface IDescriptionMatchService
{
    /// <summary>
    /// Scores a retail candidate's text fields against the file's embedded metadata.
    /// Returns a composite bonus score (0.0-1.0) and per-field breakdown.
    /// </summary>
    /// <param name="fileHints">File's embedded metadata (narrator, publisher, series, etc.).</param>
    /// <param name="candidateTitle">Retail candidate's title.</param>
    /// <param name="candidateDescription">Retail candidate's description text (may contain HTML).</param>
    /// <param name="candidateCopyright">Retail candidate's copyright field (nullable).</param>
    /// <param name="mediaType">Media category for selecting the right field config.</param>
    /// <returns>Result with composite score and per-field match details.</returns>
    DescriptionMatchResult Score(
        IReadOnlyDictionary<string, string> fileHints,
        string candidateTitle,
        string? candidateDescription,
        string? candidateCopyright,
        string mediaType);
}

/// <summary>
/// Result of description-based fuzzy matching for a single retail candidate.
/// </summary>
public sealed class DescriptionMatchResult
{
    /// <summary>Weighted composite bonus score (0.0-1.0).</summary>
    public double CompositeScore { get; init; }

    /// <summary>Per-field match details for UI display.</summary>
    public IReadOnlyList<DescriptionFieldMatch> FieldMatches { get; init; } = [];

    /// <summary>Creates an empty result (no matches).</summary>
    public static DescriptionMatchResult Empty => new() { CompositeScore = 0.0 };
}

/// <summary>
/// A single field's match result — shown to the user so they can see which
/// embedded metadata contributed to the candidate's ranking.
/// </summary>
public sealed class DescriptionFieldMatch
{
    /// <summary>The file hint key (e.g. "narrator", "publisher").</summary>
    public string FieldKey { get; init; } = "";

    /// <summary>The value from the file's embedded metadata.</summary>
    public string FileValue { get; init; } = "";

    /// <summary>Whether this field matched in the candidate's text.</summary>
    public bool Matched { get; init; }

    /// <summary>Raw fuzzy score (0-100) or 0/100 for contains matches.</summary>
    public int RawScore { get; init; }

    /// <summary>Weighted contribution to the composite score.</summary>
    public double WeightedScore { get; init; }

    /// <summary>The configured weight for this field.</summary>
    public double Weight { get; init; }

    /// <summary>Which text field was matched against (description, title, copyright).</summary>
    public string MatchedAgainst { get; init; } = "description";
}
