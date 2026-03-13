using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Background metadata harvesting service.
///
/// Accepts <see cref="HarvestRequest"/> items from the ingestion pipeline and
/// dispatches them to registered <see cref="IExternalMetadataProvider"/> adapters
/// without blocking the ingestion thread.
///
/// Architecture:
/// - A bounded <c>Channel&lt;HarvestRequest&gt;</c> (capacity 500, DropOldest policy)
///   decouples producers (ingestion) from consumers (adapters).
/// - A single reader task processes requests sequentially within the channel.
/// - A <c>SemaphoreSlim(3)</c> limits simultaneous in-flight adapter calls.
/// - First provider to return claims wins; remaining providers for that request
///   are skipped.
/// - After persisting new claims, the entity is re-scored and canonical values
///   are upserted.  A <c>"MetadataHarvested"</c> SignalR event is published.
/// - For <see cref="EntityType.Person"/> entities, Wikidata claims trigger a
///   call to <see cref="IPersonRepository.UpdateEnrichmentAsync"/> and a
///   <c>"PersonEnriched"</c> SignalR event.
///
/// Spec: Phase 9 – Non-Blocking Harvesting.
/// </summary>
public sealed class MetadataHarvestingService : IMetadataHarvestingService, IAsyncDisposable
{
    // ── Channel ───────────────────────────────────────────────────────────────

    private readonly Channel<HarvestRequest> _channel;
    private readonly Task _processingLoop;
    private readonly CancellationTokenSource _cts = new();

    // ── Concurrency ───────────────────────────────────────────────────────────

