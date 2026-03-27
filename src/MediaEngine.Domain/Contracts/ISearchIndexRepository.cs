namespace MediaEngine.Domain.Contracts;

/// <summary>
/// SQLite FTS5 search index for full-text search over works.
/// Supports cross-language title search including original titles,
/// Wikidata alternate titles (aliases), authors, and descriptions.
/// </summary>
public interface ISearchIndexRepository
{
    /// <summary>
    /// Upserts a work's search data into the FTS5 index, resolving work_id
    /// from a media asset entity ID via the editions → works join.
    /// Indexes title, original title, alternate titles, author, and description
    /// for prefix matching and BM25 ranking across all languages.
    /// </summary>
    Task UpsertByEntityIdAsync(
        Guid entityId,
        string? title,
        string? originalTitle,
        string? alternateTitles,
        string? author,
        string? description,
        CancellationToken ct = default);

    /// <summary>
    /// Full-text search using FTS5 MATCH with BM25 ranking.
    /// Returns matching work IDs ordered by relevance.
    /// </summary>
    Task<IReadOnlyList<Guid>> SearchAsync(string query, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Drops and repopulates the entire FTS5 index from canonical_values.
    /// </summary>
    Task RebuildAsync(CancellationToken ct = default);
}
