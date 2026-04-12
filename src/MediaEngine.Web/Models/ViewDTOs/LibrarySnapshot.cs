namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Pre-computed library statistics passed to insight generators.
/// Gathered by LibraryInsightService (future), consumed by any generator.
/// </summary>
public sealed record LibrarySnapshot
{
    public int TotalCollections { get; init; }
    public IReadOnlyDictionary<string, int> CollectionsByMediaType { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> CollectionsByGenre { get; init; } = new Dictionary<string, int>();
    public int ActiveJourneyCount { get; init; }
    public int UnfinishedCount { get; init; }
    public string? MostActiveGenre { get; init; }
    public string? MostActiveAuthor { get; init; }
    public int RecentlyAddedCount { get; init; }
}
