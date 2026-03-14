using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Three-stage authority-first hydration pipeline orchestrator.
///
/// <list type="number">
///   <item><b>Stage 1 — Authority Match:</b> Wikidata resolves the work's
///     identity via bridge IDs or title search, SPARQL deep hydration, Hub
///     Intelligence, and Person Enrichment. On failure an
///     <see cref="ReviewTrigger.AuthorityMatchFailed"/> review item is created.</item>
///   <item><b>Stage 2 — Context Match:</b> Wikipedia provides a human-readable
///     description using the QID resolved in Stage 1. Skipped when no QID
///     was found and <c>SkipWikipediaWithoutQid</c> is <c>true</c>.</item>
///   <item><b>Stage 3 — Retail Match:</b> Runs retail providers in waterfall
///     order from <c>config/slots.json</c>, using bridge IDs deposited by
///     Stage 1 for precise lookups. On all-provider failure a
///     <see cref="ReviewTrigger.ContentMatchFailed"/> review item is created.</item>
/// </list>
///
/// Architecture:
/// - A bounded <c>Channel&lt;HarvestRequest&gt;</c> (capacity 500, DropOldest policy)
///   decouples ingestion from the pipeline.
/// - A single reader task processes requests sequentially.
/// - After each stage, claims are persisted and the entity is re-scored.
/// - Post-pipeline: overall confidence check creates review entries for low-confidence entities.
/// </summary>
public sealed class HydrationPipelineService : IHydrationPipelineService, IAsyncDisposable
{
    // ── Channel ───────────────────────────────────────────────────────────────

