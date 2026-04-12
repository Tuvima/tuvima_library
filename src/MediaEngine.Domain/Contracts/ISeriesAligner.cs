namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered series alignment. Groups related works into ordered shelves
/// using Wikidata relationship properties (P527, P361, P155, P156).
/// </summary>
public interface ISeriesAligner
{
    /// <summary>
    /// Infer the position of a work within a series from its title and sibling titles.
    /// Returns null if position cannot be determined.
    /// </summary>
    Task<int?> InferPositionAsync(
        string workTitle,
        string seriesName,
        IReadOnlyList<string> siblingTitles,
        CancellationToken ct = default);

    /// <summary>
    /// Detect works that belong to the same series but aren't grouped.
    /// Returns pairs of (workId, suggestedCollectionId) for regrouping.
    /// </summary>
    Task<IReadOnlyList<(Guid WorkId, Guid SuggestedCollectionId)>> DetectUngroupedAsync(
        CancellationToken ct = default);
}
