namespace MediaEngine.Domain.Contracts;

/// <summary>
/// SQLite FTS5 search index for full-text search over works (title + author).
/// Replaces the in-memory FuzzySharp re-ranking that loaded all rows client-side.
/// </summary>
public interface ISearchIndexRepository
{
    /// <summary>
    /// Upserts a work's title and author in the FTS5 index, resolving work_id
    /// from a media asset entity ID via the editions → works join.
    /// </summary>
    Task UpsertByEntityIdAsync(Guid entityId, string? title, string? author, CancellationToken ct = default);

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