    private readonly Channel<HarvestRequest> _channel;
    private readonly Task _processingLoop;
    private readonly CancellationTokenSource _cts = new();

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IReadOnlyList<IExternalMetadataProvider> _providers;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IScoringEngine _scoringEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfigurationLoader _configLoader;
    private readonly IRecursiveIdentityService _identity;
    private readonly IRecursiveFictionalEntityService _fictionalEntityService;
    private readonly INarrativeRootResolver _narrativeRootResolver;
    private readonly IReviewQueueRepository _reviewRepo;
    private readonly IHubRepository _hubRepo;
    private readonly IMediaAssetRepository _assetRepo;
    private readonly IImageCacheRepository _imageCache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IWriteBackService _writeBack;
    private readonly IAutoOrganizeService _autoOrganize;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly ICanonicalValueArrayRepository _arrayRepo;
    private readonly IHeroBannerGenerator _heroGenerator;
    private readonly IDeferredEnrichmentRepository _deferredRepo;
    private readonly ILogger<HydrationPipelineService> _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public HydrationPipelineService(
        IEnumerable<IExternalMetadataProvider> providers,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IPersonRepository personRepo,
        IScoringEngine scoringEngine,
        IEventPublisher eventPublisher,
        IConfigurationLoader configLoader,
        IRecursiveIdentityService identity,
        IRecursiveFictionalEntityService fictionalEntityService,
        INarrativeRootResolver narrativeRootResolver,
        IReviewQueueRepository reviewRepo,
        IHubRepository hubRepo,
        IMediaAssetRepository assetRepo,
        IImageCacheRepository imageCache,
        IHttpClientFactory httpFactory,
        IWriteBackService writeBack,
        IAutoOrganizeService autoOrganize,
        ISystemActivityRepository activityRepo,
        ICanonicalValueArrayRepository arrayRepo,
        IHeroBannerGenerator heroGenerator,
        IDeferredEnrichmentRepository deferredRepo,
        ILogger<HydrationPipelineService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(claimRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(scoringEngine);
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(fictionalEntityService);
        ArgumentNullException.ThrowIfNull(narrativeRootResolver);
        ArgumentNullException.ThrowIfNull(reviewRepo);
        ArgumentNullException.ThrowIfNull(hubRepo);
        ArgumentNullException.ThrowIfNull(assetRepo);
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(writeBack);
        ArgumentNullException.ThrowIfNull(autoOrganize);
        ArgumentNullException.ThrowIfNull(activityRepo);
        ArgumentNullException.ThrowIfNull(arrayRepo);
        ArgumentNullException.ThrowIfNull(heroGenerator);
        ArgumentNullException.ThrowIfNull(deferredRepo);
        ArgumentNullException.ThrowIfNull(logger);

        _providers      = providers.ToList();
        _claimRepo      = claimRepo;
        _canonicalRepo  = canonicalRepo;
        _personRepo     = personRepo;
        _scoringEngine  = scoringEngine;
        _eventPublisher = eventPublisher;
        _configLoader   = configLoader;
        _identity                = identity;
        _fictionalEntityService  = fictionalEntityService;
        _narrativeRootResolver   = narrativeRootResolver;
        _reviewRepo              = reviewRepo;
        _hubRepo        = hubRepo;
        _assetRepo      = assetRepo;
        _imageCache     = imageCache;
        _httpFactory     = httpFactory;
        _writeBack      = writeBack;
        _autoOrganize   = autoOrganize;
        _activityRepo   = activityRepo;
        _arrayRepo      = arrayRepo;
        _heroGenerator  = heroGenerator;
        _deferredRepo   = deferredRepo;
        _logger         = logger;

        _channel = Channel.CreateBounded<HarvestRequest>(new BoundedChannelOptions(500)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _processingLoop = Task.Run(ProcessLoopAsync);
    }

    // ── IHydrationPipelineService ─────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_channel.Writer.TryWrite(request))
        {
            _logger.LogError(
                "Hydration queue overflow — request for entity {Id} was dropped. " +
                "Consider increasing queue capacity or reducing ingestion rate.",
                request.EntityId);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<HydrationResult> RunSynchronousAsync(
        HarvestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await RunPipelineAsync(request, ct).ConfigureAwait(false);
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
                try
                {
                    await RunPipelineAsync(request, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Hydration pipeline failed for entity {Id} — no MediaAdded entry created",
                        request.EntityId);
                }
            }
        }
        catch (OperationCanceledException) { /* Graceful shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HydrationPipelineService processing loop terminated unexpectedly");
        }
    }

    // ── Pipeline orchestration ────────────────────────────────────────────────

    private async Task<HydrationResult> RunPipelineAsync(
        HarvestRequest request, CancellationToken ct)
    {
        var result       = new HydrationResult();
        var hydration    = _configLoader.LoadHydration();
        var provConfigs  = _configLoader.LoadAllProviders();
        var slots        = _configLoader.LoadSlots();
        var core         = _configLoader.LoadCore();
        var lang         = string.IsNullOrWhiteSpace(core.Language) ? "en" : core.Language.ToLowerInvariant();
        var country      = string.IsNullOrWhiteSpace(core.Country)  ? "us" : core.Country.ToLowerInvariant();

        // §3.24: Determine effective pass. When two-pass is disabled,
        // all requests run the full Universe pipeline for backward compat.
        var effectivePass = hydration.TwoPassEnabled
            ? request.Pass
            : HydrationPass.Universe;

        // Build composite endpoint map.
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        var sparqlBaseUrl = endpointMap.TryGetValue("wikidata_sparql", out var sparql)
            ? sparql : null;

        _logger.LogDebug(
            "Pipeline endpoint map: {Endpoints}",
            string.Join(", ", endpointMap.Select(kv => $"{kv.Key}={kv.Value}")));

        var pipelineSw = System.Diagnostics.Stopwatch.StartNew();
        long s1Ms = 0, s2Ms = 0, s3Ms = 0;

        // ── Stage 1: Authority Match (Wikidata) ─────────────────────────────
        //
        // Resolves the work's identity via Wikidata: bridge ID lookup or title
        // search → SPARQL deep hydration → Hub Intelligence → Person Enrichment.
        // This stage runs first so that bridge IDs (ISBN, ASIN, TMDB, IMDb)
        // are available for Stage 3's precise retail lookups.

        // D2: inject folder hint bridge IDs into the request hints so providers
        // can use them for direct lookups instead of title search.
        if (request.FolderHintBridgeIds is { Count: > 0 })
        {
            var mergedHints = new Dictionary<string, string>(
                request.Hints, StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in request.FolderHintBridgeIds)
            {
                // Only inject hints that are not already present from embedded metadata.
                mergedHints.TryAdd(key, value);
            }

            // If hint provides a wikidata_qid, set it as PreResolvedQid so the
            // WikidataAdapter skips bridge lookup and goes straight to deep hydration.
            if (request.PreResolvedQid is null
                && request.FolderHintBridgeIds.TryGetValue("wikidata_qid", out var hintQid)
                && !string.IsNullOrWhiteSpace(hintQid))
            {
                request = new HarvestRequest
                {
                    EntityId            = request.EntityId,
                    EntityType          = request.EntityType,
                    MediaType           = request.MediaType,
                    Hints               = mergedHints,
                    PreResolvedQid      = hintQid,
                    SuppressActivityEntry = request.SuppressActivityEntry,
                    IngestionRunId      = request.IngestionRunId,
                    FolderHintBridgeIds = request.FolderHintBridgeIds,
                    HintedHubId         = request.HintedHubId,
                    Pass                = request.Pass,
                };
            }
            else
            {
                request = new HarvestRequest
                {
                    EntityId            = request.EntityId,
                    EntityType          = request.EntityType,
                    MediaType           = request.MediaType,
                    Hints               = mergedHints,
                    PreResolvedQid      = request.PreResolvedQid,
                    SuppressActivityEntry = request.SuppressActivityEntry,
                    IngestionRunId      = request.IngestionRunId,
                    FolderHintBridgeIds = request.FolderHintBridgeIds,
                    HintedHubId         = request.HintedHubId,
                    Pass                = request.Pass,
                };
            }

            _logger.LogDebug(
                "Folder hint bridge IDs injected for entity {Id}: {Keys}",
                request.EntityId,
                string.Join(", ", request.FolderHintBridgeIds.Keys));
        }

        var stageSw = System.Diagnostics.Stopwatch.StartNew();
        var stage1Providers = GetProvidersForStage(1, provConfigs, request);
        var stage1Claims    = 0;

        var titleHint = request.Hints.GetValueOrDefault("title", "(unknown)");
        _logger.LogInformation(
            "Pipeline Stage 1 (Authority Match) starting for entity {Id} — title: \"{Title}\", media type: {MediaType}, providers: [{Providers}]",
            request.EntityId, titleHint, request.MediaType,
            stage1Providers.Count > 0
                ? string.Join(", ", stage1Providers.Select(p => p.Name))
                : "(none)");

        // Accumulate raw Stage-1 claims so that person extraction uses the
        // most current SPARQL data — not the post-scoring canonical values,
        // which can be stale when the entity has been hydrated multiple times.
        var stage1RawClaims = new List<ProviderClaim>();

        foreach (var provider in stage1Providers)
        {
            var claims = await FetchFromProviderAsync(
                provider, request, endpointMap, sparqlBaseUrl, lang, country, ct).ConfigureAwait(false);

            if (claims.Count == 0) continue;

            // Check for QID.
            var qidClaim = claims.FirstOrDefault(c => c.Key == "wikidata_qid");
            if (qidClaim is not null)
            {
                result.WikidataQid = qidClaim.Value;
            }

            // Keep raw claims intact for person enrichment (multi-valued
            // author_qid is needed to discover all co-authors).
            stage1RawClaims.AddRange(claims);

            // For scoring, collapse multi-valued person LABEL claims to the
            // first value only.  The display author should be a single name
            // (pen name or primary author), never a "|||"-joined string.
            // The *_qid companions keep their multi-values — they are not
            // displayed and feed only the person enrichment pipeline.
            var claimsForScoring = DeMultiValuePersonLabels(claims);

            await ScoringHelper.PersistClaimsAndScoreAsync(
                request.EntityId, claimsForScoring, provider.ProviderId,
                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                _providers, ct, _arrayRepo, _logger).ConfigureAwait(false);

            stage1Claims += claimsForScoring.Count;

            await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                .ConfigureAwait(false);
        }

        result.Stage1ClaimsAdded = stage1Claims;

        if (stage1Claims > 0)
        {
            await _eventPublisher.PublishAsync(
                "HydrationStageCompleted",
                new HydrationStageCompletedEvent(request.EntityId, 1, stage1Claims, "authority_match"),
                ct).ConfigureAwait(false);

            // Hub Intelligence: assign Work to a Hub based on Wikidata relationships.
            var qidConfirmed = result.WikidataQid is not null;
            await RunHubIntelligenceAsync(request.EntityId, qidConfirmed, ct)
                .ConfigureAwait(false);

            // Pseudonym author protection: if the existing author canonical value
            // names a person flagged as a pseudonym (e.g. "Richard Bachman"), emit
            // a high-confidence author claim so the real person's name from Wikidata
            // does not overwrite the pseudonym.  The sidecar should say the name the
            // work was *published* under.
            var canonicalsAfterS1 = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                .ConfigureAwait(false);

            // Pseudonym author protection is deep enrichment (Pass 2 only).
            if (effectivePass == HydrationPass.Universe)
            {
                await ProtectPseudonymAuthorAsync(request.EntityId, canonicalsAfterS1, ct)
                    .ConfigureAwait(false);
            }

            // Person enrichment: use Stage-1 raw claims when available so that
            // multi-valued author_qid values from Wikidata SPARQL are captured in
            // full.  This avoids the scoring-election side-effect where an entity
            // that has been re-hydrated multiple times has stale single-value votes
            // outvoting the newer multi-value SPARQL result.
            var personRefs = stage1RawClaims.Count > 0
                ? ExtractPersonReferencesFromRawClaims(stage1RawClaims)
                : ExtractPersonReferences(canonicalsAfterS1);
            if (personRefs.Count > 0)
            {
                try
                {
                    await _identity.EnrichAsync(request.EntityId, personRefs, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Person enrichment failed for entity {Id}; continuing",
                        request.EntityId);
                }
            }

            // Write-back: write resolved metadata to the physical file after authority match.
            // Skip on suppressed re-enqueue runs (cover-only) — the initial run
            // already wrote metadata.
            if (request.EntityType == EntityType.MediaAsset
                && !request.SuppressActivityEntry)
            {
                try
                {
                    await _writeBack.WriteMetadataAsync(request.EntityId, "authority_match", ct, request.IngestionRunId)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Write-back after Stage 1 failed for entity {Id}; continuing",
                        request.EntityId);
                }
            }

            // Universe graph: if Stage 1 deposited franchise/series/universe data,
            // resolve the narrative root and discover fictional entities (characters,
            // locations, organizations).  Only runs for works with QIDs.
            // Fictional entity enrichment is deep enrichment (Pass 2 only).
            if (result.WikidataQid is not null && effectivePass == HydrationPass.Universe)
            {
                try
                {
                    await RunFictionalEntityEnrichmentAsync(
                        request.EntityId, result.WikidataQid, canonicalsAfterS1, ct,
                        request.IngestionRunId)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Fictional entity enrichment failed for entity {Id}; continuing",
                        request.EntityId);
                }
            }
        }
        else if (stage1Providers.Count > 0)
        {
            _logger.LogWarning(
                "Pipeline Stage 1 (Authority Match) produced no results for entity {Id}",
                request.EntityId);

            // Authority match failed — create review item.
            await CreateReviewItemAsync(
                request, ReviewTrigger.AuthorityMatchFailed, 0.0,
                $"Wikidata authority match failed for this {request.MediaType}",
                result, ct).ConfigureAwait(false);

            if (!hydration.ContinuePipelineOnAuthorityFailure)
            {
                _logger.LogInformation(
                    "Pipeline halted after Stage 1 failure for entity {Id} — ContinuePipelineOnAuthorityFailure is false",
                    request.EntityId);
                goto PostPipeline;
            }
        }
        else
        {
            _logger.LogWarning(
                "Pipeline Stage 1 skipped for entity {Id}: no authority providers configured",
                request.EntityId);
        }


        s1Ms = stageSw.ElapsedMilliseconds;
        stageSw.Restart();

        // ── Stage 2: Context Match (Wikipedia) ──────────────────────────────
        //
        // Fetches a human-readable description from Wikipedia using the QID
        // resolved in Stage 1.  Silent on failure — no review item is created.

        // Reload canonical values to pick up QID from Stage 1.
        var canonicalsForS2 = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        var resolvedQid = result.WikidataQid
            ?? canonicalsForS2.FirstOrDefault(c => c.Key == "wikidata_qid")?.Value;

        if (hydration.SkipWikipediaWithoutQid && string.IsNullOrEmpty(resolvedQid)
            && string.IsNullOrEmpty(request.PreResolvedQid))
        {
            _logger.LogDebug(
                "Skipping Stage 2 (Wikipedia) for entity {Id} — no QID available",
                request.EntityId);
        }
        else
        {
            var stage2Providers = GetProvidersForStage(2, provConfigs, request);
            var stage2Claims    = 0;

            _logger.LogInformation(
                "Pipeline Stage 2 (Context Match) starting for entity {Id} — QID: {Qid}, providers: [{Providers}]",
                request.EntityId,
                resolvedQid ?? request.PreResolvedQid ?? "(none)",
                stage2Providers.Count > 0
                    ? string.Join(", ", stage2Providers.Select(p => p.Name))
                    : "(none)");

            // Enrich the request with the QID so Wikipedia can look up the article.
            var stage2Request = request;
            if (!string.IsNullOrEmpty(resolvedQid) && string.IsNullOrEmpty(request.PreResolvedQid))
            {
                stage2Request = new HarvestRequest
                {
                    EntityId       = request.EntityId,
                    EntityType     = request.EntityType,
                    MediaType      = request.MediaType,
                    Hints          = new Dictionary<string, string>(request.Hints, StringComparer.OrdinalIgnoreCase),
                    PreResolvedQid = resolvedQid,
                    IngestionRunId = request.IngestionRunId,
                    SuppressActivityEntry = request.SuppressActivityEntry,
                };
            }

            foreach (var provider in stage2Providers)
            {
                var claims = await FetchFromProviderAsync(
                    provider, stage2Request, endpointMap, sparqlBaseUrl, lang, country, ct)
                    .ConfigureAwait(false);

                if (claims.Count == 0) continue;

                await ScoringHelper.PersistClaimsAndScoreAsync(
                    request.EntityId, claims, provider.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                    _providers, ct, _arrayRepo, _logger).ConfigureAwait(false);

                stage2Claims += claims.Count;

                await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                    .ConfigureAwait(false);
            }

            result.Stage2ClaimsAdded = stage2Claims;

            if (stage2Claims > 0)
            {
                await _eventPublisher.PublishAsync(
                    "HydrationStageCompleted",
                    new HydrationStageCompletedEvent(request.EntityId, 2, stage2Claims, "context_match"),
                    ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug(
                    "Stage 2 (Context Match) returned no claims for entity {Id} — continuing silently",
                    request.EntityId);
            }
        }


        s2Ms = stageSw.ElapsedMilliseconds;
        stageSw.Restart();

        // ── Stage 3: Retail Match (Waterfall) ───────────────────────────────
        //
        // Runs retail providers in priority order (primary -> secondary ->
        // tertiary) from slots.json. Bridge IDs deposited by Stage 1 are used
        // for precise lookups. After each provider, overall confidence is
        // checked against the waterfall threshold.

        // Reload canonical values to extract bridge IDs from Stage 1.
        var canonicalsForS3 = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        var bridgeHints = ExtractBridgeHints(canonicalsForS3);

        // Enrich the request with bridge IDs from Stage 1 for precise retail lookups.
        var stage3Request = EnrichRequestWithBridgeHints(request, bridgeHints);

        var waterfallProviders = ResolveWaterfallProviders(slots, provConfigs, stage3Request);
        var stage3Claims = 0;
        IExternalMetadataProvider? lastSuccessfulProvider = null;

        _logger.LogInformation(
            "Pipeline Stage 3 (Retail Match) starting for entity {Id} — bridge IDs: [{BridgeKeys}], providers: [{Providers}]",
            request.EntityId,
            bridgeHints.Count > 0 ? string.Join(", ", bridgeHints.Keys) : "(none)",
            waterfallProviders.Count > 0
                ? string.Join(" -> ", waterfallProviders.Select(p => p.Name))
                : "(none)");

        foreach (var provider in waterfallProviders)
        {
            var claims = await FetchFromProviderAsync(
                provider, stage3Request, endpointMap, sparqlBaseUrl, lang, country, ct).ConfigureAwait(false);

            if (claims.Count > 0)
            {
                await ScoringHelper.PersistClaimsAndScoreAsync(
                    request.EntityId, claims, provider.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                    _providers, ct, _arrayRepo, _logger).ConfigureAwait(false);

                stage3Claims += claims.Count;
                lastSuccessfulProvider = provider;

                await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                    .ConfigureAwait(false);

                // Check confidence after this provider
                var currentConfidence = await ComputeOverallConfidenceAsync(request.EntityId, ct)
                    .ConfigureAwait(false);

                await _eventPublisher.PublishAsync(
                    "HydrationStageCompleted",
                    new HydrationStageCompletedEvent(request.EntityId, 3, claims.Count,
                        $"waterfall_{provider.Name}"),
                    ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Pipeline Stage 3 waterfall: provider '{Provider}' returned {Claims} claims, confidence now {Confidence:P0} (threshold: {Threshold:P0})",
                    provider.Name, claims.Count, currentConfidence,
                    hydration.Stage3WaterfallConfidenceThreshold);

                if (currentConfidence >= hydration.Stage3WaterfallConfidenceThreshold)
                {
                    _logger.LogDebug(
                        "Pipeline Stage 3 waterfall: confidence sufficient after '{Provider}', stopping",
                        provider.Name);
                    break;
                }
            }
            else
            {
                _logger.LogInformation(
                    "Pipeline Stage 3 waterfall: provider '{Provider}' returned no results, continuing",
                    provider.Name);
            }
        }

        result.Stage3ClaimsAdded = stage3Claims;

        if (stage3Claims > 0)
        {
            // Post-hydration auto-resolve: if Stage 3 returned 3+ claims and this
            // entity has a pending AmbiguousMediaType review item, the provider match
            // confirms the media type — auto-resolve the review item.
            if (stage3Claims >= 3 && request.EntityType == EntityType.MediaAsset)
            {
                try
                {
                    var reviews = await _reviewRepo.GetByEntityAsync(request.EntityId, ct)
                        .ConfigureAwait(false);
                    foreach (var review in reviews.Where(r =>
                        r.Status == ReviewStatus.Pending &&
                        r.Trigger == ReviewTrigger.AmbiguousMediaType))
                    {
                        await _reviewRepo.UpdateStatusAsync(
                            review.Id, ReviewStatus.Resolved, "auto_hydration", ct)
                            .ConfigureAwait(false);

                        await _activityRepo.LogAsync(new SystemActivityEntry
                        {
                            ActionType = SystemActionType.ReviewItemResolved,
                            EntityId   = request.EntityId,
                            Detail     = $"AmbiguousMediaType auto-resolved: Stage 3 returned {stage3Claims} claims, confirming media type.",
                        }, ct).ConfigureAwait(false);

                        await _eventPublisher.PublishAsync("ReviewItemResolved", new
                        {
                            review_item_id = review.Id,
                            entity_id      = request.EntityId,
                            status         = "Resolved",
                        }, ct).ConfigureAwait(false);

                        _logger.LogInformation(
                            "AmbiguousMediaType review auto-resolved for entity {Id} — {Claims} claims confirmed type",
                            request.EntityId, stage3Claims);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to auto-resolve AmbiguousMediaType review for entity {Id}",
                        request.EntityId);
                }
            }

            // Write-back: write resolved metadata to the physical file after retail match.
            // Skip on suppressed re-enqueue runs (cover-only) — the initial run
            // already wrote metadata.
            if (request.EntityType == EntityType.MediaAsset
                && !request.SuppressActivityEntry)
            {
                try
                {
                    await _writeBack.WriteMetadataAsync(request.EntityId, "retail_match", ct, request.IngestionRunId)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Write-back after Stage 3 failed for entity {Id}; continuing",
                        request.EntityId);
                }
            }

            // Download cover art from provider URL if no cover.jpg exists yet.
            // This runs even on suppressed re-enqueue — it's the reason for the re-enqueue.
            if (request.EntityType == EntityType.MediaAsset)
            {
                await PersistCoverFromUrlAsync(request.EntityId, ct).ConfigureAwait(false);
            }
        }
        else if (waterfallProviders.Count > 0)
        {
            _logger.LogWarning(
                "Pipeline Stage 3 (Retail Match) produced no results for entity {Id} from any provider in waterfall [{Providers}]",
                request.EntityId, string.Join(", ", waterfallProviders.Select(p => p.Name)));

            // All waterfall providers ran but returned no results -> ContentMatchFailed.
            await CreateReviewItemAsync(
                request, ReviewTrigger.ContentMatchFailed, 0.0,
                $"All retail waterfall providers returned no results for this {request.MediaType}",
                result, ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Pipeline Stage 3 skipped for entity {Id}: no retail providers configured for {MediaType}",
                request.EntityId, request.MediaType);
        }


        s3Ms = stageSw.ElapsedMilliseconds;

        // ── NF Placeholder: retail-only match without Wikidata QID ──────────
        // When Stage 1 (Wikidata) failed to find a QID but Stage 3 (retail)
        // succeeded, the book is likely new or in early release.  Assign a
        // placeholder QID in "NF{6-digit}" format and flag it for future review.
        if (result.WikidataQid is null && result.Stage3ClaimsAdded > 0)
        {
            try
            {
                // Check if entity already has a wikidata_qid (could have been set earlier).
                var existingQid = (await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                    .ConfigureAwait(false))
                    .FirstOrDefault(c => string.Equals(c.Key, "wikidata_qid", StringComparison.OrdinalIgnoreCase));

                if (existingQid is null || string.IsNullOrWhiteSpace(existingQid.Value))
                {
                    var placeholder = await GenerateNfPlaceholderAsync(ct).ConfigureAwait(false);

                    await _canonicalRepo.UpsertBatchAsync(new[]
                    {
                        new CanonicalValue
                        {
                            EntityId     = request.EntityId,
                            Key          = "wikidata_qid",
                            Value        = placeholder,
                            LastScoredAt = DateTimeOffset.UtcNow,
                        },
                        new CanonicalValue
                        {
                            EntityId     = request.EntityId,
                            Key          = "qid_status",
                            Value        = "pending",
                            LastScoredAt = DateTimeOffset.UtcNow,
                        },
                    }, ct).ConfigureAwait(false);

                    // Create a review item so the user can revisit when Wikidata has it.
                    await CreateReviewItemAsync(
                        request, ReviewTrigger.MissingQid, 0.0,
                        $"No Wikidata QID found. Retail match exists ({result.Stage3ClaimsAdded} claims). Placeholder: {placeholder}",
                        result, ct).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Assigned NF placeholder {Placeholder} to entity {Id} — retail match but no Wikidata QID",
                        placeholder, request.EntityId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "NF placeholder generation failed for entity {Id}; continuing",
                    request.EntityId);
            }
        }


        // ── Post-pipeline: confidence check ─────────────────────────────────
        PostPipeline:

        pipelineSw.Stop();
        _logger.LogInformation(
            "[PERF] Hydration {EntityId}: S1={S1Ms}ms S2={S2Ms}ms S3={S3Ms}ms Total={TotalMs}ms (claims: {S1Claims}+{S2Claims}+{S3Claims}={TotalClaims})",
            request.EntityId, s1Ms, s2Ms, s3Ms, pipelineSw.ElapsedMilliseconds,
            result.Stage1ClaimsAdded, result.Stage2ClaimsAdded, result.Stage3ClaimsAdded,
            result.TotalClaimsAdded);

        if (result.TotalClaimsAdded > 0)
        {
            // Reload all claims and get current scoring result.
            var allClaims = await _claimRepo.GetByEntityAsync(request.EntityId, ct)
                .ConfigureAwait(false);

            var providerConfigs2 = _configLoader.LoadAllProviders();
            var scoring = _configLoader.LoadScoring();
            var (weights, fieldWeights) = ScoringHelper.BuildWeightMaps(providerConfigs2, _providers);

            var scoringContext = new Intelligence.Models.ScoringContext
            {
                EntityId             = request.EntityId,
                Claims               = allClaims,
                ProviderWeights      = weights,
                ProviderFieldWeights = fieldWeights,
                Configuration        = new Intelligence.Models.ScoringConfiguration
                {
                    AutoLinkThreshold     = scoring.AutoLinkThreshold,
                    ConflictThreshold     = scoring.ConflictThreshold,
                    ConflictEpsilon       = scoring.ConflictEpsilon,
                    StaleClaimDecayDays   = scoring.StaleClaimDecayDays,
                    StaleClaimDecayFactor = scoring.StaleClaimDecayFactor,
                },
            };

            var scored = await _scoringEngine.ScoreEntityAsync(scoringContext, ct)
                .ConfigureAwait(false);

            if (scored.OverallConfidence < hydration.AutoReviewConfidenceThreshold)
            {
                await CreateReviewItemAsync(
                    request, ReviewTrigger.LowConfidence, scored.OverallConfidence,
                    $"Overall confidence {scored.OverallConfidence:P0} below threshold {hydration.AutoReviewConfidenceThreshold:P0}",
                    result, ct).ConfigureAwait(false);
            }
            else if (scored.OverallConfidence >= scoring.AutoLinkThreshold)
            {
                // Confidence improved above the auto-organize threshold (0.85).
                // Auto-resolve any pending LowConfidence, ContentMatchFailed, or
                // AuthorityMatchFailed review items and organize the file from
                // staging into the library.
                await TryAutoResolveAndOrganizeAsync(
                    request, scored.OverallConfidence, ct).ConfigureAwait(false);
            }

            // Check for metadata conflicts after post-hydration re-scoring.
            // Conflicts don't block organization — just surface them for user review.
            var conflictedFields = scored.FieldScores
                .Where(f => f.IsConflicted && f.Key != "media_type")
                .Select(f => f.Key)
                .ToList();

            if (conflictedFields.Count > 0)
            {
                await CreateMetadataConflictReviewItemAsync(
                    request.EntityId, scored.OverallConfidence, conflictedFields, ct, request.IngestionRunId)
                    .ConfigureAwait(false);
            }
            else
            {
                // No conflicts remain — auto-resolve any pending MetadataConflict review items
                // that were created during initial ingestion or a prior hydration run.
                await TryAutoResolveMetadataConflictsAsync(
                    request.EntityId, scored.OverallConfidence, ct)
                    .ConfigureAwait(false);
            }
        }

        // ── Post-pipeline: expected-fields scan (Unit 5) ─────────────────────
        // After all three stages, check whether the entity is missing expected
        // fields for its media type, or has fields sourced only from the local
        // filesystem with low confidence.  Flag those canonical values as
        // NeedsReview and create a FieldLevelReview queue entry if any are found.
        if (result.TotalClaimsAdded > 0)
        {
            try
            {
                await RunExpectedFieldsScanAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Expected-fields scan failed for entity {Id}; continuing",
                    request.EntityId);
            }
        }

        // Create a single consolidated MediaAdded activity entry at the very end.
        // Skip when the caller set SuppressActivityEntry (e.g. re-enqueue from
        // TryReorganizeExistingAsync for cover art download — the original
        // pipeline run already logged the MediaAdded entry).
        if (!request.SuppressActivityEntry)
        {
            await CreateMediaAddedEntryAsync(request.EntityId, result, request.IngestionRunId, ct).ConfigureAwait(false);
        }

        // §3.24: After Pass 1 completes, enqueue a deferred Pass 2 request.
        if (effectivePass == HydrationPass.Quick
            && request.EntityType == EntityType.MediaAsset)
        {
            try
            {
                var hintsDict = new Dictionary<string, string>(
                    request.Hints, StringComparer.OrdinalIgnoreCase);

                await _deferredRepo.InsertAsync(new DeferredEnrichmentRequest
                {
                    Id          = Guid.NewGuid(),
                    EntityId    = request.EntityId,
                    WikidataQid = result.WikidataQid,
                    MediaType   = request.MediaType,
                    HintsJson   = JsonSerializer.Serialize(hintsDict),
                    CreatedAt   = DateTimeOffset.UtcNow,
                    Status      = "Pending",
                }, ct).ConfigureAwait(false);

                _logger.LogDebug(
                    "Enqueued Pass 2 (Universe) request for entity {Id} (QID: {Qid})",
                    request.EntityId, result.WikidataQid ?? "none");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to enqueue Pass 2 for entity {Id}; universe enrichment " +
                    "will be picked up by the nightly sweep", request.EntityId);
            }
        }

        _logger.LogInformation(
            "Hydration pipeline complete for entity {Id}: S1={S1} S2={S2} S3={S3} total={Total} review={Review}",
            request.EntityId, result.Stage1ClaimsAdded, result.Stage2ClaimsAdded,
            result.Stage3ClaimsAdded, result.TotalClaimsAdded, result.NeedsReview);

        return result;
    }

    // ── Auto-resolve after hydration ─────────────────────────────────────────

    /// <summary>
    /// When hydration improves an entity's confidence above the auto-link
    /// threshold (0.85), auto-resolve any pending <see cref="ReviewTrigger.LowConfidence"/>,
    /// <see cref="ReviewTrigger.ContentMatchFailed"/>, or
    /// <see cref="ReviewTrigger.AuthorityMatchFailed"/> review items, then
    /// attempt to organize the file from staging into the library.
    /// </summary>
    private async Task TryAutoResolveAndOrganizeAsync(
        HarvestRequest request, double confidence, CancellationToken ct)
    {
        try
        {
            var reviews = await _reviewRepo.GetByEntityAsync(request.EntityId, ct)
                .ConfigureAwait(false);

            var resolvable = reviews.Where(r =>
                r.Status == ReviewStatus.Pending &&
                r.Trigger is ReviewTrigger.LowConfidence
                          or ReviewTrigger.ContentMatchFailed
                          or ReviewTrigger.AuthorityMatchFailed
                          or ReviewTrigger.UniverseMatchFailed).ToList();
            // AuthorityMatchFailed is auto-resolved here because high confidence from
            // Stage 3 retail providers is sufficient to identify and organise the file
            // — Wikidata authority matching is optional enrichment.
            // UniverseMatchFailed is kept for backward compatibility with existing rows.

            foreach (var review in resolvable)
            {
                await _reviewRepo.UpdateStatusAsync(
                    review.Id, ReviewStatus.Resolved, "auto_hydration", ct)
                    .ConfigureAwait(false);

                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ReviewItemResolved,
                    EntityId   = request.EntityId,
                    Detail     = $"Auto-resolved ({review.Trigger}): confidence improved to {confidence:P0} after hydration.",
                }, ct).ConfigureAwait(false);

                await _eventPublisher.PublishAsync("ReviewItemResolved", new
                {
                    review_item_id = review.Id,
                    entity_id      = request.EntityId,
                    status         = "Resolved",
                }, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Review item {ReviewId} auto-resolved for entity {EntityId} — confidence {Confidence:P0}",
                    review.Id, request.EntityId, confidence);
            }

            // Always attempt to organize the file into the library when confidence
            // is above the auto-link threshold, regardless of whether there were
            // pending review items to resolve.
            if (request.EntityType == EntityType.MediaAsset)
            {
                await _autoOrganize.TryAutoOrganizeAsync(request.EntityId, ct, request.IngestionRunId)
                    .ConfigureAwait(false);

                // Now the file is in the Library Root — try downloading cover art.
                // PersistCoverFromUrlAsync guards against watcher-path downloads,
                // so this second call succeeds once the file is organized.
                await PersistCoverFromUrlAsync(request.EntityId, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Auto-resolve after hydration failed for entity {Id}",
                request.EntityId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the primary provider for Stage 1 (Content Match) from the slot
    /// configuration. Returns <c>null</c> if no primary provider is configured
    /// or the configured provider is disabled/not found.
    /// </summary>
    private IExternalMetadataProvider? ResolvePrimaryProvider(
        ProviderSlotConfiguration slots,
        IReadOnlyList<Storage.Models.ProviderConfiguration> provConfigs,
        HarvestRequest request)
    {
        var mediaTypeKey   = ProviderSlotConfiguration.MediaTypeToDisplayName(request.MediaType);
        var slotConfig     = slots.GetSlotForMediaType(mediaTypeKey);
        var primaryName    = slotConfig.Primary;

        if (string.IsNullOrWhiteSpace(primaryName))
        {
            _logger.LogDebug(
                "No primary provider slot configured for media type '{MediaType}'",
                mediaTypeKey);
            return null;
        }

        // Verify the provider is enabled in its config.
        var provConfig = provConfigs.FirstOrDefault(
            pc => string.Equals(pc.Name, primaryName, StringComparison.OrdinalIgnoreCase));

        if (provConfig is null || !provConfig.Enabled)
        {
            _logger.LogWarning(
                "Primary provider '{Provider}' for media type '{MediaType}' is not found or disabled",
                primaryName, mediaTypeKey);
            return null;
        }

        // Find the registered adapter by name.
        var adapter = _providers.FirstOrDefault(
            p => string.Equals(p.Name, primaryName, StringComparison.OrdinalIgnoreCase));

        if (adapter is null)
        {
            _logger.LogWarning(
                "No adapter registered for primary provider '{Provider}'",
                primaryName);
        }

        return adapter;
    }

    /// <summary>
    /// Returns an ordered list of providers for the retail waterfall execution:
    /// primary, then secondary, then tertiary — filtering out nulls/disabled.
    /// Used for Stage 3 (Retail Match).
    /// </summary>
    private List<IExternalMetadataProvider> ResolveWaterfallProviders(
        ProviderSlotConfiguration slots,
        IReadOnlyList<Storage.Models.ProviderConfiguration> provConfigs,
        HarvestRequest request)
    {
        var mediaTypeKey = ProviderSlotConfiguration.MediaTypeToDisplayName(request.MediaType);
        var slotConfig   = slots.GetSlotForMediaType(mediaTypeKey);
        var result       = new List<IExternalMetadataProvider>();

        foreach (var providerName in new[] { slotConfig.Primary, slotConfig.Secondary, slotConfig.Tertiary })
        {
            if (string.IsNullOrWhiteSpace(providerName))
                continue;

            var provConfig = provConfigs.FirstOrDefault(
                pc => string.Equals(pc.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (provConfig is null || !provConfig.Enabled)
                continue;

            var adapter = _providers.FirstOrDefault(
                p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (adapter is not null)
                result.Add(adapter);
        }

        return result;
    }

    /// <summary>
    /// Computes the overall confidence for an entity by loading all claims,
    /// running the scoring engine, and returning the overall confidence value.
    /// Used during Stage 3 waterfall to decide whether to call the next provider.
    /// </summary>
    private async Task<double> ComputeOverallConfidenceAsync(
        Guid entityId, CancellationToken ct)
    {
        try
        {
            var allClaims       = await _claimRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
            var providerConfigs = _configLoader.LoadAllProviders();
            var scoring         = _configLoader.LoadScoring();
            var (weights, fieldWeights) = ScoringHelper.BuildWeightMaps(providerConfigs, _providers);

            var ctx = new Intelligence.Models.ScoringContext
            {
                EntityId             = entityId,
                Claims               = allClaims,
                ProviderWeights      = weights,
                ProviderFieldWeights = fieldWeights,
                Configuration        = new Intelligence.Models.ScoringConfiguration
                {
                    AutoLinkThreshold     = scoring.AutoLinkThreshold,
                    ConflictThreshold     = scoring.ConflictThreshold,
                    ConflictEpsilon       = scoring.ConflictEpsilon,
                    StaleClaimDecayDays   = scoring.StaleClaimDecayDays,
                    StaleClaimDecayFactor = scoring.StaleClaimDecayFactor,
                },
            };

            var scored = await _scoringEngine.ScoreEntityAsync(ctx, ct).ConfigureAwait(false);
            return scored.OverallConfidence;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Returns providers configured for the given stage, filtered by media type
    /// and entity type compatibility. Used for Stage 1 (Authority Match) and
    /// Stage 2 (Context Match) providers.
    /// </summary>
    private List<IExternalMetadataProvider> GetProvidersForStage(
        int stage,
        IReadOnlyList<Storage.Models.ProviderConfiguration> provConfigs,
        HarvestRequest request)
    {
        var result = new List<IExternalMetadataProvider>();

        foreach (var provider in _providers)
        {
            // Find matching config.
            var config = provConfigs.FirstOrDefault(
                pc => string.Equals(pc.Name, provider.Name, StringComparison.OrdinalIgnoreCase));

            if (config is null || !config.Enabled)
                continue;

            // Check stage membership.
#pragma warning disable CS0618 // HumanHub is obsolete but still valid in config files
            if (!config.HydrationStages.Contains(stage))
                continue;
#pragma warning restore CS0618

            // Check capability filters.
            if (!provider.CanHandle(request.MediaType) || !provider.CanHandle(request.EntityType))
                continue;

            result.Add(provider);
        }

        return result;
    }

    /// <summary>
    /// Creates a single consolidated <see cref="SystemActionType.MediaAdded"/> activity
    /// entry at the END of the hydration pipeline. Reads final canonical values to build
    /// a rich JSON payload: title, author, year, media_type, cover URL, hub_name,
    /// confidence, organized_path, wikidata_qid, stage claim counts, needs_review flag.
    /// Wrapped in try/catch — never aborts the pipeline.
    /// </summary>
    private async Task CreateMediaAddedEntryAsync(
        Guid entityId, HydrationResult result, Guid? ingestionRunId, CancellationToken ct)
    {
        try
        {
            var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            string? Val(string key) => canonicals.FirstOrDefault(c => c.Key == key)?.Value;

            var title     = Val("title")      ?? "Unknown";
            var author    = Val("author")      ?? string.Empty;
            var year      = Val("year")        ?? string.Empty;
            var mediaType = Val("media_type")  ?? string.Empty;
            var qid       = Val("wikidata_qid");

            // Look up the asset to get the final file path.
            var asset = await _assetRepo.FindByIdAsync(entityId, ct).ConfigureAwait(false);
            var organizedPath = asset?.FilePathRoot;

            // Cover art URL for Dashboard rich card display — only emit when the
            // actual cover.jpg file exists on disk to avoid broken images.
            string? coverUrl = null;
            if (asset is not null)
            {
                var assetDir = Path.GetDirectoryName(asset.FilePathRoot);
                if (!string.IsNullOrEmpty(assetDir)
                    && File.Exists(Path.Combine(assetDir, "cover.jpg")))
                {
                    coverUrl = $"/stream/{entityId}/cover";
                }
            }

            // Resolve hub name from the work -> hub chain via a single targeted query.
            string? hubName = null;
            var workId = await _hubRepo.GetWorkIdByMediaAssetAsync(entityId, ct)
                .ConfigureAwait(false);
            if (workId is not null)
            {
                hubName = await _hubRepo.FindHubNameByWorkIdAsync(workId.Value, ct)
                    .ConfigureAwait(false);
            }

            // Compute overall confidence from current canonical state.
            double confidence = 0;
            try
            {
                var allClaims = await _claimRepo.GetByEntityAsync(entityId, ct)
                    .ConfigureAwait(false);
                var providerConfigs = _configLoader.LoadAllProviders();
                var scoring = _configLoader.LoadScoring();
                var (weights, fieldWeights) = ScoringHelper.BuildWeightMaps(providerConfigs, _providers);
                var ctx = new Intelligence.Models.ScoringContext
                {
                    EntityId             = entityId,
                    Claims               = allClaims,
                    ProviderWeights      = weights,
                    ProviderFieldWeights = fieldWeights,
                    Configuration        = new Intelligence.Models.ScoringConfiguration
                    {
                        AutoLinkThreshold     = scoring.AutoLinkThreshold,
                        ConflictThreshold     = scoring.ConflictThreshold,
                        ConflictEpsilon       = scoring.ConflictEpsilon,
                        StaleClaimDecayDays   = scoring.StaleClaimDecayDays,
                        StaleClaimDecayFactor = scoring.StaleClaimDecayFactor,
                    },
                };
                var scored = await _scoringEngine.ScoreEntityAsync(ctx, ct)
                    .ConfigureAwait(false);
                confidence = scored.OverallConfidence;
            }
            catch
            {
                // If scoring fails, use 0 — the MediaAdded entry is still useful.
            }

            // Build per-field provenance from the final scored state so the
            // Dashboard can show which provider won each field after all three
            // hydration stages have run (not just the local-processor snapshot).
            var localProcessorId = new Guid("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f");
            List<object> fieldSources = [];
            string matchMethod = "embedded_metadata";
            try
            {
                var allClaims2     = await _claimRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
                var providerCfgs2  = _configLoader.LoadAllProviders();
                var scoring2       = _configLoader.LoadScoring();
                var (wts2, fwts2)  = ScoringHelper.BuildWeightMaps(providerCfgs2, _providers);
                var ctx2 = new Intelligence.Models.ScoringContext
                {
                    EntityId             = entityId,
                    Claims               = allClaims2,
                    ProviderWeights      = wts2,
                    ProviderFieldWeights = fwts2,
                    Configuration        = new Intelligence.Models.ScoringConfiguration
                    {
                        AutoLinkThreshold     = scoring2.AutoLinkThreshold,
                        ConflictThreshold     = scoring2.ConflictThreshold,
                        ConflictEpsilon       = scoring2.ConflictEpsilon,
                        StaleClaimDecayDays   = scoring2.StaleClaimDecayDays,
                        StaleClaimDecayFactor = scoring2.StaleClaimDecayFactor,
                    },
                };
                var finalScored = await _scoringEngine.ScoreEntityAsync(ctx2, ct).ConfigureAwait(false);
                fieldSources = [.. finalScored.FieldScores
                    .Where(f => !string.IsNullOrEmpty(f.WinningValue))
                    .Select(f => (object)new
                    {
                        field       = f.Key,
                        value       = f.WinningValue,
                        confidence  = f.Confidence,
                        source      = f.WinningProviderId == localProcessorId ? "embedded"
                                    : f.WinningProviderId.HasValue ? "provider" : "unknown",
                        provider_id = f.WinningProviderId?.ToString(),
                        conflicted  = f.IsConflicted,
                    })];

                var titleScore = finalScored.FieldScores.FirstOrDefault(f => f.Key == "title");
                if (!string.IsNullOrEmpty(qid))
                    matchMethod = "provider_match";
                else if (titleScore?.WinningProviderId is not null
                         && titleScore.WinningProviderId != localProcessorId)
                    matchMethod = "provider_match";
                else if (result.Stage3ClaimsAdded > 0)
                    matchMethod = "provider_match";
                else if (titleScore?.WinningProviderId == localProcessorId)
                    matchMethod = "embedded_metadata";
                else
                    matchMethod = "filename_fallback";
            }
            catch (Exception ex2)
            {
                _logger.LogDebug(ex2, "Could not build field_sources for MediaAdded entry {Id}", entityId);
            }

            var authorPart = string.IsNullOrWhiteSpace(author) ? string.Empty : $" by {author}";
            var richJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                title,
                author,
                year,
                media_type      = mediaType,
                confidence,
                cover           = coverUrl,
                hub_name        = hubName ?? string.Empty,
                organized_path  = organizedPath ?? string.Empty,
                wikidata_qid    = qid ?? string.Empty,
                stage1_claims   = result.Stage1ClaimsAdded,
                stage2_claims   = result.Stage2ClaimsAdded,
                stage3_claims   = result.Stage3ClaimsAdded,
                needs_review    = result.NeedsReview,
                entity_id       = entityId.ToString(),
                match_method    = matchMethod,
                field_sources   = fieldSources,
            });

            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType     = SystemActionType.MediaAdded,
                EntityId       = entityId,
                EntityType     = "MediaAsset",
                HubName        = hubName ?? title,
                ChangesJson    = richJson,
                Detail         = $"Added — \"{title}\"{authorPart}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to create MediaAdded activity entry for entity {Id}", entityId);
        }
    }

    /// <summary>
    /// Creates a review queue entry and publishes events.
    /// Shared by AuthorityMatchFailed, ContentMatchFailed, and LowConfidence triggers.
    /// Includes dedup check: skips creation if a pending item with the same trigger
    /// already exists for this entity.
    /// </summary>
    private async Task CreateReviewItemAsync(
        HarvestRequest request,
        string trigger,
        double confidence,
        string detail,
        HydrationResult result,
        CancellationToken ct)
    {
        // Dedup: skip if a pending review with the same trigger already exists.
        var existing = await _reviewRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        if (existing.Any(r => r.Status == ReviewStatus.Pending && r.Trigger == trigger))
        {
            _logger.LogDebug(
                "Review item '{Trigger}' already exists for entity {Id} — skipping duplicate",
                trigger, request.EntityId);
            result.NeedsReview  = true;
            result.ReviewReason = trigger;
            return;
        }

        var reviewEntry = new ReviewQueueEntry
        {
            Id              = Guid.NewGuid(),
            EntityId        = request.EntityId,
            EntityType      = request.EntityType.ToString(),
            Trigger         = trigger,
            ConfidenceScore = confidence,
            Detail          = detail,
        };

        await _reviewRepo.InsertAsync(reviewEntry, ct).ConfigureAwait(false);

        result.NeedsReview  = true;
        result.ReviewReason = trigger;
        result.ReviewItemId = reviewEntry.Id;

        // Resolve title for the event.
        var canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        var titleCanonical = canonicals.FirstOrDefault(c => c.Key == "title");

        await _activityRepo.LogAsync(new SystemActivityEntry
        {
            ActionType = SystemActionType.ReviewItemCreated,
            EntityId   = request.EntityId,
            Detail     = $"Review item created: {trigger}",
            IngestionRunId = request.IngestionRunId,
        }, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(
            "ReviewItemCreated",
            new ReviewItemCreatedEvent(
                reviewEntry.Id, request.EntityId, trigger,
                titleCanonical?.Value),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.MetadataConflict"/> review queue entry
    /// when the scoring engine detects conflicting canonical values after hydration.
    /// Conflicts don't block organization — the file proceeds with the best guess.
    /// </summary>
    private async Task CreateMetadataConflictReviewItemAsync(
        Guid entityId,
        double confidence,
        List<string> conflictedFields,
        CancellationToken ct,
        Guid? ingestionRunId = null)
    {
        try
        {
            // Dedup: skip if a pending MetadataConflict review already exists.
            var existing = await _reviewRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            if (existing.Any(r => r.Status == ReviewStatus.Pending
                                  && r.Trigger == ReviewTrigger.MetadataConflict))
            {
                _logger.LogDebug(
                    "MetadataConflict review item already exists for entity {Id} — skipping.",
                    entityId);
                return;
            }

            var detail = $"Conflicting metadata: {string.Join(", ", conflictedFields)}";
            var entry = new ReviewQueueEntry
            {
                Id              = Guid.NewGuid(),
                EntityId        = entityId,
                EntityType      = "MediaAsset",
                Trigger         = ReviewTrigger.MetadataConflict,
                ConfidenceScore = confidence,
                Detail          = detail,
                CreatedAt       = DateTimeOffset.UtcNow,
            };

            await _reviewRepo.InsertAsync(entry, ct).ConfigureAwait(false);

            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.ReviewItemCreated,
                EntityId   = entityId,
                Detail     = detail,
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);

            await _eventPublisher.PublishAsync(
                "ReviewItemCreated",
                new ReviewItemCreatedEvent(entry.Id, entityId, ReviewTrigger.MetadataConflict, null),
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "MetadataConflict review item created for entity {Id}: {Detail}",
                entityId, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to create MetadataConflict review item for entity {Id}", entityId);
        }
    }

    /// <summary>
    /// Auto-resolves pending <see cref="ReviewTrigger.MetadataConflict"/> review items
    /// when post-hydration re-scoring shows that no fields remain conflicted.
    /// </summary>
    private async Task TryAutoResolveMetadataConflictsAsync(
        Guid entityId, double confidence, CancellationToken ct)
    {
        try
        {
            var reviews = await _reviewRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            var conflicts = reviews.Where(r =>
                r.Status == ReviewStatus.Pending &&
                r.Trigger == ReviewTrigger.MetadataConflict).ToList();

            foreach (var review in conflicts)
            {
                await _reviewRepo.UpdateStatusAsync(
                    review.Id, ReviewStatus.Resolved, "auto_conflict_cleared", ct)
                    .ConfigureAwait(false);

                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ReviewItemResolved,
                    EntityId   = entityId,
                    Detail     = $"Auto-resolved (MetadataConflict): conflicts cleared after hydration, confidence {confidence:P0}.",
                }, ct).ConfigureAwait(false);

                await _eventPublisher.PublishAsync("ReviewItemResolved", new
                {
                    review_item_id = review.Id,
                    entity_id      = entityId,
                    status         = "Resolved",
                }, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "MetadataConflict review item {ReviewId} auto-resolved for entity {EntityId} — conflicts cleared",
                    review.Id, entityId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to auto-resolve MetadataConflict review items for entity {Id}", entityId);
        }
    }

    /// <summary>
    /// Fetches claims from a single provider, handling errors gracefully.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> FetchFromProviderAsync(
        IExternalMetadataProvider provider,
        HarvestRequest request,
        Dictionary<string, string> endpointMap,
        string? sparqlBaseUrl,
        string language,
        string country,
        CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl(provider, endpointMap);
        var lookupRequest = BuildLookupRequest(request, provider, baseUrl, language, country, sparqlBaseUrl);

        try
        {
            return await provider.FetchAsync(lookupRequest, ct).ConfigureAwait(false);
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
            return [];
        }
    }

    /// <summary>Publishes a MetadataHarvested event for the given claims.</summary>
    private async Task PublishHarvestEvent(
        Guid entityId, string providerName,
        IReadOnlyList<ProviderClaim> claims, CancellationToken ct)
    {
        var updatedFields = claims.Select(c => c.Key).Distinct().ToList();
        await _eventPublisher.PublishAsync(
            "MetadataHarvested",
            new MetadataHarvestedEvent(entityId, providerName, updatedFields),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts bridge identifier hints from canonical values.
    /// </summary>
    private static Dictionary<string, string> ExtractBridgeHints(
        IReadOnlyList<CanonicalValue> canonicals)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Only keys with meaningful Wikidata coverage count as real bridge IDs.
        // apple_books_id and audible_id are excluded — Wikidata has near-zero coverage
        // for both, so treating them as bridges creates UniverseMatchFailed review noise
        // for every book Apple Books matches. ISBN, ASIN, TMDB, IMDb, and Goodreads
        // are indexed in Wikidata and serve as genuine cross-reference keys.
        string[] bridgeKeys = ["isbn", "asin", "tmdb_id", "imdb_id", "goodreads_id"];

        foreach (var cv in canonicals)
        {
            if (bridgeKeys.Contains(cv.Key, StringComparer.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(cv.Value))
            {
                hints.TryAdd(cv.Key, cv.Value);
            }
        }

        return hints;
    }

    /// <summary>
    /// Creates a new HarvestRequest enriched with bridge hints from canonical values.
    /// </summary>
    private static HarvestRequest EnrichRequestWithBridgeHints(
        HarvestRequest original,
        Dictionary<string, string> bridgeHints)
    {
        if (bridgeHints.Count == 0) return original;

        var mergedHints = new Dictionary<string, string>(
            original.Hints, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in bridgeHints)
            mergedHints.TryAdd(key, value);

        return new HarvestRequest
        {
            EntityId       = original.EntityId,
            EntityType     = original.EntityType,
            MediaType      = original.MediaType,
            Hints          = mergedHints,
            PreResolvedQid = original.PreResolvedQid,
        };
    }

    /// <summary>
    /// Resolves the narrative root for a work and discovers fictional entities
    /// (characters, locations, organizations) from the work's canonical values.
    /// Only called when Stage 1 produced a Wikidata QID for the work.
    /// </summary>
    private async Task RunFictionalEntityEnrichmentAsync(
        Guid entityId,
        string workQid,
        IReadOnlyList<CanonicalValue> canonicals,
        CancellationToken ct,
        Guid? ingestionRunId = null)
    {
        // 1. Resolve the narrative root (fictional_universe → franchise → series → standalone).
        var narrativeRoot = await _narrativeRootResolver.ResolveAsync(entityId, ct, ingestionRunId)
            .ConfigureAwait(false);

        if (narrativeRoot is null)
        {
            _logger.LogDebug(
                "No narrative root resolved for entity {Id} (QID={Qid}) — " +
                "standalone work, skipping fictional entity enrichment",
                entityId, workQid);
            return;
        }

        // 2. Extract fictional entity references from canonical values.
        var entityRefs = ExtractFictionalEntityReferences(canonicals);
        if (entityRefs.Count == 0)
        {
            _logger.LogDebug(
                "No fictional entity references found for entity {Id} (QID={Qid})",
                entityId, workQid);
            return;
        }

        // 3. Discover and enqueue enrichment for fictional entities.
        var workLabel = canonicals.FirstOrDefault(c => c.Key == "title")?.Value;

        _logger.LogInformation(
            "Fictional entity enrichment: {Count} entities for work '{Title}' (QID={Qid}) " +
            "in universe '{Universe}' (QID={UniverseQid})",
            entityRefs.Count, workLabel ?? "(unknown)", workQid,
            narrativeRoot.Label ?? "(unknown)", narrativeRoot.Qid);

        await _fictionalEntityService.EnrichAsync(
            workQid, workLabel,
            narrativeRoot.Qid, narrativeRoot.Label,
            entityRefs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts fictional entity references (characters, locations, organizations)
    /// from canonical values deposited by Stage 1 SPARQL deep hydration.
    /// Entity-valued claims produce <c>{claimKey}_qid</c> canonical values.
    /// </summary>
    private static IReadOnlyList<FictionalEntityReference> ExtractFictionalEntityReferences(
        IReadOnlyList<CanonicalValue> canonicals)
    {
        var refs = new List<FictionalEntityReference>();

        // Map of QID key → (label companion key, entity sub-type).
        // The label companion is the same key without the "_qid" suffix (e.g. "characters").
        var entityKeys = new Dictionary<string, (string LabelKey, string EntitySubType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["characters_qid"]         = ("characters",        "Character"),
            ["cast_member_qid"]        = ("cast_member",       "Character"),
            ["narrative_location_qid"] = ("narrative_location","Location"),
        };

        static string[] Split(string? v) =>
            v is null ? [] : v.Split(["|||", "; "], StringSplitOptions.RemoveEmptyEntries);

        foreach (var (qidKey, (labelKey, entitySubType)) in entityKeys)
        {
            var qidValue = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, qidKey, StringComparison.OrdinalIgnoreCase))?.Value;

            if (string.IsNullOrWhiteSpace(qidValue)) continue;

            var qidParts   = Split(qidValue);
            var labelParts = Split(canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, labelKey, StringComparison.OrdinalIgnoreCase))?.Value);

            for (var i = 0; i < qidParts.Length; i++)
            {
                var qidPart = qidParts[i].Trim();
                var segments = qidPart.Split("::", 2, StringSplitOptions.None);
                var qid = segments[0];

                if (!qid.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                    || qid.Length < 2 || !char.IsDigit(qid[1]))
                    continue;

                // Pair QID with its directly attached label if available, otherwise fall back to the legacy positional array.
                var label = segments.Length > 1 && !string.IsNullOrWhiteSpace(segments[1]) 
                            ? segments[1].Trim() 
                            : ((i < labelParts.Length ? labelParts[i].Trim() : null) ?? qid);

                refs.Add(new FictionalEntityReference(qid, label, entitySubType));
            }
        }

        return refs;
    }

    /// <summary>
    /// If the work's current <c>author</c> canonical value names a person who is
    /// flagged as a pseudonym (<c>IsPseudonym = true</c>), emit a high-confidence
    /// author claim (0.98) to prevent Wikidata's real-person author from overwriting
    /// the pseudonym name.
    ///
    /// <summary>
    /// Generates the next NF placeholder QID (e.g. "NF000001") by scanning
    /// existing canonical values for the highest NF-prefixed value and
    /// incrementing.  Thread-safe within a single pipeline run — concurrent
    /// access is prevented by the single-reader processing loop.
    /// </summary>
    private async Task<string> GenerateNfPlaceholderAsync(CancellationToken ct)
    {
        // Find the highest existing NF placeholder across all entities.
        var allNf = await _canonicalRepo.FindByKeyAndPrefixAsync("wikidata_qid", "NF", ct)
            .ConfigureAwait(false);

        int maxNumber = 0;
        foreach (var nf in allNf)
        {
            if (nf.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(nf.Value.AsSpan(2), out var num)
                && num > maxNumber)
            {
                maxNumber = num;
            }
        }

        return $"NF{maxNumber + 1:D6}";
    }

    /// <para>Example: a book published under "Richard Bachman" should keep that name
    /// in the sidecar even after Wikidata resolves it to Stephen King.</para>
    /// </summary>
    private async Task ProtectPseudonymAuthorAsync(
        Guid entityId,
        IReadOnlyList<CanonicalValue> canonicals,
        CancellationToken ct)
    {
        try
        {
            var authorCanonical = canonicals.FirstOrDefault(
                c => string.Equals(c.Key, "author", StringComparison.OrdinalIgnoreCase));
            if (authorCanonical is null || string.IsNullOrWhiteSpace(authorCanonical.Value))
                return;

            var authorName = authorCanonical.Value;
            // Handle multi-valued author (take first).
            if (authorName.Contains("|||", StringComparison.Ordinal))
                authorName = authorName.Split("|||")[0].Trim();

            // Look up the person by name (as Author) to check the pseudonym flag.
            var pseudonymPerson = await _personRepo.FindByNameAsync(authorName, "Author", ct).ConfigureAwait(false);
            if (pseudonymPerson is null || !pseudonymPerson.IsPseudonym)
            {
                // Also check Narrator role (audiobooks credited to pseudonym narrators).
                pseudonymPerson = await _personRepo.FindByNameAsync(authorName, "Narrator", ct).ConfigureAwait(false);
            }

            if (pseudonymPerson is null || !pseudonymPerson.IsPseudonym)
                return;

            // Emit a high-confidence author claim using a stable "pseudonym_lock" provider GUID
            // to ensure the pseudonym name wins the scoring election.
            var pseudonymClaims = new List<ProviderClaim>
            {
                new("author", pseudonymPerson.Name, 0.98),
            };

            // Use a stable provider GUID for pseudonym protection claims.
            var pseudonymProviderId = Guid.Parse("ffa00001-0000-4000-8000-000000000099");
            await ScoringHelper.PersistClaimsAndScoreAsync(
                entityId, pseudonymClaims, pseudonymProviderId,
                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                _providers, ct, _arrayRepo, _logger).ConfigureAwait(false);

            _logger.LogInformation(
                "Protected pseudonym author \"{PseudonymName}\" for entity {EntityId}",
                pseudonymPerson.Name, entityId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "Pseudonym author protection check failed for entity {Id}; continuing",
                entityId);
        }
    }

    /// <summary>
    /// Person-role label claim keys whose display value must be a single name.
    /// The corresponding <c>*_qid</c> claims are intentionally NOT included —
    /// they stay multi-valued for person enrichment.
    /// </summary>
    private static readonly HashSet<string> PersonLabelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "author", "narrator", "director", "illustrator", "voice_actor", "performer",
    };

    /// <summary>
    /// Replaces multi-valued person label claims (e.g. <c>"Ty Franck|||James S. A. Corey|||Daniel Abraham"</c>)
    /// with a single claim containing the first individual name.  This ensures the scoring
    /// engine never stores a <c>|||</c>-joined string as the canonical author name.
    /// <para>
    /// The <c>*_qid</c> companions are left untouched — they carry the full QID list
    /// for person enrichment.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ProviderClaim> DeMultiValuePersonLabels(
        IReadOnlyList<ProviderClaim> claims)
    {
        var needsRewrite = false;
        foreach (var c in claims)
        {
            if (PersonLabelKeys.Contains(c.Key) && c.Value.Contains("|||", StringComparison.Ordinal))
            {
                needsRewrite = true;
                break;
            }
        }

        if (!needsRewrite)
            return claims;  // fast path — nothing to change

        var result = new List<ProviderClaim>(claims.Count);
        foreach (var c in claims)
        {
            if (PersonLabelKeys.Contains(c.Key) && c.Value.Contains("|||", StringComparison.Ordinal))
            {
                var firstValue = c.Value.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                result.Add(new ProviderClaim(c.Key, firstValue, c.Confidence));
            }
            else
            {
                result.Add(c);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts person references directly from the Stage-1 raw <see cref="ProviderClaim"/>
    /// list returned by the Wikidata adapter.  Using raw claims rather than the
    /// post-scoring canonical values avoids a pitfall where an entity that has been
    /// re-hydrated multiple times accumulates stale single-value votes that outvote
    /// the current multi-value SPARQL result in the scoring election.
    ///
    /// Precedence: the most recently fetched <c>author_qid</c>/<c>narrator_qid</c>
    /// claim (last in the list) is used for QID resolution.
    /// </summary>
    private static IReadOnlyList<PersonReference> ExtractPersonReferencesFromRawClaims(
        IReadOnlyList<ProviderClaim> rawClaims)
    {
        // Convert the raw claims to a CanonicalValue-like lookup by taking the
        // LAST claim for each key (most recent wins).
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in rawClaims)
            byKey[c.Key] = c.Value;   // last write wins

        // Wrap as fake CanonicalValue list so the existing helper can be reused.
        var fakeCanonicals = byKey
            .Select(kv => new CanonicalValue { Key = kv.Key, Value = kv.Value })
            .ToList();

        return ExtractPersonReferences(fakeCanonicals);
    }

    /// Extracts person references (author, narrator) from canonical values
    /// for person enrichment (runs as part of Stage 1).
    ///
    /// When a <c>*_qid</c> companion canonical is present (e.g. <c>author_qid</c>),
    /// each QID::Label pair is parsed and the Wikidata QID is forwarded to the
    /// PersonReference so enrichment can skip the name-based search entirely.
    /// Multi-valued entries (joined with <c>|||</c>) are split into individual refs.
    /// </summary>
    private static IReadOnlyList<PersonReference> ExtractPersonReferences(
        IReadOnlyList<CanonicalValue> canonicals)
    {
        var refs = new List<PersonReference>();

        AddPersonRefs(refs, "Author",   canonicals, "author",   "author_qid");
        AddPersonRefs(refs, "Narrator", canonicals, "narrator", "narrator_qid");
        AddPersonRefs(refs, "Director", canonicals, "director", "director_qid");

        // Collective pseudonym constituent members: the author audit query
        // deposits QID::Label pairs for the real people behind a collective
        // pen name (e.g. "James S. A. Corey" → Daniel Abraham + Ty Franck).
        // Create Person references for each constituent so they get enriched.
        var collectiveValue = canonicals.FirstOrDefault(c => c.Key == "collective_members_qid")?.Value;
        if (!string.IsNullOrEmpty(collectiveValue))
        {
            foreach (var segment in collectiveValue.Split("|||",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colonIndex = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    var qid   = segment[..colonIndex].Trim();
                    var label = segment[(colonIndex + 2)..].Trim();
                    if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(qid))
                    {
                        // Only add if not already present (the constituent may also
                        // appear as a direct P50 author on the work).
                        var alreadyPresent = refs.Any(r =>
                            string.Equals(r.WikidataQid, qid, StringComparison.OrdinalIgnoreCase));
                        if (!alreadyPresent)
                            refs.Add(new PersonReference("Author", label, qid));
                    }
                }
            }
        }

        return refs;
    }

    /// <summary>
    /// Parses a potentially multi-valued canonical and its optional QID companion,
    /// emitting one <see cref="PersonReference"/> per entry.
    /// </summary>
    private static void AddPersonRefs(
        List<PersonReference> refs,
        string role,
        IReadOnlyList<CanonicalValue> canonicals,
        string nameKey,
        string qidKey)
    {
        var nameValue = canonicals.FirstOrDefault(c => c.Key == nameKey)?.Value;
        if (string.IsNullOrEmpty(nameValue))
            return;

        var qidValue = canonicals.FirstOrDefault(c => c.Key == qidKey)?.Value;

        // Build a label→QID lookup from the companion _qid canonical
        // (format: "Q123::Label|||Q456::Label").
        var qidByLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(qidValue))
        {
            foreach (var segment in qidValue.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colonIndex = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    var qid   = segment[..colonIndex].Trim();
                    var label = segment[(colonIndex + 2)..].Trim();
                    if (!string.IsNullOrEmpty(label))
                        qidByLabel[label] = qid;
                }
                else
                {
                    // Plain QID with no label — index under itself.
                    var bare = segment.Trim();
                    if (!string.IsNullOrEmpty(bare))
                        qidByLabel[bare] = bare;
                }
            }
        }

        foreach (var name in nameValue.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrEmpty(name))
                continue;

            qidByLabel.TryGetValue(name, out var qid);
            refs.Add(new PersonReference(role, name, string.IsNullOrEmpty(qid) ? null : qid));
        }
    }

    private static string ResolveBaseUrl(
        IExternalMetadataProvider provider,
        Dictionary<string, string> endpointMap)
    {
        var key = provider.Name switch
        {
            "wikidata"  => "wikidata_api",
            "wikipedia" => "wikipedia_api",
            _           => provider.Name,
        };

        if (endpointMap.TryGetValue(key, out var url))
            return url;
        if (endpointMap.TryGetValue("api", out var apiUrl))
            return apiUrl;

        return string.Empty;
    }

    private static ProviderLookupRequest BuildLookupRequest(
        HarvestRequest request,
        IExternalMetadataProvider provider,
        string baseUrl,
        string language = "en",
        string country = "us",
        string? sparqlBaseUrl = null)
    {
        var h = request.Hints;
        return new ProviderLookupRequest
        {
            EntityId      = request.EntityId,
            EntityType    = request.EntityType,
            MediaType     = request.MediaType,
            Title         = h.GetValueOrDefault("title"),
            Author        = h.GetValueOrDefault("author"),
            Narrator      = h.GetValueOrDefault("narrator"),
            Asin          = h.GetValueOrDefault("asin"),
            Isbn          = h.GetValueOrDefault("isbn"),
            AppleBooksId  = h.GetValueOrDefault("apple_books_id"),
            AudibleId     = h.GetValueOrDefault("audible_id"),
            TmdbId        = h.GetValueOrDefault("tmdb_id"),
            ImdbId        = h.GetValueOrDefault("imdb_id"),
            PersonName     = h.GetValueOrDefault("name"),
            PersonRole     = h.GetValueOrDefault("role"),
            PreResolvedQid = request.PreResolvedQid,
            BaseUrl        = baseUrl,
            SparqlBaseUrl  = sparqlBaseUrl,
            Language       = language,
            Country        = country,
            HydrationPass  = request.Pass,
        };
    }

    // ── Hub Intelligence ──────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a Work to a Hub based on Wikidata relationship properties.
    /// Path A (QID confirmed): uses franchise/series/universe QIDs for firm linking.
    /// Path B (QID pending): falls back to text-based provisional matching.
    /// </summary>
    private async Task RunHubIntelligenceAsync(
        Guid mediaAssetId, bool qidConfirmed, CancellationToken ct)
    {
        try
        {
            // Resolve the actual Work ID from the MediaAsset ID.
            // Claims and canonical values are indexed by MediaAsset ID,
            // but works.hub_id must be updated using the Work's own ID.
            var workId = await _hubRepo.GetWorkIdByMediaAssetAsync(mediaAssetId, ct)
                .ConfigureAwait(false);

            if (workId is null)
            {
                _logger.LogWarning(
                    "Hub intelligence: could not resolve Work ID for asset {AssetId} — skipped",
                    mediaAssetId);
                return;
            }

            var canonicals = await _canonicalRepo.GetByEntityAsync(mediaAssetId, ct)
                .ConfigureAwait(false);

            if (qidConfirmed)
            {
                await RunFirmHubLinkAsync(workId.Value, mediaAssetId, canonicals, ct)
                    .ConfigureAwait(false);

                // Mark the Work's wikidata_status as "confirmed" now that QID is verified.
                await _hubRepo.UpdateWorkWikidataStatusAsync(workId.Value, "confirmed", ct)
                    .ConfigureAwait(false);
            }
            else
            {
                // Path B: no Wikidata QID found.  Create a provisional singleton Hub so
                // the work appears in the library immediately.  If the QID is confirmed
                // later (e.g. via manual hydration) the firm-link path will reassign the
                // work to a franchise/series Hub, and the singleton is pruned by reconciliation.
                var title = canonicals
                    .FirstOrDefault(c => c.Key.Equals("title", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(title))
                    await CreateSingletonHubAsync(workId.Value, title, ct).ConfigureAwait(false);

                await _hubRepo.UpdateWorkWikidataStatusAsync(workId.Value, "pending", ct)
                    .ConfigureAwait(false);

                _logger.LogDebug(
                    "Hub intelligence: no QID for work {WorkId} — singleton hub created (pending)",
                    workId.Value);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Hub intelligence failed for asset {AssetId}; work remains standalone",
                mediaAssetId);
        }
    }

    /// <summary>
    /// Creates a singleton Hub for a standalone work, or reuses an existing Hub
    /// with the same display name (case-insensitive).  Safe to call idempotently.
    /// </summary>
    private async Task CreateSingletonHubAsync(Guid workId, string title, CancellationToken ct)
    {
        var existing = await _hubRepo.FindByDisplayNameAsync(title, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await _hubRepo.AssignWorkToHubAsync(workId, existing.Id, ct).ConfigureAwait(false);
            return;
        }

        var hub = new Hub
        {
            Id          = Guid.NewGuid(),
            DisplayName = title,
            CreatedAt   = DateTimeOffset.UtcNow,
        };
        await _hubRepo.UpsertAsync(hub, ct).ConfigureAwait(false);
        await _hubRepo.AssignWorkToHubAsync(workId, hub.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Path A: firm Hub assignment using Wikidata relationship QIDs.
    /// Searches Tier 1 (franchise, series, fictional_universe) first,
    /// then Tier 2 (based_on, preceded_by/followed_by).
    /// </summary>
    private async Task RunFirmHubLinkAsync(
        Guid workId, Guid mediaAssetId, IReadOnlyList<CanonicalValue> canonicals, CancellationToken ct)
    {
        // Claims are indexed by MediaAsset ID; workId is used only for hub assignment.
        var claims = await _claimRepo.GetByEntityAsync(mediaAssetId, ct).ConfigureAwait(false);
        var relClaims = ExtractRelationshipQids(claims);

        if (relClaims.Count == 0)
        {
            // QID confirmed but work is standalone (no franchise/series/universe relationships).
            // Every work needs a Hub to appear in the library, so create a singleton Hub keyed
            // on the canonical title.  If a Hub with that name already exists, reuse it.
            var title = canonicals
                .FirstOrDefault(c => c.Key.Equals("title", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(title))
                await CreateSingletonHubAsync(workId, title, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Hub intelligence: work {WorkId} is standalone (QID confirmed, no relationships) — singleton hub",
                workId);
            return;
        }

        // Tier 1: franchise, series, fictional_universe
        string[] tier1Types = ["franchise", "series", "fictional_universe"];
        // Tier 2: based_on, preceded_by, followed_by
        string[] tier2Types = ["based_on", "preceded_by", "followed_by"];

        Hub? matchedHub = null;
        var allRelationships = new List<HubRelationship>();

        // Search Tier 1 first
        foreach (var (relType, qids) in relClaims.Where(r => tier1Types.Contains(r.RelType)))
        {
            foreach (var (qid, label) in qids)
            {
                var hub = await _hubRepo.FindByRelationshipQidAsync(relType, qid, ct)
                    .ConfigureAwait(false);
                if (hub is not null)
                {
                    matchedHub = hub;
                    break;
                }

                allRelationships.Add(new HubRelationship
                {
                    Id           = Guid.NewGuid(),
                    RelType      = relType,
                    RelQid       = qid,
                    RelLabel     = label,
                    Confidence   = relType is "fictional_universe" ? 0.8 : 0.9,
                    DiscoveredAt = DateTimeOffset.UtcNow,
                });
            }
            if (matchedHub is not null) break;
        }

        // Search Tier 2 if no Tier 1 match
        if (matchedHub is null)
        {
            foreach (var (relType, qids) in relClaims.Where(r => tier2Types.Contains(r.RelType)))
            {
                foreach (var (qid, label) in qids)
                {
                    var hub = await _hubRepo.FindByRelationshipQidAsync(relType, qid, ct)
                        .ConfigureAwait(false);
                    if (hub is not null)
                    {
                        matchedHub = hub;
                        break;
                    }

                    allRelationships.Add(new HubRelationship
                    {
                        Id           = Guid.NewGuid(),
                        RelType      = MapToNarrativeChain(relType),
                        RelQid       = qid,
                        RelLabel     = label,
                        Confidence   = 0.8,
                        DiscoveredAt = DateTimeOffset.UtcNow,
                    });
                }
                if (matchedHub is not null) break;
            }
        }

        if (matchedHub is not null)
        {
            // Assign to existing hub.
            await _hubRepo.AssignWorkToHubAsync(workId, matchedHub.Id, ct)
                .ConfigureAwait(false);

            // Add any new relationships the hub doesn't have yet.
            if (allRelationships.Count > 0)
            {
                foreach (var r in allRelationships)
                    r.HubId = matchedHub.Id;
                await _hubRepo.InsertRelationshipsAsync(allRelationships, ct)
                    .ConfigureAwait(false);
            }

            // Demoted from activity ledger to debug log (Phase 5 — activity consolidation).
            _logger.LogDebug(
                "HubAssigned — work {WorkId} assigned to existing Hub {HubId} '{HubName}'",
                workId, matchedHub.Id, matchedHub.DisplayName);
        }
        else if (allRelationships.Count > 0)
        {
            // Create a new Hub from the highest-confidence relationship.
            var bestRel = allRelationships.OrderByDescending(r => r.Confidence).First();
            var displayName = bestRel.RelLabel ?? bestRel.RelQid;

            var newHub = new Hub
            {
                Id          = Guid.NewGuid(),
                DisplayName = displayName,
                CreatedAt   = DateTimeOffset.UtcNow,
            };

            await _hubRepo.UpsertAsync(newHub, ct).ConfigureAwait(false);

            foreach (var r in allRelationships)
                r.HubId = newHub.Id;
            await _hubRepo.InsertRelationshipsAsync(allRelationships, ct)
                .ConfigureAwait(false);

            await _hubRepo.AssignWorkToHubAsync(workId, newHub.Id, ct)
                .ConfigureAwait(false);

            // Demoted from activity ledger to debug log (Phase 5 — activity consolidation).
            _logger.LogDebug(
                "HubCreated — Hub '{HubName}' created from {RelType} ({Qid}); work {WorkId} assigned",
                displayName, bestRel.RelType, bestRel.RelQid, workId);
        }
    }

    /// <summary>
    /// Extracts relationship QIDs from claims (franchise, series, fictional_universe,
    /// based_on, preceded_by, followed_by). Groups by relationship type.
    /// </summary>
    private static List<(string RelType, List<(string Qid, string? Label)> Qids)> ExtractRelationshipQids(
        IReadOnlyList<MetadataClaim> claims)
    {
        string[] relKeys = ["franchise", "series", "fictional_universe", "based_on", "preceded_by", "followed_by"];

        // Build a quick lookup for companion _qid claims (e.g. "franchise_qid" -> ["Q937618", ...]).
        var qidLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in claims)
        {
            if (c.ClaimKey.EndsWith("_qid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.ClaimValue))
            {
                var baseKey = c.ClaimKey[..^4]; // "franchise_qid" -> "franchise"
                if (!qidLookup.TryGetValue(baseKey, out var list))
                {
                    list = [];
                    qidLookup[baseKey] = list;
                }
                list.Add(c.ClaimValue.Trim());
            }
        }

        var result = new List<(string RelType, List<(string Qid, string? Label)> Qids)>();

        foreach (var key in relKeys)
        {
            var matchingClaims = claims
                .Where(c => string.Equals(c.ClaimKey, key, StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(c.ClaimValue))
                .ToList();

            if (matchingClaims.Count == 0) continue;

            // Get companion QIDs for this relationship key, if available.
            qidLookup.TryGetValue(key, out var companionQids);

            var qids = new List<(string Qid, string? Label)>();
            for (var i = 0; i < matchingClaims.Count; i++)
            {
                var label = matchingClaims[i].ClaimValue.Trim();

                // Use the companion QID if available (matched by position);
                // fall back to the label value if no companion QID exists.
                var actualQid = companionQids is not null && i < companionQids.Count
                    ? companionQids[i]
                    : label;

                qids.Add((actualQid, label));
            }

            if (qids.Count > 0)
                result.Add((key, qids));
        }

        return result;
    }

    /// <summary>Maps preceded_by/followed_by to the narrative_chain rel_type.</summary>
    private static string MapToNarrativeChain(string relType)
        => relType is "preceded_by" or "followed_by" ? "narrative_chain" : relType;

    // ── Cover art download ───────────────────────────────────────────────────

    /// <summary>
    /// Downloads cover art from a provider-supplied URL and saves it as
    /// <c>cover.jpg</c> in the media file's directory.  Skips if the file
    /// already has a cover or if no cover URL canonical value exists.
    /// Uses <see cref="IImageCacheRepository"/> for content-hash dedup.
    /// </summary>
    private async Task PersistCoverFromUrlAsync(Guid assetId, CancellationToken ct)
    {
        try
        {
            // 1. Look up the asset to find its file path.
            var asset = await _assetRepo.FindByIdAsync(assetId, ct).ConfigureAwait(false);
            if (asset is null) return;

            var fileDir = Path.GetDirectoryName(asset.FilePathRoot);
            if (string.IsNullOrEmpty(fileDir)) return;

            // Guard: only download cover when the file is already in the Library Root.
            // During first ingestion the file is still in the Watch Folder; downloading
            // cover.jpg there would place it in the wrong directory and never alongside
            // the organised media file.
            var core = _configLoader.LoadCore();
            if (!string.IsNullOrWhiteSpace(core.LibraryRoot)
                && !asset.FilePathRoot.StartsWith(core.LibraryRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Cover download skipped for asset {Id} — file not yet in Library Root",
                    assetId);
                return;
            }

            var coverPath = Path.Combine(fileDir, "cover.jpg");
            if (File.Exists(coverPath)) return; // already have a cover

            // 2. Check for a cover URL in canonical values.
            var canonicals = await _canonicalRepo.GetByEntityAsync(assetId, ct)
                .ConfigureAwait(false);
            var coverUrl = canonicals
                .Where(c => c.Key is "cover" or "cover_url")
                .Select(c => c.Value)
                .FirstOrDefault(v => v.StartsWith("http", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(coverUrl)) return;

            // 3. Download the image.
            using var client = _httpFactory.CreateClient("cover_download");
            var bytes = await client.GetByteArrayAsync(coverUrl, ct).ConfigureAwait(false);
            if (bytes.Length == 0) return;

            // 4. Content-hash dedup via image cache.
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var cached = await _imageCache.FindByHashAsync(hash, ct).ConfigureAwait(false);
            if (cached is not null && File.Exists(cached))
            {
                // Same image already exists elsewhere — copy it.
                File.Copy(cached, coverPath, overwrite: false);
            }
            else
            {
                await File.WriteAllBytesAsync(coverPath, bytes, ct).ConfigureAwait(false);
                await _imageCache.InsertAsync(hash, coverPath, coverUrl, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Cover art downloaded for asset {Id} from {Url}",
                assetId, coverUrl);

            // Generate cinematic hero banner from the newly downloaded cover art.
            await GenerateHeroBannerAsync(assetId, coverPath, fileDir, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Cover art download failed for asset {Id}; retracting cover canonical value",
                assetId);

            // Retract the provider-sourced cover URL so downstream consumers
            // (activity log, Dashboard cards) don't reference a nonexistent file.
            try
            {
                await _canonicalRepo.DeleteByKeyAsync(assetId, "cover", ct)
                    .ConfigureAwait(false);
            }
            catch (Exception retractEx) when (retractEx is not OperationCanceledException)
            {
                _logger.LogWarning(retractEx,
                    "Failed to retract cover canonical value for asset {Id}", assetId);
            }
        }
    }

    /// <summary>
    /// Generates a cinematic hero banner from cover art and persists
    /// <c>dominant_color</c> and <c>hero</c> canonical values.
    /// </summary>
    private async Task GenerateHeroBannerAsync(
        Guid assetId, string coverPath, string outputDir, CancellationToken ct)
    {
        try
        {
            var heroResult = await _heroGenerator.GenerateAsync(coverPath, outputDir, ct)
                .ConfigureAwait(false);

            var heroCanonicals = new List<CanonicalValue>();
            if (!string.IsNullOrEmpty(heroResult.DominantHexColor))
            {
                heroCanonicals.Add(new CanonicalValue
                {
                    EntityId = assetId, Key = "dominant_color",
                    Value = heroResult.DominantHexColor,
                    LastScoredAt = DateTimeOffset.UtcNow,
                });
            }
            heroCanonicals.Add(new CanonicalValue
            {
                EntityId = assetId, Key = "hero",
                Value = $"/stream/{assetId}/hero",
                LastScoredAt = DateTimeOffset.UtcNow,
            });
            await _canonicalRepo.UpsertBatchAsync(heroCanonicals, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Hero banner generated for asset {Id} (dominant color: {Color})",
                assetId, heroResult.DominantHexColor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Hero banner generation failed for asset {Id}; continuing",
                assetId);
        }
    }

    // ── Expected-fields scan (Unit 5: Per-Field NeedsReview) ────────────────

    /// <summary>
    /// Expected metadata fields per media type.  Fields listed here are checked
    /// after all three hydration stages complete — any that are missing or
    /// sourced only from the local filesystem with low overall confidence are
    /// flagged with <c>NeedsReview = true</c>.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedFieldsByMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Books"]      = ["title", "author", "year", "cover", "isbn", "genre"],
        ["Epub"]       = ["title", "author", "year", "cover", "isbn", "genre"],
        ["Audiobook"]  = ["title", "author", "year", "cover", "narrator", "genre"],
        ["Audiobooks"] = ["title", "author", "year", "cover", "narrator", "genre"],
        ["Movies"]     = ["title", "director", "year", "cover", "genre", "duration"],
        ["TV"]         = ["title", "year", "cover", "genre"],
        ["Comic"]      = ["title", "author", "year", "cover", "genre"],
        ["Comics"]     = ["title", "author", "year", "cover", "genre"],
        ["Music"]      = ["title", "performer", "year", "cover", "genre"],
        ["Podcasts"]   = ["title", "year", "cover", "genre"],
    };

    /// <summary>
    /// Well-known provider GUID for the local filesystem processor.
    /// Matches <c>IngestionEngine.LocalProcessorProviderId</c>.
    /// </summary>
    private static readonly Guid LocalFilesystemProviderId =
        new("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f");

    /// <summary>
    /// After all three hydration stages complete, scans the entity's canonical
    /// values against the expected fields for its media type.  Fields that are
    /// missing or sourced only from the local filesystem with low overall
    /// confidence are flagged with <see cref="CanonicalValue.NeedsReview"/> = true.
    /// If any fields are flagged, a <see cref="ReviewTrigger.FieldLevelReview"/>
    /// review queue entry is created.
    /// </summary>
    private async Task RunExpectedFieldsScanAsync(HarvestRequest request, CancellationToken ct)
    {
        // Determine media type string — prefer the request's MediaType, fall back
        // to a canonical value.
        var mediaTypeStr = request.MediaType != MediaType.Unknown
            ? request.MediaType.ToString()
            : null;

        if (string.IsNullOrEmpty(mediaTypeStr))
        {
            var canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                .ConfigureAwait(false);
            mediaTypeStr = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        if (string.IsNullOrEmpty(mediaTypeStr) ||
            !ExpectedFieldsByMediaType.TryGetValue(mediaTypeStr, out var expectedFields))
        {
            _logger.LogDebug(
                "Expected-fields scan skipped for entity {Id}: unknown media type '{MediaType}'",
                request.EntityId, mediaTypeStr ?? "(null)");
            return;
        }

        // Load current canonical values for this entity.
        var values = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        var valuesByKey = values.ToDictionary(v => v.Key, v => v, StringComparer.OrdinalIgnoreCase);

        // Compute overall confidence for the local-only check.
        double overallConfidence = 0.0;
        if (values.Count > 0)
        {
            overallConfidence = await ComputeOverallConfidenceAsync(request.EntityId, ct)
                .ConfigureAwait(false);
        }

        var flaggedFields = new List<string>();

        foreach (var field in expectedFields)
        {
            if (!valuesByKey.TryGetValue(field, out var cv))
            {
                // Field is entirely missing — flag it.
                flaggedFields.Add(field);
                continue;
            }

            // Field exists but was sourced only from local_filesystem with low confidence.
            if (cv.WinningProviderId == LocalFilesystemProviderId && overallConfidence < 0.70)
            {
                cv.NeedsReview = true;
                flaggedFields.Add(field);
            }
        }

        // Persist any NeedsReview updates to existing canonical values.
        var updatedValues = values.Where(v => v.NeedsReview).ToList();
        if (updatedValues.Count > 0)
        {
            await _canonicalRepo.UpsertBatchAsync(updatedValues, ct).ConfigureAwait(false);
        }

        if (flaggedFields.Count > 0)
        {
            var detail = $"Expected fields flagged for review: {string.Join(", ", flaggedFields)}";

            _logger.LogInformation(
                "Expected-fields scan for entity {Id} ({MediaType}): {Count} field(s) flagged — {Fields}",
                request.EntityId, mediaTypeStr, flaggedFields.Count, string.Join(", ", flaggedFields));

            // Create a FieldLevelReview queue entry (dedup handled inside CreateReviewItemAsync).
            await CreateReviewItemAsync(
                request, ReviewTrigger.FieldLevelReview, overallConfidence,
                detail, new HydrationResult(), ct).ConfigureAwait(false);
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { await _processingLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
