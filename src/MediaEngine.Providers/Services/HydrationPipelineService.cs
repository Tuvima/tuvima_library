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
    private readonly IWriteBackService _writeBack;
    private readonly IAutoOrganizeService _autoOrganize;
    private readonly ISystemActivityRepository _activityRepo;
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
        IWriteBackService writeBack,
        IAutoOrganizeService autoOrganize,
        ISystemActivityRepository activityRepo,
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
        ArgumentNullException.ThrowIfNull(writeBack);
        ArgumentNullException.ThrowIfNull(autoOrganize);
        ArgumentNullException.ThrowIfNull(activityRepo);
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
        _writeBack      = writeBack;
        _autoOrganize   = autoOrganize;
        _activityRepo   = activityRepo;
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
        _channel.Writer.TryWrite(request);
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
                    _logger.LogWarning(ex,
                        "Unhandled error in hydration pipeline for entity {Id}",
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

        // Build composite endpoint map.
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        var sparqlBaseUrl = endpointMap.TryGetValue("wikidata_sparql", out var sparql)
            ? sparql : null;

        // ── Stage 1: Content Match ────────────────────────────────────────────
        //
        // Runs ONLY the primary provider from slots.json for this media type.
        // Secondary/tertiary are reserved for manual querying via the search API.

        var primaryProvider = ResolvePrimaryProvider(slots, provConfigs, request);
        var stage1Claims    = 0;

        if (primaryProvider is not null)
        {
            var claims = await FetchFromProviderAsync(
                primaryProvider, request, endpointMap, sparqlBaseUrl, ct).ConfigureAwait(false);

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
            if (request.EntityType == EntityType.MediaAsset)
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
        }
        else if (primaryProvider is not null)
        {
            // Primary provider ran but returned no results → ContentMatchFailed.
            await CreateReviewItemAsync(
                request, ReviewTrigger.ContentMatchFailed, 0.0,
                $"Primary provider '{primaryProvider.Name}' returned no results for this {request.MediaType}",
                result, ct).ConfigureAwait(false);
        }
        else
        {
            // No primary provider configured for this media type.
            _logger.LogDebug(
                "No primary provider configured for {MediaType}; skipping Stage 1 for entity {Id}",
                request.MediaType, request.EntityId);
        }

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
                    provider, stage2Request, endpointMap, sparqlBaseUrl, ct)
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

                // Write-back: write universe-enriched metadata to the physical file.
                if (request.EntityType == EntityType.MediaAsset)
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
                // Had bridge IDs but Wikidata didn't match → UniverseMatchFailed.
                await CreateReviewItemAsync(
                    request, ReviewTrigger.UniverseMatchFailed, 0.0,
                    "Bridge ID lookup and title search failed to resolve a Wikidata QID",
                    result, ct).ConfigureAwait(false);
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
                          or ReviewTrigger.ContentMatchFailed).ToList();

            if (resolvable.Count == 0)
                return;

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

            // Now attempt to organize the file from staging into the library.
            if (request.EntityType == EntityType.MediaAsset)
            {
                await _autoOrganize.TryAutoOrganizeAsync(request.EntityId, ct)
                    .ConfigureAwait(false);
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
    /// Creates a review queue entry and publishes events.
    /// Shared by ContentMatchFailed, UniverseMatchFailed, and LowConfidence triggers.
    /// </summary>
    private async Task CreateReviewItemAsync(
        HarvestRequest request,
        string trigger,
        double confidence,
        string detail,
        HydrationResult result,
        CancellationToken ct)
    {
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
    /// Fetches claims from a single provider, handling errors gracefully.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> FetchFromProviderAsync(
        IExternalMetadataProvider provider,
        HarvestRequest request,
        Dictionary<string, string> endpointMap,
        string? sparqlBaseUrl,
        CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl(provider, endpointMap);
        var lookupRequest = BuildLookupRequest(request, provider, baseUrl, sparqlBaseUrl);

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
        string[] bridgeKeys = ["isbn", "asin", "apple_books_id", "audible_id",
                               "tmdb_id", "imdb_id", "goodreads_id"];

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
        };
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
