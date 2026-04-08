namespace MediaEngine.Domain.Contracts;

/// <summary>
/// SQLite FTS5 search index for full-text search over works.
/// Supports cross-language title search including original titles,
/// Wikidata alternate titles (aliases), authors, and descriptions.
/// </summary>
public interface ISearchIndexRepository
{
    /// <summary>
    /// Refreshes a work's row in the FTS5 index by reading the current
    /// canonical state from the database. <paramref name="entityId"/> may be
    /// either a media_assets id or a works id — both resolve to the same
    /// leaf work row. Self-scope fields (title, original_title, hero) are
    /// read from the asset row; parent-scope fields (author, description,
    /// alternate_title array) are read from the topmost Work row by walking
    /// the parent_work_id chain.
    /// </summary>
    Task UpsertByEntityIdAsync(Guid entityId, CancellationToken ct = default);

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
