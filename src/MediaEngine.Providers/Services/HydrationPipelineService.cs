using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Two-stage authority-first hydration pipeline orchestrator.
///
/// <list type="number">
///   <item><b>Stage 1 — Reconciliation:</b> Wikidata reconciliation API resolves the
///     work's identity via bridge IDs or title search, deposits bridge IDs, performs
///     Hub Intelligence and Person Enrichment. On failure an
///     <see cref="ReviewTrigger.AuthorityMatchFailed"/> review item is created.</item>
///   <item><b>Stage 2 — Enrichment:</b> Retail providers run in waterfall order from
///     <c>config/slots.json</c>, using bridge IDs deposited by Stage 1 for precise
///     lookups. On all-provider failure a
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
    private readonly IWikibaseApiService _wikibaseApi;
    private readonly IFictionalEntityRepository _fictionalEntityRepo;
    private readonly IIngestionBatchRepository _batchRepo;
    private readonly IMetadataHarvestingService _harvesting;
    private readonly ISearchIndexRepository _searchIndex;
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
        IWikibaseApiService wikibaseApi,
        IFictionalEntityRepository fictionalEntityRepo,
        IIngestionBatchRepository batchRepo,
        IMetadataHarvestingService harvesting,
        ISearchIndexRepository searchIndex,
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
        ArgumentNullException.ThrowIfNull(wikibaseApi);
        ArgumentNullException.ThrowIfNull(batchRepo);
        ArgumentNullException.ThrowIfNull(harvesting);
        ArgumentNullException.ThrowIfNull(searchIndex);
        ArgumentNullException.ThrowIfNull(fictionalEntityRepo);
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
        _batchRepo           = batchRepo;
        _harvesting          = harvesting;
        _searchIndex         = searchIndex;
        _deferredRepo        = deferredRepo;
        _wikibaseApi         = wikibaseApi;
        _fictionalEntityRepo = fictionalEntityRepo;
        _logger              = logger;

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
            while (!ct.IsCancellationRequested)
            {
                // Wait for the first request (blocking).
                if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    break; // Channel completed.

                var hydration = _configLoader.LoadHydration();
                var batch = new List<HarvestRequest>();

                // Drain available requests up to batch_max_size, with a timeout
                // to flush partial batches when no more files arrive.
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(hydration.BatchAccumulationTimeoutMs);
                while (batch.Count < hydration.BatchMaxSize)
                {
                    if (_channel.Reader.TryRead(out var request))
                    {
                        batch.Add(request);
                        continue;
                    }

                    // No more immediately available — wait briefly for more.
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero || batch.Count > 0)
                        break; // Timeout reached or we have items and nothing pending.

                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delayCts.CancelAfter(remaining);
                    try
                    {
                        if (!await _channel.Reader.WaitToReadAsync(delayCts.Token).ConfigureAwait(false))
                            break;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        break; // Timeout — flush what we have.
                    }
                }

                if (batch.Count == 0) continue;

                if (batch.Count >= hydration.BatchMinSize)
                {
                    _logger.LogInformation(
                        "Batch hydration: processing {Count} requests in a single batch",
                        batch.Count);
                }

                // Process each request through the full pipeline.
                // Stage 1 batch reconciliation is handled inside RunPipelineAsync
                // via the existing ReconcileBatchAsync when multiple items share
                // the same provider. For now, process sequentially but log as batch.
                foreach (var req in batch)
                {
                    try
                    {
                        await RunPipelineAsync(req, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Let shutdown propagate.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Hydration pipeline failed for entity {Id} — will retry",
                            req.EntityId);

                        req.RetryCount += 1;
                        if (req.RetryCount <= 3)
                        {
                            // Re-enqueue for retry — TryWrite is non-blocking.
                            _channel.Writer.TryWrite(req);
                            _logger.LogInformation(
                                "Re-enqueued entity {Id} for retry (attempt {Attempt}/3)",
                                req.EntityId, req.RetryCount);
                        }
                        else
                        {
                            _logger.LogError(
                                "Entity {Id} failed after 3 retries — no further automatic attempts will be made",
                                req.EntityId);
                        }
                    }
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
        var titleHintForLog = request.Hints.GetValueOrDefault("title", "unknown");
        _logger.LogInformation(
            "[HYDRATION] Starting pipeline for entity {Id} — title: \"{Title}\", media type: {MediaType}",
            request.EntityId, titleHintForLog, request.MediaType);

        // History: hydration started.
        try { await _activityRepo.LogAsync(new SystemActivityEntry { ActionType = "HydrationStarted", EntityId = request.EntityId, Detail = "Background enrichment started" }, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to log item history (HydrationStarted)"); }

        // ── Language mismatch guard (before Stage 1) ────────────────────────
        // If the file declares a language that differs from the configured app
        // language, block the entire pipeline immediately — no Wikidata call is
        // made, saving the API call for items that should never be hydrated.
        if (request.EntityType == EntityType.MediaAsset)
        {
            try
            {
                var earlyCanonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                    .ConfigureAwait(false);
                var langCanonical = earlyCanonicals.FirstOrDefault(c =>
                    string.Equals(c.Key, "language", StringComparison.OrdinalIgnoreCase));

                if (langCanonical is not null && !string.IsNullOrWhiteSpace(langCanonical.Value))
                {
                    var coreConfig     = _configLoader.LoadCore();
                    var appLanguage    = coreConfig.Language ?? "en";
                    var fileLang       = langCanonical.Value.Split('-', '_')[0].ToLowerInvariant().Trim();
                    var configuredLang = appLanguage.Split('-', '_')[0].ToLowerInvariant().Trim();

                    if (!string.IsNullOrEmpty(fileLang)
                        && !string.IsNullOrEmpty(configuredLang)
                        && !string.Equals(fileLang, configuredLang, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "[HYDRATION] Skipping pipeline for entity {Id} — language '{Lang}' does not match configured '{ConfigLang}'",
                            request.EntityId, fileLang, configuredLang);

                        var langResult               = new HydrationResult();
                        var langDeferredNotifications = new List<Guid>();

                        await CreateReviewItemAsync(
                            request, ReviewTrigger.LanguageMismatch, 0.0,
                            $"File language '{langCanonical.Value}' does not match the configured library language '{appLanguage}'. " +
                            "This may be a foreign edition or incorrectly tagged file.",
                            langResult, ct, langDeferredNotifications).ConfigureAwait(false);

                        // Publish deferred SignalR notifications (same pattern as end-of-pipeline flush).
                        foreach (var rid in langDeferredNotifications)
                        {
                            try
                            {
                                var review = await _reviewRepo.GetByIdAsync(rid, ct).ConfigureAwait(false);
                                if (review?.Status == ReviewStatus.Pending)
                                {
                                    await _eventPublisher.PublishAsync(
                                        "ReviewItemCreated",
                                        new ReviewItemCreatedEvent(
                                            review.Id, review.EntityId, review.Trigger, null),
                                        ct).ConfigureAwait(false);
                                }
                            }
                            catch (Exception evEx) when (evEx is not OperationCanceledException)
                            {
                                _logger.LogWarning(evEx,
                                    "Failed to publish language-mismatch ReviewItemCreated event for review {ReviewId}", rid);
                            }
                        }

                        return langResult;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex,
                    "Language mismatch pre-check failed for entity {Id}; continuing",
                    request.EntityId);
            }
        }

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

        _logger.LogDebug(
            "Pipeline: effectivePass={EffectivePass} (TwoPassEnabled={TwoPass}) for entity {Id}",
            effectivePass, hydration.TwoPassEnabled, request.EntityId);

        // Build composite endpoint map.
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        _logger.LogDebug(
            "Pipeline endpoint map: {Endpoints}",
            string.Join(", ", endpointMap.Select(kv => $"{kv.Key}={kv.Value}")));

        var pipelineSw = System.Diagnostics.Stopwatch.StartNew();
        long s1Ms = 0, s2Ms = 0;

        // Collect review item IDs created during the pipeline run.
        // SignalR notifications are deferred until pipeline completion so the
        // Dashboard doesn't flash "Needs Review" for items that may be
        // auto-resolved by later stages (e.g. Stage 2 enrichment improving
        // confidence above the review threshold).
        var deferredReviewNotifications = new List<Guid>();

        // ── Stage 1: Reconciliation ──────────────────────────────────────────
        //
        // Resolves the work's identity via the Wikidata reconciliation API:
        // bridge ID lookup or title search → deep enrichment → Hub Intelligence
        // → Person Enrichment.
        // This stage runs first so that bridge IDs (ISBN, ASIN, TMDB, IMDb)
        // are available for Stage 2's precise retail lookups.

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

        // Pre-search confidence gate: when the entity has only a filename-derived
        // title with no corroborating signal (author, year, or bridge identifier),
        // a Wikidata title search produces high-confidence false positives — any
        // matching title wins at 1.0 confidence because there is nothing to
        // disambiguate against.  Block Stage 1 and create a review item so the
        // user can add metadata manually.
        if (stage1Providers.Count > 0
            && request.PreResolvedQid is null
            && !HasSufficientMetadataForAuthorityMatch(request))
        {
            _logger.LogInformation(
                "Pipeline Stage 1 (Reconciliation) blocked for entity {Id} " +
                "(title: \"{Title}\") — no real title, author, year, or bridge identifiers: " +
                "file appears to have only placeholder or missing metadata",
                request.EntityId, titleHint);

            await CreateReviewItemAsync(
                request, ReviewTrigger.AuthorityMatchFailed, 0.0,
                "Cannot perform authority match: file has no usable title, author, year, or " +
                "identifiers embedded. Add metadata manually to enable library matching.",
                result, ct, deferredReviewNotifications).ConfigureAwait(false);

            stage1Providers = [];
        }

        _logger.LogInformation(
            "Pipeline Stage 1 (Reconciliation) starting for entity {Id} — title: \"{Title}\", media type: {MediaType}, providers: [{Providers}]",
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
                provider, request, endpointMap, lang, country, ct, effectivePass).ConfigureAwait(false);

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
                _providers, ct, _arrayRepo, _logger, _searchIndex).ConfigureAwait(false);

            stage1Claims += claimsForScoring.Count;

            await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                .ConfigureAwait(false);
        }

        result.Stage1ClaimsAdded = stage1Claims;

        if (stage1Claims > 0)
        {
            // History: Wikidata matched.
            try { await _activityRepo.LogAsync(new SystemActivityEntry { ActionType = "WikidataMatched", EntityId = request.EntityId, Detail = result.WikidataQid is not null ? $"Identified on Wikidata — QID: {result.WikidataQid}" : "Identified on Wikidata" }, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to log item history (WikidataMatched)"); }

            await _eventPublisher.PublishAsync(
                "HydrationStageCompleted",
                new HydrationStageCompletedEvent(request.EntityId, 1, stage1Claims, "reconciliation"),
                ct).ConfigureAwait(false);

            // Hub Intelligence is deferred to Pass 2 (Universe work). Works are displayed
            // directly via /library/works without requiring hub_id assignment.
            _logger.LogDebug("Hub intelligence deferred to Pass 2 for asset {AssetId}", request.EntityId);

            // Pseudonym author protection: if the existing author canonical value
            // names a person flagged as a pseudonym (e.g. "Richard Bachman"), emit
            // a high-confidence author claim so the real person's name from Wikidata
            // does not overwrite the pseudonym.  The sidecar should say the name the
            // work was *published* under.
            var canonicalsAfterS1 = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                .ConfigureAwait(false);

            // Pseudonym author protection runs in both passes (Quick and Universe)
            // as a safety net. The ReconciliationAdapter's pen-name detection handles
            // the primary case (P742 shared by co-authors), but this covers the
            // person-record-based fallback for known pen names like Richard Bachman.
            await ProtectPseudonymAuthorAsync(request.EntityId, canonicalsAfterS1, ct)
                .ConfigureAwait(false);

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
                    var personRequests = await _identity.EnrichAsync(request.EntityId, personRefs, ct)
                        .ConfigureAwait(false);

                    // Process person enrichment synchronously so author data
                    // (headshot, biography) is available immediately — not deferred
                    // to the background queue.  Activity entries share the same
                    // IngestionRunId so they appear as one batch in the registry.
                    foreach (var personReq in personRequests)
                    {
                        try
                        {
                            await _harvesting.ProcessSynchronousAsync(personReq, ct)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex,
                                "Synchronous person enrichment failed for person {Id}; continuing",
                                personReq.EntityId);
                        }
                    }
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

            // Audiobook edition discovery: when the work has a QID and the media
            // type is Audiobook, discover audiobook editions via P747 and deposit
            // narrator/duration/ASIN as additional claims.
            if (result.WikidataQid is not null
                && request.MediaType == MediaType.Audiobooks)
            {
                try
                {
                    var reconAdapter = _providers
                        .OfType<MediaEngine.Providers.Adapters.ReconciliationAdapter>()
                        .FirstOrDefault();
                    if (reconAdapter is not null)
                    {
                        request.Hints.TryGetValue("narrator", out var narratorHint);
                        var editions = await reconAdapter.DiscoverAudiobookEditionsAsync(
                            result.WikidataQid, narratorHint, ct).ConfigureAwait(false);

                        if (editions.Count > 0)
                        {
                            var editionClaims = new List<ProviderClaim>();
                            var first = editions[0]; // Use first audiobook edition found

                            if (!string.IsNullOrWhiteSpace(first.Narrator))
                                editionClaims.Add(new ProviderClaim("narrator", first.Narrator, 0.90));
                            if (!string.IsNullOrWhiteSpace(first.Duration))
                                editionClaims.Add(new ProviderClaim("duration", first.Duration, 0.85));
                            if (!string.IsNullOrWhiteSpace(first.ASIN))
                                editionClaims.Add(new ProviderClaim("asin", first.ASIN, 0.95));
                            if (!string.IsNullOrWhiteSpace(first.Publisher))
                                editionClaims.Add(new ProviderClaim("publisher", first.Publisher, 0.85));

                            if (editionClaims.Count > 0)
                            {
                                await ScoringHelper.PersistClaimsAndScoreAsync(
                                    request.EntityId, editionClaims, reconAdapter.ProviderId,
                                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                                    _providers, ct, _arrayRepo, _logger, _searchIndex).ConfigureAwait(false);

                                result.Stage1ClaimsAdded += editionClaims.Count;

                                _logger.LogInformation(
                                    "Audiobook edition discovery: found {Count} edition(s) for QID {Qid}, deposited {Claims} claims",
                                    editions.Count, result.WikidataQid, editionClaims.Count);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Audiobook edition discovery failed for entity {Id}; continuing",
                        request.EntityId);
                }
            }
        }
        else if (stage1Providers.Count > 0)
        {
            _logger.LogWarning(
                "Pipeline Stage 1 (Reconciliation) produced no results for entity {Id}",
                request.EntityId);

            // History: Wikidata match failed.
            try { await _activityRepo.LogAsync(new SystemActivityEntry { ActionType = "WikidataMatchFailed", EntityId = request.EntityId, Detail = "No Wikidata match found" }, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to log item history (WikidataMatchFailed)"); }

            // Authority match failed — create review item.
            await CreateReviewItemAsync(
                request, ReviewTrigger.AuthorityMatchFailed, 0.0,
                $"Wikidata authority match failed for this {request.MediaType}",
                result, ct, deferredReviewNotifications).ConfigureAwait(false);

            // Always skip Stage 2 when Stage 1 (authority) failed.
            // Retail providers must not run title-based searches for
            // unconfirmed identities — this would pull covers and metadata
            // for potentially wrong matches.  Stage 2 will run later when
            // the user resolves the review item (PreResolvedQid path).
            _logger.LogInformation(
                "Pipeline skipping Stage 2 after Stage 1 failure for entity {Id} — "
                + "retail providers will run after identity is confirmed",
                request.EntityId);
            goto PostPipeline;
        }
        else
        {
            _logger.LogWarning(
                "Pipeline Stage 1 skipped for entity {Id}: no reconciliation providers configured",
                request.EntityId);
        }


        s1Ms = stageSw.ElapsedMilliseconds;
        stageSw.Restart();

        // ── Stage 2: Enrichment (Waterfall) ─────────────────────────────────
        //
        // Runs retail providers in priority order (primary -> secondary ->
        // tertiary) from slots.json. Bridge IDs deposited by Stage 1 are used
        // for precise lookups. After each provider, overall confidence is
        // checked against the waterfall threshold.

        // Reload canonical values to extract bridge IDs from Stage 1.
        var canonicalsForS2 = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);
        var stage2ProviderConfigs = provConfigs
            .Where(pc => pc.HydrationStages.Contains(2))
            .ToList();
        var bridgeHints = ExtractBridgeHints(canonicalsForS2, request.MediaType, stage2ProviderConfigs);

        // Update title hint with canonical value from Stage 1 (the Reconciliation
        // label is typically shorter/cleaner than the embedded P1476 formal title,
        // e.g. "Frankenstein" rather than "Frankenstein; or, The Modern Prometheus").
        // Use direct assignment so the canonical title overrides any embedded title
        // already in bridgeHints or the original request hints.
        var canonicalTitle = canonicalsForS2
            .FirstOrDefault(cv => string.Equals(cv.Key, "title", StringComparison.OrdinalIgnoreCase));
        if (canonicalTitle is not null && !string.IsNullOrWhiteSpace(canonicalTitle.Value))
        {
            bridgeHints["title"] = canonicalTitle.Value;
        }

        // ── Bridge ID fallback from raw claims ────────────────────────────────
        // If Stage 1 failed or hasn't deposited canonical values yet, the
        // canonical_values table may be empty even though EpubProcessor (or
        // another processor) has already written ISBN / ASIN claims directly
        // into metadata_claims. Load raw claims and merge any bridge IDs that
        // are not already present in bridgeHints — canonical values always take
        // priority, but raw claim values are better than nothing for retail lookups.
        try
        {
            var rawClaims = await _claimRepo.GetByEntityAsync(request.EntityId, ct)
                .ConfigureAwait(false);

            var bridgeClaimKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "isbn", "isbn_13", "isbn_10",
                "asin", "apple_books_id",
                "tmdb_id", "imdb_id",
                "audible_id", "goodreads_id",
                "musicbrainz_id", "comic_vine_id",
            };

            foreach (var claim in rawClaims)
            {
                if (string.IsNullOrWhiteSpace(claim.ClaimValue)) continue;

                var effectiveKey = IdentifierNormalizationService.GetClaimKeyAlias(claim.ClaimKey)
                                   ?? claim.ClaimKey;

                if (!bridgeClaimKeys.Contains(effectiveKey)) continue;
                if (bridgeHints.ContainsKey(effectiveKey)) continue;

                var normalizedValue = effectiveKey switch
                {
                    "isbn" => NormalizeIsbnForRetail(claim.ClaimValue),
                    "asin" => claim.ClaimValue.Trim().ToUpperInvariant(),
                    _      => claim.ClaimValue.Trim(),
                };

                if (!string.IsNullOrWhiteSpace(normalizedValue))
                {
                    bridgeHints[effectiveKey] = normalizedValue;
                    _logger.LogDebug(
                        "Bridge ID '{Key}' = '{Value}' sourced from raw claims for entity {Id}",
                        effectiveKey, normalizedValue, request.EntityId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "Bridge ID fallback from raw claims failed for entity {Id}; proceeding with canonical bridge IDs only",
                request.EntityId);
        }

        // Enrich the request with bridge IDs from Stage 1 for precise retail lookups.
        var stage2Request = EnrichRequestWithBridgeHints(request, bridgeHints);

        var waterfallProviders = ResolveWaterfallProviders(slots, provConfigs, stage2Request);
        var stage2Claims = 0;
        IExternalMetadataProvider? lastSuccessfulProvider = null;

        _logger.LogInformation(
            "Pipeline Stage 2 (Enrichment) starting for entity {Id} — bridge IDs: [{BridgeKeys}], providers: [{Providers}]",
            request.EntityId,
            bridgeHints.Count > 0 ? string.Join(", ", bridgeHints.Keys) : "(none)",
            waterfallProviders.Count > 0
                ? string.Join(" -> ", waterfallProviders.Select(p => p.Name))
                : "(none)");

        // ── Wikipedia: runs in parallel with Stage 2 waterfall ───────────────
        // Wikipedia only needs the QID resolved by Stage 1 — it has no dependency
        // on cover art or ratings from retail providers. Starting it now lets it
        // fetch rich descriptions concurrently while the waterfall runs.
        var wikipediaProvider = _providers.FirstOrDefault(p =>
            p.Name.Contains("Wikipedia", StringComparison.OrdinalIgnoreCase));

        Task<IReadOnlyList<ProviderClaim>> wikipediaTask;
        if (wikipediaProvider is not null && result.WikidataQid is not null)
        {
            var wikiRequest = new HarvestRequest
            {
                EntityId              = request.EntityId,
                EntityType            = request.EntityType,
                MediaType             = request.MediaType,
                Hints                 = request.Hints,
                PreResolvedQid        = result.WikidataQid,
                SuppressActivityEntry = request.SuppressActivityEntry,
                IngestionRunId        = request.IngestionRunId,
                FolderHintBridgeIds   = request.FolderHintBridgeIds,
                HintedHubId           = request.HintedHubId,
                Pass                  = request.Pass,
            };
            _logger.LogInformation(
                "Wikipedia fetch started in parallel with Stage 2 for entity {Id} (QID: {Qid})",
                request.EntityId, result.WikidataQid);
            wikipediaTask = Task.Run(
                () => FetchFromProviderAsync(
                    wikipediaProvider, wikiRequest, endpointMap, lang, country, ct, effectivePass),
                ct);
        }
        else
        {
            // Log at Information level so missing Wikipedia descriptions are visible in normal logs.
            if (wikipediaProvider is null)
            {
                _logger.LogWarning(
                    "Wikipedia provider not found in registered providers for entity {Id} — " +
                    "no rich description will be fetched. Check that WikipediaAdapter is registered as IExternalMetadataProvider.",
                    request.EntityId);
            }
            else
            {
                _logger.LogInformation(
                    "Wikipedia fetch skipped for entity {Id}: Stage 1 did not resolve a Wikidata QID. " +
                    "No QID means no Wikipedia sitelink lookup is possible.",
                    request.EntityId);
            }
            wikipediaTask = Task.FromResult<IReadOnlyList<ProviderClaim>>([]);
        }

        foreach (var provider in waterfallProviders)
        {
            var claims = await FetchFromProviderAsync(
                provider, stage2Request, endpointMap, lang, country, ct, effectivePass).ConfigureAwait(false);

            if (claims.Count > 0)
            {
                await ScoringHelper.PersistClaimsAndScoreAsync(
                    request.EntityId, claims, provider.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                    _providers, ct, _arrayRepo, _logger, _searchIndex).ConfigureAwait(false);

                stage2Claims += claims.Count;
                lastSuccessfulProvider = provider;

                await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                    .ConfigureAwait(false);

                // Check confidence after this provider
                var currentConfidence = await ComputeOverallConfidenceAsync(request.EntityId, ct)
                    .ConfigureAwait(false);

                await _eventPublisher.PublishAsync(
                    "HydrationStageCompleted",
                    new HydrationStageCompletedEvent(request.EntityId, 2, claims.Count,
                        $"waterfall_{provider.Name}"),
                    ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Pipeline Stage 2 waterfall: provider '{Provider}' returned {Claims} claims, confidence now {Confidence:P0} (threshold: {Threshold:P0})",
                    provider.Name, claims.Count, currentConfidence,
                    hydration.Stage3WaterfallConfidenceThreshold);

                if (currentConfidence >= hydration.Stage3WaterfallConfidenceThreshold)
                {
                    _logger.LogDebug(
                        "Pipeline Stage 2 waterfall: confidence sufficient after '{Provider}', stopping",
                        provider.Name);
                    break;
                }
            }
            else
            {
                _logger.LogInformation(
                    "Pipeline Stage 2 waterfall: provider '{Provider}' returned no results, continuing",
                    provider.Name);
            }
        }

        // ── Merge Wikipedia results (parallel task started before waterfall) ─
        try
        {
            var wikipediaClaims = await wikipediaTask.ConfigureAwait(false);
            if (wikipediaClaims.Count > 0 && wikipediaProvider is not null)
            {
                await ScoringHelper.PersistClaimsAndScoreAsync(
                    request.EntityId, wikipediaClaims, wikipediaProvider.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                    _providers, ct, _arrayRepo, _logger, _searchIndex).ConfigureAwait(false);

                stage2Claims += wikipediaClaims.Count;

                await PublishHarvestEvent(request.EntityId, wikipediaProvider.Name, wikipediaClaims, ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Wikipedia returned {Claims} claims for entity {Id} (fields: {Fields})",
                    wikipediaClaims.Count, request.EntityId,
                    string.Join(", ", wikipediaClaims.Select(c => c.Key).Distinct()));
            }
            else if (wikipediaProvider is not null && result.WikidataQid is not null)
            {
                // Wikipedia was attempted but returned nothing (no sitelink, empty extract, or HTTP error).
                // This is logged at Information so operators can see when Wikipedia descriptions are missing.
                _logger.LogInformation(
                    "Wikipedia returned 0 claims for entity {Id} (QID: {Qid}). " +
                    "Possible causes: no Wikipedia article for this QID, Wikipedia API unreachable, or language '{Lang}' has no sitelink.",
                    request.EntityId, result.WikidataQid, lang);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Wikipedia parallel fetch failed for entity {Id}; continuing without Wikipedia claims",
                request.EntityId);
        }

        result.Stage2ClaimsAdded = stage2Claims;

        if (stage2Claims > 0)
        {
            // History: retail enrichment succeeded.
            try { await _activityRepo.LogAsync(new SystemActivityEntry { ActionType = "RetailEnriched", EntityId = request.EntityId, Detail = "Additional metadata retrieved" }, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to log item history (RetailEnriched)"); }
            // Post-hydration auto-resolve: if Stage 2 returned 3+ claims and this
            // entity has a pending AmbiguousMediaType review item, the provider match
            // confirms the media type — auto-resolve the review item.
            if (stage2Claims >= 3 && request.EntityType == EntityType.MediaAsset)
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

                        // Adjust batch counters: shift one file from Review → Registered.
                        await AdjustBatchForResolveAsync(request.IngestionRunId, ct).ConfigureAwait(false);

                        await _activityRepo.LogAsync(new SystemActivityEntry
                        {
                            ActionType = SystemActionType.ReviewItemResolved,
                            EntityId   = request.EntityId,
                            Detail     = $"AmbiguousMediaType auto-resolved: Stage 2 returned {stage2Claims} claims, confirming media type.",
                        }, ct).ConfigureAwait(false);

                        await _eventPublisher.PublishAsync("ReviewItemResolved", new
                        {
                            review_item_id = review.Id,
                            entity_id      = request.EntityId,
                            status         = "Resolved",
                        }, ct).ConfigureAwait(false);

                        _logger.LogInformation(
                            "AmbiguousMediaType review auto-resolved for entity {Id} — {Claims} claims confirmed type",
                            request.EntityId, stage2Claims);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to auto-resolve AmbiguousMediaType review for entity {Id}",
                        request.EntityId);
                }
            }

            // Write-back: write resolved metadata to the physical file after enrichment.
            // Skip on suppressed re-enqueue runs (cover-only) — the initial run
            // already wrote metadata.
            if (request.EntityType == EntityType.MediaAsset
                && !request.SuppressActivityEntry)
            {
                try
                {
                    await _writeBack.WriteMetadataAsync(request.EntityId, "enrichment", ct, request.IngestionRunId)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Write-back after Stage 2 failed for entity {Id}; continuing",
                        request.EntityId);
                }
            }

            // Download cover art from provider URL if no cover.jpg exists yet.
            // This runs even on suppressed re-enqueue — it's the reason for the re-enqueue.
            if (request.EntityType == EntityType.MediaAsset)
            {
                await PersistCoverFromUrlAsync(request.EntityId, ct).ConfigureAwait(false);
            }

            // ArtworkUnconfirmed: if cover art was deposited but no precise bridge ID
            // lookup was available (ISBN, apple_books_id), the cover came from a fuzzy
            // text search and needs user confirmation.
            if (request.EntityType == EntityType.MediaAsset)
            {
                var postS2Canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                    .ConfigureAwait(false);
                var hasCover = postS2Canonicals.Any(c =>
                    string.Equals(c.Key, "cover", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(c.Value));
                var hadPreciseLookup = bridgeHints.ContainsKey("isbn")
                    || bridgeHints.ContainsKey("apple_books_id");

                if (hasCover && !hadPreciseLookup)
                {
                    await CreateReviewItemAsync(
                        request, ReviewTrigger.ArtworkUnconfirmed, 0.0,
                        "Cover art was sourced via text search (no ISBN or Apple Books ID available). Please confirm the artwork is correct.",
                        result, ct, deferredReviewNotifications).ConfigureAwait(false);
                }
            }
        }
        else if (waterfallProviders.Count > 0)
        {
            _logger.LogWarning(
                "Pipeline Stage 2 (Enrichment) produced no results for entity {Id} from any provider in waterfall [{Providers}]",
                request.EntityId, string.Join(", ", waterfallProviders.Select(p => p.Name)));

            // History: retail enrichment failed.
            try { await _activityRepo.LogAsync(new SystemActivityEntry { ActionType = "RetailEnrichFailed", EntityId = request.EntityId, Detail = "No additional metadata found" }, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to log item history (RetailEnrichFailed)"); }

            // All waterfall providers ran but returned no results -> ContentMatchFailed.
            await CreateReviewItemAsync(
                request, ReviewTrigger.ContentMatchFailed, 0.0,
                $"All enrichment waterfall providers returned no results for this {request.MediaType}",
                result, ct, deferredReviewNotifications).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Pipeline Stage 2 skipped for entity {Id}: no enrichment providers configured for {MediaType}",
                request.EntityId, request.MediaType);
        }


        s2Ms = stageSw.ElapsedMilliseconds;

        // ── NF Placeholder: enrichment-only match without Wikidata QID ──────
        // When Stage 1 (Reconciliation) failed to find a QID but Stage 2
        // (Enrichment) succeeded, the item is likely new or in early release.
        // Assign a placeholder QID in "NF{6-digit}" format and flag for review.
        if (result.WikidataQid is null && result.Stage2ClaimsAdded > 0)
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
                        $"No Wikidata QID found. Enrichment match exists ({result.Stage2ClaimsAdded} claims). Placeholder: {placeholder}",
                        result, ct, deferredReviewNotifications).ConfigureAwait(false);

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
            "[PERF] Hydration {EntityId}: S1={S1Ms}ms S2={S2Ms}ms Total={TotalMs}ms (claims: {S1Claims}+{S2Claims}={TotalClaims})",
            request.EntityId, s1Ms, s2Ms, pipelineSw.ElapsedMilliseconds,
            result.Stage1ClaimsAdded, result.Stage2ClaimsAdded,
            result.TotalClaimsAdded);

        if (result.TotalClaimsAdded > 0 || !string.IsNullOrWhiteSpace(request.PreResolvedQid))
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
                    result, ct, deferredReviewNotifications).ConfigureAwait(false);
            }
            else
            {
                // When Wikidata confirmed a QID, identity is proven — use the lower
                // post-hydration threshold (default 0.70) instead of the standard
                // auto-organize gate (0.85).  This allows audiobooks and other media
                // with conservative processor confidence to organize once identified.
                double organizeThreshold = result.WikidataQid != null
                    ? hydration.PostHydrationOrganizeThreshold
                    : scoring.AutoLinkThreshold;

                if (scored.OverallConfidence >= organizeThreshold)
                {
                    await TryAutoResolveAndOrganizeAsync(
                        request, scored.OverallConfidence, ct).ConfigureAwait(false);
                }

                // Auto-resolve LowConfidence / AuthorityMatchFailed / ContentMatchFailed
                // items when confidence has improved above the review threshold, even if
                // not yet high enough for auto-organize.  This prevents orphaned review
                // items from piling up after successful hydration enrichment.
                await TryAutoResolveStaleReviewItemsAsync(
                    request.EntityId, scored.OverallConfidence, ct).ConfigureAwait(false);
            }

            // Check for metadata conflicts after post-hydration re-scoring.
            // Conflicts don't block organization — just surface them for user review.
            var conflictedFields = scored.FieldScores
                .Where(f => f.IsConflicted && f.Key != "media_type")
                .Select(f => f.Key)
                .ToList();

            if (!request.SuppressReviewCreation && conflictedFields.Count > 0)
            {
                await CreateMetadataConflictReviewItemAsync(
                    request.EntityId, scored.OverallConfidence, conflictedFields, ct, request.IngestionRunId,
                    deferredReviewNotifications).ConfigureAwait(false);
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
            "Hydration pipeline complete for entity {Id}: S1={S1} S2={S2} total={Total} review={Review}",
            request.EntityId, result.Stage1ClaimsAdded, result.Stage2ClaimsAdded,
            result.TotalClaimsAdded, result.NeedsReview);

        // ── Flush deferred review notifications ──────────────────────────────
        // Now that the pipeline is complete (including auto-resolve passes),
        // publish SignalR events only for review items that are still Pending.
        // Items auto-resolved during the pipeline are silently dropped.
        foreach (var reviewId in deferredReviewNotifications)
        {
            try
            {
                var review = await _reviewRepo.GetByIdAsync(reviewId, ct).ConfigureAwait(false);
                if (review?.Status == ReviewStatus.Pending)
                {
                    await _eventPublisher.PublishAsync(
                        "ReviewItemCreated",
                        new ReviewItemCreatedEvent(
                            review.Id, review.EntityId, review.Trigger, null),
                        ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to publish deferred ReviewItemCreated event for review {ReviewId}",
                    reviewId);
            }
        }

        _logger.LogInformation(
            "[HYDRATION] Completed pipeline for entity {Id} — title: \"{Title}\", claims: {ClaimCount}, QID: {Qid}",
            request.EntityId, titleHintForLog,
            result.TotalClaimsAdded,
            result.WikidataQid ?? "none");

        // History: hydration completed.
        try { await _activityRepo.LogAsync(new SystemActivityEntry { ActionType = "HydrationCompleted", EntityId = request.EntityId, Detail = $"Enrichment complete — {result.TotalClaimsAdded} claims added" }, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Failed to log item history (HydrationCompleted)"); }

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
                          or ReviewTrigger.AuthorityMatchFailed).ToList();
            // AuthorityMatchFailed is auto-resolved here because high confidence from
            // Stage 2 enrichment providers is sufficient to identify and organise the file
            // — Wikidata reconciliation is optional enrichment.

            foreach (var review in resolvable)
            {
                await _reviewRepo.UpdateStatusAsync(
                    review.Id, ReviewStatus.Resolved, "auto_hydration", ct)
                    .ConfigureAwait(false);

                // Adjust batch counters: shift one file from Review → Registered.
                await AdjustBatchForResolveAsync(request.IngestionRunId, ct).ConfigureAwait(false);

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

            // Check if hydration assigned a QID. If not, create a MissingQid review
            // so the user can manually identify the item. Nothing enters the library
            // without a confirmed Wikidata identity.
            if (request.EntityType == EntityType.MediaAsset)
            {
                var postCanonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
                    .ConfigureAwait(false);
                var qidCv = postCanonicals.FirstOrDefault(cv =>
                    string.Equals(cv.Key, "wikidata_qid", StringComparison.OrdinalIgnoreCase));
                var hasQid = qidCv is not null
                    && !string.IsNullOrWhiteSpace(qidCv.Value)
                    && !qidCv.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase);

                if (!hasQid)
                {
                    await CreateReviewItemAsync(request, ReviewTrigger.MissingQid, 0.0,
                        "Hydration completed but no Wikidata QID was resolved. Manual identification required.",
                        new HydrationResult(), ct).ConfigureAwait(false);
                }
            }

            // Attempt to organize the file into the library. AutoOrganizeService
            // enforces the QID gate — files without a QID stay in staging.
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

    /// <summary>
    /// Auto-resolves stale LowConfidence, AuthorityMatchFailed, and ContentMatchFailed
    /// review items when hydration has improved confidence above the review threshold
    /// (default 0.60) but not necessarily above the organize threshold (0.70/0.85).
    /// This prevents orphaned review items from piling up after successful enrichment.
    /// </summary>
    /// <remarks>
    /// This method is intentionally separate from <see cref="TryAutoResolveAndOrganizeAsync"/>
    /// which handles the higher-confidence organize path.  It runs independently so that
    /// review items are cleaned up even when the file isn't ready for auto-organize.
    /// </remarks>
    private async Task TryAutoResolveStaleReviewItemsAsync(
        Guid entityId, double confidence, CancellationToken ct)
    {
        try
        {
            var reviews = await _reviewRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            var staleReviews = reviews.Where(r =>
                r.Status == ReviewStatus.Pending &&
                r.Trigger is ReviewTrigger.LowConfidence
                          or ReviewTrigger.AuthorityMatchFailed
                          or ReviewTrigger.ContentMatchFailed).ToList();

            foreach (var review in staleReviews)
            {
                await _reviewRepo.UpdateStatusAsync(
                    review.Id, ReviewStatus.Resolved, "auto_hydration", ct)
                    .ConfigureAwait(false);

                // Note: no batch adjustment here — this method is called outside the
                // normal pipeline flow (e.g. on-demand hydration) and may not have
                // a batch context.

                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ReviewItemResolved,
                    EntityId   = entityId,
                    Detail     = $"Auto-resolved ({review.Trigger}): confidence improved to {confidence:P0} after hydration.",
                }, ct).ConfigureAwait(false);

                await _eventPublisher.PublishAsync("ReviewItemResolved", new
                {
                    review_item_id = review.Id,
                    entity_id      = entityId,
                    status         = "Resolved",
                }, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Stale review item {ReviewId} ({Trigger}) auto-resolved for entity {EntityId} — confidence {Confidence:P0}",
                    review.Id, review.Trigger, entityId, confidence);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Auto-resolve stale review items failed for entity {Id}",
                entityId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the harvest request carries enough corroborating
    /// metadata to make a Wikidata title search reliable.
    ///
    /// A "title-only" request — nothing but a filename-derived title with no
    /// author, year, ISBN, ASIN, or other bridge identifier — produces false
    /// positives because Wikidata title search auto-accepts any match at full
    /// confidence (1.0) when there is nothing else to disambiguate against.
    /// </summary>
    private static bool HasSufficientMetadataForAuthorityMatch(HarvestRequest request)
    {
        if (request.PreResolvedQid is not null) return true;

        var h = request.Hints;

        if (!string.IsNullOrWhiteSpace(h.GetValueOrDefault("author"))) return true;

        var year = h.GetValueOrDefault("year");
        if (!string.IsNullOrWhiteSpace(year) && year.Length >= 4) return true;

        // Any bridge identifier enables a direct lookup instead of a noisy search.
        foreach (var key in new[]
        {
            "isbn", "asin", "tmdb_id", "imdb_id", "goodreads_id",
            "musicbrainz_id", "apple_books_id", "audible_asin", "open_library_id",
            "comic_vine_id", "apple_podcasts_id",
        })
        {
            if (!string.IsNullOrWhiteSpace(h.GetValueOrDefault(key))) return true;
        }

        // Allow title-only requests when the title is a real, non-placeholder value.
        // Wikidata's Reconciliation API is designed for fuzzy title search, and the
        // existing TokenSet ratio >= 0.60 verification already guards against
        // false-positive matches.
        var title = h.GetValueOrDefault("title");
        if (!string.IsNullOrWhiteSpace(title) && IsRealTitle(title)) return true;

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="title"/> looks like a genuine work
    /// title rather than a placeholder or filename-derived hash.
    /// </summary>
    private static bool IsRealTitle(string title)
    {
        var t = title.Trim();
        if (string.IsNullOrEmpty(t)) return false;
        if (t.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Equals("(unknown)", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Equals("(untitled)", StringComparison.OrdinalIgnoreCase)) return false;
        // Content-hash filenames: all hex characters, typically 16–64 chars.
        if (t.Length >= 8 && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[0-9a-fA-F]+$"))
            return false;
        return true;
    }

    /// <summary>
    /// Resolves the primary provider for Stage 1 (Reconciliation) from the slot
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
    /// Used for Stage 2 (Enrichment).
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
    /// Used during Stage 2 waterfall to decide whether to call the next provider.
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
    /// and entity type compatibility. Used for Stage 1 (Reconciliation) providers.
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
                else if (result.Stage2ClaimsAdded > 0)
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
        CancellationToken ct,
        List<Guid>? deferredReviewNotifications = null)
    {
        // Suppress: during user-triggered review resolution, don't create new reviews.
        if (request.SuppressReviewCreation)
        {
            return;
        }

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

        // Adjust batch counters: shift one file from Registered → Review.
        await AdjustBatchForReviewAsync(request.IngestionRunId, ct).ConfigureAwait(false);

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

        // Defer SignalR notification until pipeline completion so the Registry
        // doesn't show "Needs Match" for items still being processed.
        if (deferredReviewNotifications is not null)
        {
            deferredReviewNotifications.Add(reviewEntry.Id);
        }
        else
        {
            await _eventPublisher.PublishAsync(
                "ReviewItemCreated",
                new ReviewItemCreatedEvent(
                    reviewEntry.Id, request.EntityId, trigger,
                    titleCanonical?.Value),
                ct).ConfigureAwait(false);
        }
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
        Guid? ingestionRunId = null,
        List<Guid>? deferredReviewNotifications = null)
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

            // Defer SignalR notification until pipeline completion so the Registry
            // doesn't show "Needs Match" for items still being processed.
            if (deferredReviewNotifications is not null)
            {
                deferredReviewNotifications.Add(entry.Id);
            }
            else
            {
                await _eventPublisher.PublishAsync(
                    "ReviewItemCreated",
                    new ReviewItemCreatedEvent(entry.Id, entityId, ReviewTrigger.MetadataConflict, null),
                    ct).ConfigureAwait(false);
            }

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
        string language,
        string country,
        CancellationToken ct,
        HydrationPass effectivePass = HydrationPass.Quick)
    {
        var baseUrl = ResolveBaseUrl(provider, endpointMap);
        var lookupRequest = BuildLookupRequest(request, provider, baseUrl, language, country, effectivePass);

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
    /// Extracts bridge identifier hints from canonical values, driven by Stage 2 provider
    /// <c>preferred_bridge_ids</c> config. Applies key aliases (<c>isbn_13</c> → <c>isbn</c>)
    /// and retail format normalization.
    /// </summary>
    private static Dictionary<string, string> ExtractBridgeHints(
        IReadOnlyList<CanonicalValue> canonicals,
        MediaType mediaType,
        IReadOnlyList<MediaEngine.Storage.Models.ProviderConfiguration> stage2ProviderConfigs)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Collect desired bridge keys from all Stage 2 providers' preferred_bridge_ids
        // for the current media type.
        var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mediaTypeName = mediaType.ToString();

        foreach (var cfg in stage2ProviderConfigs)
        {
            if (cfg.PreferredBridgeIds is null) continue;

            if (cfg.PreferredBridgeIds.TryGetValue(mediaTypeName, out var keys))
            {
                foreach (var k in keys) desiredKeys.Add(k);
            }
        }

        if (desiredKeys.Count == 0) return hints;

        foreach (var cv in canonicals)
        {
            if (string.IsNullOrEmpty(cv.Value)) continue;

            // Check if the canonical key matches directly or via alias.
            var effectiveKey = cv.Key;
            var alias = IdentifierNormalizationService.GetClaimKeyAlias(cv.Key);
            if (alias is not null) effectiveKey = alias;

            if (!desiredKeys.Contains(effectiveKey)) continue;

            // Avoid duplicates — first value wins.
            if (hints.ContainsKey(effectiveKey)) continue;

            // Apply retail format normalization (strip ISBN dashes, etc.)
            // We need the P-code for normalization, but we only have claim keys here.
            // Apply a best-effort normalization by stripping common ISBN formatting.
            var normalizedValue = effectiveKey switch
            {
                "isbn" => NormalizeIsbnForRetail(cv.Value),
                "asin" => cv.Value.Trim().ToUpperInvariant(),
                _      => cv.Value.Trim()
            };

            if (!string.IsNullOrWhiteSpace(normalizedValue))
                hints[effectiveKey] = normalizedValue;
        }

        return hints;
    }

    /// <summary>
    /// Strips dashes and spaces from ISBN values for retail API lookups.
    /// </summary>
    private static string NormalizeIsbnForRetail(string isbn)
    {
        // Strip all non-digit characters (dashes, spaces, URI prefixes like "urn:isbn:").
        var cleaned = new string(isbn.Where(char.IsDigit).ToArray());
        return cleaned;
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

        // Bridge hints come from canonical values resolved by Stage 1 — they are
        // authoritative and must override any embedded metadata in the original hints.
        // Use direct assignment (not TryAdd) so Stage 1 canonical values win.
        foreach (var (key, value) in bridgeHints)
            mergedHints[key] = value;

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

        // 4. Resolve actor→character mappings via Wikibase P161+P453 qualifiers.
        try
        {
            await ResolveActorCharacterMappingsAsync(workQid, entityId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Actor-character mapping failed for work {Qid}; continuing", workQid);
        }
    }

    /// <summary>
    /// Uses the Wikibase API to fetch P161 (cast_member) statements with P453 (character)
    /// qualifiers for a work. For each (actor, character) pair found:
    /// 1. Creates or finds a Person record for the actor.
    /// 2. Finds the FictionalEntity for the character.
    /// 3. Links them via character_performer_links.
    /// </summary>
    private async Task ResolveActorCharacterMappingsAsync(
        string workQid,
        Guid mediaAssetId,
        CancellationToken ct)
    {
        IReadOnlyList<QualifiedStatement> castStatements;
        try
        {
            castStatements = await _wikibaseApi.GetClaimsAsync(workQid, "P161", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to fetch P161 cast statements for work {Qid}", workQid);
            return;
        }

        if (castStatements.Count == 0)
            return;

        var linkedCount = 0;

        foreach (var statement in castStatements)
        {
            ct.ThrowIfCancellationRequested();

            var actorQid = statement.ValueQid;
            if (string.IsNullOrWhiteSpace(actorQid))
                continue;

            // Look for P453 (character played in this work) qualifier.
            if (!statement.Qualifiers.TryGetValue("P453", out var characterQualifiers) ||
                characterQualifiers.Count == 0)
                continue;

            foreach (var charQual in characterQualifiers)
            {
                var characterQid = charQual.EntityQid;
                if (string.IsNullOrWhiteSpace(characterQid))
                    continue;

                try
                {
                    // 1. Find or create Person record for the actor.
                    //    Use RecursiveIdentityService to create person and enrich synchronously.
                    var actorLabel = statement.ValueLabel ?? actorQid;
                    var personRefs = new List<PersonReference>
                    {
                        new("Cast Member", actorLabel, actorQid)
                    };
                    var actorRequests = await _identity.EnrichAsync(mediaAssetId, personRefs, ct)
                        .ConfigureAwait(false);

                    // Process actor enrichment synchronously.
                    foreach (var actorReq in actorRequests)
                    {
                        try
                        {
                            await _harvesting.ProcessSynchronousAsync(actorReq, ct)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex,
                                "Synchronous actor enrichment failed for person {Id}; continuing",
                                actorReq.EntityId);
                        }
                    }

                    // 2. Find the Person record we just created/found.
                    var person = await _personRepo.FindByQidAsync(actorQid, ct)
                        .ConfigureAwait(false);

                    if (person is null)
                    {
                        // Fallback: find by name if QID hasn't been set yet
                        // (enrichment is async, QID may not be written yet).
                        person = await _personRepo.FindByNameAsync(actorLabel, "Cast Member", ct)
                            .ConfigureAwait(false);
                    }

                    if (person is null)
                    {
                        _logger.LogDebug(
                            "Could not find Person record for actor {ActorQid} ({Label})",
                            actorQid, actorLabel);
                        continue;
                    }

                    // 3. Find the FictionalEntity for the character.
                    var entity = await _fictionalEntityRepo.FindByQidAsync(characterQid, ct)
                        .ConfigureAwait(false);

                    if (entity is null)
                    {
                        _logger.LogDebug(
                            "No FictionalEntity found for character {CharQid} — " +
                            "may not have been discovered yet",
                            characterQid);
                        continue;
                    }

                    // 4. Link actor to character for this work.
                    await _personRepo.LinkToCharacterAsync(
                        person.Id, entity.Id, workQid, ct)
                        .ConfigureAwait(false);

                    linkedCount++;

                    _logger.LogDebug(
                        "Linked actor '{Actor}' ({ActorQid}) to character '{Character}' ({CharQid}) for work {WorkQid}",
                        actorLabel, actorQid, entity.Label, characterQid, workQid);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to link actor {ActorQid} to character {CharQid} for work {WorkQid}",
                        actorQid, characterQid, workQid);
                }
            }
        }

        if (linkedCount > 0)
        {
            _logger.LogInformation(
                "Created {Count} actor-character links for work {Qid}",
                linkedCount, workQid);
        }
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
                _providers, ct, _arrayRepo, _logger, _searchIndex).ConfigureAwait(false);

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
        // Accumulate ALL values per key as a list (preserves multi-valued fields like author).
        var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in rawClaims)
        {
            if (!byKey.TryGetValue(c.Key, out var list))
            {
                list = [];
                byKey[c.Key] = list;
            }
            list.Add(c.Value);
        }

        var refs = new List<PersonReference>();
        AddPersonRefsFromLists(refs, "Author",   byKey, "author",    "author_qid");
        AddPersonRefsFromLists(refs, "Narrator", byKey, "narrator",  "narrator_qid");
        AddPersonRefsFromLists(refs, "Narrator", byKey, "performer", "performer_qid");
        AddPersonRefsFromLists(refs, "Director", byKey, "director",  "director_qid");

        // Mark author refs as collective pseudonyms when the adapter flagged it.
        // This prevents person enrichment from looking up the pen name on Wikidata
        // (which would return one of the co-authors instead of the pen name entity).
        // NOTE: We mark ALL existing author refs (regardless of whether they have a QID)
        // because the pen name QID resolution may have succeeded, giving the pen name
        // author a QID. The real co-authors are added AFTER this block via
        // collective_members_qid and are therefore unaffected.
        if (byKey.TryGetValue("author_is_collective_pseudonym", out var pseudoFlags)
            && pseudoFlags.Any(f => string.Equals(f, "true", StringComparison.OrdinalIgnoreCase)))
        {
            for (int i = 0; i < refs.Count; i++)
            {
                if (string.Equals(refs[i].Role, "Author", StringComparison.OrdinalIgnoreCase))
                {
                    refs[i] = refs[i] with { IsCollectivePseudonym = true };
                }
            }
        }

        // Collective pseudonym constituent members.
        if (byKey.TryGetValue("collective_members_qid", out var collectiveValues))
        {
            foreach (var segment in collectiveValues)
            {
                var colonIndex = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    var qid   = segment[..colonIndex].Trim();
                    var label = segment[(colonIndex + 2)..].Trim();
                    if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(qid))
                    {
                        // Skip if this QID or name already exists in the refs list.
                        // This prevents the pen name entity (e.g. Q6142591 "James S. A. Corey")
                        // from being re-added as a non-pseudonym when it's already marked as one.
                        var alreadyPresent = refs.Any(r =>
                            string.Equals(r.WikidataQid, qid, StringComparison.OrdinalIgnoreCase)
                            || (r.IsCollectivePseudonym && string.Equals(r.Name, label, StringComparison.OrdinalIgnoreCase)));
                        if (!alreadyPresent)
                            refs.Add(new PersonReference("Author", label, qid));
                    }
                }
            }
        }

        // QID-first: only emit references with a confirmed Wikidata QID.
        // Name-only references (from processor metadata before Wikidata match)
        // are dropped — Person records require a verified identity.
        // Deduplicate by (Role, QID).
        return refs
            .Where(r => !string.IsNullOrEmpty(r.WikidataQid))
            .GroupBy(r => (r.Role, Key: r.WikidataQid!),
                     new RoleKeyComparer())
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Builds person references from a list-based claim accumulator.
    /// Pairs name claims with QID counterparts by index (they are emitted in matching order
    /// by <c>ExtensionToClaims</c>).
    /// </summary>
    private static void AddPersonRefsFromLists(
        List<PersonReference> refs,
        string role,
        Dictionary<string, List<string>> byKey,
        string nameKey,
        string qidKey)
    {
        if (!byKey.TryGetValue(nameKey, out var names))
            return;

        byKey.TryGetValue(qidKey, out var qids);

        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string? qid = null;
            if (qids is not null && i < qids.Count)
            {
                var segment = qids[i];
                var colonIdx = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIdx > 0)
                    qid = segment[..colonIdx].Trim();
                else if (!string.IsNullOrWhiteSpace(segment))
                    qid = segment.Trim();
            }

            refs.Add(new PersonReference(role, name, string.IsNullOrEmpty(qid) ? null : qid));
        }
    }

    /// <summary>Comparer for deduplicating person references by (Role, Key) tuple.</summary>
    private sealed class RoleKeyComparer : IEqualityComparer<(string Role, string Key)>
    {
        public bool Equals((string Role, string Key) x, (string Role, string Key) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Role, y.Role) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Key, y.Key);

        public int GetHashCode((string Role, string Key) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Role),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
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
        AddPersonRefs(refs, "Narrator", canonicals, "performer", "performer_qid");
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

        // QID-first: only emit references with a confirmed Wikidata QID.
        // Deduplicate by (Role, QID).
        return refs
            .Where(r => !string.IsNullOrEmpty(r.WikidataQid))
            .GroupBy(r => (r.Role, Key: r.WikidataQid!),
                     new RoleKeyComparer())
            .Select(g => g.First())
            .ToList();
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
        var key = provider.Name;

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
        HydrationPass effectivePass = HydrationPass.Quick)
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
            Isbn          = NormalizeIsbnHint(h.GetValueOrDefault("isbn")),
            AppleBooksId  = h.GetValueOrDefault("apple_books_id"),
            AudibleId     = h.GetValueOrDefault("audible_id"),
            TmdbId        = h.GetValueOrDefault("tmdb_id"),
            ImdbId        = h.GetValueOrDefault("imdb_id"),
            PersonName     = h.GetValueOrDefault("name"),
            PersonRole     = h.GetValueOrDefault("role"),
            PreResolvedQid = request.PreResolvedQid,
            BaseUrl        = baseUrl,
            SparqlBaseUrl  = null,
            Language       = language,
            Country        = country,
            HydrationPass  = effectivePass,
        };
    }

    // ── Hub Intelligence ──────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a Work to a Hub based on Wikidata relationship properties.
    /// Path A (QID confirmed): uses franchise/series/universe QIDs for firm linking.
    /// Path B (QID pending): falls back to text-based provisional matching.
    /// </summary>
