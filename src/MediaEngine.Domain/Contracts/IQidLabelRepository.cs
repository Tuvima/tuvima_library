namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the QID label cache.
///
/// Every Wikidata Q-identifier encountered by the system (authors, genres,
/// characters, franchises, locations) is cached here with its human-readable
/// label. This provides offline-resilient display-name resolution without
/// requiring a network call to Wikidata at render time.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface IQidLabelRepository
{
    /// <summary>
    /// Returns the cached label for a single QID, or <c>null</c> if not cached.
    /// </summary>
    Task<string?> GetLabelAsync(string qid, CancellationToken ct = default);

    /// <summary>
    /// Returns cached labels for a batch of QIDs.
    /// QIDs not in the cache are omitted from the result dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a single QID label entry.
    /// </summary>
    Task UpsertAsync(
        string qid,
        string label,
        string? description,
        string? entityType,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a batch of QID label entries.
    /// </summary>
    Task UpsertBatchAsync(
        IReadOnlyList<QidLabel> labels,
        CancellationToken ct = default);

    /// <summary>
    /// Returns full label details (label, description, entity type) for a batch of QIDs.
    /// QIDs not in the cache are omitted from the result.
    /// </summary>
    Task<IReadOnlyList<QidLabel>> GetLabelDetailsAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all cached QID labels. Used for diagnostics and export.
    /// </summary>
    Task<IReadOnlyList<QidLabel>> GetAllAsync(CancellationToken ct = default);
}

/// <summary>
/// A cached QID-to-label mapping entry.
/// </summary>
public sealed class QidLabel
{
    public required string Qid { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public string? EntityType { get; init; }
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
