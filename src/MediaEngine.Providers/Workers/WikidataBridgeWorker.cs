using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Stage 2: Wikidata Bridge Resolution.
/// Leases jobs in <see cref="IdentityJobState.RetailMatched"/> or
/// <see cref="IdentityJobState.RetailMatchedNeedsReview"/> state.
/// Never processes <see cref="IdentityJobState.RetailNoMatch"/> — the strict retail gate.
///
/// Uses bridge IDs from Stage 1 to find the canonical Wikidata entity (QID).
/// Falls back to text reconciliation when bridge IDs don't resolve.
///
/// This is a plain service — the Api layer wraps it in a <c>BackgroundService</c>.
/// </summary>
public sealed class WikidataBridgeWorker
{
    private readonly IIdentityJobRepository _jobRepo;
    private readonly IWikidataCandidateRepository _candidateRepo;
    private readonly StageOutcomeFactory _outcomeFactory;
    private readonly TimelineRecorder _timeline;
    private readonly BridgeIdHelper _bridgeIdHelper;
    private readonly IEnumerable<IExternalMetadataProvider> _providers;
    private readonly IBridgeIdRepository _bridgeIdRepo;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IScoringEngine _scoringEngine;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<WikidataBridgeWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private const int BatchSize = 5;

    public WikidataBridgeWorker(
        IIdentityJobRepository jobRepo,
        IWikidataCandidateRepository candidateRepo,
        StageOutcomeFactory outcomeFactory,
        TimelineRecorder timeline,
        BridgeIdHelper bridgeIdHelper,
        IEnumerable<IExternalMetadataProvider> providers,
        IBridgeIdRepository bridgeIdRepo,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        ILogger<WikidataBridgeWorker> logger)
    {
        _jobRepo = jobRepo;
        _candidateRepo = candidateRepo;
        _outcomeFactory = outcomeFactory;
        _timeline = timeline;
        _bridgeIdHelper = bridgeIdHelper;
        _providers = providers;
        _bridgeIdRepo = bridgeIdRepo;
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _scoringEngine = scoringEngine;
        _configLoader = configLoader;
        _logger = logger;
    }

    /// <summary>
    /// Polls for <see cref="IdentityJobState.RetailMatched"/> and
    /// <see cref="IdentityJobState.RetailMatchedNeedsReview"/> jobs.
    /// Returns the number of jobs processed.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        // Strict retail gate: only RetailMatched or RetailMatchedNeedsReview.
        // RetailNoMatch is NEVER included — enforced at the SQL level.
        var jobs = await _jobRepo.LeaseNextAsync(
            "WikidataBridgeWorker",
            [IdentityJobState.RetailMatched, IdentityJobState.RetailMatchedNeedsReview],
            BatchSize,
            LeaseDuration,
            ct);

