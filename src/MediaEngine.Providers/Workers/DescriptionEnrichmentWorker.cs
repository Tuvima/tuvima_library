using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Wikipedia description fetch and persistence.
/// </summary>
public sealed class DescriptionEnrichmentWorker
{
    private readonly ILogger<DescriptionEnrichmentWorker> _logger;

    public DescriptionEnrichmentWorker(ILogger<DescriptionEnrichmentWorker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fetches Wikipedia description via ReconciliationAdapter and persists as a claim.
    /// </summary>
    public Task EnrichAsync(Guid entityId, string qid, CancellationToken ct = default)
    {
        // TODO: Wire to ReconciliationAdapter.FetchWikipediaDescriptionAsync
        _logger.LogDebug("DescriptionEnrichmentWorker.EnrichAsync called for entity {Id} (QID {Qid})", entityId, qid);
        return Task.CompletedTask;
    }
}
