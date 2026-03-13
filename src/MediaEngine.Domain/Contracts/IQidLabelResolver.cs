namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Resolves a Wikidata QID to its human-readable display label.
///
/// The resolver checks the local <see cref="IQidLabelRepository"/> cache first.
/// If the QID is not cached, it returns the provided fallback string. This
/// ensures the UI always has something to display, even when offline.
///
/// Implementations live in <c>MediaEngine.Providers</c>.
/// </summary>
public interface IQidLabelResolver
{
    /// <summary>
    /// Resolves a single QID to its display label.
    /// Returns <paramref name="fallbackLabel"/> if the QID is not in the cache.
    /// </summary>
    Task<string> ResolveAsync(
        string qid,
        string fallbackLabel,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a batch of QIDs to their display labels.
    /// QIDs not in the cache are mapped to their raw QID string.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveBatchAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default);
}
