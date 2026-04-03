using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Helpers;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Stage 2: Wikidata Bridge Resolution.
/// Leases jobs in <see cref="IdentityJobState.RetailMatched"/> or
/// <see cref="IdentityJobState.RetailMatchedNeedsReview"/> state.
/// Never processes <see cref="IdentityJobState.RetailNoMatch"/> — the strict retail gate.
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
    private readonly ILogger<WikidataBridgeWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private const int BatchSize = 5;

    public WikidataBridgeWorker(
        IIdentityJobRepository jobRepo,
        IWikidataCandidateRepository candidateRepo,
        StageOutcomeFactory outcomeFactory,
        TimelineRecorder timeline,
        BridgeIdHelper bridgeIdHelper,
        ILogger<WikidataBridgeWorker> logger)
    {
        _jobRepo = jobRepo;
        _candidateRepo = candidateRepo;
        _outcomeFactory = outcomeFactory;
        _timeline = timeline;
        _bridgeIdHelper = bridgeIdHelper;
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

    private async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.BridgeSearching, ct: ct);

        // TODO: Integrate with ReconciliationAdapter bridge resolution.
        // In production, this will:
        // 1. Load the accepted retail candidate's bridge IDs
        // 2. Try exact bridge ID lookup (ISBN → P212, TMDB → P4947, etc.)
        // 3. If no exact match, run constrained text reconciliation
        // 4. Score candidates and apply acceptance thresholds
        // 5. Persist ALL candidates to wikidata_bridge_candidates
        // 6. Set outcome: QidResolved, QidNeedsReview, or QidNoMatch

        _logger.LogDebug("WikidataBridgeWorker processing job {JobId} for entity {EntityId}",
            job.Id, job.EntityId);

        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidNoMatch, ct: ct);
    }
}
