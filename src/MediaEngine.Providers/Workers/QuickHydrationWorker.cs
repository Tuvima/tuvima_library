using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
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
    private readonly CollectionAssignmentService _collectionAssignment;
    private readonly ILogger<QuickHydrationWorker> _logger;
    private readonly PostPipelineService _postPipeline;
    private readonly BatchProgressService? _batchProgress;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly IUniverseEnrichmentScheduler _universeEnrichment;

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
        CollectionAssignmentService collectionAssignment,
        PostPipelineService postPipeline,
        ICanonicalValueRepository canonicalRepo,
        ICollectionRepository collectionRepo,
        IUniverseEnrichmentScheduler universeEnrichment,
        IConfigurationLoader configLoader,
        ILogger<QuickHydrationWorker> logger,
        BatchProgressService? batchProgress = null,
        ImagePathService? imagePathService = null)
    {
        _jobRepo = jobRepo;
        _enrichment = enrichment;
        _collectionAssignment = collectionAssignment;
        _postPipeline = postPipeline;
        _canonicalRepo = canonicalRepo;
        _collectionRepo = collectionRepo;
        _universeEnrichment = universeEnrichment;
        _logger = logger;
        _batchProgress = batchProgress;
        _ = imagePathService;

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
            ct: ct);

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

        if (_batchProgress is not null)
        {
            foreach (var runId in jobs
                         .Select(j => j.IngestionRunId)
                         .Where(id => id.HasValue)
                         .Select(id => id!.Value)
                         .Distinct())
            {
                await _batchProgress.EmitProgressAsync(runId, isFinal: false, ct).ConfigureAwait(false);
            }
        }

        return jobs.Count;
    }

    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.Hydrating, ct: ct);

        if (string.IsNullOrEmpty(job.ResolvedQid))
        {
            _logger.LogWarning("QuickHydrationWorker: job {JobId} has no resolved QID; skipping", job.Id);
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

        // Assign the work to a ContentGroup collection based on Wikidata relationships
        // (series, franchise, fictional_universe). Must run after enrichment
        // populates canonical values but before PostPipeline gates organization.
        try
        {
            await _collectionAssignment.AssignAsync(job.EntityId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Collection assignment failed for entity {EntityId}; continuing", job.EntityId);
        }

        await _postPipeline.EvaluateAndOrganizeAsync(
            job.EntityId, job.Id, job.ResolvedQid, job.IngestionRunId, ct);

        var refreshedCanonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct).ConfigureAwait(false);
        var batchKey = await BuildUniverseBatchKeyAsync(job.EntityId, job.ResolvedQid, job.MediaType, refreshedCanonicals, ct)
            .ConfigureAwait(false);

        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.UniverseEnriching, ct: ct);
        await _universeEnrichment.QueueInlineAsync(
            new UniverseEnrichmentRequest(
                job.Id,
                job.EntityId,
                job.IngestionRunId,
                job.ResolvedQid,
                job.MediaType,
                batchKey,
                titleForLog),
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Hydration ready for Stage 3: '{Title}'{AuthorPart} — {Qid} ({MediaType}) [entity {EntityId}]",
            titleForLog,
            string.IsNullOrWhiteSpace(authorForLog) ? string.Empty : $" by {authorForLog}",
            job.ResolvedQid,
            job.MediaType,
            job.EntityId);
    }

    private async Task<string> BuildUniverseBatchKeyAsync(
        Guid entityId,
        string workQid,
        string mediaType,
        IReadOnlyList<CanonicalValue> canonicals,
        CancellationToken ct)
    {
        var lookup = canonicals.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        var workId = await _collectionRepo.GetWorkIdByMediaAssetAsync(entityId, ct).ConfigureAwait(false);
        var collectionId = workId.HasValue
            ? await _collectionRepo.GetCollectionIdByWorkIdAsync(workId.Value, ct).ConfigureAwait(false)
            : null;

        return mediaType switch
        {
            nameof(MediaType.Movies) => $"movie:work:{workQid}",
            nameof(MediaType.TV) when collectionId.HasValue => $"tv:collection:{collectionId.Value}",
            nameof(MediaType.TV) when lookup.TryGetValue("series_qid", out var tvSeriesQid) && !string.IsNullOrWhiteSpace(tvSeriesQid)
                => $"tv:series:{tvSeriesQid}",
            nameof(MediaType.TV) => $"tv:work:{workQid}",
            nameof(MediaType.Music) when collectionId.HasValue => $"music:collection:{collectionId.Value}",
            nameof(MediaType.Music) when lookup.TryGetValue("musicbrainz_release_group_id", out var releaseGroupId) && !string.IsNullOrWhiteSpace(releaseGroupId)
                => $"music:release-group:{releaseGroupId}",
            nameof(MediaType.Music) => $"music:work:{workQid}",
            nameof(MediaType.Books) when collectionId.HasValue => $"book:collection:{collectionId.Value}",
            nameof(MediaType.Books) when lookup.TryGetValue("series_qid", out var bookSeriesQid) && !string.IsNullOrWhiteSpace(bookSeriesQid)
                => $"book:series:{bookSeriesQid}",
            nameof(MediaType.Books) => $"book:work:{workQid}",
            nameof(MediaType.Audiobooks) when collectionId.HasValue => $"audiobook:collection:{collectionId.Value}",
            nameof(MediaType.Audiobooks) when lookup.TryGetValue("series_qid", out var audioSeriesQid) && !string.IsNullOrWhiteSpace(audioSeriesQid)
                => $"audiobook:series:{audioSeriesQid}",
            nameof(MediaType.Audiobooks) => $"audiobook:work:{workQid}",
            nameof(MediaType.Comics) when collectionId.HasValue => $"comic:collection:{collectionId.Value}",
            nameof(MediaType.Comics) when lookup.TryGetValue("series_qid", out var comicSeriesQid) && !string.IsNullOrWhiteSpace(comicSeriesQid)
                => $"comic:series:{comicSeriesQid}",
            nameof(MediaType.Comics) => $"comic:work:{workQid}",
            _ => $"work:{workQid}",
        };
    }
}