    /// <summary>Maximum parallel adapter calls in flight at once.</summary>
    private readonly SemaphoreSlim _concurrency = new(3, 3);

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IReadOnlyList<IExternalMetadataProvider> _providers;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IFictionalEntityRepository _fictionalEntityRepo;
    private readonly IRelationshipPopulationService _relPopService;
    private readonly IUniverseGraphWriterService _universeWriter;
    private readonly IScoringEngine _scoringEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfigurationLoader _configLoader;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IImageCacheRepository _imageCache;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IQidLabelRepository _qidLabelRepo;
    private readonly ILogger<MetadataHarvestingService> _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MetadataHarvestingService(
        IEnumerable<IExternalMetadataProvider> providers,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IPersonRepository personRepo,
        IFictionalEntityRepository fictionalEntityRepo,
        IRelationshipPopulationService relPopService,
        IUniverseGraphWriterService universeWriter,
        IScoringEngine scoringEngine,
        IEventPublisher eventPublisher,
        IConfigurationLoader configLoader,
        IHttpClientFactory httpFactory,
        IImageCacheRepository imageCache,
        ISystemActivityRepository activityRepo,
        IQidLabelRepository qidLabelRepo,
        ILogger<MetadataHarvestingService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(claimRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(fictionalEntityRepo);
        ArgumentNullException.ThrowIfNull(relPopService);
        ArgumentNullException.ThrowIfNull(universeWriter);
        ArgumentNullException.ThrowIfNull(scoringEngine);
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(activityRepo);
        ArgumentNullException.ThrowIfNull(qidLabelRepo);
        ArgumentNullException.ThrowIfNull(logger);

        _providers            = providers.ToList();
        _claimRepo            = claimRepo;
        _canonicalRepo        = canonicalRepo;
        _personRepo           = personRepo;
        _fictionalEntityRepo  = fictionalEntityRepo;
        _relPopService        = relPopService;
        _universeWriter       = universeWriter;
        _scoringEngine        = scoringEngine;
        _eventPublisher       = eventPublisher;
        _configLoader         = configLoader;
        _httpFactory          = httpFactory;
        _imageCache           = imageCache;
        _activityRepo         = activityRepo;
        _qidLabelRepo         = qidLabelRepo;
        _logger               = logger;

        _channel = Channel.CreateBounded<HarvestRequest>(new BoundedChannelOptions(500)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        // Start the background processing loop immediately.
        _processingLoop = Task.Run(ProcessLoopAsync);
    }

    // ── IMetadataHarvestingService ────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // TryWrite is non-blocking; DropOldest handles back-pressure silently.
        _channel.Writer.TryWrite(request);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : -1;

    // ── Background loop ───────────────────────────────────────────────────────

    private async Task ProcessLoopAsync()
    {
        var ct = _cts.Token;
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(ct))
            {
                await _concurrency.WaitAsync(ct).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    try { await ProcessOneAsync(request, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Unhandled error processing harvest request for entity {Id}",
                            request.EntityId);
                    }
                    finally { _concurrency.Release(); }
                }, ct);
            }
        }
        catch (OperationCanceledException) { /* Graceful shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MetadataHarvestingService processing loop terminated unexpectedly");
        }
    }

    private async Task ProcessOneAsync(HarvestRequest request, CancellationToken ct)
    {
        var allProviderConfigs = _configLoader.LoadAllProviders();
        var scoring            = _configLoader.LoadScoring();

        // Build composite endpoint map from all provider configs.
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in allProviderConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        // Build provider weight maps from provider configs.
        var (providerWeights, providerFieldWeights) = BuildWeightMaps(allProviderConfigs);

        var sparqlBaseUrl = ResolveSparqlBaseUrl(endpointMap);

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(request.MediaType) || !provider.CanHandle(request.EntityType))
                continue;

            var baseUrl = ResolveBaseUrl(provider, endpointMap);
            var lookupRequest = BuildLookupRequest(request, provider, baseUrl, sparqlBaseUrl);

            IReadOnlyList<ProviderClaim> providerClaims;
            try
            {
                providerClaims = await provider.FetchAsync(lookupRequest, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Provider {Provider} threw unexpectedly for entity {Id}; skipping",
                    provider.Name, request.EntityId);
                continue;
            }

            if (providerClaims.Count == 0)
                continue;

            // Wrap provider claims as domain MetadataClaim rows.
            var domainClaims = providerClaims
                .Select(pc => new MetadataClaim
                {
                    Id          = Guid.NewGuid(),
                    EntityId    = request.EntityId,
                    ProviderId  = provider.ProviderId,
                    ClaimKey    = pc.Key,
                    ClaimValue  = pc.Value,
                    Confidence  = pc.Confidence,
                    ClaimedAt   = DateTimeOffset.UtcNow,
                    IsUserLocked = false,
                })
                .ToList();

            // Persist claims (append-only).
            await _claimRepo.InsertBatchAsync(domainClaims, ct).ConfigureAwait(false);

            // Load ALL claims for this entity and re-score.
            var allClaims = await _claimRepo.GetByEntityAsync(request.EntityId, ct).ConfigureAwait(false);
            var scoringConfig = new ScoringConfiguration
            {
                AutoLinkThreshold    = scoring.AutoLinkThreshold,
                ConflictThreshold    = scoring.ConflictThreshold,
                ConflictEpsilon      = scoring.ConflictEpsilon,
                StaleClaimDecayDays  = scoring.StaleClaimDecayDays,
                StaleClaimDecayFactor = scoring.StaleClaimDecayFactor,
            };
            var scoringContext = new ScoringContext
            {
                EntityId           = request.EntityId,
                Claims             = allClaims,
                ProviderWeights    = providerWeights,
                ProviderFieldWeights = providerFieldWeights,
                Configuration      = scoringConfig,
            };

            var scored = await _scoringEngine.ScoreEntityAsync(scoringContext, ct).ConfigureAwait(false);

            // Upsert canonical values (current best answers).
            // Phase B: also persist the IsConflicted flag from the scoring engine.
            var canonicals = scored.FieldScores
                .Where(f => !string.IsNullOrEmpty(f.WinningValue))
                .Select(f => new CanonicalValue
                {
                    EntityId     = request.EntityId,
                    Key          = f.Key,
                    Value        = f.WinningValue!,
                    LastScoredAt = scored.ScoredAt,
                    IsConflicted = f.IsConflicted,
                })
                .ToList();
            await _canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

            // Special handling: Wikidata claims for a Person entity.
            if (request.EntityType == EntityType.Person)
            {
                await HandlePersonEnrichmentAsync(request, providerClaims, provider, ct)
                    .ConfigureAwait(false);
            }

            // Special handling: Wikidata claims for fictional entity types.
            if (request.EntityType is EntityType.Character or EntityType.Location or EntityType.Organization)
            {
                await HandleFictionalEntityEnrichmentAsync(request, canonicals, provider, ct)
                    .ConfigureAwait(false);
            }

            // Publish MetadataHarvested event.
            var updatedFields = domainClaims.Select(c => c.ClaimKey).Distinct().ToList();
            await _eventPublisher.PublishAsync(
                "MetadataHarvested",
                new MetadataHarvestedEvent(request.EntityId, provider.Name, updatedFields),
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Harvested {Count} claims from {Provider} for entity {Id}",
                domainClaims.Count, provider.Name, request.EntityId);

            // First provider to succeed wins; skip remaining providers.
            break;
        }
    }

    /// <summary>
    /// After a fictional entity (Character/Location/Organization) is enriched from
    /// Wikidata SPARQL, updates the entity's <c>EnrichedAt</c> timestamp, populates
    /// relationship edges from <c>_qid</c> claims, and triggers debounced
    /// <c>universe.xml</c> writing.
    /// </summary>
    private async Task HandleFictionalEntityEnrichmentAsync(
        HarvestRequest request,
        IReadOnlyList<CanonicalValue> canonicals,
        IExternalMetadataProvider provider,
        CancellationToken ct)
    {
        // Only Wikidata produces fictional entity enrichment claims.
        if (!string.Equals(provider.Name, "wikidata", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            // Mark entity as enriched.
            await _fictionalEntityRepo.UpdateEnrichmentAsync(
                request.EntityId, description: null, imageUrl: null,
                DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

            // Build canonical values dictionary for relationship population.
            var canonicalDict = canonicals
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            // Resolve universe QID from hints.
            var universeQid = request.Hints.GetValueOrDefault("universe_qid");

            // Populate relationship edges from _qid claims.
            await _relPopService.PopulateAsync(
                request.Hints.GetValueOrDefault("wikidata_qid") ?? string.Empty,
                canonicalDict,
                universeQid ?? string.Empty,
                universeLabel: null,
                contextWorkQid: null,
                ct).ConfigureAwait(false);

            // Determine action type for activity log.
            var entitySubType = request.Hints.GetValueOrDefault("entity_sub_type") ?? "Character";
            var actionType = entitySubType switch
            {
                "Location"     => SystemActionType.LocationEnriched,
                "Organization" => SystemActionType.OrganizationEnriched,
                _              => SystemActionType.CharacterEnriched,
            };

            // Log activity.
            var label = request.Hints.GetValueOrDefault("label") ?? request.EntityId.ToString();

            // Cache fictional entity QID → label for offline resolution.
            var entityQid = request.Hints.GetValueOrDefault("wikidata_qid");
            if (!string.IsNullOrWhiteSpace(entityQid) && !string.IsNullOrWhiteSpace(label))
            {
                try
                {
                    await _qidLabelRepo.UpsertAsync(entityQid, label, null, entitySubType, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Failed to cache fictional entity QID label for {Qid}", entityQid);
                }
            }

            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = actionType,
                EntityId   = request.EntityId,
                EntityType = entitySubType,
                Detail     = $"Fictional entity \"{label}\" enriched from Wikidata",
            }, ct).ConfigureAwait(false);

            // Publish SignalR event.
            await _eventPublisher.PublishAsync(
                "FictionalEntityEnriched",
                new FictionalEntityEnrichedEvent(request.EntityId, label, entitySubType, universeQid),
                ct).ConfigureAwait(false);

            // Trigger debounced universe.xml write.
            if (!string.IsNullOrWhiteSpace(universeQid))
            {
                await _universeWriter.NotifyEntityEnrichedAsync(universeQid, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Fictional entity enrichment post-processing failed for entity {Id}",
                request.EntityId);
        }
    }

    private async Task HandlePersonEnrichmentAsync(
        HarvestRequest request,
        IReadOnlyList<ProviderClaim> claims,
        IExternalMetadataProvider provider,
        CancellationToken ct)
    {
        // Only Wikidata produces person-enrichment claims.
        if (!string.Equals(provider.Name, "wikidata", StringComparison.OrdinalIgnoreCase))
            return;

        var qid         = claims.FirstOrDefault(c => c.Key == "wikidata_qid")?.Value;
        var headshotUrl = claims.FirstOrDefault(c => c.Key == "headshot_url")?.Value;
        var biography   = claims.FirstOrDefault(c => c.Key == "biography")?.Value;

        if (qid is null && headshotUrl is null && biography is null)
            return;

        // ── QID-based deduplication ───────────────────────────────────────────
        // If another person record already owns this Wikidata QID, skip creating
        // a duplicate folder.  Both DB records will end up pointing at the same
        // filesystem folder (same "Name (QID)" path) via TryRenameExistingPersonFolder;
        // we just need to make sure the first-arriving enrichment wins the write.
        bool isQidDuplicate = false;
        if (!string.IsNullOrWhiteSpace(qid))
        {
            var canonicalPerson = await _personRepo.FindByQidAsync(qid, ct).ConfigureAwait(false);
            if (canonicalPerson is not null && canonicalPerson.Id != request.EntityId)
            {
                isQidDuplicate = true;
                _logger.LogInformation(
                    "Person {Id} shares QID {Qid} with canonical person {CanonicalId} — merging",
                    request.EntityId, qid, canonicalPerson.Id);

                // Merge: reassign all links from duplicate → canonical, then delete duplicate.
                await _personRepo.ReassignAllLinksAsync(request.EntityId, canonicalPerson.Id, ct)
                    .ConfigureAwait(false);

                // Delete the duplicate person folder from disk before deleting the DB record.
                var duplicatePerson = await _personRepo.FindByIdAsync(request.EntityId, ct)
                    .ConfigureAwait(false);
                if (duplicatePerson is not null)
                {
                    DeletePersonFolder(duplicatePerson);
                }

                await _personRepo.DeleteAsync(request.EntityId, ct).ConfigureAwait(false);

                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.PersonMerged,
                    EntityId   = canonicalPerson.Id,
                    EntityType = "Person",
                    Detail     = $"Merged duplicate person \"{duplicatePerson?.Name ?? "?"}\" into canonical \"{canonicalPerson.Name}\" ({qid})",
                }, ct).ConfigureAwait(false);

                await _eventPublisher.PublishAsync(
                    "PersonEnriched",
                    new PersonEnrichedEvent(canonicalPerson.Id, canonicalPerson.Name, canonicalPerson.HeadshotUrl, qid),
                    ct).ConfigureAwait(false);

                return; // Merge complete — skip remaining enrichment for the deleted duplicate.
            }
        }

        await _personRepo.UpdateEnrichmentAsync(request.EntityId, qid, headshotUrl, biography, ct)
            .ConfigureAwait(false);

        // Persist biographical fields from Wikidata claims.
        var dateOfBirth  = claims.FirstOrDefault(c => c.Key == "date_of_birth")?.Value;
        var dateOfDeath  = claims.FirstOrDefault(c => c.Key == "date_of_death")?.Value;
        var placeOfBirth = claims.FirstOrDefault(c => c.Key == "place_of_birth")?.Value;
        var placeOfDeath = claims.FirstOrDefault(c => c.Key == "place_of_death")?.Value;
        var nationality  = claims.FirstOrDefault(c => c.Key == "country_of_citizenship")?.Value;
        var isPseudonym  = claims.Any(c =>
            c.Key == "instance_of" &&
            c.Value.Contains("Q15632617", StringComparison.OrdinalIgnoreCase));

        if (dateOfBirth is not null || dateOfDeath is not null || placeOfBirth is not null ||
            placeOfDeath is not null || nationality is not null || isPseudonym)
        {
            await _personRepo.UpdateBiographicalFieldsAsync(
                request.EntityId, dateOfBirth, dateOfDeath,
                placeOfBirth, placeOfDeath, nationality, isPseudonym, ct)
                .ConfigureAwait(false);
        }

        // Social media and contact fields from Wikidata.
        var occupation = claims.FirstOrDefault(c => c.Key == "occupation")?.Value;
        var instagram  = claims.FirstOrDefault(c => c.Key == "instagram")?.Value;
        var twitter    = claims.FirstOrDefault(c => c.Key == "twitter")?.Value;
        var tiktok     = claims.FirstOrDefault(c => c.Key == "tiktok")?.Value;
        var mastodon   = claims.FirstOrDefault(c => c.Key == "mastodon")?.Value;
        var website    = claims.FirstOrDefault(c => c.Key == "website")?.Value;

        if (occupation is not null || instagram is not null || twitter is not null ||
            tiktok is not null || mastodon is not null || website is not null)
        {
            await _personRepo.UpdateSocialFieldsAsync(
                request.EntityId, occupation, instagram, twitter,
                tiktok, mastodon, website, ct)
                .ConfigureAwait(false);
        }

        // Pseudonym resolution: link pen names to real people (and vice versa).
        await ResolvePseudonymsAsync(request.EntityId, claims, isPseudonym, ct)
            .ConfigureAwait(false);

        // Fetch Wikipedia description for richer biography (Stage 2 for Persons).
        await FetchWikipediaForPersonAsync(request.EntityId, qid, ct)
            .ConfigureAwait(false);

        // Look up the person for event payload and people storage.
        var person = await _personRepo.FindByIdAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        var personName = person?.Name ?? string.Empty;

        // Cache person QID → label for offline resolution.
        if (!string.IsNullOrWhiteSpace(qid) && !string.IsNullOrWhiteSpace(personName))
        {
            try
            {
                await _qidLabelRepo.UpsertAsync(qid, personName, biography, "Person", ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to cache person QID label for {Qid}", qid);
            }
        }

        // Persist headshot + person sidecar under {LibraryRoot}/.people/{Name} ({QID})/
        // Skip if this person shares a QID with a canonical record — avoids duplicate folders.
        if (!isQidDuplicate)
        {
            await PersistPersonStorageAsync(request.EntityId, person, headshotUrl, ct)
                .ConfigureAwait(false);
        }

        await _eventPublisher.PublishAsync(
            "PersonEnriched",
            new PersonEnrichedEvent(request.EntityId, personName, headshotUrl, qid),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves pseudonym relationships after a Person is enriched from Wikidata.
    ///
    /// If the person is a pseudonym (P1773 = attributed_to), links to real people.
    /// If the person has pseudonyms (P742 = pseudonym), links to pen name records.
    /// </summary>
    private async Task ResolvePseudonymsAsync(
        Guid personId,
        IReadOnlyList<ProviderClaim> claims,
        bool isPseudonym,
        CancellationToken ct)
    {
        try
        {
            // P1773 (attributed_to): real people behind a pen name.
            var attributedTo = claims
                .Where(c => c.Key == "attributed_to_qid")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (isPseudonym && attributedTo.Count > 0)
            {
                foreach (var realQidList in attributedTo)
                {
                    var qids = realQidList.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var rawQid in qids)
                    {
                        var realQid = rawQid.Split("::")[0];
                        var realPerson = await _personRepo.FindByQidAsync(realQid, ct)
                            .ConfigureAwait(false);
                        if (realPerson is not null)
                        {
                            await _personRepo.LinkAliasAsync(personId, realPerson.Id, ct)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await CreateAndLinkStubPersonAsync(personId, realQid, false, ct).ConfigureAwait(false);
                        }
                    }
                }
            }

            // P742 (pseudonym): pen names used by this person.
            var pseudonymQids = claims
                .Where(c => c.Key == "pseudonym_qid")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            foreach (var penQidList in pseudonymQids)
            {
                var qids = penQidList.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var rawQid in qids)
                {
                    var penQid = rawQid.Split("::")[0];
                    var penPerson = await _personRepo.FindByQidAsync(penQid, ct)
                        .ConfigureAwait(false);
                    if (penPerson is not null)
                    {
                        await _personRepo.LinkAliasAsync(penPerson.Id, personId, ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await CreateAndLinkStubPersonAsync(personId, penQid, true, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Pseudonym resolution failed for person {Id}; continuing", personId);
        }
    }

    private async Task CreateAndLinkStubPersonAsync(
        Guid existingPersonId,
        string missingQid,
        bool isMissingPersonPseudonym,
        CancellationToken ct)
    {
        var stubId = Guid.NewGuid();
        var stub = new Person
        {
            Id = stubId,
            Name = $"Unknown Person ({missingQid})",
            Role = "Author", // Default role
            WikidataQid = missingQid,
            CreatedAt = DateTimeOffset.UtcNow,
            IsPseudonym = isMissingPersonPseudonym
        };
        
        await _personRepo.CreateAsync(stub, ct).ConfigureAwait(false);
        
        if (isMissingPersonPseudonym)
        {
            // The missing person is a pen name for the existing real person
            await _personRepo.LinkAliasAsync(stubId, existingPersonId, ct).ConfigureAwait(false);
        }
        else
        {
            // The missing person is a real person behind the existing pen name
            await _personRepo.LinkAliasAsync(existingPersonId, stubId, ct).ConfigureAwait(false);
        }
        
        // Enqueue the stub for hydration
        await EnqueueAsync(new HarvestRequest
        {
            EntityId = stubId,
            EntityType = EntityType.Person,
            MediaType = MediaType.Unknown,
            PreResolvedQid = missingQid
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls the Wikipedia adapter to fetch a richer biography description for a
    /// Person entity, replacing the short Wikidata description.
    /// </summary>
    private async Task FetchWikipediaForPersonAsync(
        Guid personId, string? qid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(qid))
            return;

        try
        {
            var wikipediaAdapter = _providers
                .FirstOrDefault(p => string.Equals(p.Name, "wikipedia", StringComparison.OrdinalIgnoreCase));

            if (wikipediaAdapter is null || !wikipediaAdapter.CanHandle(EntityType.Person))
                return;

            var lookupRequest = new ProviderLookupRequest
            {
                EntityId      = personId,
                EntityType    = EntityType.Person,
                MediaType     = MediaType.Unknown,
                PreResolvedQid = qid,
            };

            var wikiClaims = await wikipediaAdapter.FetchAsync(lookupRequest, ct)
                .ConfigureAwait(false);

            var description = wikiClaims
                .FirstOrDefault(c => c.Key == "description")?.Value;

            if (!string.IsNullOrWhiteSpace(description))
            {
                // Update the person's biography with the richer Wikipedia description.
                var person = await _personRepo.FindByIdAsync(personId, ct).ConfigureAwait(false);
                if (person is not null)
                {
                    await _personRepo.UpdateEnrichmentAsync(
                        personId, person.WikidataQid, person.HeadshotUrl, description, ct)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Wikipedia fetch for person {Id} failed; continuing", personId);
        }
    }

    /// <summary>
    /// Downloads headshot from Wikimedia Commons and writes person.xml sidecar
    /// under <c>{LibraryRoot}/.people/{Name} ({QID})/</c>.
    ///
    /// Folder naming uses <c>Name (QID)</c> format when a Wikidata QID is available
    /// (post-enrichment). Falls back to sanitized name or GUID before enrichment.
    /// QID guarantees uniqueness — no collision detection logic needed.
    ///
    /// The person.xml v1.1 includes biographical fields, characters played,
    /// pseudonym links, and a <c>&lt;known-names&gt;</c> list tracking all
    /// historical names for reconnection after a database wipe.
    /// </summary>
    private async Task PersistPersonStorageAsync(
        Guid personId,
        Person? person,
        string? headshotUrl,
        CancellationToken ct)
    {
        try
        {
            var core = _configLoader.LoadCore();
            if (string.IsNullOrWhiteSpace(core.LibraryRoot))
                return;

            var peopleRoot = Path.Combine(core.LibraryRoot, ".people");
            Directory.CreateDirectory(peopleRoot);

            // Resolve the folder name: "Name (QID)" when QID is available,
            // sanitized name or GUID as fallback before enrichment.
            string folderName;
            if (person is not null && !string.IsNullOrWhiteSpace(person.WikidataQid) &&
                !string.IsNullOrWhiteSpace(person.Name))
            {
                folderName = $"{SanitizeForFilesystem(person.Name)} ({person.WikidataQid})";
            }
            else if (person is not null && !string.IsNullOrWhiteSpace(person.Name))
            {
                folderName = SanitizeForFilesystem(person.Name);
            }
            else
            {
                folderName = personId.ToString();
            }

            var personFolder = Path.Combine(peopleRoot, folderName);

            // ── Rename detection: if this person already has a folder under a
            //    different name (old name, GUID, or pre-QID format), rename it. ──
            if (!Directory.Exists(personFolder) || ReadPersonIdFromXml(Path.Combine(personFolder, "person.xml")) != personId)
            {
                var renamed = TryRenameExistingPersonFolder(peopleRoot, personId, personFolder);
                if (renamed)
                {
                    await LogPersonFolderRenamedAsync(personId, person?.Name ?? folderName, ct)
                        .ConfigureAwait(false);
                }
            }

            // Collision detection: if folder exists, read person.xml → check <id>.
            // With QID-based naming this should be rare, but kept for safety.
            if (Directory.Exists(personFolder))
            {
                var existingId = ReadPersonIdFromXml(Path.Combine(personFolder, "person.xml"));
                if (existingId is not null && existingId != personId)
                {
                    // Different person occupies this folder — append short hash.
                    folderName = $"{folderName} [{personId.ToString()[..4]}]";
                    personFolder = Path.Combine(peopleRoot, folderName);
                }
            }

            Directory.CreateDirectory(personFolder);

            // Download headshot if URL is available and file doesn't exist.
            var headshotPath = Path.Combine(personFolder, "headshot.jpg");
            if (!string.IsNullOrEmpty(headshotUrl) && !File.Exists(headshotPath))
            {
                try
                {
                    using var client = _httpFactory.CreateClient("headshot_download");
                    var bytes = await client.GetByteArrayAsync(headshotUrl, ct)
                        .ConfigureAwait(false);

                    if (bytes.Length > 0)
                    {
                        var hash = Convert.ToHexStringLower(
                            System.Security.Cryptography.SHA256.HashData(bytes));
                        var cached = await _imageCache.FindByHashAsync(hash, ct)
                            .ConfigureAwait(false);

                        if (cached is not null && File.Exists(cached))
                        {
                            File.Copy(cached, headshotPath, overwrite: false);
                        }
                        else
                        {
                            await File.WriteAllBytesAsync(headshotPath, bytes, ct)
                                .ConfigureAwait(false);
                            await _imageCache.InsertAsync(hash, headshotPath, headshotUrl, ct)
                                .ConfigureAwait(false);
                        }

                        await _personRepo.UpdateLocalHeadshotPathAsync(personId, headshotPath, ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Headshot download failed for person {Id}; continuing",
                        personId);
                }
            }

            // Write person.xml v1.1 sidecar with biographical fields, characters,
            // pseudonym links, and all metadata.
            if (person is not null)
            {
                // Build <known-names> list: merge existing names from any prior person.xml
                // with the current name to preserve historical aliases.
                var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingXmlPath = Path.Combine(personFolder, "person.xml");
                if (File.Exists(existingXmlPath))
                {
                    try
                    {
                        var existingDoc = System.Xml.Linq.XDocument.Load(existingXmlPath);
                        var existingKnown = existingDoc.Root?
                            .Element("known-names")?
                            .Elements("name")
                            .Select(e => e.Value.Trim())
                            .Where(n => !string.IsNullOrWhiteSpace(n));

                        if (existingKnown is not null)
                            foreach (var n in existingKnown)
                                knownNames.Add(n);
                    }
                    catch
                    {
                        // Corrupt XML — start fresh with just the current name.
                    }
                }
                knownNames.Add(person.Name);

                var knownNamesElement = new System.Xml.Linq.XElement("known-names");
                foreach (var name in knownNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    knownNamesElement.Add(new System.Xml.Linq.XElement("name", name));

                // Build identity element with pseudonym flag.
                var identityElement = new System.Xml.Linq.XElement("identity",
                    new System.Xml.Linq.XElement("name",         person.Name),
                    new System.Xml.Linq.XElement("role",         person.Role),
                    new System.Xml.Linq.XElement("wikidata-qid", person.WikidataQid ?? string.Empty),
                    new System.Xml.Linq.XElement("occupation",   person.Occupation  ?? string.Empty));

                if (person.IsPseudonym)
                    identityElement.Add(new System.Xml.Linq.XElement("is-pseudonym", "true"));

                // Build details element with biographical fields.
                var detailsElement = new System.Xml.Linq.XElement("details");
                if (!string.IsNullOrEmpty(person.DateOfBirth))
                    detailsElement.Add(new System.Xml.Linq.XElement("date-of-birth", person.DateOfBirth));
                if (!string.IsNullOrEmpty(person.DateOfDeath))
                    detailsElement.Add(new System.Xml.Linq.XElement("date-of-death", person.DateOfDeath));
                if (!string.IsNullOrEmpty(person.PlaceOfBirth))
                    detailsElement.Add(new System.Xml.Linq.XElement("place-of-birth", person.PlaceOfBirth));
                if (!string.IsNullOrEmpty(person.PlaceOfDeath))
                    detailsElement.Add(new System.Xml.Linq.XElement("place-of-death", person.PlaceOfDeath));
                if (!string.IsNullOrEmpty(person.Nationality))
                    detailsElement.Add(new System.Xml.Linq.XElement("nationality", person.Nationality));

                // Build characters element: fictional characters this person portrays.
                var charactersElement = new System.Xml.Linq.XElement("characters");
                try
                {
                    var charLinks = await _personRepo.GetCharacterLinksAsync(personId, ct)
                        .ConfigureAwait(false);
                    foreach (var (entityId, workQid) in charLinks)
                    {
                        var entity = await _fictionalEntityRepo.FindByIdAsync(entityId, ct)
                            .ConfigureAwait(false);
                        if (entity is null) continue;

                        var charElement = new System.Xml.Linq.XElement("character",
                            new System.Xml.Linq.XAttribute("qid", entity.WikidataQid),
                            new System.Xml.Linq.XAttribute("label", entity.Label));

                        if (!string.IsNullOrEmpty(entity.FictionalUniverseQid))
                        {
                            charElement.Add(new System.Xml.Linq.XAttribute("universe-qid", entity.FictionalUniverseQid));
                            if (!string.IsNullOrEmpty(entity.FictionalUniverseLabel))
                                charElement.Add(new System.Xml.Linq.XAttribute("universe-label", entity.FictionalUniverseLabel));
                        }

                        if (!string.IsNullOrEmpty(workQid))
                            charElement.Add(new System.Xml.Linq.XAttribute("work-qid", workQid));

                        charactersElement.Add(charElement);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to load character links for person.xml");
                }

                // Build pseudonym link elements.
                System.Xml.Linq.XElement? realIdentitiesElement = null;
                System.Xml.Linq.XElement? pseudonymsElement = null;
                try
                {
                    var aliases = await _personRepo.FindAliasesAsync(personId, ct)
                        .ConfigureAwait(false);
                    foreach (var alias in aliases)
                    {
                        if (alias.IsPseudonym && !person.IsPseudonym)
                        {
                            // This alias is a pen name for the real person.
                            pseudonymsElement ??= new System.Xml.Linq.XElement("pseudonyms");
                            pseudonymsElement.Add(new System.Xml.Linq.XElement("person",
                                new System.Xml.Linq.XAttribute("qid", alias.WikidataQid ?? string.Empty),
                                new System.Xml.Linq.XAttribute("label", alias.Name)));
                        }
                        else if (!alias.IsPseudonym && person.IsPseudonym)
                        {
                            // This alias is a real person behind the pen name.
                            realIdentitiesElement ??= new System.Xml.Linq.XElement("real-identities");
                            realIdentitiesElement.Add(new System.Xml.Linq.XElement("person",
                                new System.Xml.Linq.XAttribute("qid", alias.WikidataQid ?? string.Empty),
                                new System.Xml.Linq.XAttribute("label", alias.Name)));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to load aliases for person.xml");
                }

                var rootElement = new System.Xml.Linq.XElement("library-person",
                    new System.Xml.Linq.XAttribute("version", "1.1"),
                    identityElement,
                    new System.Xml.Linq.XElement("biography", person.Biography ?? string.Empty),
                    detailsElement,
                    new System.Xml.Linq.XElement("social",
                        new System.Xml.Linq.XElement("instagram", person.Instagram ?? string.Empty),
                        new System.Xml.Linq.XElement("twitter",   person.Twitter   ?? string.Empty),
                        new System.Xml.Linq.XElement("tiktok",    person.TikTok    ?? string.Empty),
                        new System.Xml.Linq.XElement("mastodon",  person.Mastodon  ?? string.Empty),
                        new System.Xml.Linq.XElement("website",   person.Website   ?? string.Empty)
                    ),
                    knownNamesElement,
                    charactersElement);

                if (realIdentitiesElement is not null)
                    rootElement.Add(realIdentitiesElement);
                if (pseudonymsElement is not null)
                    rootElement.Add(pseudonymsElement);

                var doc = new System.Xml.Linq.XDocument(
                    new System.Xml.Linq.XDeclaration("1.0", "utf-8", null),
                    rootElement);
                doc.Save(existingXmlPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Person storage persistence failed for person {Id}; continuing",
                personId);
        }
    }

    /// <summary>
    /// Reads the <c>&lt;id&gt;</c> element from a person.xml file.
    /// Returns <c>null</c> if the file doesn't exist or can't be parsed.
    /// Used for collision detection when multiple persons share the same display name.
    /// </summary>
    private static Guid? ReadPersonIdFromXml(string xmlPath)
    {
        try
        {
            if (!File.Exists(xmlPath)) return null;
            var doc = System.Xml.Linq.XDocument.Load(xmlPath);
            var idText = doc.Root?.Element("identity")?.Element("id")?.Value;
            return Guid.TryParse(idText, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the person folder from disk during QID-based deduplication.
    /// Searches .people/ for any folder owned by the given person (via person.xml ID).
    /// </summary>
    private void DeletePersonFolder(Person person)
    {
        try
        {
            var core = _configLoader.LoadCore();
            if (string.IsNullOrWhiteSpace(core.LibraryRoot)) return;

            var peopleRoot = Path.Combine(core.LibraryRoot, ".people");
            if (!Directory.Exists(peopleRoot)) return;

            foreach (var dir in Directory.EnumerateDirectories(peopleRoot))
            {
                var xmlPath = Path.Combine(dir, "person.xml");
                var ownerId = ReadPersonIdFromXml(xmlPath);
                if (ownerId == person.Id)
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Deleted duplicate person folder: {Folder}", dir);
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to delete person folder for {PersonId}", person.Id);
        }
    }

    /// <summary>
    /// Sanitizes a string for use as a filesystem path segment.
    /// Replaces invalid path characters with underscores and trims trailing dots
    /// (which Windows silently strips, causing path mismatches).
    /// </summary>
    internal static string SanitizeForFilesystem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];

        return new string(sanitized).TrimEnd('.', ' ');
    }

    /// <summary>
    /// Scans .people/ for an existing folder owned by this person (matched by
    /// person.xml &lt;id&gt;) and renames it to the target path if found.
    /// Returns <c>true</c> if a rename was performed.
    /// </summary>
    private bool TryRenameExistingPersonFolder(
        string peopleRoot, Guid personId, string targetFolder)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(peopleRoot))
            {
                if (string.Equals(subDir, targetFolder, StringComparison.OrdinalIgnoreCase))
                    continue; // Already the target folder.

                var xmlPath = Path.Combine(subDir, "person.xml");
                var existingId = ReadPersonIdFromXml(xmlPath);
                if (existingId == personId)
                {
                    // Found this person's old folder — rename it.
                    var oldName = Path.GetFileName(subDir);
                    var newName = Path.GetFileName(targetFolder);

                    // Ensure target doesn't exist (collision detection handles this later).
                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.Move(subDir, targetFolder);

                        // Update the local_headshot_path if it pointed to the old folder.
                        var oldHeadshot = Path.Combine(subDir, "headshot.jpg");
                        var newHeadshot = Path.Combine(targetFolder, "headshot.jpg");
                        if (File.Exists(newHeadshot))
                        {
                            _personRepo.UpdateLocalHeadshotPathAsync(
                                personId, newHeadshot, CancellationToken.None)
                                .GetAwaiter().GetResult();
                        }

                        _logger.LogInformation(
                            "Renamed person folder: '{OldName}' → '{NewName}'",
                            oldName, newName);
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Person folder rename failed for {PersonId}; continuing", personId);
        }

        return false;
    }

    /// <summary>
    /// Logs a <see cref="SystemActionType.PersonFolderRenamed"/> activity entry.
    /// </summary>
    private async Task LogPersonFolderRenamedAsync(
        Guid personId, string newName, CancellationToken ct)
    {
        try
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.PersonFolderRenamed,
                EntityId   = personId,
                EntityType = "Person",
                Detail     = $"Person folder renamed to \"{newName}\"",
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to log person folder rename activity");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveBaseUrl(
        IExternalMetadataProvider provider,
        Dictionary<string, string> endpointMap)
    {
        // Wikidata uses a dedicated "wikidata_api" endpoint key.
        // Config-driven adapters self-resolve from their own endpoints map,
        // so the default (provider.Name) is a reasonable fallback.
        var key = provider.Name switch
        {
            "wikidata" => "wikidata_api",
            _          => provider.Name,
        };

        // Try the mapped key first, then fall back to the "api" conventional key.
        if (endpointMap.TryGetValue(key, out var url))
            return url;
        if (endpointMap.TryGetValue("api", out var apiUrl))
            return apiUrl;

        return string.Empty;
    }

    private static string? ResolveSparqlBaseUrl(Dictionary<string, string> endpointMap)
        => endpointMap.TryGetValue("wikidata_sparql", out var url) ? url : null;

    private static ProviderLookupRequest BuildLookupRequest(
        HarvestRequest request,
        IExternalMetadataProvider provider,
        string baseUrl,
        string? sparqlBaseUrl = null)
    {
        var h = request.Hints;
        return new ProviderLookupRequest
        {
            EntityId     = request.EntityId,
            EntityType   = request.EntityType,
            MediaType    = request.MediaType,
            Title        = h.GetValueOrDefault("title"),
            Author       = h.GetValueOrDefault("author"),
            Narrator     = h.GetValueOrDefault("narrator"),
            Asin         = h.GetValueOrDefault("asin"),
            Isbn         = h.GetValueOrDefault("isbn"),
            AppleBooksId = h.GetValueOrDefault("apple_books_id"),
            AudibleId    = h.GetValueOrDefault("audible_id"),
            TmdbId       = h.GetValueOrDefault("tmdb_id"),
            ImdbId       = h.GetValueOrDefault("imdb_id"),
            PersonName   = h.GetValueOrDefault("name"),
            PersonRole   = h.GetValueOrDefault("role"),
            BaseUrl      = baseUrl,
            SparqlBaseUrl = sparqlBaseUrl,
            Hints        = h,
        };
    }

    private (IReadOnlyDictionary<Guid, double> Weights,
             IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, double>>? FieldWeights)
        BuildWeightMaps(IReadOnlyList<MediaEngine.Storage.Models.ProviderConfiguration> providerConfigs)
    {
        var weights      = new Dictionary<Guid, double>();
        Dictionary<Guid, IReadOnlyDictionary<string, double>>? fieldWeights = null;

        foreach (var provider in _providers)
        {
            var provConfig = providerConfigs
                .FirstOrDefault(p => string.Equals(p.Name, provider.Name,
                    StringComparison.OrdinalIgnoreCase));

            if (provConfig is null) continue;

            weights[provider.ProviderId] = provConfig.Weight;

            if (provConfig.FieldWeights.Count > 0)
            {
                fieldWeights ??= new Dictionary<Guid, IReadOnlyDictionary<string, double>>();
                fieldWeights[provider.ProviderId] =
                    (IReadOnlyDictionary<string, double>)provConfig.FieldWeights;
            }
        }

        return (weights, fieldWeights);
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { await _processingLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
        _concurrency.Dispose();
    }
}
