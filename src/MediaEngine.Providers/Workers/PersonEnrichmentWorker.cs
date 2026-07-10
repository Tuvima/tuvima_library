using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
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
    private readonly ICanonicalValueArrayRepository? _canonicalArrayRepo;
    private readonly IRecursiveIdentityService _identity;
    private readonly IMetadataHarvestingService _harvesting;
    private readonly IPersonRepository _personRepo;
    private readonly IFictionalEntityRepository _fictionalEntityRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly ReconciliationAdapter? _reconciliationAdapter;
    private readonly IPersonReconciliationService? _personReconciliation;
    private readonly PersonImageEnrichmentWorker? _personImages;
    private readonly ILogger<PersonEnrichmentWorker> _logger;

    public PersonEnrichmentWorker(
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IRecursiveIdentityService identity,
        IMetadataHarvestingService harvesting,
        IPersonRepository personRepo,
        IFictionalEntityRepository fictionalEntityRepo,
        ICollectionRepository collectionRepo,
        ILogger<PersonEnrichmentWorker> logger,
        ReconciliationAdapter? reconciliationAdapter = null,
        IPersonReconciliationService? personReconciliation = null,
        PersonImageEnrichmentWorker? personImages = null,
        ICanonicalValueArrayRepository? canonicalArrayRepo = null)
    {
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _identity = identity;
        _harvesting = harvesting;
        _personRepo = personRepo;
        _fictionalEntityRepo = fictionalEntityRepo;
        _collectionRepo = collectionRepo;
        _reconciliationAdapter = reconciliationAdapter;
        _logger = logger;
        _personReconciliation = personReconciliation;
        _personImages = personImages;
        _canonicalArrayRepo = canonicalArrayRepo;
    }

    /// <summary>
    /// Extracts person references from raw claims and canonicals, enriches
    /// QID-linked persons, and reconciles unlinked names via Wikidata search.
    /// </summary>
    public async Task EnrichFromClaimsAsync(Guid entityId, CancellationToken ct)
    {
        var claims = (await _claimRepo.GetByEntityAsync(entityId, ct)).ToList();
        var canonicals = (await _canonicalRepo.GetByEntityAsync(entityId, ct)).ToList();
        var canonicalArrays = new Dictionary<string, List<CanonicalArrayEntry>>(StringComparer.OrdinalIgnoreCase);
        await AddCanonicalArraysAsync(entityId, canonicalArrays, ct).ConfigureAwait(false);

        // Lineage-aware scoring stores TV/movie people on Work rows. TV cast is
        // commonly routed to the parent show row while enrichment is invoked
        // for the episode asset, so read the whole Work lineage before
        // extracting credits. Links still point at the owned file asset.
        IReadOnlyList<Guid> lineageWorkIds = await _collectionRepo.GetWorkLineageIdsByMediaAssetAsync(entityId, ct)
            .ConfigureAwait(false);
        if (lineageWorkIds.Count == 0)
        {
            var workId = await _collectionRepo.GetWorkIdByMediaAssetAsync(entityId, ct)
                .ConfigureAwait(false);
            if (workId.HasValue)
                lineageWorkIds = new[] { workId.Value };
        }

        foreach (var lineageWorkId in lineageWorkIds.Where(id => id != entityId).Distinct())
        {
            claims.AddRange(await _claimRepo.GetByEntityAsync(lineageWorkId, ct).ConfigureAwait(false));
            canonicals.AddRange(await _canonicalRepo.GetByEntityAsync(lineageWorkId, ct).ConfigureAwait(false));
            await AddCanonicalArraysAsync(lineageWorkId, canonicalArrays, ct).ConfigureAwait(false);
        }

        // Determine media type from canonicals
        var mediaTypeStr = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        var mediaType = Enum.TryParse<MediaType>(mediaTypeStr, true, out var mt) ? mt : MediaType.Unknown;

        // Convert stored claims to ProviderClaim format
        var providerClaims = claims
            .Select(mc => new ProviderClaim(mc.ClaimKey, mc.ClaimValue, mc.Confidence))
            .ToList();
        var tmdbImageHints = BuildTmdbImageHints(providerClaims);
        var readOnlyCanonicalArrays = ToReadOnlyCanonicalArrays(canonicalArrays);

        // Extract person refs — prefer raw claims (QID-first), fall back to canonicals
        var personRefs = PersonReferenceExtractor.FromRawClaims(providerClaims, mediaType)
            .Concat(PersonReferenceExtractor.FromCanonicalArrays(readOnlyCanonicalArrays, mediaType))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.WikidataQid))
            .GroupBy(reference => $"{reference.WikidataQid}::{reference.Role}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        _logger.LogInformation(
            "Person extraction for entity {EntityId}: {Count} person ref(s)",
            entityId, personRefs.Count);

        if (personRefs.Count > 0)
        {
            var failures = new List<Exception>();

            try
            {
                var personRequests = await _identity.EnrichAsync(entityId, personRefs, ct);
                var imageEnrichedPeople = new HashSet<Guid>();

                foreach (var personReq in personRequests)
                {
                    try
                    {
                        await _harvesting.ProcessSynchronousAsync(personReq, ct);
                        await EnsurePersonHarvestCompletedAsync(personReq.EntityId, ct).ConfigureAwait(false);
                        await EnrichPersonImageAsync(
                            personReq.EntityId,
                            personReq.Hints.GetValueOrDefault("role"),
                            mediaType,
                            FindTmdbImageHint(tmdbImageHints, personReq.Hints.GetValueOrDefault("role"), personReq.Hints.GetValueOrDefault("name")),
                            ct);
                        imageEnrichedPeople.Add(personReq.EntityId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Synchronous person enrichment failed for person {Id}",
                            personReq.EntityId);
                        failures.Add(ex);
                    }
                }

                if (_personImages is not null)
                {
                    foreach (var reference in personRefs)
                    {
                        var person = await _personRepo.FindByQidAsync(reference.WikidataQid!, ct)
                            .ConfigureAwait(false);
                        if (person is not null && imageEnrichedPeople.Add(person.Id))
                            await EnrichPersonImageAsync(
                                person.Id,
                                reference.Role,
                                mediaType,
                                FindTmdbImageHint(tmdbImageHints, reference.Role, reference.Name),
                                ct);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Person enrichment failed for entity {Id}", entityId);
                failures.Add(ex);
            }

            if (failures.Count > 0)
            {
                _logger.LogWarning(
                    "Person enrichment completed with {FailureCount} linked profile/detail failure(s) for entity {EntityId}; persisted person links remain usable and ingestion will continue",
                    failures.Count,
                    entityId);
            }
        }

        // Standalone person reconciliation for unlinked names
        if (_personReconciliation is not null)
        {
            var unlinkedRefs = PersonReferenceExtractor.FromRawClaimsUnlinked(providerClaims, mediaType)
                .Concat(PersonReferenceExtractor.FromCanonicalArrays(readOnlyCanonicalArrays, mediaType)
                    .Where(reference => string.IsNullOrWhiteSpace(reference.WikidataQid)))
                .GroupBy(reference => $"{reference.Role}::{reference.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var titleHint = ResolvePersonWorkTitleHint(canonicals, mediaType) ?? "";

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

                        var harvestRequest = await PersistReconciledPersonAsync(
                            entityId, unlinked.Name, unlinked.Role,
                            searchResult.WikidataQid, searchResult.Name, ct);

                        if (harvestRequest is not null)
                        {
                            try
                            {
                                await _harvesting.ProcessSynchronousAsync(harvestRequest, ct);
                                await EnsurePersonHarvestCompletedAsync(harvestRequest.EntityId, ct).ConfigureAwait(false);
                                await EnrichPersonImageAsync(
                                    harvestRequest.EntityId,
                                    unlinked.Role,
                                    mediaType,
                                    FindTmdbImageHint(tmdbImageHints, unlinked.Role, unlinked.Name),
                                    ct);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _logger.LogWarning(ex,
                                    "Synchronous person enrichment failed for reconciled person {Id}",
                                    harvestRequest.EntityId);
                                throw;
                            }
                        }
                        else
                        {
                            var existing = await _personRepo.FindByQidAsync(searchResult.WikidataQid, ct)
                                .ConfigureAwait(false);
                            if (existing is not null)
                                await EnrichPersonImageAsync(
                                    existing.Id,
                                    unlinked.Role,
                                    mediaType,
                                    FindTmdbImageHint(tmdbImageHints, unlinked.Role, unlinked.Name),
                                    ct);
                        }
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

    private async Task AddCanonicalArraysAsync(
        Guid entityId,
        Dictionary<string, List<CanonicalArrayEntry>> target,
        CancellationToken ct)
    {
        if (_canonicalArrayRepo is null)
            return;

        var arrays = await _canonicalArrayRepo.GetAllByEntityAsync(entityId, ct).ConfigureAwait(false);
        foreach (var (key, values) in arrays)
        {
            if (!target.TryGetValue(key, out var list))
            {
                list = [];
                target[key] = list;
            }

            list.AddRange(values);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CanonicalArrayEntry>> ToReadOnlyCanonicalArrays(
        IReadOnlyDictionary<string, List<CanonicalArrayEntry>> arrays)
    {
        var result = new Dictionary<string, IReadOnlyList<CanonicalArrayEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in arrays)
            result[key] = values;

        return result;
    }

    private async Task EnsurePersonHarvestCompletedAsync(Guid personId, CancellationToken ct)
    {
        var person = await _personRepo.FindByIdAsync(personId, ct).ConfigureAwait(false);
        if (person?.EnrichedAt is null)
        {
            throw new InvalidOperationException(
                $"Person {personId} was queued for enrichment but did not complete.");
        }
    }

    private async Task EnrichPersonImageAsync(
        Guid personId,
        string? role,
        MediaType mediaType,
        TmdbPersonImageHint? tmdbImageHint,
        CancellationToken ct)
    {
        if (_personImages is null)
            return;

        try
        {
            await _personImages.EnrichAsync(
                personId,
                role,
                mediaType,
                ct,
                tmdbImageHint?.PersonId,
                tmdbImageHint?.ProfileUrl).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Person image enrichment failed for person {PersonId}", personId);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, TmdbPersonImageHint> BuildTmdbImageHints(IReadOnlyList<ProviderClaim> claims)
    {
        var hints = new Dictionary<string, TmdbPersonImageHint>(StringComparer.OrdinalIgnoreCase);

        AddTmdbImageHints(hints, "Actor", ValuesFor(claims, MetadataFieldConstants.CastMember), ValuesFor(claims, "cast_member_tmdb_id"), ValuesFor(claims, "cast_member_profile_url"));
        AddTmdbImageHints(hints, "Director", ValuesFor(claims, "director"), ValuesFor(claims, "director_tmdb_id"), ValuesFor(claims, "director_profile_url"));
        AddTmdbImageHints(hints, "Screenwriter", ValuesFor(claims, "screenwriter"), ValuesFor(claims, "screenwriter_tmdb_id"), ValuesFor(claims, "screenwriter_profile_url"));
        AddTmdbImageHints(hints, "Composer", ValuesFor(claims, "composer"), ValuesFor(claims, "composer_tmdb_id"), ValuesFor(claims, "composer_profile_url"));
        AddTmdbImageHints(hints, "Producer", ValuesFor(claims, "producer"), ValuesFor(claims, "producer_tmdb_id"), ValuesFor(claims, "producer_profile_url"));

        return hints;
    }

    private static void AddTmdbImageHints(
        Dictionary<string, TmdbPersonImageHint> hints,
        string role,
        IReadOnlyList<string> names,
        IReadOnlyList<string> ids,
        IReadOnlyList<string> profileUrls)
    {
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            int? personId = null;
            if (i < ids.Count && int.TryParse(ids[i], out var parsedId))
                personId = parsedId;

            var profileUrl = i < profileUrls.Count ? profileUrls[i] : null;
            if (!personId.HasValue && string.IsNullOrWhiteSpace(profileUrl))
                continue;

            hints[$"{role}::{NormalizePersonName(name)}"] = new TmdbPersonImageHint(personId, profileUrl);
        }
    }

    private static TmdbPersonImageHint? FindTmdbImageHint(
        IReadOnlyDictionary<string, TmdbPersonImageHint> hints,
        string? role,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || hints.Count == 0)
            return null;

        var normalizedName = NormalizePersonName(name);
        if (!string.IsNullOrWhiteSpace(role)
            && hints.TryGetValue($"{role}::{normalizedName}", out var exact))
        {
            return exact;
        }

        return hints.TryGetValue($"Actor::{normalizedName}", out var actorHint)
            ? actorHint
            : null;
    }

    private static IReadOnlyList<string> ValuesFor(IReadOnlyList<ProviderClaim> claims, string key)
        => claims
            .Where(claim => string.Equals(claim.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(claim => claim.Value)
            .ToList();

    private static string NormalizePersonName(string name)
        => string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string? ResolvePersonWorkTitleHint(
        IReadOnlyList<CanonicalValue> canonicals,
        MediaType mediaType)
    {
        string? Value(string key) => canonicals
            .FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return mediaType switch
        {
            MediaType.TV => FirstNonBlank(
                Value(MetadataFieldConstants.ShowName),
                Value(MetadataFieldConstants.Series),
                Value(MetadataFieldConstants.Title)),
            MediaType.Music => FirstNonBlank(
                Value(MetadataFieldConstants.Album),
                Value(MetadataFieldConstants.Title)),
            _ => FirstNonBlank(Value(MetadataFieldConstants.Title), Value(MetadataFieldConstants.Series)),
        };
    }

    private sealed record TmdbPersonImageHint(int? PersonId, string? ProfileUrl);

    /// <summary>
    /// Creates or locates a Person record for a name-only person reference that was
    /// successfully reconciled to a Wikidata QID by <see cref="PersonReconciliationService"/>,
    /// writes back the resolved QID + canonical name, and writes a
    /// <c>person_media_links</c> row so the detail drawer shows library presence.
    ///
    /// Idempotent: uses INSERT OR IGNORE semantics via <see cref="IPersonRepository.LinkToMediaAssetAsync"/>.
    /// </summary>
    private async Task<HarvestRequest?> PersistReconciledPersonAsync(
        Guid mediaAssetId,
        string rawName,
        string role,
        string resolvedQid,
        string resolvedName,
        CancellationToken ct)
    {
        var existing = await _personRepo.FindByQidAsync(resolvedQid, ct).ConfigureAwait(false);

        Guid personId;
        bool needsHarvest;
        if (existing is not null)
        {
            personId = existing.Id;
            needsHarvest = existing.EnrichedAt is null;
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
            needsHarvest = true;

            _logger.LogDebug(
                "Created person record for reconciled '{RawName}' -> '{ResolvedName}' ({Qid}), id={Id}",
                rawName, resolvedName, resolvedQid, personId);
        }

        try
        {
            await _personRepo.LinkToMediaAssetAsync(mediaAssetId, personId, role, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Persisted reconciled person '{Name}' ({Qid}) linked to asset {AssetId} as {Role}",
                resolvedName, resolvedQid, mediaAssetId, role);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "Could not link reconciled person '{Name}' ({Qid}) to asset {AssetId}; continuing enrichment",
                resolvedName, resolvedQid, mediaAssetId);
        }

        if (!needsHarvest)
            return null;

        return new HarvestRequest
        {
            EntityId = personId,
            EntityType = EntityType.Person,
            MediaType = MediaType.Unknown,
            Hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = resolvedName,
                ["role"] = role,
                [BridgeIdKeys.WikidataQid] = resolvedQid,
            },
        };
    }

    /// <summary>
    /// Fetches P161 (cast member) claims with P453 (character role) qualifiers from Wikidata
    /// and links actors to fictional entities already known for the same work.
    /// </summary>
    public async Task EnrichActorCharacterMappingsAsync(
        Guid entityId, string workQid, CancellationToken ct)
    {
        var candidateEntityIds = await ResolveActorCharacterArrayEntityIdsAsync(entityId, ct).ConfigureAwait(false);
        var canonicals = await LoadActorCharacterCanonicalsAsync(candidateEntityIds, ct).ConfigureAwait(false);
        var hasArrayEvidence = await HasActorCharacterArrayEvidenceAsync(candidateEntityIds, ct).ConfigureAwait(false);
        var mediaType = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (!IsVideoMediaType(mediaType) && !hasArrayEvidence)
        {
            return;
        }

        if (_reconciliationAdapter is null)
        {
            var fallbackOnlyCount = await LinkActorCharacterMappingsFromCanonicalArraysAsync(candidateEntityIds, workQid, canonicals, ct)
                .ConfigureAwait(false);
            _logger.LogDebug(
                "Actor-character mapping skipped live Wikidata for entity {EntityId} ({Qid}) because ReconciliationAdapter is unavailable; canonical fallback created {Count} link(s)",
                entityId,
                workQid,
                fallbackOnlyCount);
            return;
        }

        var extension = await _reconciliationAdapter.ExtendAsync([workQid], ["P161"], ct).ConfigureAwait(false);
        if (!extension.TryGetValue(workQid, out var properties)
            || !properties.TryGetValue("P161", out var castClaims)
            || castClaims.Count == 0)
        {
            var fallbackOnlyCount = await LinkActorCharacterMappingsFromCanonicalArraysAsync(candidateEntityIds, workQid, canonicals, ct)
                .ConfigureAwait(false);
            _logger.LogDebug(
                "Actor-character mapping found no live cast-member claims for entity {EntityId} ({Qid}); canonical fallback created {Count} link(s)",
                entityId,
                workQid,
                fallbackOnlyCount);
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
                {
                    fictionalEntity = new FictionalEntity
                    {
                        Id = Guid.NewGuid(),
                        WikidataQid = characterValue.EntityId,
                        Label = ResolveCharacterLabel(characterValue),
                        EntitySubType = FictionalEntityType.Character,
                        FictionalUniverseQid = ResolveCanonicalQid(canonicals, "fictional_universe_qid")
                            ?? ResolveCanonicalQid(canonicals, "franchise_qid")
                            ?? ResolveCanonicalQid(canonicals, "series_qid")
                            ?? workQid,
                        FictionalUniverseLabel = ResolveCanonicalLabel(canonicals, "fictional_universe")
                            ?? ResolveCanonicalLabel(canonicals, "franchise")
                            ?? ResolveCanonicalLabel(canonicals, "series"),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };

                    await _fictionalEntityRepo.CreateAsync(fictionalEntity, ct).ConfigureAwait(false);
                }

                await _fictionalEntityRepo.LinkToWorkAsync(fictionalEntity.Id, workQid, null, "portrayed_in", ct)
                    .ConfigureAwait(false);

                await _personRepo.LinkToCharacterAsync(person.Id, fictionalEntity.Id, workQid, ct).ConfigureAwait(false);
                await EnqueueCharacterHarvestIfNeededAsync(fictionalEntity, ct).ConfigureAwait(false);
                linkCount++;
            }
        }

        linkCount += await LinkActorCharacterMappingsFromCanonicalArraysAsync(candidateEntityIds, workQid, canonicals, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Actor-character mapping created {Count} link(s) for entity {EntityId} ({Qid})",
            linkCount,
            entityId,
            workQid);
    }

    private async Task<int> LinkActorCharacterMappingsFromCanonicalArraysAsync(
        IReadOnlyList<Guid> entityIds,
        string workQid,
        IReadOnlyList<CanonicalValue> canonicals,
        CancellationToken ct)
    {
        if (_canonicalArrayRepo is null)
            return 0;

        var linkCount = 0;

        foreach (var candidateEntityId in entityIds)
        {
            var arrays = await _canonicalArrayRepo.GetAllByEntityAsync(candidateEntityId, ct).ConfigureAwait(false);
            if (!arrays.TryGetValue(MetadataFieldConstants.CastMember, out var castMembers)
                || !arrays.TryGetValue(MetadataFieldConstants.Characters, out var characters)
                || castMembers.Count == 0
                || characters.Count == 0)
            {
                continue;
            }

            var charactersByOrdinal = characters
                .Where(entry => NormalizeEntityQid(entry.ValueQid) is not null)
                .GroupBy(entry => entry.Ordinal)
                .ToDictionary(group => group.Key, group => group.First());

            var singleCharacter = characters.Count == 1
                ? characters.FirstOrDefault(entry => NormalizeEntityQid(entry.ValueQid) is not null)
                : null;

            foreach (var castMember in castMembers)
            {
                var personQid = NormalizeEntityQid(castMember.ValueQid);
                if (personQid is null)
                    continue;

                var character = charactersByOrdinal.GetValueOrDefault(castMember.Ordinal) ?? singleCharacter;
                var characterQid = NormalizeEntityQid(character?.ValueQid);
                if (character is null || characterQid is null)
                    continue;

                var person = await _personRepo.FindByQidAsync(personQid, ct).ConfigureAwait(false)
                    ?? await _personRepo.FindByNameAsync(castMember.Value, ct).ConfigureAwait(false)
                    ?? await _personRepo.CreateAsync(new Person
                    {
                        Name = castMember.Value,
                        Roles = ["Actor"],
                        WikidataQid = personQid,
                    }, ct).ConfigureAwait(false);

                await _personRepo.AddRoleAsync(person.Id, "Actor", ct).ConfigureAwait(false);

                var fictionalEntity = await _fictionalEntityRepo.FindByQidAsync(characterQid, ct).ConfigureAwait(false);
                if (fictionalEntity is null)
                {
                    fictionalEntity = new FictionalEntity
                    {
                        Id = Guid.NewGuid(),
                        WikidataQid = characterQid,
                        Label = character.Value,
                        EntitySubType = FictionalEntityType.Character,
                        FictionalUniverseQid = ResolveCanonicalQid(canonicals, "fictional_universe_qid")
                            ?? ResolveCanonicalQid(canonicals, "franchise_qid")
                            ?? ResolveCanonicalQid(canonicals, "series_qid")
                            ?? workQid,
                        FictionalUniverseLabel = ResolveCanonicalLabel(canonicals, "fictional_universe")
                            ?? ResolveCanonicalLabel(canonicals, "franchise")
                            ?? ResolveCanonicalLabel(canonicals, "series"),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };

                    await _fictionalEntityRepo.CreateAsync(fictionalEntity, ct).ConfigureAwait(false);
                }

                var existingLinks = await _personRepo.GetCharacterLinksAsync(person.Id, ct).ConfigureAwait(false);
                if (existingLinks.Any(link =>
                    link.FictionalEntityId == fictionalEntity.Id
                    && string.Equals(link.WorkQid, workQid, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                await _fictionalEntityRepo.LinkToWorkAsync(fictionalEntity.Id, workQid, null, "portrayed_in", ct)
                    .ConfigureAwait(false);

                await _personRepo.LinkToCharacterAsync(person.Id, fictionalEntity.Id, workQid, ct).ConfigureAwait(false);
                await EnqueueCharacterHarvestIfNeededAsync(fictionalEntity, ct).ConfigureAwait(false);
                linkCount++;
            }
        }

        return linkCount;
    }

    private async Task<IReadOnlyList<CanonicalValue>> LoadActorCharacterCanonicalsAsync(
        IReadOnlyList<Guid> entityIds,
        CancellationToken ct)
    {
        var canonicals = new List<CanonicalValue>();
        foreach (var candidateEntityId in entityIds)
        {
            canonicals.AddRange(await _canonicalRepo.GetByEntityAsync(candidateEntityId, ct).ConfigureAwait(false));
        }

        return canonicals;
    }

    private async Task<bool> HasActorCharacterArrayEvidenceAsync(
        IReadOnlyList<Guid> entityIds,
        CancellationToken ct)
    {
        if (_canonicalArrayRepo is null)
            return false;

        foreach (var candidateEntityId in entityIds)
        {
            var arrays = await _canonicalArrayRepo.GetAllByEntityAsync(candidateEntityId, ct).ConfigureAwait(false);
            if (arrays.TryGetValue(MetadataFieldConstants.CastMember, out var castMembers)
                && arrays.TryGetValue(MetadataFieldConstants.Characters, out var characters)
                && castMembers.Any(entry => NormalizeEntityQid(entry.ValueQid) is not null)
                && characters.Any(entry => NormalizeEntityQid(entry.ValueQid) is not null))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<Guid>> ResolveActorCharacterArrayEntityIdsAsync(
        Guid entityId,
        CancellationToken ct)
    {
        var entityIds = new List<Guid> { entityId };

        var workId = await _collectionRepo.GetWorkIdByMediaAssetAsync(entityId, ct).ConfigureAwait(false);
        if (workId.HasValue && !entityIds.Contains(workId.Value))
            entityIds.Add(workId.Value);

        var lineageWorkIds = await _collectionRepo.GetWorkLineageIdsByMediaAssetAsync(entityId, ct).ConfigureAwait(false);
        foreach (var lineageWorkId in lineageWorkIds)
        {
            if (!entityIds.Contains(lineageWorkId))
                entityIds.Add(lineageWorkId);
        }

        return entityIds;
    }

    private static string ResolveCharacterLabel(WikidataValue value)
        => FirstNonBlank(value.EntityLabel, value.RawValue, value.EntityId) ?? "Character";

    private async Task EnqueueCharacterHarvestIfNeededAsync(FictionalEntity entity, CancellationToken ct)
    {
        if (entity.EnrichedAt is not null || string.IsNullOrWhiteSpace(entity.WikidataQid))
            return;

        await _harvesting.EnqueueAsync(new HarvestRequest
        {
            EntityId = entity.Id,
            EntityType = EntityType.Character,
            MediaType = MediaType.Unknown,
            Hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["wikidata_qid"] = entity.WikidataQid,
                ["label"] = entity.Label,
                ["entity_sub_type"] = entity.EntitySubType,
                ["universe_qid"] = entity.FictionalUniverseQid ?? string.Empty,
            },
        }, ct).ConfigureAwait(false);
    }

    private static bool IsVideoMediaType(string? mediaType)
        => mediaType is not null
           && (mediaType.Equals(nameof(MediaType.Movies), StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals(nameof(MediaType.TV), StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("TvShow", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("TvEpisode", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("Television", StringComparison.OrdinalIgnoreCase));

    private static string? ResolveCanonicalQid(
        IReadOnlyList<CanonicalValue> canonicals,
        string key)
    {
        var raw = canonicals.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var first = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            return null;

        var qid = first.Split("::", 2)[0].Trim();
        return qid.StartsWith('Q') ? qid : null;
    }

    private static string? NormalizeEntityQid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Contains('/') ? raw.Split('/')[^1] : raw;
        value = value.Split("::", 2)[0].Trim();
        return value.StartsWith('Q') ? value : null;
    }

    private static string? ResolveCanonicalLabel(
        IReadOnlyList<CanonicalValue> canonicals,
        string key)
    {
        var raw = canonicals.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var first = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            return null;

        var segments = first.Split("::", 2);
        return segments.Length == 2 && !string.IsNullOrWhiteSpace(segments[1])
            ? segments[1].Trim()
            : first;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