#pragma warning disable CS0612
    [Obsolete("Deferred to Pass 2 Universe work — hub creation disabled during ingestion")]
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
#pragma warning restore CS0612

    /// <summary>
    /// Creates a singleton Hub for a standalone work, or reuses an existing Hub
    /// with the same display name (case-insensitive).  Safe to call idempotently.
    /// </summary>
#pragma warning disable CS0612
    [Obsolete("Deferred to Pass 2 Universe work — hub creation disabled during ingestion")]
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
#pragma warning restore CS0612

    /// <summary>
    /// Path A: firm Hub assignment using Wikidata relationship QIDs.
    /// Searches Tier 1 (franchise, series, fictional_universe) first,
    /// then Tier 2 (based_on, preceded_by/followed_by).
    /// </summary>
#pragma warning disable CS0612
    [Obsolete("Deferred to Pass 2 Universe work — hub creation disabled during ingestion")]
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
            // Guard against non-ASCII labels (e.g. Amharic returned by reconci.link quirk):
            // fall back to the bare QID so the Hub name is at least machine-readable.
            var rawLabel = bestRel.RelLabel ?? bestRel.RelQid;
            var displayName = rawLabel.Any(c => c > 127) ? bestRel.RelQid : rawLabel;

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
#pragma warning restore CS0612

    /// <summary>
    /// Extracts relationship QIDs from claims (franchise, series, fictional_universe,
    /// based_on, preceded_by, followed_by). Groups by relationship type.
    /// </summary>
