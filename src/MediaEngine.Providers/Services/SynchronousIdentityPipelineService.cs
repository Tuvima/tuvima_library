using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Workers;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Replacement for <c>HydrationPipelineService</c> that implements
/// <see cref="IHydrationPipelineService"/> using the durable identity pipeline
/// workers instead of the monolith.
///
/// <see cref="EnqueueAsync"/> creates an <c>identity_jobs</c> row for async processing.
/// <see cref="RunSynchronousAsync"/> runs the three pipeline workers inline and
/// returns a <see cref="HydrationResult"/>.
/// </summary>
public sealed class SynchronousIdentityPipelineService : IHydrationPipelineService
{
    private readonly IIdentityJobRepository _jobRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly RetailMatchWorker _retailWorker;
    private readonly WikidataBridgeWorker _bridgeWorker;
    private readonly QuickHydrationWorker _hydrationWorker;
    private readonly ILogger<SynchronousIdentityPipelineService> _logger;

    public SynchronousIdentityPipelineService(
        IIdentityJobRepository jobRepo,
        ICanonicalValueRepository canonicalRepo,
        RetailMatchWorker retailWorker,
        WikidataBridgeWorker bridgeWorker,
        QuickHydrationWorker hydrationWorker,
        ILogger<SynchronousIdentityPipelineService> logger)
    {
        _jobRepo = jobRepo;
        _canonicalRepo = canonicalRepo;
        _retailWorker = retailWorker;
        _bridgeWorker = bridgeWorker;
        _hydrationWorker = hydrationWorker;
        _logger = logger;
    }

    public int PendingCount => 0;

    /// <summary>
    /// Creates an <c>identity_jobs</c> row for asynchronous processing by the
    /// hosted service workers.
    /// </summary>
    public async ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
    {
        var job = new IdentityJob
        {
            EntityId = request.EntityId,
            EntityType = request.EntityType.ToString(),
            MediaType = request.MediaType.ToString(),
            IngestionRunId = request.IngestionRunId,
            Pass = request.Pass.ToString(),
        };

        await _jobRepo.CreateAsync(job, ct);

        _logger.LogInformation(
            "Enqueued identity job {JobId} for entity {EntityId} ({MediaType})",
            job.Id, job.EntityId, job.MediaType);
    }

    /// <summary>
    /// Runs the full identity pipeline inline: Stage 1 (retail) → Stage 2 (bridge)
    /// → Quick hydration. Returns a <see cref="HydrationResult"/> for the caller.
    /// </summary>
    public async Task<HydrationResult> RunSynchronousAsync(
        HarvestRequest request, CancellationToken ct = default)
    {
        var job = new IdentityJob
        {
            EntityId = request.EntityId,
            EntityType = request.EntityType.ToString(),
            MediaType = request.MediaType.ToString(),
            IngestionRunId = request.IngestionRunId,
            Pass = request.Pass.ToString(),
        };

        // If caller provided a pre-resolved QID (e.g. Fix Match), skip retail + bridge
        if (!string.IsNullOrWhiteSpace(request.PreResolvedQid) && request.IsUserResolution)
        {
            job.State = nameof(IdentityJobState.QidResolved);
            job.ResolvedQid = request.PreResolvedQid;
            await _jobRepo.CreateAsync(job, ct);
            await _jobRepo.SetResolvedQidAsync(job.Id, request.PreResolvedQid, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidResolved, ct: ct);
        }
        else
        {
            await _jobRepo.CreateAsync(job, ct);

            // Stage 1: Retail identification
            await _retailWorker.ProcessJobAsync(job, ct);

            // Reload job state after retail worker
            var updatedJob = await _jobRepo.GetByIdAsync(job.Id, ct);
            if (updatedJob is null)
            {
                _logger.LogWarning("Job {JobId} not found after retail stage", job.Id);
                return BuildResult(job);
            }
            job = updatedJob;

            // Only proceed to Stage 2 if retail matched (strict gate)
            if (job.State == nameof(IdentityJobState.RetailMatched) ||
                job.State == nameof(IdentityJobState.RetailMatchedNeedsReview))
            {
                await _bridgeWorker.ProcessJobAsync(job, ct);

                updatedJob = await _jobRepo.GetByIdAsync(job.Id, ct);
                if (updatedJob is not null)
                    job = updatedJob;
            }
        }

        // Reload to get latest state
        var finalJob = await _jobRepo.GetByIdAsync(job.Id, ct);
        if (finalJob is not null)
            job = finalJob;

        // Stage 3: Quick hydration (if QID resolved)
        if (job.State == nameof(IdentityJobState.QidResolved))
        {
            await _hydrationWorker.ProcessJobAsync(job, ct);

            finalJob = await _jobRepo.GetByIdAsync(job.Id, ct);
            if (finalJob is not null)
                job = finalJob;
        }

        _logger.LogInformation(
            "Synchronous pipeline completed for entity {EntityId}: state={State}, QID={Qid}",
            job.EntityId, job.State, job.ResolvedQid);

        return BuildResult(job);
    }

    /// <summary>
    /// Creates identity jobs for batch bridge resolution. The workers process
    /// them asynchronously.
    /// </summary>
    public async Task RunBatchBridgeResolutionAsync(Guid batchId, CancellationToken ct = default)
    {
        var jobs = await _jobRepo.GetByStateAsync(IdentityJobState.RetailMatched, 500, ct);

        _logger.LogInformation(
            "Batch bridge resolution: {Count} jobs eligible for Stage 2",
            jobs.Count);

        // Workers will pick these up automatically — no additional action needed.
    }

    private HydrationResult BuildResult(IdentityJob job)
    {
        var needsReview = job.State is
            nameof(IdentityJobState.RetailNoMatch) or
            nameof(IdentityJobState.RetailMatchedNeedsReview) or
            nameof(IdentityJobState.QidNeedsReview) or
            nameof(IdentityJobState.QidNoMatch) or
            nameof(IdentityJobState.Failed);

        string? reviewReason = job.State switch
        {
            nameof(IdentityJobState.RetailNoMatch) => "Retail identification failed",
            nameof(IdentityJobState.QidNoMatch) => "Wikidata bridge resolution failed",
            nameof(IdentityJobState.Failed) => job.LastError,
            _ => null,
        };

        return new HydrationResult
        {
            WikidataQid = job.ResolvedQid,
            NeedsReview = needsReview,
            ReviewReason = reviewReason,
        };
    }
}
