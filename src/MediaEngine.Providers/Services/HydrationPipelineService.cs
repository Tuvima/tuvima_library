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
/// Two-stage hydration pipeline orchestrator.
///
/// <list type="number">
///   <item><b>Stage 1 — Content Match:</b> Runs only the <b>primary</b> provider
///     from <c>config/slots.json</c> for the file's media type. If the primary
///     provider returns no results, a <see cref="ReviewTrigger.ContentMatchFailed"/>
///     review item is created.</item>
///   <item><b>Stage 2 — Universe Match:</b> Bridge IDs from Stage 1 resolve a
///     Wikidata QID. SPARQL deep hydration fetches 50+ properties. Person
///     enrichment runs as a sub-step of this stage.</item>
/// </list>
///
/// Architecture:
/// - A bounded <c>Channel&lt;HarvestRequest&gt;</c> (capacity 500, DropOldest policy)
///   decouples ingestion from the pipeline.
/// - A single reader task processes requests sequentially.
/// - After the primary provider, claims are persisted and the entity is re-scored.
/// - If Stage 2 encounters multiple QID candidates, a review queue entry is created.
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
    private readonly IReviewQueueRepository _reviewRepo;
    private readonly IHubRepository _hubRepo;
    private readonly IMediaAssetRepository _assetRepo;
    private readonly IImageCacheRepository _imageCache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IWriteBackService _writeBack;
    private readonly IAutoOrganizeService _autoOrganize;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IHeroBannerGenerator _heroGenerator;
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
        IReviewQueueRepository reviewRepo,
        IHubRepository hubRepo,
        IMediaAssetRepository assetRepo,
        IImageCacheRepository imageCache,
        IHttpClientFactory httpFactory,
        IWriteBackService writeBack,
        IAutoOrganizeService autoOrganize,
        ISystemActivityRepository activityRepo,
        IHeroBannerGenerator heroGenerator,
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
        ArgumentNullException.ThrowIfNull(reviewRepo);
        ArgumentNullException.ThrowIfNull(hubRepo);
        ArgumentNullException.ThrowIfNull(assetRepo);
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(writeBack);
        ArgumentNullException.ThrowIfNull(autoOrganize);
        ArgumentNullException.ThrowIfNull(activityRepo);
        ArgumentNullException.ThrowIfNull(heroGenerator);
        ArgumentNullException.ThrowIfNull(logger);

        _providers      = providers.ToList();
        _claimRepo      = claimRepo;
        _canonicalRepo  = canonicalRepo;
        _personRepo     = personRepo;
        _scoringEngine  = scoringEngine;
        _eventPublisher = eventPublisher;
        _configLoader   = configLoader;
        _identity       = identity;
        _reviewRepo     = reviewRepo;
        _hubRepo        = hubRepo;
        _assetRepo      = assetRepo;
        _imageCache     = imageCache;
        _httpFactory     = httpFactory;
        _writeBack      = writeBack;
        _autoOrganize   = autoOrganize;
        _activityRepo   = activityRepo;
        _heroGenerator  = heroGenerator;
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

        // ── Stage 1: Content Match ────────────────────────────────────────────
        //
        // Runs ONLY the primary provider from slots.json for this media type.
        // Secondary/tertiary are reserved for manual querying via the search API.

        var primaryProvider = ResolvePrimaryProvider(slots, provConfigs, request);
        var stage1Claims    = 0;

        var titleHint = request.Hints.GetValueOrDefault("title", "(unknown)");
        _logger.LogInformation(
            "Pipeline Stage 1 starting for entity {Id} — title: \"{Title}\", media type: {MediaType}, provider: {Provider}",
            request.EntityId, titleHint, request.MediaType,
            primaryProvider?.Name ?? "(none)");

        if (primaryProvider is not null)
        {
            var claims = await FetchFromProviderAsync(
                primaryProvider, request, endpointMap, sparqlBaseUrl, lang, country, ct).ConfigureAwait(false);

            if (claims.Count > 0)
            {
                await ScoringHelper.PersistClaimsAndScoreAsync(
                    request.EntityId, claims, primaryProvider.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                    _providers, ct).ConfigureAwait(false);

                stage1Claims = claims.Count;

                await PublishHarvestEvent(request.EntityId, primaryProvider.Name, claims, ct)
                    .ConfigureAwait(false);
            }
        }

        result.Stage1ClaimsAdded = stage1Claims;

        if (stage1Claims > 0)
        {
            await _eventPublisher.PublishAsync(
                "HydrationStageCompleted",
                new HydrationStageCompletedEvent(request.EntityId, 1, stage1Claims, "content_match"),
                ct).ConfigureAwait(false);

            // Post-hydration auto-resolve: if Stage 1 returned 3+ claims and this
            // entity has a pending AmbiguousMediaType review item, the provider match
            // confirms the media type — auto-resolve the review item.
            if (stage1Claims >= 3 && request.EntityType == EntityType.MediaAsset)
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
                            Detail     = $"AmbiguousMediaType auto-resolved: Stage 1 returned {stage1Claims} claims, confirming media type.",
                        }, ct).ConfigureAwait(false);

                        await _eventPublisher.PublishAsync("ReviewItemResolved", new
                        {
                            review_item_id = review.Id,
                            entity_id      = request.EntityId,
                            status         = "Resolved",
                        }, ct).ConfigureAwait(false);

                        _logger.LogInformation(
                            "AmbiguousMediaType review auto-resolved for entity {Id} — {Claims} claims confirmed type",
                            request.EntityId, stage1Claims);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to auto-resolve AmbiguousMediaType review for entity {Id}",
                        request.EntityId);
                }
            }

            // Write-back: write resolved metadata to the physical file after auto-match.
            // Skip on suppressed re-enqueue runs (cover-only) — the initial run
            // already wrote metadata.
            if (request.EntityType == EntityType.MediaAsset
                && !request.SuppressActivityEntry)
            {
                try
                {
                    await _writeBack.WriteMetadataAsync(request.EntityId, "auto_match", ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Write-back after Stage 1 failed for entity {Id}; continuing",
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
        else if (primaryProvider is not null)
        {
            _logger.LogWarning(
                "Pipeline Stage 1 produced no results for entity {Id} from provider '{Provider}'",
                request.EntityId, primaryProvider.Name);

            // Primary provider ran but returned no results → ContentMatchFailed.
            await CreateReviewItemAsync(
                request, ReviewTrigger.ContentMatchFailed, 0.0,
                $"Primary provider '{primaryProvider.Name}' returned no results for this {request.MediaType}",
                result, ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Pipeline Stage 1 skipped for entity {Id}: no primary provider configured for {MediaType}",
                request.EntityId, request.MediaType);
        }

        _logger.LogInformation(
            "Pipeline Stage 1 completed for entity {Id}: {ClaimCount} claims",
            request.EntityId, stage1Claims);

        // ── Stage 2: Universe Match ───────────────────────────────────────────
        //
        // 1. Extract bridge IDs from Stage 1 canonical values
        // 2. Try Wikidata bridge lookup (provider ID → QID)
        // 3. Fallback: title search on Wikidata
        // 4. If QID found → SPARQL deep hydration
        // 5. Person enrichment runs silently

        var canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);

        var bridgeHints  = ExtractBridgeHints(canonicals);
        var hasBridgeIds = bridgeHints.Count > 0;

        _logger.LogInformation(
            "Pipeline Stage 2 starting for entity {Id} — bridge IDs: [{BridgeKeys}], SPARQL URL: {HasSparql}",
            request.EntityId,
            hasBridgeIds ? string.Join(", ", bridgeHints.Keys) : "(none)",
            sparqlBaseUrl is not null ? "configured" : "MISSING");

        if (hydration.SkipStage2WithoutBridgeIds && !hasBridgeIds
            && string.IsNullOrEmpty(request.PreResolvedQid))
        {
            _logger.LogDebug(
                "Skipping Stage 2 for entity {Id} — no bridge IDs and no pre-resolved QID",
                request.EntityId);
        }
        else
        {
            var stage2Providers = GetProvidersForStage(2, provConfigs, request);
            var stage2Claims    = 0;

            // Enrich the request hints with bridge IDs from canonical values.
            var stage2Request = EnrichRequestWithBridgeHints(request, bridgeHints);

            foreach (var provider in stage2Providers)
            {
                var claims = await FetchFromProviderAsync(
                    provider, stage2Request, endpointMap, sparqlBaseUrl, lang, country, ct)
                    .ConfigureAwait(false);

                if (claims.Count == 0) continue;

                // Check for QID.
                var qidClaim = claims.FirstOrDefault(c => c.Key == "wikidata_qid");
                if (qidClaim is not null)
                {
                    result.WikidataQid = qidClaim.Value;
                }

                await ScoringHelper.PersistClaimsAndScoreAsync(
                    request.EntityId, claims, provider.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                    _providers, ct).ConfigureAwait(false);

                stage2Claims += claims.Count;

                await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                    .ConfigureAwait(false);
            }

            result.Stage2ClaimsAdded = stage2Claims;

            if (stage2Claims > 0)
            {
                await _eventPublisher.PublishAsync(
                    "HydrationStageCompleted",
                    new HydrationStageCompletedEvent(request.EntityId, 2, stage2Claims, "universe_match"),
                    ct).ConfigureAwait(false);

                // Hub Intelligence: assign Work to a Hub based on Wikidata relationships.
                var qidConfirmed = result.WikidataQid is not null;
                await RunHubIntelligenceAsync(request.EntityId, qidConfirmed, ct)
                    .ConfigureAwait(false);

                // Write-back: write universe-enriched metadata to the physical file.
                // Skip on suppressed re-enqueue runs — the initial run already wrote.
                if (request.EntityType == EntityType.MediaAsset
                    && !request.SuppressActivityEntry)
                {
                    try
                    {
                        await _writeBack.WriteMetadataAsync(request.EntityId, "universe_enrichment", ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Write-back after Stage 2 failed for entity {Id}; continuing",
                            request.EntityId);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(request.PreResolvedQid))
            {
                // Pre-resolved QID was provided but no claims came back.
                _logger.LogDebug(
                    "Stage 2 for entity {Id} with pre-resolved QID '{Qid}' returned no claims",
                    request.EntityId, request.PreResolvedQid);
            }
            else if (hasBridgeIds)
            {
                // Bridge IDs were present (ASIN, ISBN, Apple Books ID, etc.) but every
                // lookup and the fallback title search all failed.  This is a genuine
                // unexpected failure that warrants user attention.
                await CreateReviewItemAsync(
                    request, ReviewTrigger.UniverseMatchFailed, 0.0,
                    "Bridge ID lookup and title search failed to resolve a Wikidata QID",
                    result, ct).ConfigureAwait(false);
            }
            else
            {
                // No bridge IDs available — title-only Wikidata search is inherently
                // unreliable (non-English titles, regional editions, obscure works).
                // Silently log the miss; the user can trigger manual hydration later
                // from the Hub detail page.  No review item is created.
                _logger.LogDebug(
                    "Stage 2: no Wikidata QID found for entity {Id} " +
                    "(title-only search, no bridge IDs — skipping review item)",
                    request.EntityId);
            }
        }

        // ── Person enrichment (runs as part of Stage 2) ──────────────────────

        // Reload canonical values with Stage 2 additions.
        canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);

        var personRefs = ExtractPersonReferences(canonicals);
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

        // ── Post-pipeline: confidence check ───────────────────────────────────

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
                // Auto-resolve any pending LowConfidence or ContentMatchFailed
                // review items and organize the file from staging into the library.
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
                    request.EntityId, scored.OverallConfidence, conflictedFields, ct)
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

        // Create a single consolidated MediaAdded activity entry at the very end.
        // Skip when the caller set SuppressActivityEntry (e.g. re-enqueue from
        // TryReorganizeExistingAsync for cover art download — the original
        // pipeline run already logged the MediaAdded entry).
        if (!request.SuppressActivityEntry)
        {
            await CreateMediaAddedEntryAsync(request.EntityId, result, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Hydration pipeline complete for entity {Id}: S1={S1} S2={S2} total={Total} review={Review}",
            request.EntityId, result.Stage1ClaimsAdded, result.Stage2ClaimsAdded,
            result.TotalClaimsAdded, result.NeedsReview);

        return result;
    }

    // ── Auto-resolve after hydration ─────────────────────────────────────────

    /// <summary>
    /// When hydration improves an entity's confidence above the auto-link
    /// threshold (0.85), auto-resolve any pending <see cref="ReviewTrigger.LowConfidence"/>
    /// or <see cref="ReviewTrigger.ContentMatchFailed"/> review items, then
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
                          or ReviewTrigger.UniverseMatchFailed).ToList();
            // UniverseMatchFailed is auto-resolved here because high confidence from
            // Stage 1 providers (Apple Books, Audnexus, etc.) is sufficient to
            // identify and organise the file — Wikidata matching is optional enrichment.

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
                await _autoOrganize.TryAutoOrganizeAsync(request.EntityId, ct)
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
    /// Returns providers configured for the given stage, filtered by media type
    /// and entity type compatibility. Used for Stage 2 (Universe Match) providers.
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
        Guid entityId, HydrationResult result, CancellationToken ct)
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

            // Resolve hub name from the work → hub chain via a single targeted query.
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
                else if (result.Stage1ClaimsAdded > 0)
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
                ActionType  = SystemActionType.MediaAdded,
                EntityId    = entityId,
                EntityType  = "MediaAsset",
                HubName     = hubName ?? title,
                ChangesJson = richJson,
                Detail      = $"Added — \"{title}\"{authorPart}",
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
    /// Shared by ContentMatchFailed, UniverseMatchFailed, and LowConfidence triggers.
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
        CancellationToken ct)
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
    /// Extracts person references (author, narrator) from canonical values
    /// for person enrichment (runs as part of Stage 2).
    /// </summary>
    private static IReadOnlyList<PersonReference> ExtractPersonReferences(
        IReadOnlyList<CanonicalValue> canonicals)
    {
        var refs = new List<PersonReference>();

        var author   = canonicals.FirstOrDefault(c => c.Key == "author")?.Value;
        var narrator = canonicals.FirstOrDefault(c => c.Key == "narrator")?.Value;

        if (!string.IsNullOrEmpty(author))
            refs.Add(new PersonReference("Author", author));
        if (!string.IsNullOrEmpty(narrator))
            refs.Add(new PersonReference("Narrator", narrator));

        return refs;
    }

    private static string ResolveBaseUrl(
        IExternalMetadataProvider provider,
        Dictionary<string, string> endpointMap)
    {
        var key = provider.Name switch
        {
            "wikidata" => "wikidata_api",
            _          => provider.Name,
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

        // Build a quick lookup for companion _qid claims (e.g. "franchise_qid" → ["Q937618", ...]).
        var qidLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in claims)
        {
            if (c.ClaimKey.EndsWith("_qid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.ClaimValue))
            {
                var baseKey = c.ClaimKey[..^4]; // "franchise_qid" → "franchise"
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