#pragma warning disable CS0612
    [Obsolete("Deferred to Pass 2 Universe work — hub creation disabled during ingestion")]
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
#pragma warning restore CS0612

    /// <summary>Maps preceded_by/followed_by to the narrative_chain rel_type.</summary>
    private static string MapToNarrativeChain(string relType)
        => relType is "preceded_by" or "followed_by" ? "narrative_chain" : relType;

    // ── Cover art download ───────────────────────────────────────────────────

    /// <summary>
    /// Downloads cover art from a provider-supplied URL and saves it as
    /// <c>cover.jpg</c> in the media file's directory.  Always overwrites
    /// an existing cover (e.g. EPUB-embedded) with the provider image,
    /// since the ISBN-matched provider cover is edition-correct.
    /// Skips if no cover URL canonical value exists.
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
                File.Copy(cached, coverPath, overwrite: true);
            }
            else
            {
                await File.WriteAllBytesAsync(coverPath, bytes, ct).ConfigureAwait(false);
                await _imageCache.InsertAsync(hash, coverPath, coverUrl, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Cover art downloaded for asset {Id} from {Url}",
                assetId, coverUrl);

            // Write cover_url canonical so the Registry listing query and Dashboard
            // cards can display the thumbnail.  During initial ingestion this value
            // is only set when the file has embedded cover art (IngestionEngine.cs).
            // For files without embedded art (e.g. MP3 audiobooks), this is the
            // first opportunity to set it — after the provider cover image is on disk.
            await _canonicalRepo.UpsertBatchAsync(
            [
                new CanonicalValue
                {
                    EntityId     = assetId,
                    Key          = "cover_url",
                    Value        = $"/stream/{assetId}/cover",
                    LastScoredAt = DateTimeOffset.UtcNow,
                },
            ], ct).ConfigureAwait(false);

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

    // ── Batch Counter Adjustments ──────────────────────────────────────────────

    /// <summary>
    /// Shifts one file from Registered → Review in the batch counters.
    /// Called when hydration creates a review item for an already-counted file.
    /// </summary>
    private async Task AdjustBatchForReviewAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is null) return;
        try
        {
            var batch = await _batchRepo.GetByIdAsync(batchId.Value, ct).ConfigureAwait(false);
            if (batch is null || batch.FilesRegistered <= 0) return;

            await _batchRepo.UpdateCountsAsync(
                batchId.Value,
                batch.FilesTotal,
                batch.FilesProcessed,
                batch.FilesRegistered - 1,
                batch.FilesReview + 1,
                batch.FilesNoMatch,
                batch.FilesFailed,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch review adjustment (reg→review) failed for {BatchId}", batchId);
        }
    }

    /// <summary>
    /// Shifts one file from Review → Registered in the batch counters.
    /// Called when hydration auto-resolves a review item.
    /// </summary>
    private async Task AdjustBatchForResolveAsync(Guid? batchId, CancellationToken ct)
    {
        if (batchId is null) return;
        try
        {
            var batch = await _batchRepo.GetByIdAsync(batchId.Value, ct).ConfigureAwait(false);
            if (batch is null || batch.FilesReview <= 0) return;

            await _batchRepo.UpdateCountsAsync(
                batchId.Value,
                batch.FilesTotal,
                batch.FilesProcessed,
                batch.FilesRegistered + 1,
                batch.FilesReview - 1,
                batch.FilesNoMatch,
                batch.FilesFailed,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Batch resolve adjustment (review→reg) failed for {BatchId}", batchId);
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
