using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Stage 1: Retail Identification.
/// Leases <see cref="IdentityJobState.Queued"/> jobs, runs retail providers
/// per the configured strategy, scores candidates, and persists evidence.
///
/// This is a plain service — the Api layer wraps it in a <c>BackgroundService</c>
/// for polling lifecycle management.
/// </summary>
public sealed class RetailMatchWorker
{
    private readonly IIdentityJobRepository _jobRepo;
    private readonly IRetailCandidateRepository _candidateRepo;
    private readonly StageOutcomeFactory _outcomeFactory;
    private readonly TimelineRecorder _timeline;
    private readonly Services.BatchProgressService _batchProgress;
    private readonly IEnumerable<IExternalMetadataProvider> _providers;
    private readonly IRetailMatchScoringService _retailScoring;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IScoringEngine _scoringEngine;
    private readonly IConfigurationLoader _configLoader;
    private readonly IBridgeIdRepository _bridgeIdRepo;
    private readonly ILogger<RetailMatchWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private const int BatchSize = 10;

    public RetailMatchWorker(
        IIdentityJobRepository jobRepo,
        IRetailCandidateRepository candidateRepo,
        StageOutcomeFactory outcomeFactory,
        TimelineRecorder timeline,
        Services.BatchProgressService batchProgress,
        IEnumerable<IExternalMetadataProvider> providers,
        IRetailMatchScoringService retailScoring,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        IBridgeIdRepository bridgeIdRepo,
        ILogger<RetailMatchWorker> logger)
    {
        _jobRepo = jobRepo;
        _candidateRepo = candidateRepo;
        _outcomeFactory = outcomeFactory;
        _timeline = timeline;
        _batchProgress = batchProgress;
        _providers = providers;
        _retailScoring = retailScoring;
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _scoringEngine = scoringEngine;
        _configLoader = configLoader;
        _bridgeIdRepo = bridgeIdRepo;
        _logger = logger;
    }

