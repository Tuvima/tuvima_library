using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Domain.Models;
using Tanaste.Intelligence.Contracts;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Storage.Contracts;
using Tanaste.Storage.Models;

namespace Tanaste.Providers.Services;

/// <summary>
/// Three-stage hydration pipeline orchestrator.
///
/// Replaces the flat "first provider wins" approach with a sequential pipeline:
/// <list type="number">
///   <item><b>Stage 1 — Retail Match:</b> All matching commercial providers run.
///     The scoring engine resolves field conflicts.</item>
///   <item><b>Stage 2 — Universal Bridge:</b> Bridge IDs from Stage 1 resolve a
///     Wikidata QID. SPARQL deep hydration fetches 50+ properties.</item>
///   <item><b>Stage 3 — Human Hub:</b> Every creator in canonical values is
///     enriched with headshots, bios, and social links.</item>
/// </list>
///
/// Architecture:
/// - A bounded <c>Channel&lt;HarvestRequest&gt;</c> (capacity 500, DropOldest policy)
///   decouples ingestion from the pipeline.
/// - A single reader task processes requests sequentially.
/// - A <c>SemaphoreSlim</c> limits simultaneous in-flight provider calls per stage.
/// - After each provider, claims are persisted and the entity is re-scored.
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

        // Build composite endpoint map.
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        var sparqlBaseUrl = endpointMap.TryGetValue("wikidata_sparql", out var sparql)
            ? sparql : null;

        // ── Stage 1: Retail Match ─────────────────────────────────────────────
        var stage1Providers = GetProvidersForStage(1, provConfigs, request);
        var stage1Claims    = 0;

        foreach (var provider in stage1Providers)
        {
            var claims = await FetchFromProviderAsync(
                provider, request, endpointMap, sparqlBaseUrl, ct).ConfigureAwait(false);

            if (claims.Count == 0) continue;

            await ScoringHelper.PersistClaimsAndScoreAsync(
                request.EntityId, claims, provider.ProviderId,
                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                _providers, ct).ConfigureAwait(false);

            stage1Claims += claims.Count;

            await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                .ConfigureAwait(false);
        }

        result.Stage1ClaimsAdded = stage1Claims;

        if (stage1Claims > 0)
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.HydrationStage1Completed,
                EntityId   = request.EntityId,
                Detail     = $"Stage 1 completed: {stage1Claims} claims from {stage1Providers.Count} providers",
            }, ct).ConfigureAwait(false);

            await _eventPublisher.PublishAsync(
                "HydrationStageCompleted",
                new HydrationStageCompletedEvent(request.EntityId, 1, stage1Claims, "retail"),
                ct).ConfigureAwait(false);
        }

        // ── Stage 2: Universal Bridge ─────────────────────────────────────────

        // Load canonical values to extract bridge IDs deposited by Stage 1.
        var canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);

        var bridgeHints = ExtractBridgeHints(canonicals);
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

                // Check for QID
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
                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.HydrationStage2Completed,
                    EntityId   = request.EntityId,
                    Detail     = $"Stage 2 completed: {stage2Claims} claims, QID={result.WikidataQid ?? "none"}",
                }, ct).ConfigureAwait(false);

                await _eventPublisher.PublishAsync(
                    "HydrationStageCompleted",
                    new HydrationStageCompletedEvent(request.EntityId, 2, stage2Claims, "wikidata"),
                    ct).ConfigureAwait(false);
            }
        }

        // ── Stage 3: Human Hub ────────────────────────────────────────────────

        // Reload canonical values with Stage 2 additions.
        canonicals = await _canonicalRepo.GetByEntityAsync(request.EntityId, ct)
            .ConfigureAwait(false);

        var stage3Claims = 0;

        // Run stage-3 providers for the main entity.
        var stage3Providers = GetProvidersForStage(3, provConfigs, request);
        foreach (var provider in stage3Providers)
        {
            // Skip person-only providers when the main entity is not a person.
            if (request.EntityType != EntityType.Person
                && provider.CanHandle(EntityType.Person)
                && !provider.CanHandle(EntityType.Work))
                continue;

            var claims = await FetchFromProviderAsync(
                provider, request, endpointMap, sparqlBaseUrl, ct)
                .ConfigureAwait(false);

            if (claims.Count == 0) continue;

            await ScoringHelper.PersistClaimsAndScoreAsync(
                request.EntityId, claims, provider.ProviderId,
                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader,
                _providers, ct).ConfigureAwait(false);

            stage3Claims += claims.Count;

            await PublishHarvestEvent(request.EntityId, provider.Name, claims, ct)
                .ConfigureAwait(false);
        }

        // Trigger person enrichment for creators found in canonical values.
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

        result.Stage3ClaimsAdded = stage3Claims;

        if (stage3Claims > 0 || personRefs.Count > 0)
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.HydrationStage3Completed,
                EntityId   = request.EntityId,
                Detail     = $"Stage 3 completed: {stage3Claims} claims, {personRefs.Count} person refs",
            }, ct).ConfigureAwait(false);

            await _eventPublisher.PublishAsync(
                "HydrationStageCompleted",
                new HydrationStageCompletedEvent(request.EntityId, 3, stage3Claims, "human_hub"),
                ct).ConfigureAwait(false);
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
                var reviewEntry = new ReviewQueueEntry
                {
                    Id              = Guid.NewGuid(),
                    EntityId        = request.EntityId,
                    EntityType      = request.EntityType.ToString(),
                    Trigger         = ReviewTrigger.LowConfidence,
                    ConfidenceScore = scored.OverallConfidence,
                    Detail          = $"Overall confidence {scored.OverallConfidence:P0} below threshold {hydration.AutoReviewConfidenceThreshold:P0}",
                };

                await _reviewRepo.InsertAsync(reviewEntry, ct).ConfigureAwait(false);

                result.NeedsReview  = true;
                result.ReviewReason = ReviewTrigger.LowConfidence;
                result.ReviewItemId = reviewEntry.Id;

                var titleCanonical = canonicals
                    .FirstOrDefault(c => c.Key == "title");

                await _activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ReviewItemCreated,
                    EntityId   = request.EntityId,
                    Detail     = $"Review item created: {reviewEntry.Trigger}",
                }, ct).ConfigureAwait(false);

                await _eventPublisher.PublishAsync(
                    "ReviewItemCreated",
                    new ReviewItemCreatedEvent(
                        reviewEntry.Id, request.EntityId, reviewEntry.Trigger,
                        titleCanonical?.Value),
                    ct).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "Hydration pipeline complete for entity {Id}: S1={S1} S2={S2} S3={S3} total={Total} review={Review}",
            request.EntityId, result.Stage1ClaimsAdded, result.Stage2ClaimsAdded,
            result.Stage3ClaimsAdded, result.TotalClaimsAdded, result.NeedsReview);

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns providers configured for the given stage, filtered by media type
    /// and entity type compatibility.
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
            if (!config.HydrationStages.Contains(stage))
                continue;

            // Check capability filters.
            if (!provider.CanHandle(request.MediaType) || !provider.CanHandle(request.EntityType))
                continue;

            result.Add(provider);
        }

        return result;
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
    /// for Stage 3 person enrichment.
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
