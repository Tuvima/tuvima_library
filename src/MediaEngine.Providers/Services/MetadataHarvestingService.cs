using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
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

    /// <summary>
    /// Per-QID lock preventing concurrent merge attempts for the same Wikidata
    /// identifier. The merge is idempotent but the lock eliminates redundant
    /// ReassignAllLinksAsync / DeleteAsync / PersonMerged log noise.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _qidMergeLocks = new(StringComparer.OrdinalIgnoreCase);

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IReadOnlyList<IExternalMetadataProvider> _providers;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IFictionalEntityRepository _fictionalEntityRepo;
    private readonly IRelationshipPopulationService _relPopService;
    private readonly IScoringEngine _scoringEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfigurationLoader _configLoader;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IImageCacheRepository _imageCache;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IQidLabelRepository _qidLabelRepo;
    private readonly IEntityTimelineRepository? _timelineRepo;
    private readonly AssetPathService? _assetPathService;
    private readonly ImagePathService? _imagePathService;
    private readonly ILogger<MetadataHarvestingService> _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MetadataHarvestingService(
        IEnumerable<IExternalMetadataProvider> providers,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IPersonRepository personRepo,
        IFictionalEntityRepository fictionalEntityRepo,
        IRelationshipPopulationService relPopService,
        IScoringEngine scoringEngine,
        IEventPublisher eventPublisher,
        IConfigurationLoader configLoader,
        IHttpClientFactory httpFactory,
        IImageCacheRepository imageCache,
        ISystemActivityRepository activityRepo,
        IQidLabelRepository qidLabelRepo,
        ILogger<MetadataHarvestingService> logger,
        AssetPathService? assetPathService = null,
        ImagePathService? imagePathService = null,
        IEntityTimelineRepository? timelineRepo = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(claimRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(fictionalEntityRepo);
        ArgumentNullException.ThrowIfNull(relPopService);
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
        _scoringEngine        = scoringEngine;
        _eventPublisher       = eventPublisher;
        _configLoader         = configLoader;
        _httpFactory          = httpFactory;
        _imageCache           = imageCache;
        _activityRepo         = activityRepo;
        _qidLabelRepo         = qidLabelRepo;
        _assetPathService     = assetPathService;
        _imagePathService     = imagePathService;
        _timelineRepo         = timelineRepo;
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

    /// <inheritdoc/>
    public Task ProcessSynchronousAsync(HarvestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProcessOneAsync(request, ct);
    }

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

        // Build per-provider endpoint map (avoids key collisions across providers).
        var providerEndpoints = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in allProviderConfigs)
            providerEndpoints[pc.Name] = new Dictionary<string, string>(pc.Endpoints, StringComparer.OrdinalIgnoreCase);

        // Build provider weight maps from provider configs.
        var (providerWeights, providerFieldWeights) = BuildWeightMaps(allProviderConfigs);

        var sparqlBaseUrl = ResolveSparqlBaseUrl(providerEndpoints);

        // For Person entities: skip only when a concurrent worker already filled the visible profile.
        if (request.EntityType == EntityType.Person && request.EntityId != Guid.Empty)
        {
            var alreadyEnriched = await _personRepo.FindByIdAsync(request.EntityId, ct).ConfigureAwait(false);
            if (alreadyEnriched?.EnrichedAt is not null && HasCompletePersonProfile(alreadyEnriched))
            {
                _logger.LogDebug(
                    "Person {Id} already enriched at {EnrichedAt} — skipping duplicate harvest",
                    request.EntityId, alreadyEnriched.EnrichedAt);
                return;
            }
        }

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(request.MediaType) || !provider.CanHandle(request.EntityType))
                continue;

            var baseUrl = ResolveBaseUrl(provider, providerEndpoints);
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
                SignalREvents.MetadataHarvested,
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
            // Build canonical values dictionary for relationship population.
            var canonicalDict = canonicals
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            if (canonicalDict.Count == 0)
            {
                _logger.LogWarning(
                    "Fictional entity enrichment returned no canonical values for entity {EntityId}; leaving it retryable",
                    request.EntityId);
                return;
            }

            // Resolve universe QID from hints.
            var universeQid = request.Hints.GetValueOrDefault("universe_qid");

            // Resolve depth parameters from hint and config.
            var currentDepth = 0;
            if (request.Hints.TryGetValue("enrichment_depth", out var depthStr) &&
                int.TryParse(depthStr, out var parsedDepth))
            {
                currentDepth = parsedDepth;
            }

            var maxDepth = 1;
            try
            {
                maxDepth = _configLoader.LoadHydration().FictionalEntityEnrichmentDepth;
            }
            catch
            {
                // Fall back to default if config is unavailable.
            }

            // Populate relationship edges from _qid claims.
            await _relPopService.PopulateAsync(
                request.Hints.GetValueOrDefault(BridgeIdKeys.WikidataQid) ?? string.Empty,
                canonicalDict,
                universeQid ?? string.Empty,
                universeLabel: null,
                contextWorkQid: null,
                temporalQualifiers: null,
                currentDepth: currentDepth,
                maxDepth: maxDepth,
                ct: ct).ConfigureAwait(false);

            await _fictionalEntityRepo.UpdateEnrichmentAsync(
                request.EntityId,
                canonicalDict.GetValueOrDefault(MetadataFieldConstants.Description),
                imageUrl: null,
                DateTimeOffset.UtcNow,
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
            var entityQid = request.Hints.GetValueOrDefault(BridgeIdKeys.WikidataQid);
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
                SignalREvents.FictionalEntityEnriched,
                new FictionalEntityEnrichedEvent(request.EntityId, label, entitySubType, universeQid),
                ct).ConfigureAwait(false);

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Fictional entity enrichment post-processing failed for entity {Id}",
                request.EntityId);
        }
    }

    private static bool HasCompletePersonProfile(Person person)
        => !string.IsNullOrWhiteSpace(person.Biography)
           && (!string.IsNullOrWhiteSpace(person.HeadshotUrl)
               || !string.IsNullOrWhiteSpace(person.LocalHeadshotPath));

    private async Task HandlePersonEnrichmentAsync(
        HarvestRequest request,
        IReadOnlyList<ProviderClaim> claims,
        IExternalMetadataProvider provider,
        CancellationToken ct)
    {
        var personSw = System.Diagnostics.Stopwatch.StartNew();

        var qid         = claims.FirstOrDefault(c => c.Key == BridgeIdKeys.WikidataQid)?.Value;
        var headshotUrl = claims.FirstOrDefault(c => c.Key == "headshot_url")?.Value;
        var biography   = claims.FirstOrDefault(c => c.Key == "biography")?.Value;
        var name        = claims.FirstOrDefault(c => c.Key == "name")?.Value;

        if (qid is null && headshotUrl is null && biography is null && name is null)
            return;

        // ── Pseudonym detection from current claims ───────────────────────────
        // Compute isPseudonym HERE — before the dedup block — so the merge guard
        // can use the in-memory value even before UpdateBiographicalFieldsAsync
        // has persisted it to the DB (which happens further down).
        //
        // A person is a pseudonym when:
        //   1. P31 (instance_of) QID is Q127843 (pen name) or Q15632617 (fictional
        //      human used as shared pen name), OR
        //   2. P1773 (attributed_to) claims exist — meaning real people stand behind
        //      this name, OR
        //   3. P527 (has_parts) claims exist with QID references — collective pen
        //      names like "James S. A. Corey" use P527 to list constituent authors
        //      (Daniel Abraham + Ty Franck). The Data Extension API may not return
        //      P31 for some entities, so P527 presence is a reliable fallback signal.
        // Check the _qid claim (stable Wikidata IDs) first; fall back to the
        // label claim for the "pen name" string in case the adapter returned
        // labels-only.
        var isPseudonym = claims.Any(c =>
                c.Key == "instance_of_qid" &&
                (c.Value.Contains("Q127843", StringComparison.OrdinalIgnoreCase) ||
                 c.Value.Contains("Q15632617", StringComparison.OrdinalIgnoreCase)))
            || claims.Any(c =>
                c.Key == "instance_of" &&
                c.Value.Contains("pen name", StringComparison.OrdinalIgnoreCase))
            || claims.Any(c =>
                c.Key == "attributed_to_qid" &&
                !string.IsNullOrWhiteSpace(c.Value))
            || claims.Any(c =>
                c.Key == "has_parts_qid" &&
                !string.IsNullOrWhiteSpace(c.Value));

        // ── Group detection from current claims ───────────────────────────────
        // A person is a group (musical ensemble / band) when P31 (instance_of) QID
        // is Q215380 (musical group) or Q5741069 (musical ensemble).
        var isGroup = claims.Any(c =>
            c.Key == "instance_of_qid" &&
            (c.Value.Contains("Q215380", StringComparison.OrdinalIgnoreCase) ||
             c.Value.Contains("Q5741069", StringComparison.OrdinalIgnoreCase)));

        // ── QID-based deduplication ───────────────────────────────────────────
        // If another person record already owns this Wikidata QID, skip creating
        // a duplicate folder.  Both DB records will end up pointing at the same
        // filesystem folder (same "Name (QID)" path) via TryRenameExistingPersonFolder;
        // we just need to make sure the first-arriving enrichment wins the write.
        //
        // EXCEPTION: pseudonym pairs must NOT be merged. A pseudonym (e.g. "Richard
        // Bachman") and its real person (e.g. "Stephen King") carry different QIDs
        // but may share links. If either person is a pseudonym or they are linked
        // via person_aliases, we skip the merge and let both records coexist.
        //
        // A per-QID semaphore ensures only one thread executes the FindByQidAsync +
        // merge block at a time.  The merge is idempotent but the lock eliminates
        // redundant ReassignAllLinksAsync / DeleteAsync / PersonMerged log noise when
        // two enrichment tasks race to the same QID (e.g. James S.A. Corey Q6142591).
        bool isQidDuplicate = false;
        if (!string.IsNullOrWhiteSpace(qid))
        {
            var qidLock = _qidMergeLocks.GetOrAdd(qid, _ => new SemaphoreSlim(1, 1));
            await qidLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var canonicalPerson = await _personRepo.FindByQidAsync(qid, ct).ConfigureAwait(false);
                if (canonicalPerson is not null && canonicalPerson.Id != request.EntityId)
                {
                    // Check pseudonym relationship — never merge pseudonym ↔ real person.
                    // isPseudonym is the value computed from the current claims batch (above);
                    // this catches the case where the DB flag hasn't been persisted yet
                    // because UpdateBiographicalFieldsAsync hasn't run yet at this point.
                    var currentPerson = await _personRepo.FindByIdAsync(request.EntityId, ct).ConfigureAwait(false);
                    bool isPseudonymPair = isPseudonym                          // in-memory, from current claims
                                       || canonicalPerson.IsPseudonym           // DB flag on the canonical record
                                       || currentPerson?.IsPseudonym == true;   // DB flag on current record

                    // Also use IsPseudonymOrAliasAsync for a comprehensive DB-level check
                    // on both persons — catches alias links created in prior enrichment runs.
                    if (!isPseudonymPair)
                    {
                        isPseudonymPair = await _personRepo.IsPseudonymOrAliasAsync(request.EntityId, ct).ConfigureAwait(false)
                                       || await _personRepo.IsPseudonymOrAliasAsync(canonicalPerson.Id, ct).ConfigureAwait(false);
                    }

                    if (isPseudonymPair)
                    {
                        _logger.LogInformation(
                            "Person {Id} shares QID {Qid} with {CanonicalId} but is a pseudonym pair — skipping merge",
                            request.EntityId, qid, canonicalPerson.Id);
                    }
                    else
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
                            SignalREvents.PersonEnriched,
                            new PersonEnrichedEvent(canonicalPerson.Id, canonicalPerson.Name, canonicalPerson.HeadshotUrl, qid),
                            ct).ConfigureAwait(false);

                        return; // Merge complete — skip remaining enrichment for the deleted duplicate.
                    }
                }
            }
            finally
            {
                qidLock.Release();
            }
        }

        await _personRepo.UpdateEnrichmentAsync(request.EntityId, qid, headshotUrl, biography, name, ct)
            .ConfigureAwait(false);

        // Persist biographical fields from Wikidata claims.
        var dateOfBirth  = claims.FirstOrDefault(c => c.Key == "date_of_birth")?.Value;
        var dateOfDeath  = claims.FirstOrDefault(c => c.Key == "date_of_death")?.Value;
        var placeOfBirth = claims.FirstOrDefault(c => c.Key == "place_of_birth")?.Value;
        var placeOfDeath = claims.FirstOrDefault(c => c.Key == "place_of_death")?.Value;
        var nationality  = claims.FirstOrDefault(c => c.Key == "nationality")?.Value
            ?? claims.FirstOrDefault(c => c.Key == "country_of_citizenship")?.Value;
        // isPseudonym was computed from claims before the dedup block above.

        if (dateOfBirth is not null || dateOfDeath is not null || placeOfBirth is not null ||
            placeOfDeath is not null || nationality is not null || isPseudonym || isGroup)
        {
            await _personRepo.UpdateBiographicalFieldsAsync(
                request.EntityId, dateOfBirth, dateOfDeath,
                placeOfBirth, placeOfDeath, nationality, isPseudonym, isGroup, ct)
                .ConfigureAwait(false);
        }

        // Social media and contact fields from Wikidata.
        var occupation = NormalizeMultiValue(claims.FirstOrDefault(c => c.Key == "occupation")?.Value);
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

        // Wikipedia description is now fetched by ReconciliationAdapter.FetchPersonAsync
        // (folded in as part of Task 1 cleanup). No separate call needed here.

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

        // Log PersonHydrated to the persistent activity ledger with headshot URL.
        try
        {
            var changesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                name = personName,
                qid,
                headshot = headshotUrl,
            });

            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType  = SystemActionType.PersonHydrated,
                EntityId    = request.EntityId,
                EntityType  = "Person",
                CollectionName     = personName,
                ChangesJson = changesJson,
                Detail      = $"Person \"{personName}\" enriched from Wikidata",
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to log PersonHydrated activity for {PersonId}", request.EntityId);
        }

        await _eventPublisher.PublishAsync(
            SignalREvents.PersonEnriched,
            new PersonEnrichedEvent(request.EntityId, personName, headshotUrl, qid),
            ct).ConfigureAwait(false);

        // Timeline: record person enrichment.
        if (_timelineRepo is not null)
        {
            try
            {
                await _timelineRepo.InsertEventAsync(new EntityEvent
                {
                    EntityId    = request.EntityId,
                    EntityType  = "Person",
                    EventType   = "person_enriched",
                    Stage       = null,
                    Trigger     = "person_enrichment",
                    ResolvedQid = qid,
                    Detail      = $"Person \"{personName}\" enriched from Wikidata",
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to write person_enriched timeline event for {PersonId}",
                    request.EntityId);
            }
        }

        personSw.Stop();
        _logger.LogInformation(
            "[PERF] Person enrichment {PersonId} (\"{Name}\"): {ElapsedMs}ms (QID={Qid}, pseudonym={IsPseudonym})",
            request.EntityId, personName, personSw.ElapsedMilliseconds,
            qid ?? "(none)", isPseudonym);
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
            // P1773 (attributed_to) or P527 (has_parts): real people behind a pen name.
            // James S. A. Corey (Q6142591) uses P527 to list Daniel Abraham + Ty Franck.
            var attributedTo = claims
                .Where(c => c.Key == "attributed_to_qid" || c.Key == "has_parts_qid")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (isPseudonym && attributedTo.Count > 0)
            {
                foreach (var realQidList in attributedTo)
                {
                    var realQid = realQidList.Split("::")[0];
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

            // P742 (pseudonym): pen names used by this person.
            var pseudonymQids = claims
                .Where(c => c.Key == "pseudonym_qid")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            foreach (var penQidList in pseudonymQids)
            {
                var penQid = penQidList.Split("::")[0];
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
        // Fix 3: dedup — reuse an existing person row if the QID is already in the DB
        // instead of creating a second "Unknown Person (Qxxx)" stub.
        var existing = await _personRepo.FindByQidAsync(missingQid, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogDebug(
                "CreateAndLinkStubPersonAsync: reusing existing person {Id} for QID {Qid}",
                existing.Id, missingQid);

            if (isMissingPersonPseudonym)
                await _personRepo.LinkAliasAsync(existing.Id, existingPersonId, ct).ConfigureAwait(false);
            else
                await _personRepo.LinkAliasAsync(existingPersonId, existing.Id, ct).ConfigureAwait(false);

            return;
        }

        // Best-effort: use the locally cached Wikidata label if we have one
        // (populated by prior enrichment runs via _qidLabelRepo.UpsertAsync).
        // If the cache misses, fall back to a placeholder name; the next
        // enrichment pass will overwrite it once Wikidata is queried.
        string stubName = $"Unknown Person ({missingQid})";
        try
        {
            var cachedLabel = await _qidLabelRepo.GetLabelAsync(missingQid, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(cachedLabel))
                stubName = cachedLabel;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "QID label cache lookup failed for {Qid}; using placeholder name", missingQid);
        }

        var stubId = Guid.NewGuid();
        var stub = new Person
        {
            Id = stubId,
            Name = stubName,
            Roles = ["Author"], // Default role
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
    /// Downloads headshot from Wikimedia Commons and saves it to the person's image
    /// directory under <c>{LibraryRoot}/.data/images/people/{QID}/headshot.jpg</c>
    /// (via <see cref="ImagePathService"/>). The directory is created only when a
    /// headshot URL is available — no empty directories are created during entity
    /// creation without an image.
    ///
    /// Person metadata is stored exclusively in the database; the images directory
    /// holds only the headshot image.
    /// </summary>
    private async Task PersistPersonStorageAsync(
        Guid personId,
        Person? person,
        string? headshotUrl,
        CancellationToken ct)
    {
        // Skip entirely if there is no headshot URL — no directory is created.
        if (string.IsNullOrEmpty(headshotUrl))
        {
            var noUrlName = person?.Name ?? personId.ToString();
            _logger.LogDebug("Person {Name}: no headshot URL available from Wikidata", noUrlName);
            return;
        }

        // Both Name and QID are required to resolve a stable image path.
        if (person is null)
            return;

        _logger.LogDebug("Person {Name}: headshot URL found at {Url}", person.Name, headshotUrl);

        try
        {
            string headshotPath;
            if (_assetPathService is not null)
            {
                headshotPath = _assetPathService.GetPersonHeadshotPath(personId);
            }
            else if (_imagePathService is not null
                     && !string.IsNullOrWhiteSpace(person.WikidataQid))
            {
                // Use centralized .data/images/people/{QID}/ path.
                headshotPath = Path.Combine(_imagePathService.GetPersonImageDir(person.WikidataQid), "headshot.jpg");
            }
            else
            {
                // Legacy fallback: .people/{Name} ({QID})/ under LibraryRoot.
                var core = _configLoader.LoadCore();
                if (string.IsNullOrWhiteSpace(core.LibraryRoot)
                    || string.IsNullOrWhiteSpace(person.WikidataQid)
                    || string.IsNullOrWhiteSpace(person.Name))
                    return;
                var folderName = $"{SanitizeForFilesystem(person.Name)} ({person.WikidataQid})";
                headshotPath = Path.Combine(core.LibraryRoot, ".people", folderName, "headshot.jpg");
            }

            // Download headshot if URL is available and file doesn't exist.
            if (File.Exists(headshotPath))
            {
                _logger.LogDebug("Person {Name}: headshot already exists at {Path} — skipping download", person.Name, headshotPath);
                await _personRepo.UpdateLocalHeadshotPathAsync(personId, headshotPath, ct)
                    .ConfigureAwait(false);
                return;
            }

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

                    // Create the directory only when we have bytes to write.
                    AssetPathService.EnsureDirectory(headshotPath);

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

                    _logger.LogInformation("Person {Name}: headshot downloaded to {Path}", person.Name, headshotPath);
                }
                else
                {
                    _logger.LogWarning("Person {Name}: headshot download failed — empty response body from {Url}", person.Name, headshotUrl);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Person {Name}: headshot download failed — {Reason}",
                    person.Name, ex.Message);
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
    /// Normalizes multi-valued strings that use <c>|||</c> separator into
    /// human-readable comma-separated form (e.g. "Actor, Screenwriter, Producer").
    /// Returns <c>null</c> for null/whitespace input.
    /// <para>DEPRECATED: Legacy safety net. New Reconciliation API emits individual claims;
    /// canonical_values_array stores decomposed values. This helper is retained for
    /// backward compatibility with pre-array data.</para>
    /// </summary>
    private static string? NormalizeMultiValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (!value.Contains("|||", StringComparison.Ordinal)) return value;

        var parts = value.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? string.Join(", ", parts) : value;
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
    /// Deletes the person image directory from disk during QID-based deduplication.
    /// When ImagePathService is active, deletes the .data/images/people/{QID}/ directory.
    /// Legacy fallback: searches .people/ for any folder owned by the given person (via person.xml ID).
    /// </summary>
    private void DeletePersonFolder(Person person)
    {
        try
        {
            if (_assetPathService is not null)
            {
                var personDir = _assetPathService.GetPersonRoot(person.Id);
                if (Directory.Exists(personDir))
                {
                    Directory.Delete(personDir, recursive: true);
                    _logger.LogInformation("Deleted duplicate person asset dir: {Dir}", personDir);
                }
                return;
            }

            // Legacy fallback: search .people/ for a folder identified by person.xml.
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


    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveBaseUrl(
        IExternalMetadataProvider provider,
        Dictionary<string, Dictionary<string, string>> providerEndpoints)
    {
        if (providerEndpoints.TryGetValue(provider.Name, out var endpoints))
        {
            if (endpoints.TryGetValue(provider.Name, out var url))
                return url.TrimEnd('/');
            if (endpoints.TryGetValue("api", out var apiUrl))
                return apiUrl.TrimEnd('/');
            if (endpoints.Count > 0)
                return endpoints.Values.First().TrimEnd('/');
        }
        return string.Empty;
    }

    private static string? ResolveSparqlBaseUrl(Dictionary<string, Dictionary<string, string>> providerEndpoints)
        => providerEndpoints.Values
            .SelectMany(e => e)
            .FirstOrDefault(kv => kv.Key.Equals("wikidata_sparql", StringComparison.OrdinalIgnoreCase))
            .Value;

    private ProviderLookupRequest BuildLookupRequest(
        HarvestRequest request,
        IExternalMetadataProvider provider,
        string baseUrl,
        string? sparqlBaseUrl = null)
    {
        var h       = request.Hints;
        var core    = _configLoader.LoadCore();
        var lang    = string.IsNullOrWhiteSpace(core.Language.Metadata) ? "en" : core.Language.Metadata;
        var country = string.IsNullOrWhiteSpace(core.Country)  ? "us" : core.Country.ToUpperInvariant();
        return new ProviderLookupRequest
        {
            EntityId     = request.EntityId,
            EntityType   = request.EntityType,
            MediaType    = request.MediaType,
            Title        = h.GetValueOrDefault("title"),
            Author       = h.GetValueOrDefault("author"),
            Year         = h.GetValueOrDefault("year"),
            Narrator     = h.GetValueOrDefault("narrator"),
            Asin         = h.GetValueOrDefault(BridgeIdKeys.Asin),
            Isbn         = NormalizeIsbnHint(h.GetValueOrDefault(BridgeIdKeys.Isbn)),
            AppleBooksId = h.GetValueOrDefault(BridgeIdKeys.AppleBooksId),
            AudibleId    = h.GetValueOrDefault(BridgeIdKeys.AudibleId),
            TmdbId       = h.GetValueOrDefault(BridgeIdKeys.TmdbId),
            ImdbId       = h.GetValueOrDefault(BridgeIdKeys.ImdbId),
            PersonName   = h.GetValueOrDefault("name"),
            PersonRole   = h.GetValueOrDefault("role"),
            PreResolvedQid = request.PreResolvedQid ?? h.GetValueOrDefault(BridgeIdKeys.WikidataQid),
            BaseUrl      = baseUrl,
            SparqlBaseUrl = sparqlBaseUrl,
            Language     = lang,
            Country      = country,
            HydrationPass = request.Pass,
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

    /// <summary>
    /// Safety-net normalization for ISBN hints — strips URI prefixes and non-digit characters.
    /// </summary>
    private static string? NormalizeIsbnHint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length is 10 or 13 ? digits : raw?.Trim();
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