    /// <summary>
    /// Polls for <see cref="IdentityJobState.Queued"/> jobs and processes them.
    /// Called by the Api-layer hosted service on each poll tick.
    /// Returns the number of jobs processed.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        var jobs = await _jobRepo.LeaseNextAsync(
            "RetailMatchWorker",
            [IdentityJobState.Queued],
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
                _logger.LogError(ex, "RetailMatchWorker failed for job {JobId} (entity {EntityId})",
                    job.Id, job.EntityId);
                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }

        return jobs.Count;
    }

    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailSearching, ct: ct);

        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType))
            mediaType = MediaType.Unknown;

        // Load pipeline configuration for this media type
        var pipelineConfig = _configLoader.LoadPipelines();
        var pipeline = pipelineConfig.GetPipelineForMediaType(job.MediaType);
        var strategy = pipeline.Strategy;
        var hydrationConfig = _configLoader.LoadHydration();

        var retailAcceptThreshold = hydrationConfig.RetailAutoAcceptThreshold;
        var retailAmbiguousThreshold = hydrationConfig.RetailAmbiguousThreshold;

        // Get ranked providers for this media type
        var providerConfigs = _configLoader.LoadAllProviders();
        var rankedProviders = pipeline.Providers.Count > 0
            ? pipeline.Providers.OrderBy(p => p.Rank).Select(p => p.Name).ToList()
            : providerConfigs.Select(p => p.Name).ToList();

        // Build hints from existing claims/canonicals
        var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in canonicals)
        {
            hints.TryAdd(c.Key, c.Value);
        }

        var allCandidates = new List<RetailMatchCandidate>();
        RetailMatchCandidate? bestCandidate = null;
        var bestScore = 0.0;
        var providerRank = 0;
        var sequentialBridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Iterate providers per strategy
        foreach (var providerName in rankedProviders)
        {
            providerRank++;
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (provider is null) continue;

            try
            {
                // Build lookup request
                var lookupRequest = new ProviderLookupRequest
                {
                    EntityId = job.EntityId,
                    EntityType = EntityType.MediaAsset,
                    MediaType = mediaType,
                    Title = hints.GetValueOrDefault(MetadataFieldConstants.Title),
                    Author = hints.GetValueOrDefault(MetadataFieldConstants.Author),
                    Narrator = hints.GetValueOrDefault(MetadataFieldConstants.Narrator),
                    ShowName = hints.GetValueOrDefault(MetadataFieldConstants.ShowName)
                        ?? hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Album = hints.GetValueOrDefault(MetadataFieldConstants.Album),
                    Artist = hints.GetValueOrDefault(MetadataFieldConstants.Artist),
                    Director = hints.GetValueOrDefault(MetadataFieldConstants.Director),
                    SeasonNumber = hints.GetValueOrDefault(MetadataFieldConstants.SeasonNumber)
                        ?? hints.GetValueOrDefault("season"),
                    EpisodeNumber = hints.GetValueOrDefault(MetadataFieldConstants.EpisodeNumber)
                        ?? hints.GetValueOrDefault("episode"),
                    TrackNumber = hints.GetValueOrDefault(MetadataFieldConstants.TrackNumber),
                    Series = hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Genre = hints.GetValueOrDefault(MetadataFieldConstants.Genre),
                    Isbn = hints.GetValueOrDefault(BridgeIdKeys.Isbn),
                    Asin = hints.GetValueOrDefault(BridgeIdKeys.Asin),
                    Hints = hints,
                    PriorProviderBridgeIds = strategy == ProviderStrategy.Sequential
                        ? sequentialBridgeIds : null,
                };

                var claims = await provider.FetchAsync(lookupRequest, ct);
                if (claims.Count == 0) continue;

                // Extract candidate metadata from claims
                var candidateTitle = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                        StringComparison.OrdinalIgnoreCase))?.Value;
                var candidateAuthor = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Author,
                        StringComparison.OrdinalIgnoreCase))?.Value;
                var candidateYear = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Year,
                        StringComparison.OrdinalIgnoreCase))?.Value;

                // Score candidate
                var retailScore = _retailScoring.ScoreCandidate(
                    hints, candidateTitle, candidateAuthor, candidateYear, mediaType);

                // Determine candidate outcome
                string outcome;
                if (retailScore.CompositeScore >= retailAcceptThreshold)
                    outcome = "AutoAccepted";
                else if (retailScore.CompositeScore >= retailAmbiguousThreshold)
                    outcome = "Ambiguous";
                else
                    outcome = "Rejected";

                // Extract bridge IDs from claims
                var bridgeIdsJson = System.Text.Json.JsonSerializer.Serialize(
                    claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key))
                          .ToDictionary(c => c.Key, c => c.Value));

                // Build candidate record
                var candidate = new RetailMatchCandidate
                {
                    JobId = job.Id,
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.Name,
                    ProviderItemId = claims
                        .FirstOrDefault(c => string.Equals(c.Key, "provider_item_id",
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Rank = providerRank,
                    Title = candidateTitle ?? "(unknown)",
                    Creator = candidateAuthor,
                    Year = candidateYear,
                    ScoreTotal = retailScore.CompositeScore,
                    ScoreBreakdownJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        title = retailScore.TitleScore,
                        author = retailScore.AuthorScore,
                        year = retailScore.YearScore,
                        format = retailScore.FormatScore,
                    }),
                    BridgeIdsJson = bridgeIdsJson,
                    Description = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Description,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    ImageUrl = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Outcome = outcome,
                };

                allCandidates.Add(candidate);

                // Track best candidate
                if (retailScore.CompositeScore > bestScore)
                {
                    bestScore = retailScore.CompositeScore;
                    bestCandidate = candidate;
                }

                // Persist claims if candidate is accepted or ambiguous
                if (outcome != "Rejected")
                {
                    await ScoringHelper.PersistClaimsAndScoreAsync(
                        job.EntityId, claims, provider.ProviderId,
                        _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                        logger: _logger);

                    // Extract bridge IDs for Stage 2
                    var bridgeEntries = claims
                        .Where(c => BridgeIdHelper.IsBridgeId(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                        .Select(c => new BridgeIdEntry
                        {
                            EntityId = job.EntityId,
                            IdType = c.Key,
                            IdValue = c.Value,
                            ProviderId = provider.ProviderId.ToString(),
                        }).ToList();

                    if (bridgeEntries.Count > 0)
                        await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

                    // Sequential: accumulate bridge IDs for next provider
                    if (strategy == ProviderStrategy.Sequential)
                    {
                        foreach (var c in claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key)))
                            sequentialBridgeIds.TryAdd(c.Key, c.Value);
                    }
                }

                // Waterfall: stop after first accepted candidate
                if (strategy == ProviderStrategy.Waterfall && outcome == "AutoAccepted")
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Provider {Provider} failed for entity {EntityId}",
                    providerName, job.EntityId);
            }
        }

        // Persist ALL candidates (winners and losers)
        if (allCandidates.Count > 0)
            await _candidateRepo.InsertBatchAsync(allCandidates, ct);

        // Determine final job state based on best candidate
        if (bestCandidate is not null && bestScore >= retailAcceptThreshold)
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match found for entity {EntityId}: '{Title}' from {Provider} (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestCandidate.ProviderName, bestScore);
        }
        else if (bestCandidate is not null && bestScore >= retailAmbiguousThreshold)
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, bestScore, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match ambiguous for entity {EntityId}: '{Title}' (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestScore);
        }
        else
        {
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);
            await _outcomeFactory.CreateRetailFailedAsync(
                job.EntityId, job.MediaType, job.IngestionRunId, null, ct);

            var titleHint = hints.GetValueOrDefault(MetadataFieldConstants.Title) ?? "(unknown)";
            await _timeline.RecordRetailNoMatchAsync(
                job.EntityId, titleHint, job.IngestionRunId, ct);

            _logger.LogInformation(
                "No retail match for entity {EntityId} — {CandidateCount} candidates evaluated, best score: {Score:F2}",
                job.EntityId, allCandidates.Count, bestScore);
        }
    }
}
