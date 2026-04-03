using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Discovers child entities from Wikidata: TV shows → seasons → episodes,
/// music albums → tracks, comic series → issues.
///
/// Child entity discovery is handled internally by <see cref="ReconciliationAdapter.FetchAsync"/>
/// when <c>PreResolvedQid</c> is provided. This worker logs that discovery is automatic
/// and takes no separate action.
///
/// Extracted from <c>HydrationPipelineService</c> child entity discovery section.
/// </summary>
public sealed class ChildEntityWorker
{
    private readonly IEnumerable<IExternalMetadataProvider> _providers;
    private readonly ILogger<ChildEntityWorker> _logger;

    public ChildEntityWorker(
        IEnumerable<IExternalMetadataProvider> providers,
        ILogger<ChildEntityWorker> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Discovers child entities for the given parent QID and persists claims
    /// (season_count, episode_count, child_entities_json, etc.).
    /// </summary>
    public async Task DiscoverAsync(Guid entityId, string parentQid, CancellationToken ct)
    {
        var reconAdapter = _providers
            .OfType<ReconciliationAdapter>()
            .FirstOrDefault();

        if (reconAdapter is null)
        {
            _logger.LogDebug("No ReconciliationAdapter available — skipping child entity discovery");
            return;
        }

        // Child entity discovery is handled internally by ReconciliationAdapter.FetchAsync
        // when a PreResolvedQid is provided. DiscoverChildEntitiesAsync is a private method
        // and cannot be called from workers directly.
        _logger.LogDebug(
            "Child entity discovery for QID {Qid} (entity {EntityId}) is performed automatically " +
            "during WikidataBridgeWorker full property fetch — no separate step required",
            parentQid, entityId);
    }
}