        foreach (var job in jobs)
        {
            try
            {
                await ProcessJobAsync(job, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "WikidataBridgeWorker failed for job {JobId}", job.Id);
                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }

        return jobs.Count;
    }

    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.BridgeSearching, ct: ct);

        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType))
            mediaType = MediaType.Unknown;

        var reconAdapter = _providers
            .OfType<ReconciliationAdapter>()
            .FirstOrDefault();

        if (reconAdapter is null)
        {
            _logger.LogWarning("No ReconciliationAdapter available — cannot resolve bridge IDs");
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidNoMatch,
                "No reconciliation adapter configured", ct);
            return;
        }

        // Load bridge IDs from Stage 1
        var bridgeIds = await _bridgeIdRepo.GetByEntityAsync(job.EntityId, ct);
        var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);

        var bridgeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wikidataProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bridge in bridgeIds)
        {
            bridgeDict.TryAdd(bridge.IdType, bridge.IdValue);

            var pCode = _bridgeIdHelper.GetPCode(bridge.IdType);
            if (pCode is not null)
            {
                // Media-type aware: TMDB uses P4947 (movies) or P4983 (TV)
                if (string.Equals(bridge.IdType, BridgeIdKeys.TmdbId, StringComparison.OrdinalIgnoreCase)
                    && mediaType == MediaType.TV)
                {
                    pCode = "P4983";
                }
                wikidataProps.TryAdd(bridge.IdType, pCode);
            }
        }

        // Add sentinels for text reconciliation fallback
        var titleHint = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                StringComparison.OrdinalIgnoreCase))?.Value;
        var authorHint = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Author,
                StringComparison.OrdinalIgnoreCase))?.Value;

        BridgeIdHelper.InjectSentinels(bridgeDict, titleHint, authorHint);

        var allCandidates = new List<WikidataBridgeCandidate>();
        string? resolvedQid = null;

        // Try bridge resolution
        if (bridgeIds.Count > 0)
        {
            try
            {
                var isEditionAware = mediaType is MediaType.Books or MediaType.Audiobooks or MediaType.Music;
                var bridgeResult = await reconAdapter.ResolveBridgeAsync(
                    bridgeDict, wikidataProps, mediaType, isEditionAware, ct);

                if (bridgeResult.Found)
                {
                    resolvedQid = bridgeResult.WorkQid ?? bridgeResult.Qid;

                    var candidate = new WikidataBridgeCandidate
                    {
                        JobId = job.Id,
                        Qid = resolvedQid!,
                        Label = titleHint ?? resolvedQid!,
                        MatchedBy = "bridge_id",
                        BridgeIdType = bridgeIds.FirstOrDefault()?.IdType,
                        IsExactMatch = true,
                        ScoreTotal = 1.0,
                        Outcome = "AutoAccepted",
                    };
                    allCandidates.Add(candidate);

                    // Persist claims from bridge resolution
                    if (bridgeResult.Claims.Count > 0)
                    {
                        await ScoringHelper.PersistClaimsAndScoreAsync(
                            job.EntityId, bridgeResult.Claims, reconAdapter.ProviderId,
                            _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                            logger: _logger);
                    }

                    // Persist collected bridge IDs back
                    if (bridgeResult.CollectedBridgeIds.Count > 0)
                    {
                        var collectedEntries = bridgeResult.CollectedBridgeIds
                            .Select(kvp => new BridgeIdEntry
                            {
                                EntityId = job.EntityId,
                                IdType = _bridgeIdHelper.GetClaimKey(kvp.Key),
                                IdValue = kvp.Value,
                                ProviderId = reconAdapter.ProviderId.ToString(),
                                WikidataProperty = kvp.Key,
                            }).ToList();

                        await _bridgeIdRepo.UpsertBatchAsync(collectedEntries, ct);
                    }

                    await _timeline.RecordBridgeResolvedAsync(
                        job.EntityId, resolvedQid!,
                        bridgeIds.FirstOrDefault()?.IdType ?? "bridge_id",
                        job.IngestionRunId, ct);

                    _logger.LogInformation(
                        "Bridge resolution succeeded for entity {EntityId}: QID {Qid} via {BridgeType}",
                        job.EntityId, resolvedQid, bridgeIds.FirstOrDefault()?.IdType);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Bridge resolution failed for entity {EntityId}", job.EntityId);
            }
        }

        // Text reconciliation fallback
        if (resolvedQid is null && !string.IsNullOrWhiteSpace(titleHint))
        {
            try
            {
                var fallbackClaims = await reconAdapter.FetchAsync(
                    new ProviderLookupRequest
                    {
                        EntityId = job.EntityId,
                        EntityType = EntityType.MediaAsset,
                        MediaType = mediaType,
                        Title = titleHint,
                        Author = authorHint,
                    }, ct);

                if (fallbackClaims.Count > 0)
                {
                    var fallbackQidClaim = fallbackClaims
                        .FirstOrDefault(c => string.Equals(c.Key, BridgeIdKeys.WikidataQid,
                            StringComparison.OrdinalIgnoreCase));

                    if (fallbackQidClaim is not null && !string.IsNullOrWhiteSpace(fallbackQidClaim.Value))
                    {
                        resolvedQid = fallbackQidClaim.Value;

                        var candidate = new WikidataBridgeCandidate
                        {
                            JobId = job.Id,
                            Qid = resolvedQid,
                            Label = titleHint,
                            MatchedBy = "text_reconciliation",
                            IsExactMatch = false,
                            ScoreTotal = 0.75,
                            Outcome = "AutoAccepted",
                        };
                        allCandidates.Add(candidate);

                        await ScoringHelper.PersistClaimsAndScoreAsync(
                            job.EntityId, fallbackClaims, reconAdapter.ProviderId,
                            _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                            logger: _logger);

                        await _timeline.RecordTitleFallbackResolvedAsync(
                            job.EntityId, resolvedQid, job.IngestionRunId, ct);

                        _logger.LogInformation(
                            "Text reconciliation resolved entity {EntityId} to QID {Qid}",
                            job.EntityId, resolvedQid);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Text reconciliation failed for entity {EntityId}", job.EntityId);
            }
        }

        // Persist ALL candidates
        if (allCandidates.Count > 0)
            await _candidateRepo.InsertBatchAsync(allCandidates, ct);

        // Set final job state
        if (resolvedQid is not null)
        {
            await _jobRepo.SetResolvedQidAsync(job.Id, resolvedQid, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidResolved, ct: ct);

            // Fetch full properties now that we have a QID
            try
            {
                var fullClaims = await reconAdapter.FetchAsync(
                    new ProviderLookupRequest
                    {
                        EntityId = job.EntityId,
                        EntityType = EntityType.MediaAsset,
                        MediaType = mediaType,
                        Title = titleHint,
                        PreResolvedQid = resolvedQid,
                    }, ct);

                if (fullClaims.Count > 0)
                {
                    await ScoringHelper.PersistClaimsAndScoreAsync(
                        job.EntityId, fullClaims, reconAdapter.ProviderId,
                        _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                        logger: _logger);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Full property fetch failed for QID {Qid} (entity {EntityId})",
                    resolvedQid, job.EntityId);
            }
        }
        else
        {
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidNoMatch, ct: ct);
            await _outcomeFactory.CreateWikidataBridgeFailedAsync(
                job.EntityId,
                $"No Wikidata match for {mediaType} — {bridgeIds.Count} bridge IDs tried",
                job.IngestionRunId, null, ct);
            await _timeline.RecordBridgeNoMatchAsync(
                job.EntityId, job.IngestionRunId, ct);

            _logger.LogInformation(
                "No Wikidata match for entity {EntityId} — {BridgeCount} bridge IDs tried",
                job.EntityId, bridgeIds.Count);
        }
    }

    /// <summary>
    /// Fetches full Wikidata properties for an already-resolved QID and persists
    /// claims + canonical values. Called by the synchronous pipeline when the
    /// user manually selects a QID (bypassing normal Stage 2 resolution).
    /// </summary>
    internal async Task FetchAndPersistPropertiesAsync(
        Guid entityId, string qid, string mediaTypeStr, CancellationToken ct)
    {
        if (!Enum.TryParse<MediaType>(mediaTypeStr, true, out var mediaType))
            mediaType = MediaType.Unknown;

        var reconAdapter = _providers
            .OfType<ReconciliationAdapter>()
            .FirstOrDefault();

        if (reconAdapter is null)
        {
            _logger.LogWarning("No ReconciliationAdapter available — cannot fetch properties for QID {Qid}", qid);
            return;
        }

        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);
        var titleHint = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                StringComparison.OrdinalIgnoreCase))?.Value;

        try
        {
            var fullClaims = await reconAdapter.FetchAsync(
                new ProviderLookupRequest
                {
                    EntityId = entityId,
                    EntityType = EntityType.MediaAsset,
                    MediaType = mediaType,
                    Title = titleHint,
                    PreResolvedQid = qid,
                }, ct);

            if (fullClaims.Count > 0)
            {
                await ScoringHelper.PersistClaimsAndScoreAsync(
                    entityId, fullClaims, reconAdapter.ProviderId,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                    logger: _logger);
            }

            _logger.LogInformation(
                "Fetched {Count} Wikidata properties for QID {Qid} (entity {EntityId})",
                fullClaims.Count, qid, entityId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Full property fetch failed for QID {Qid} (entity {EntityId})",
                qid, entityId);
        }
    }
}
