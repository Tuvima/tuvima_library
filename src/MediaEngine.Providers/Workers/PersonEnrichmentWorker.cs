using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Person extraction from claims, standalone Wikidata reconciliation, person enrichment.
/// Extracted from HydrationPipelineService for single-responsibility.
/// </summary>
public sealed class PersonEnrichmentWorker
{
    private readonly ILogger<PersonEnrichmentWorker> _logger;

    public PersonEnrichmentWorker(ILogger<PersonEnrichmentWorker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract person refs from claims, call PersonReconciliationService for unlinked names,
    /// enrich resolved persons.
    /// </summary>
    public Task EnrichFromClaimsAsync(Guid entityId, CancellationToken ct = default)
    {
        // TODO: Wire to PersonReferenceExtractor + PersonReconciliationService
        _logger.LogDebug("PersonEnrichmentWorker.EnrichFromClaimsAsync called for entity {Id} — delegated to legacy path", entityId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// P161 cast fetch, P453 character qualifiers, link actors to fictional entities.
    /// </summary>
    public Task EnrichActorCharacterMappingsAsync(Guid entityId, string workQid, CancellationToken ct = default)
    {
        // TODO: Wire to existing actor-character mapping logic
        _logger.LogDebug("PersonEnrichmentWorker.EnrichActorCharacterMappingsAsync called for entity {Id}", entityId);
        return Task.CompletedTask;
    }
}
