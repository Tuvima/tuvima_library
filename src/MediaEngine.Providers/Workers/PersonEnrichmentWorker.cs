using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Logging;
using Tuvima.Wikidata;

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
    private readonly IPersonRepository _personRepo;
    private readonly IFictionalEntityRepository _fictionalEntityRepo;
    private readonly ReconciliationAdapter? _reconciliationAdapter;
    private readonly PersonReconciliationService? _personReconciliation;
    private readonly ILogger<PersonEnrichmentWorker> _logger;

    public PersonEnrichmentWorker(
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IRecursiveIdentityService identity,
        IMetadataHarvestingService harvesting,
        IPersonRepository personRepo,
        IFictionalEntityRepository fictionalEntityRepo,
        ILogger<PersonEnrichmentWorker> logger,
        ReconciliationAdapter? reconciliationAdapter = null,
        PersonReconciliationService? personReconciliation = null)
    {
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _identity = identity;
        _harvesting = harvesting;
        _personRepo = personRepo;
        _fictionalEntityRepo = fictionalEntityRepo;
        _reconciliationAdapter = reconciliationAdapter;
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
        var personRefs = PersonReferenceExtractor.FromRawClaims(providerClaims, mediaType)
            .Concat(PersonReferenceExtractor.FromCanonicals(canonicals, mediaType))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.WikidataQid))
            .GroupBy(reference => reference.WikidataQid!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

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
                        _logger.LogInformation(
                            "Person enrichment writeback: {OldName} -> {NewName} ({Qid}) (role: {Role})",
                            unlinked.Name, searchResult.Name, searchResult.WikidataQid, unlinked.Role);

                        await PersistReconciledPersonAsync(
                            entityId, unlinked.Name, unlinked.Role,
                            searchResult.WikidataQid, searchResult.Name, ct);
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
    /// Creates or locates a Person record for a name-only person reference that was
    /// successfully reconciled to a Wikidata QID by <see cref="PersonReconciliationService"/>,
    /// writes back the resolved QID + canonical name, and writes a
    /// <c>person_media_links</c> row so the detail drawer shows library presence.
    ///
    /// Idempotent: uses INSERT OR IGNORE semantics via <see cref="IPersonRepository.LinkToMediaAssetAsync"/>.
    /// </summary>
    private async Task PersistReconciledPersonAsync(
        Guid mediaAssetId,
        string rawName,
        string role,
        string resolvedQid,
        string resolvedName,
        CancellationToken ct)
    {
        var existing = await _personRepo.FindByQidAsync(resolvedQid, ct).ConfigureAwait(false);

        Guid personId;
        if (existing is not null)
        {
            personId = existing.Id;
            if (!string.Equals(existing.Name, resolvedName, StringComparison.OrdinalIgnoreCase))
            {
                await _personRepo.UpdateEnrichmentAsync(existing.Id, resolvedQid,
                    headshotUrl: null, biography: null, name: resolvedName, ct).ConfigureAwait(false);
            }

            await _personRepo.AddRoleAsync(existing.Id, role, ct).ConfigureAwait(false);
        }
        else
        {
            var newPerson = await _personRepo.CreateAsync(new Person
            {
                Name = resolvedName,
                Roles = [role],
                WikidataQid = resolvedQid,
            }, ct).ConfigureAwait(false);

            personId = newPerson.Id;

            _logger.LogDebug(
                "Created person record for reconciled '{RawName}' -> '{ResolvedName}' ({Qid}), id={Id}",
                rawName, resolvedName, resolvedQid, personId);
        }

        await _personRepo.LinkToMediaAssetAsync(mediaAssetId, personId, role, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Persisted reconciled person '{Name}' ({Qid}) linked to asset {AssetId} as {Role}",
            resolvedName, resolvedQid, mediaAssetId, role);
    }

    /// <summary>
    /// Fetches P161 (cast member) claims with P453 (character role) qualifiers from Wikidata
    /// and links actors to fictional entities already known for the same work.
    /// </summary>
    public async Task EnrichActorCharacterMappingsAsync(
        Guid entityId, string workQid, CancellationToken ct)
    {
        if (_reconciliationAdapter is null)
        {
            _logger.LogDebug(
                "Actor-character mapping skipped for entity {EntityId} ({Qid}) because ReconciliationAdapter is unavailable",
                entityId, workQid);
            return;
        }

        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
        var mediaType = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (!string.Equals(mediaType, nameof(MediaType.Movies), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mediaType, nameof(MediaType.TV), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var extension = await _reconciliationAdapter.ExtendAsync([workQid], ["P161"], ct).ConfigureAwait(false);
        if (!extension.TryGetValue(workQid, out var properties)
            || !properties.TryGetValue("P161", out var castClaims)
            || castClaims.Count == 0)
        {
            _logger.LogDebug(
                "Actor-character mapping found no cast-member claims for entity {EntityId} ({Qid})",
                entityId, workQid);
            return;
        }

        var linkCount = 0;
        foreach (var castClaim in castClaims)
        {
            var performerQid = castClaim.Value?.EntityId ?? castClaim.Value?.RawValue;
            if (string.IsNullOrWhiteSpace(performerQid))
                continue;

            var person = await _personRepo.FindByQidAsync(performerQid, ct).ConfigureAwait(false);
            if (person is null)
                continue;

            if (!castClaim.Qualifiers.TryGetValue("P453", out var characterValues) || characterValues.Count == 0)
                continue;

            foreach (var characterValue in characterValues)
            {
                if (characterValue.Kind != WikidataValueKind.EntityId || string.IsNullOrWhiteSpace(characterValue.EntityId))
                    continue;

                var fictionalEntity = await _fictionalEntityRepo.FindByQidAsync(characterValue.EntityId, ct).ConfigureAwait(false);
                if (fictionalEntity is null)
                    continue;

                await _personRepo.LinkToCharacterAsync(person.Id, fictionalEntity.Id, workQid, ct).ConfigureAwait(false);
                linkCount++;
            }
        }

        _logger.LogInformation(
            "Actor-character mapping created {Count} link(s) for entity {EntityId} ({Qid})",
            linkCount,
            entityId,
            workQid);
    }
}
