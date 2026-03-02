using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Domain.Models;
using Tanaste.Intelligence.Contracts;
using Tanaste.Intelligence.Models;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Storage.Contracts;

namespace Tanaste.Providers.Services;

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
    private readonly IScoringEngine _scoringEngine;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<MetadataHarvestingService> _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MetadataHarvestingService(
        IEnumerable<IExternalMetadataProvider> providers,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IPersonRepository personRepo,
        IScoringEngine scoringEngine,
        IEventPublisher eventPublisher,
        IConfigurationLoader configLoader,
        ILogger<MetadataHarvestingService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(claimRepo);
        ArgumentNullException.ThrowIfNull(canonicalRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(scoringEngine);
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);

        _providers      = providers.ToList();
        _claimRepo      = claimRepo;
        _canonicalRepo  = canonicalRepo;
        _personRepo     = personRepo;
        _scoringEngine  = scoringEngine;
        _eventPublisher = eventPublisher;
        _configLoader   = configLoader;
        _logger         = logger;

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

    private async Task HandlePersonEnrichmentAsync(
        HarvestRequest request,
        IReadOnlyList<ProviderClaim> claims,
        IExternalMetadataProvider provider,
        CancellationToken ct)
    {
        // Only Wikidata produces person-enrichment claims.
        if (!string.Equals(provider.Name, "wikidata", StringComparison.OrdinalIgnoreCase))
            return;

        var qid        = claims.FirstOrDefault(c => c.Key == "wikidata_qid")?.Value;
        var headshotUrl = claims.FirstOrDefault(c => c.Key == "headshot_url")?.Value;
        var biography  = claims.FirstOrDefault(c => c.Key == "biography")?.Value;

        if (qid is null && headshotUrl is null && biography is null)
            return;

        await _personRepo.UpdateEnrichmentAsync(request.EntityId, qid, headshotUrl, biography, ct)
            .ConfigureAwait(false);

        // Look up the name for the event payload.
        var persons = await _personRepo.GetByMediaAssetAsync(Guid.Empty, ct).ConfigureAwait(false);
        // Note: GetByMediaAssetAsync with Empty GUID returns 0 results — use a direct person lookup.
        // We'll publish with whatever info we have from the claims.
        await _eventPublisher.PublishAsync(
            "PersonEnriched",
            new PersonEnrichedEvent(request.EntityId, string.Empty, headshotUrl, qid),
            ct).ConfigureAwait(false);
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
        };
    }

    private (IReadOnlyDictionary<Guid, double> Weights,
             IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, double>>? FieldWeights)
        BuildWeightMaps(IReadOnlyList<Tanaste.Storage.Models.ProviderConfiguration> providerConfigs)
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
