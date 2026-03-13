using MediaEngine.Domain.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Resolves Wikidata QIDs to human-readable display labels using the
/// local <see cref="IQidLabelRepository"/> cache.
///
/// All resolution is local (SQLite lookup). No network calls are made.
/// When a QID is not in the cache, the fallback label is returned.
/// </summary>
public sealed class QidLabelResolver : IQidLabelResolver
{
    private readonly IQidLabelRepository _repo;

    public QidLabelResolver(IQidLabelRepository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        _repo = repo;
    }

    /// <inheritdoc/>
    public async Task<string> ResolveAsync(
        string qid,
        string fallbackLabel,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(qid))
            return fallbackLabel;

        var label = await _repo.GetLabelAsync(qid, ct).ConfigureAwait(false);
        return label ?? fallbackLabel;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> ResolveBatchAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default)
    {
        var qidList = qids.Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        if (qidList.Count == 0)
            return new Dictionary<string, string>();

        var cached = await _repo.GetLabelsAsync(qidList, ct).ConfigureAwait(false);

        // For uncached QIDs, map to the raw QID string as fallback.
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var qid in qidList)
        {
            result[qid] = cached.TryGetValue(qid, out var label) ? label : qid;
        }

        return result;
    }
}
