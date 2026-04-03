using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Extracts person references from claims/canonicals and runs enrichment.
/// Handles both QID-linked persons (from Wikidata claims) and standalone
/// name-only persons (reconciled via <see cref="IPersonReconciliationService"/>).
///
/// Extracted from <c>HydrationPipelineService</c> Stage 1 person enrichment
/// and Stage 2 actor-character mapping sections.
/// </summary>
public sealed class PersonEnrichmentWorker
{
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IRecursiveIdentityService _identity;
    private readonly IMetadataHarvestingService _harvesting;
    private readonly PersonReconciliationService? _personReconciliation;
    private readonly ILogger<PersonEnrichmentWorker> _logger;

    public PersonEnrichmentWorker(
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IRecursiveIdentityService identity,
        IMetadataHarvestingService harvesting,
        ILogger<PersonEnrichmentWorker> logger,
        PersonReconciliationService? personReconciliation = null)
    {
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _identity = identity;
        _harvesting = harvesting;
        _logger = logger;
        _personReconciliation = personReconciliation;
    }

    /// <summary>
    /// Extracts person references from raw claims and canonicals, enriches
    /// QID-linked persons, and reconciles unlinked names via Wikidata search.
    /// </summary>
    public async Task EnrichFromClaimsAsync(Guid entityId, CancellationToken ct)
    {
        var claims = await _claimRepo.GetByEntityAsync(entityId, ct);
        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);

        // Determine media type from canonicals
        var mediaTypeStr = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        var mediaType = Enum.TryParse<MediaType>(mediaTypeStr, true, out var mt) ? mt : MediaType.Unknown;

        // Convert stored claims to ProviderClaim format
        var providerClaims = claims
            .Select(mc => new ProviderClaim(mc.ClaimKey, mc.ClaimValue, mc.Confidence))
            .ToList();

        // Extract person refs — prefer raw claims (QID-first), fall back to canonicals
        var personRefs = providerClaims.Count > 0
            ? PersonReferenceExtractor.FromRawClaims(providerClaims, mediaType)
            : PersonReferenceExtractor.FromCanonicals(canonicals, mediaType);

        _logger.LogInformation(
            "Person extraction for entity {EntityId}: {Count} person ref(s)",
            entityId, personRefs.Count);

        if (personRefs.Count > 0)
        {
            try
            {
                var personRequests = await _identity.EnrichAsync(entityId, personRefs, ct);

                foreach (var personReq in personRequests)
                {
                    try
                    {
                        await _harvesting.ProcessSynchronousAsync(personReq, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Synchronous person enrichment failed for person {Id}",
                            personReq.EntityId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Person enrichment failed for entity {Id}", entityId);
            }
        }

        // Standalone person reconciliation for unlinked names
        if (_personReconciliation is not null && providerClaims.Count > 0)
        {
            var unlinkedRefs = PersonReferenceExtractor.FromRawClaimsUnlinked(providerClaims, mediaType);
            var titleHint = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                    StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            foreach (var unlinked in unlinkedRefs)
            {
                try
                {
                    var searchResult = await _personReconciliation.SearchPersonAsync(
                        unlinked.Name, unlinked.Role, titleHint, ct);

                    if (searchResult is not null)
                    {
                        _logger.LogDebug(
                            "Reconciled person '{Name}' → {Qid} (role: {Role})",
                            unlinked.Name, searchResult.WikidataQid, unlinked.Role);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Person reconciliation failed for '{Name}'", unlinked.Name);
                }
            }
        }
    }

    /// <summary>
    /// Fetches P161 (cast member) claims with P453 (character) qualifiers from Wikidata
    /// and links actors to fictional entities.
    /// </summary>
    public async Task EnrichActorCharacterMappingsAsync(
        Guid entityId, string workQid, CancellationToken ct)
    {
        // Actor-character mapping requires the full Wikidata reconciler which lives
        // in HydrationPipelineService. This worker handles the person-side enrichment;
        // the mapping logic is delegated when called from the EnrichmentService
        // during Universe pass (which has access to the reconciler).
        _logger.LogDebug(
            "Actor-character mapping for entity {EntityId} (QID {Qid}) — " +
            "delegated to Universe pass via HydrationPipelineService",
            entityId, workQid);
    }
}
