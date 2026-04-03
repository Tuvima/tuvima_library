using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Fictional entity enrichment: characters, locations, narrative root.
/// Uses INarrativeRootResolver and IRecursiveFictionalEntityService.
/// </summary>
public sealed class FictionalEntityWorker
{
    private readonly ILogger<FictionalEntityWorker> _logger;

    public FictionalEntityWorker(ILogger<FictionalEntityWorker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves narrative root, extracts character/location QIDs, dispatches to recursive enrichment.
    /// </summary>
    public Task EnrichAsync(Guid entityId, string workQid, CancellationToken ct = default)
    {
        // TODO: Wire to INarrativeRootResolver + IRecursiveFictionalEntityService
        _logger.LogDebug("FictionalEntityWorker.EnrichAsync called for entity {Id} (QID {Qid})", entityId, workQid);
        return Task.CompletedTask;
    }
}
