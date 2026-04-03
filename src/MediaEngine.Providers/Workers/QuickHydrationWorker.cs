using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Quick Hydration worker (after Stage 2 QID resolution).
/// Leases <see cref="IdentityJobState.QidResolved"/> jobs and runs the Quick
/// enrichment pass via <see cref="IEnrichmentService"/>.
///
/// This is a plain service — the Api layer wraps it in a <c>BackgroundService</c>.
/// </summary>
public sealed class QuickHydrationWorker
{
    private readonly IIdentityJobRepository _jobRepo;
    private readonly IEnrichmentService _enrichment;
    private readonly ILogger<QuickHydrationWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private const int BatchSize = 5;

    public QuickHydrationWorker(
        IIdentityJobRepository jobRepo,
        IEnrichmentService enrichment,
        ILogger<QuickHydrationWorker> logger)
    {
        _jobRepo = jobRepo;
        _enrichment = enrichment;
        _logger = logger;
    }

    /// <summary>
    /// Polls for <see cref="IdentityJobState.QidResolved"/> jobs and runs Quick hydration.
    /// Returns the number of jobs processed.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        var jobs = await _jobRepo.LeaseNextAsync(
            "QuickHydrationWorker",
            [IdentityJobState.QidResolved],
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
                _logger.LogError(ex, "QuickHydrationWorker failed for job {JobId}", job.Id);
                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }

        return jobs.Count;
    }

    private async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Hydrating, ct: ct);

        if (string.IsNullOrEmpty(job.ResolvedQid))
        {
            _logger.LogWarning("QuickHydrationWorker: job {JobId} has no resolved QID — skipping", job.Id);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, "No resolved QID", ct);
            return;
        }

        _logger.LogInformation("Quick hydration starting for entity {EntityId} (QID {Qid})",
            job.EntityId, job.ResolvedQid);

        await _enrichment.RunQuickPassAsync(job.EntityId, job.ResolvedQid, ct);

        // TODO: Call PostPipelineService.EvaluateAndOrganizeAsync for confidence check,
        // auto-resolve, and organization gating.

        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Completed, ct: ct);
        _logger.LogInformation("Quick hydration completed for entity {EntityId}", job.EntityId);
    }
}
