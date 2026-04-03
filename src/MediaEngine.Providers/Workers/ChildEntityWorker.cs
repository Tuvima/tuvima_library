using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Child entity discovery: TV seasons/episodes, album tracks, comic issues.
/// Uses ReconciliationAdapter.GetChildEntitiesAsync (Tuvima.Wikidata v1.0.0).
/// </summary>
public sealed class ChildEntityWorker
{
    private readonly ILogger<ChildEntityWorker> _logger;

    public ChildEntityWorker(ILogger<ChildEntityWorker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovers child entities for a parent QID (TV show → episodes, album → tracks, etc.).
    /// </summary>
    public Task DiscoverAsync(Guid entityId, string parentQid, CancellationToken ct = default)
    {
        // TODO: Wire to ReconciliationAdapter.DiscoverChildEntitiesAsync
        _logger.LogDebug("ChildEntityWorker.DiscoverAsync called for entity {Id} (QID {Qid})", entityId, parentQid);
        return Task.CompletedTask;
    }
}
