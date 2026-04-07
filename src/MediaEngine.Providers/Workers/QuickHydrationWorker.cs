using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
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
    private readonly HubAssignmentService _hubAssignment;
    private readonly ILogger<QuickHydrationWorker> _logger;
    private readonly PostPipelineService _postPipeline;
    private readonly ICanonicalValueRepository _canonicalRepo;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Lease batch size. Sourced from
    /// <c>config/core.json → pipeline.lease_sizes.hydration</c> at construction time.
    /// Hydration is per-job (no cross-job batching benefit), so the limit is set
    /// lower than the retail/wikidata stages to keep individual cycles responsive.
    /// </summary>
    private readonly int _batchSize;

    public QuickHydrationWorker(
        IIdentityJobRepository jobRepo,
        IEnrichmentService enrichment,
        HubAssignmentService hubAssignment,
        PostPipelineService postPipeline,
        ICanonicalValueRepository canonicalRepo,
        IConfigurationLoader configLoader,
        ILogger<QuickHydrationWorker> logger)
    {
        _jobRepo = jobRepo;
        _enrichment = enrichment;
        _hubAssignment = hubAssignment;
        _postPipeline = postPipeline;
        _canonicalRepo = canonicalRepo;
        _logger = logger;

        _batchSize = Math.Max(1, configLoader.LoadCore().Pipeline.LeaseSizes.Hydration);
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
            _batchSize,
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

    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Hydrating, ct: ct);

        if (string.IsNullOrEmpty(job.ResolvedQid))
        {
            _logger.LogWarning("QuickHydrationWorker: job {JobId} has no resolved QID — skipping", job.Id);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Failed, "No resolved QID", ct);
            return;
        }

        // Pre-fetch title for narrative logging (best-effort — enrichment will improve it)
        var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);
        var titleForLog = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value
            ?? canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.ShowName, StringComparison.OrdinalIgnoreCase))?.Value
            ?? "(unknown)";
        var authorForLog = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase))?.Value
            ?? canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Artist, StringComparison.OrdinalIgnoreCase))?.Value;

        _logger.LogInformation(
            "Hydration: starting for '{Title}'{AuthorPart} — {Qid} (entity {EntityId})",
            titleForLog,
            string.IsNullOrWhiteSpace(authorForLog) ? string.Empty : $" by {authorForLog}",
            job.ResolvedQid,
            job.EntityId);

        await _enrichment.RunQuickPassAsync(job.EntityId, job.ResolvedQid, ct);

        // Assign the work to a ContentGroup hub based on Wikidata relationships
        // (series, franchise, fictional_universe). Must run after enrichment
        // populates canonical values but before PostPipeline gates organization.
        try
        {
            await _hubAssignment.AssignAsync(job.EntityId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Hub assignment failed for entity {EntityId} — continuing", job.EntityId);
        }

        await _postPipeline.EvaluateAndOrganizeAsync(
            job.EntityId, job.Id, job.ResolvedQid, job.IngestionRunId, ct);

        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Completed, ct: ct);
        _logger.LogInformation(
            "Identified: '{Title}'{AuthorPart} — {Qid} ({MediaType}) [entity {EntityId}]",
            titleForLog,
            string.IsNullOrWhiteSpace(authorForLog) ? string.Empty : $" by {authorForLog}",
            job.ResolvedQid,
            job.MediaType,
            job.EntityId);
    }
}
