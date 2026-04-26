using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
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
using MediaEngine.Storage.Services;
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
    private readonly IWorkRepository _workRepo;
    private readonly WorkClaimRouter _claimRouter;
    private readonly CatalogUpsertService _catalogUpsert;
    private readonly IIngestionBatchRepository _batchRepo;
    private readonly PostPipelineService _postPipeline;
    private readonly CoverArtWorker _coverArt;
    private readonly BatchProgressService? _batchProgress;
    private readonly ILogger<WikidataBridgeWorker> _logger;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Cross-job batching window. Sourced from
    /// <c>config/core.json → pipeline.lease_sizes.wikidata</c> at construction time.
    /// Larger values mean more jobs share a single Wikidata reconciliation call
    /// (one call per unique album/show, one call per unique bridge ID).
    /// </summary>
    private readonly int _batchSize;

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
        IWorkRepository workRepo,
        WorkClaimRouter claimRouter,
        CatalogUpsertService catalogUpsert,
        IIngestionBatchRepository batchRepo,
        PostPipelineService postPipeline,
        CoverArtWorker coverArt,
        ILogger<WikidataBridgeWorker> logger,
        BatchProgressService? batchProgress = null)
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
        _workRepo = workRepo;
        _claimRouter = claimRouter;
        _catalogUpsert = catalogUpsert;
        _batchRepo = batchRepo;
        _postPipeline = postPipeline;
        _coverArt = coverArt;
        _logger = logger;
        _batchProgress = batchProgress;

        // Lease size is read once at construction. A restart applies any
        // config change — same lifetime as every other CoreConfiguration value.
        _batchSize = Math.Max(1, _configLoader.LoadCore().Pipeline.LeaseSizes.Wikidata);
    }

    /// <summary>
    /// Polls for <see cref="IdentityJobState.RetailMatched"/> and
    /// <see cref="IdentityJobState.RetailMatchedNeedsReview"/> jobs.
    /// Returns the number of jobs processed.
    ///
    /// PollAsync runs in six phases so that N jobs produce far fewer than N Wikidata calls:
    ///
    ///   Phase 1 — Lease: lease up to <see cref="_batchSize"/> eligible jobs.
    ///   Phase 2 — Load context: batch-fetch bridge IDs and canonical values
    ///             for all jobs in two SQL queries (vs N×2 previously).
    ///   Phase 3 — Build job contexts: assemble per-job working DTOs and
    ///             compute the grouping key (bridge signature or title+author).
    ///   Phase 4 — Resolve QIDs: a single
    ///             <see cref="ReconciliationAdapter.ResolveBatchAsync"/> call.
    ///             The adapter internally groups by music album, primary bridge
    ///             ID, and text signature so N jobs produce far fewer than N
    ///             Wikidata calls.
    ///   Phase 5 — Distribute results: propagate each group's resolved QID and
    ///             claims to all sibling jobs in that group.
    ///   Phase 6 — Per-job finalisation: persist candidates, update job state,
    ///             and trigger full property fetch (FetchAsync with PreResolvedQid).
    ///             The adapter's response cache ensures jobs sharing a QID hit the
    ///             cache on the second and subsequent FetchAsync calls.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        // ── Phase 1: Lease ────────────────────────────────────────────────────
        // Strict retail gate: only RetailMatched or RetailMatchedNeedsReview.
        // RetailNoMatch is NEVER included — enforced at the SQL level.
        //
        // Batch gate: when enabled, Stage 2 waits until all Stage 1 jobs for a
        // given ingestion run have completed. This lets the full album / season /
        // series land in one cohesive Wikidata batch instead of trickling in
        // piecemeal and paying for redundant per-album calls.
        var gatedRunIds = await GetGatedRunIdsAsync(ct);

        var jobs = await _jobRepo.LeaseNextAsync(
            "WikidataBridgeWorker",
            [IdentityJobState.RetailMatched, IdentityJobState.RetailMatchedNeedsReview],
            _batchSize,
            LeaseDuration,
            excludeRunIds: gatedRunIds.Count > 0 ? gatedRunIds : null,
            ct: ct);

        if (jobs.Count == 0)
            return 0;

        _logger.LogInformation("Wikidata: leased {JobCount} job(s) for bridge resolution", jobs.Count);

        var reconAdapter = _providers
            .OfType<ReconciliationAdapter>()
            .FirstOrDefault();

        if (reconAdapter is null)
        {
            _logger.LogWarning("No ReconciliationAdapter available — cannot resolve bridge IDs");
            foreach (var j in jobs)
            {
                await _jobRepo.UpdateStateAsync(j.Id, IdentityJobState.QidNoMatch,
                    "No reconciliation adapter configured", ct);
                await TryOrganizeRetainedRetailIdentityAsync(j, ct);
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

        // Transition all jobs to BridgeSearching before any async work.
        foreach (var job in jobs)
        {
            try { await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.BridgeSearching, ct: ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not transition job {JobId} to BridgeSearching", job.Id);
            }
        }

        var contexts = new List<JobContext>(jobs.Count);

        try
        {
            // ── Phase 2: Load context (batch SQL) ─────────────────────────────────
            // Two queries replace N×2 individual reads.
            var entityIds = jobs.Select(j => j.EntityId).ToList();
            var allBridgeIds = await _bridgeIdRepo.GetByEntitiesAsync(entityIds, ct);
            var allCanonicals = await _canonicalRepo.GetByEntitiesAsync(entityIds, ct);

            // ── Phase 3: Build job contexts ───────────────────────────────────────
            foreach (var job in jobs)
            {
                if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType))
                    mediaType = MediaType.Unknown;

                var bridgeIds = allBridgeIds.TryGetValue(job.EntityId, out var b)
                    ? b
                    : (IReadOnlyList<BridgeIdEntry>)[];
                var canonicals = allCanonicals.TryGetValue(job.EntityId, out var c)
                    ? c
                    : (IReadOnlyList<CanonicalValue>)[];

                var bridgeDict  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

                var (titleHint, authorHint, yearHint, albumHint, artistHint, seriesHint) = BuildLookupHints(mediaType, canonicals);

                BridgeIdHelper.InjectSentinels(bridgeDict, titleHint, authorHint);

                contexts.Add(new JobContext(
                    Job: job,
                    MediaType: mediaType,
                    BridgeIds: bridgeIds,
                    BridgeDict: bridgeDict,
                    WikidataProps: wikidataProps,
                    TitleHint: titleHint,
                    AuthorHint: authorHint,
                    YearHint: yearHint,
                    AlbumHint: albumHint,
                    ArtistHint: artistHint,
                    SeriesHint: seriesHint));
            }

            // ── Phase 4: Resolve QIDs via the unified facade ──────────────────────
            // ResolveBatchAsync internally groups by music album / bridge ID / text
            // signature so N jobs produce far fewer than N Wikidata calls.

            {
                var bridgeCount = contexts.Count(ctx => ctx.MediaType != MediaType.Music && ctx.BridgeIds.Count > 0);
                var textCount   = contexts.Count(ctx => ctx.MediaType != MediaType.Music && ctx.BridgeIds.Count == 0 && !string.IsNullOrWhiteSpace(ctx.TitleHint));
                var musicCount  = contexts.Count(ctx => ctx.MediaType == MediaType.Music);
                _logger.LogInformation(
                    "Wikidata: dispatching {TotalJobs} job(s) to ResolveBatchAsync — {MusicCount} music, {BridgeCount} with bridge IDs, {TextCount} text-only fallback",
                    contexts.Count, musicCount, bridgeCount, textCount);
            }

            var resolveRequests = contexts
                .Select(ctx => new WikidataResolveRequest
                {
                    CorrelationKey     = ctx.Job.Id.ToString(),
                    MediaType          = ctx.MediaType,
                    Strategy           = ResolveStrategy.Auto,
                    BridgeIds          = ctx.BridgeDict,
                    WikidataProperties = ctx.WikidataProps,
                    IsEditionAware     = ctx.MediaType is MediaType.Books or MediaType.Audiobooks or MediaType.Music,
                    AlbumTitle         = ctx.AlbumHint,
                    Artist             = ctx.ArtistHint,
                    Title              = ctx.TitleHint,
                    Author             = ctx.AuthorHint,
                    Year               = ctx.YearHint,
                    SeriesTitle        = ctx.SeriesHint,
                })
                .ToList();

            var resolveResults = await reconAdapter.ResolveBatchAsync(resolveRequests, ct);

            // ── Phase 5: Distribute results onto each job context ──────────────────
            foreach (var ctx in contexts)
            {
                if (!resolveResults.TryGetValue(ctx.Job.Id.ToString(), out var result) || !result.Found)
                    continue;

                ctx.ResolvedQid = result.WorkQid ?? result.Qid;
                ctx.AdditionalClaims.AddRange(result.Claims);
                ctx.CollectedBridgeIds = result.CollectedBridgeIds;
                ctx.PrimaryBridgeIdType = result.PrimaryBridgeIdType;
                ctx.MatchedBy = result.MatchedBy switch
                {
                    ResolveStrategy.MusicAlbum         => "music_album",
                    ResolveStrategy.BridgeId           => "bridge_id",
                    ResolveStrategy.TextReconciliation => "text_reconciliation",
                    _                                  => null,
                };

                // Persist the resolution method as a canonical value so the
                // Vault can filter items by how their Wikidata match was made.
                if (ctx.MatchedBy is not null)
                {
                    var canonicalMethod = ctx.MatchedBy switch
                    {
                        "bridge_id"          => "bridge",
                        "text_reconciliation"=> "text",
                        "music_album"        => "album",
                        _                    => ctx.MatchedBy,
                    };
                    ctx.AdditionalClaims.Add(new ProviderClaim(
                        MetadataFieldConstants.QidResolutionMethod, canonicalMethod, 1.0));
                }

                // Music tracks: ResolveMusicAlbumAsync returns the album QID but
                // doesn't always emit it as a wikidata_qid claim — without this
                // the track stalls because nothing downstream sees a resolved QID
                // on the asset.
                if (result.MatchedBy == ResolveStrategy.MusicAlbum
                    && !string.IsNullOrWhiteSpace(ctx.ResolvedQid)
                    && !ctx.AdditionalClaims.Any(c => string.Equals(
                        c.Key, BridgeIdKeys.WikidataQid, StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.AdditionalClaims.Add(new ProviderClaim(
                        BridgeIdKeys.WikidataQid, ctx.ResolvedQid, 0.95));
                }
            }

            // ── Phase 5 summary ───────────────────────────────────────────────────
            {
                var resolvedCount = contexts.Count(ctx => ctx.ResolvedQid is not null);
                _logger.LogInformation(
                    "Wikidata: distributing results — {Resolved} of {Total} job(s) have a resolved QID",
                    resolvedCount, contexts.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Wikidata: batch resolution failed for {Count} job(s) — resetting for retry",
                jobs.Count);

            // Reset all jobs from BridgeSearching back to their pre-lease state
            // so the next poll cycle can retry them.
            foreach (var job in jobs)
            {
                try
                {
                    // job.State still holds the pre-BridgeSearching value (RetailMatched
                    // or RetailMatchedNeedsReview) because UpdateStateAsync only writes
                    // to the DB, not the in-memory IdentityJob object.
                    var resetState = Enum.TryParse<IdentityJobState>(job.State, true, out var s)
                        ? s
                        : IdentityJobState.RetailMatched;
                    await _jobRepo.UpdateStateAsync(job.Id, resetState,
                        $"Batch error (will retry): {ex.Message}", ct);
                }
                catch (Exception resetEx)
                {
                    _logger.LogWarning(resetEx,
                        "Could not reset job {JobId} after batch failure", job.Id);
                }
            }

            return jobs.Count;
        }

        // ── Phase 6: Per-job finalisation ─────────────────────────────────────
        // E1 — QID dedup: group resolved non-music contexts by (QID, MediaType)
        // and call FetchAsync once per unique group. The fetched claims are stored
        // on all sibling contexts so the per-job finalisation path can apply them
        // without a second HTTP call (even if the adapter's response cache would
        // have served it from memory, this makes the dedup explicit and measurable).
        var resolvedContextsNeedingFetch = contexts
            .Where(ctx => ctx.ResolvedQid is not null
                && (ctx.MediaType != MediaType.Music || ctx.MatchedBy == "music_album"))
            .ToList();

        var qidGroups = resolvedContextsNeedingFetch
            .GroupBy(ctx => (ctx.ResolvedQid!, ctx.MediaType))
            .ToList();

        var dedupSavings = 0;
        foreach (var group in qidGroups)
        {
            var siblings = group.ToList();
            var representative = siblings[0];

            IReadOnlyList<ProviderClaim>? sharedClaims = null;
            try
            {
                sharedClaims = await reconAdapter.FetchAsync(
                    new ProviderLookupRequest
                    {
                        EntityId       = representative.Job.EntityId,
                        EntityType     = EntityType.MediaAsset,
                        MediaType      = representative.MediaType,
                        Title          = representative.TitleHint,
                        Year           = representative.YearHint,
                        PreResolvedQid = representative.ResolvedQid,
                    }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Wikidata QID dedup: FetchAsync failed for QID {Qid} ({MediaType})",
                    representative.ResolvedQid, representative.MediaType);
            }

            // Fan out the pre-fetched claims to all siblings. The representative gets
            // them too, so FinaliseJobAsync skips its own FetchAsync call for all
            // members of the group.
            foreach (var sibling in siblings)
                sibling.PreFetchedClaims = sharedClaims;

            if (siblings.Count > 1)
                dedupSavings += siblings.Count - 1;
        }

        if (dedupSavings > 0)
            _logger.LogInformation(
                "Wikidata: QID dedup saved {Savings} FetchAsync call(s) across {Groups} unique QID group(s)",
                dedupSavings, qidGroups.Count(g => g.Count() > 1));

        var allCandidates = new List<WikidataBridgeCandidate>();

        foreach (var ctx in contexts)
        {
            try
            {
                await FinaliseJobAsync(ctx, reconAdapter, allCandidates, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "WikidataBridgeWorker finalisation failed for job {JobId}", ctx.Job.Id);
                await _jobRepo.UpdateStateAsync(ctx.Job.Id, IdentityJobState.Failed, ex.Message, ct);
            }
        }

        // Batch-insert all candidates in one call.
        if (allCandidates.Count > 0)
            await _candidateRepo.InsertBatchAsync(allCandidates, ct);

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

    // -------------------------------------------------------------------------
    // Per-job finalisation (Phase 6)
    // -------------------------------------------------------------------------

    private async Task FinaliseJobAsync(
        JobContext ctx,
        ReconciliationAdapter reconAdapter,
        List<WikidataBridgeCandidate> allCandidates,
        CancellationToken ct)
    {
        var job = ctx.Job;

        // Phase 3c: fetch lineage once for this job. Used by both
        // ScoringHelper (parent-scope claim mirroring into the parent Work's
        // canonical_values) and RouteToWorksAsync (writing the resolved QID
        // and bridge IDs to works.external_identifiers). One DB round-trip
        // per job, reused throughout finalisation.
        WorkLineage? lineage = null;
        try { lineage = await _workRepo.GetLineageByAssetAsync(job.EntityId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "Phase 3c: lineage lookup failed for asset {EntityId} — parent-scope mirror and Work routing skipped",
                job.EntityId);
        }

        if (ctx.ResolvedQid is not null)
        {
            // Build candidate record.
            var (scoreTotal, isExact) = ctx.MatchedBy switch
            {
                "music_album"        => (0.95, true),
                "bridge_id"          => (1.0,  true),
                "text_reconciliation"=> (0.75, false),
                _                    => (0.75, false)
            };

            allCandidates.Add(new WikidataBridgeCandidate
            {
                JobId        = job.Id,
                Qid          = ctx.ResolvedQid,
                Label        = ctx.AlbumHint ?? ctx.TitleHint ?? ctx.ResolvedQid,
                MatchedBy    = ctx.MatchedBy ?? "unknown",
                BridgeIdType = ctx.PrimaryBridgeIdType,
                IsExactMatch = isExact,
                ScoreTotal   = scoreTotal,
                Outcome      = "AutoAccepted",
            });

            // Persist claims accumulated during group resolution.
            if (ctx.AdditionalClaims.Count > 0)
            {
                // Phase 3c: lineage-aware persist mirrors parent-scope display
                // claims (album, year, cover) onto the parent Work.
                await ScoringHelper.PersistAndScoreWithLineageAsync(
                    job.EntityId, ctx.AdditionalClaims, reconAdapter.ProviderId, lineage,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                    logger: _logger);

                // Phase 3b: route any container-level structural data (the album
                // QID, child entity manifests) onto the parent Work.
                await RouteToWorksAsync(lineage, job.EntityId, ctx.MediaType, ctx.ResolvedQid,
                    ctx.AdditionalClaims, ct);
            }

            // Persist collected bridge IDs (non-music bridge resolution only).
            // ReconciliationAdapter.BuildClaimsForResolvedQidAsync now emits the dictionary
            // keyed by bridge claim key (e.g. "isbn_13", "tmdb_id"), not raw P-code.
            if (ctx.CollectedBridgeIds is { Count: > 0 })
            {
                var collectedEntries = ctx.CollectedBridgeIds
                    .Select(kvp => new BridgeIdEntry
                    {
                        EntityId         = job.EntityId,
                        IdType           = kvp.Key,
                        IdValue          = kvp.Value,
                        ProviderId       = reconAdapter.ProviderId.ToString(),
                        WikidataProperty = _bridgeIdHelper.GetPCode(kvp.Key),
                    }).ToList();

                await _bridgeIdRepo.UpsertBatchAsync(collectedEntries, ct);
            }

            // Record timeline event.
            var timelineMethod = ctx.MatchedBy switch
            {
                "text_reconciliation" => "title_fallback",
                _ => ctx.PrimaryBridgeIdType ?? ctx.MatchedBy ?? "bridge_id"
            };
            if (ctx.MatchedBy == "text_reconciliation")
                await _timeline.RecordTitleFallbackResolvedAsync(job.EntityId, ctx.ResolvedQid, job.IngestionRunId, ct);
            else
                await _timeline.RecordBridgeResolvedAsync(job.EntityId, ctx.ResolvedQid, timelineMethod, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Wikidata: '{Title}' identified as {Qid} via {Method} [entity {EntityId}]",
                ctx.TitleHint ?? ctx.AlbumHint ?? "(unknown)", ctx.ResolvedQid, ctx.MatchedBy, job.EntityId);

            await _jobRepo.SetResolvedQidAsync(job.Id, ctx.ResolvedQid, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidResolved, ct: ct);

            // Skip post-resolve property fetch for music — the resolved QID is the
            // ALBUM, not the track. Fetching its properties would overwrite the
            // track's title/duration/artist with album-level values.
            if (ctx.MediaType == MediaType.Music)
            {
                if (ctx.MatchedBy == "music_album")
                {
                    IReadOnlyList<ProviderClaim> albumClaims;
                    if (ctx.PreFetchedClaims is not null)
                    {
                        albumClaims = ctx.PreFetchedClaims;
                    }
                    else
                    {
                        try
                        {
                            albumClaims = await reconAdapter.FetchAsync(
                                new ProviderLookupRequest
                                {
                                    EntityId       = job.EntityId,
                                    EntityType     = EntityType.MediaAsset,
                                    MediaType      = ctx.MediaType,
                                    Title          = ctx.TitleHint,
                                    Year           = ctx.YearHint,
                                    PreResolvedQid = ctx.ResolvedQid,
                                }, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex,
                                "Music album property fetch failed for QID {Qid} (entity {EntityId})",
                                ctx.ResolvedQid, job.EntityId);
                            albumClaims = [];
                        }
                    }

                    if (albumClaims.Count > 0)
                    {
                        var parentScopedAlbumClaims = albumClaims
                            .Where(c => ClaimScopeCatalog.IsParentScoped(c.Key, MediaType.Music)
                                || BridgeIdKeys.All.Contains(c.Key))
                            .ToList();

                        if (parentScopedAlbumClaims.Count > 0)
                        {
                            await ScoringHelper.PersistAndScoreWithLineageAsync(
                                job.EntityId, parentScopedAlbumClaims, reconAdapter.ProviderId, lineage,
                                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                                logger: _logger);

                            await RouteToWorksAsync(lineage, job.EntityId, ctx.MediaType, ctx.ResolvedQid,
                                parentScopedAlbumClaims, ct);
                        }
                    }
                }

                await _postPipeline.EvaluateAndOrganizeAsync(
                    job.EntityId, job.Id, ctx.ResolvedQid, job.IngestionRunId, ct);
                return;
            }

            // Fetch full properties now that we have a QID.
            // Phase 6 QID dedup (E1): if a pre-fetched claims set was computed
            // for this QID group, use it directly — no HTTP call needed.
            // Otherwise fall back to FetchAsync (covers the single-job case and
            // any group whose representative FetchAsync failed).
            IReadOnlyList<ProviderClaim> fullClaims;
            if (ctx.PreFetchedClaims is not null)
            {
                fullClaims = ctx.PreFetchedClaims;
            }
            else
            {
                try
                {
                    fullClaims = await reconAdapter.FetchAsync(
                        new ProviderLookupRequest
                        {
                            EntityId       = job.EntityId,
                            EntityType     = EntityType.MediaAsset,
                            MediaType      = ctx.MediaType,
                            Title          = ctx.TitleHint,
                            Year           = ctx.YearHint,
                            PreResolvedQid = ctx.ResolvedQid,
                        }, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Full property fetch failed for QID {Qid} (entity {EntityId})",
                        ctx.ResolvedQid, job.EntityId);
                    fullClaims = [];
                }
            }

            if (fullClaims.Count > 0)
            {
                // Phase 3c: lineage-aware persist mirrors parent-scope
                // display claims (show_name, year, description, cover,
                // genre, cast) onto the parent Work — the show or series.
                await ScoringHelper.PersistAndScoreWithLineageAsync(
                    job.EntityId, fullClaims, reconAdapter.ProviderId, lineage,
                    _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                    logger: _logger);

                // Phase 3b: route the QID and container fields onto the
                // correct Work, then upsert any catalog children.
                await RouteToWorksAsync(lineage, job.EntityId, ctx.MediaType, ctx.ResolvedQid,
                    fullClaims, ct);
            }

            await _postPipeline.EvaluateAndOrganizeAsync(
                job.EntityId, job.Id, ctx.ResolvedQid, job.IngestionRunId, ct);
        }
        else
        {
            // Text reconciliation fallback for jobs where ReconcileBatchAsync did not fire
            // (e.g. the batch call threw) or returned no match.  Run individually so we
            // still attempt FetchAsync for any job that wasn't covered above.
            if (ctx.MediaType != MediaType.Music
                && !string.IsNullOrWhiteSpace(ctx.TitleHint)
                && ctx.MediaType != MediaType.Unknown)
            {
                try
                {
                    var fallbackClaims = await reconAdapter.FetchAsync(
                        new ProviderLookupRequest
                        {
                            EntityId   = job.EntityId,
                            EntityType = EntityType.MediaAsset,
                            MediaType  = ctx.MediaType,
                            Title      = ctx.TitleHint,
                            Author     = ctx.AuthorHint,
                            Year       = ctx.YearHint,
                        }, ct);

                    if (fallbackClaims.Count > 0)
                    {
                        var fallbackQidClaim = fallbackClaims
                            .FirstOrDefault(c => string.Equals(c.Key, BridgeIdKeys.WikidataQid,
                                StringComparison.OrdinalIgnoreCase));

                        if (fallbackQidClaim is not null && !string.IsNullOrWhiteSpace(fallbackQidClaim.Value))
                        {
                            ctx.ResolvedQid = fallbackQidClaim.Value;

                            allCandidates.Add(new WikidataBridgeCandidate
                            {
                                JobId        = job.Id,
                                Qid          = ctx.ResolvedQid,
                                Label        = ctx.TitleHint,
                                MatchedBy    = "text_reconciliation",
                                IsExactMatch = false,
                                ScoreTotal   = 0.75,
                                Outcome      = "AutoAccepted",
                            });

                            // Include the resolution method alongside the fetched claims
                            // so it is persisted in the same PersistAndScoreWithLineageAsync call.
                            var fallbackClaimsWithMethod = new List<ProviderClaim>(fallbackClaims)
                            {
                                new ProviderClaim(MetadataFieldConstants.QidResolutionMethod, "text", 1.0),
                            };

                            // Phase 3c: lineage-aware persist for the
                            // text-fallback path so parent-scope claims still
                            // mirror onto the parent Work.
                            await ScoringHelper.PersistAndScoreWithLineageAsync(
                                job.EntityId, fallbackClaimsWithMethod, reconAdapter.ProviderId, lineage,
                                _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                                logger: _logger);

                            await RouteToWorksAsync(lineage, job.EntityId, ctx.MediaType, ctx.ResolvedQid,
                                fallbackClaims, ct);

                            await _timeline.RecordTitleFallbackResolvedAsync(
                                job.EntityId, ctx.ResolvedQid, job.IngestionRunId, ct);

                            _logger.LogInformation(
                                "Wikidata: '{Title}' identified as {Qid} via text reconciliation (individual fallback) [entity {EntityId}]",
                                ctx.TitleHint ?? "(unknown)", ctx.ResolvedQid, job.EntityId);

                            await _jobRepo.SetResolvedQidAsync(job.Id, ctx.ResolvedQid, ct);
                            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidResolved, ct: ct);
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Individual text reconciliation failed for entity {EntityId}", job.EntityId);
                }
            }

            // No QID found at all.
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidNoMatch, ct: ct);
            await _timeline.RecordBridgeNoMatchAsync(
                job.EntityId, job.IngestionRunId, ct);

            await TryOrganizeRetainedRetailIdentityAsync(job, ct);

            _logger.LogInformation(
                "Wikidata: no match for '{Title}' ({MediaType}) — {BridgeCount} bridge ID(s) tried; retaining retail identity without review [entity {EntityId}]",
                ctx.TitleHint ?? "(unknown)", ctx.MediaType, ctx.BridgeIds.Count, job.EntityId);
        }
    }

    // -------------------------------------------------------------------------
    // Public helpers used by the synchronous pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Processes a single identity job synchronously — used by
    /// <see cref="SynchronousIdentityPipelineService"/> when a single asset needs
    /// Stage 2 resolution without waiting for the next background poll.
    ///
    /// Internally creates a single-item batch and runs all six phases, so the
    /// semantics are identical to the batch path in <see cref="PollAsync"/>.
    /// </summary>
    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        var reconAdapter = _providers
            .OfType<ReconciliationAdapter>()
            .FirstOrDefault();

        if (reconAdapter is null)
        {
            _logger.LogWarning("No ReconciliationAdapter available — cannot resolve bridge IDs");
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidNoMatch,
                "No reconciliation adapter configured", ct);
            await TryOrganizeRetainedRetailIdentityAsync(job, ct);
            return;
        }

        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.BridgeSearching, ct: ct);

        // Load context for the single job.
        var bridgeIds  = await _bridgeIdRepo.GetByEntityAsync(job.EntityId, ct);
        var canonicals = await _canonicalRepo.GetByEntityAsync(job.EntityId, ct);

        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType))
            mediaType = MediaType.Unknown;

        var bridgeDict    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wikidataProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bridge in bridgeIds)
        {
            bridgeDict.TryAdd(bridge.IdType, bridge.IdValue);
            var pCode = _bridgeIdHelper.GetPCode(bridge.IdType);
            if (pCode is not null)
            {
                if (string.Equals(bridge.IdType, BridgeIdKeys.TmdbId, StringComparison.OrdinalIgnoreCase)
                    && mediaType == MediaType.TV)
                    pCode = "P4983";
                wikidataProps.TryAdd(bridge.IdType, pCode);
            }
        }

        var (titleHint, authorHint, yearHint, albumHint, artistHint, seriesHint) = BuildLookupHints(mediaType, canonicals);

        BridgeIdHelper.InjectSentinels(bridgeDict, titleHint, authorHint);

        var ctx = new JobContext(
            Job:           job,
            MediaType:     mediaType,
            BridgeIds:     bridgeIds,
            BridgeDict:    bridgeDict,
            WikidataProps: wikidataProps,
            TitleHint:     titleHint,
            AuthorHint:    authorHint,
            YearHint:      yearHint,
            AlbumHint:     albumHint,
            ArtistHint:    artistHint,
            SeriesHint:    seriesHint);

        // Resolve QID for this single job via the unified facade.
        try
        {
            var result = await reconAdapter.ResolveAsync(
                new WikidataResolveRequest
                {
                    CorrelationKey     = job.Id.ToString(),
                    MediaType          = mediaType,
                    Strategy           = ResolveStrategy.Auto,
                    BridgeIds          = bridgeDict,
                    WikidataProperties = wikidataProps,
                    IsEditionAware     = mediaType is MediaType.Books or MediaType.Audiobooks or MediaType.Music,
                    AlbumTitle         = albumHint,
                    Artist             = artistHint,
                    Title              = titleHint,
                    Author             = authorHint,
                    Year               = yearHint,
                    SeriesTitle        = seriesHint,
                }, ct);

            if (result.Found)
            {
                ctx.ResolvedQid = result.WorkQid ?? result.Qid;
                ctx.AdditionalClaims.AddRange(result.Claims);
                ctx.CollectedBridgeIds = result.CollectedBridgeIds;
                ctx.PrimaryBridgeIdType = result.PrimaryBridgeIdType;
                ctx.MatchedBy = result.MatchedBy switch
                {
                    ResolveStrategy.MusicAlbum         => "music_album",
                    ResolveStrategy.BridgeId           => "bridge_id",
                    ResolveStrategy.TextReconciliation => "text_reconciliation",
                    _                                  => null,
                };

                // Persist the resolution method as a canonical value (mirrors batch path).
                if (ctx.MatchedBy is not null)
                {
                    var canonicalMethod = ctx.MatchedBy switch
                    {
                        "bridge_id"          => "bridge",
                        "text_reconciliation"=> "text",
                        "music_album"        => "album",
                        _                    => ctx.MatchedBy,
                    };
                    ctx.AdditionalClaims.Add(new ProviderClaim(
                        MetadataFieldConstants.QidResolutionMethod, canonicalMethod, 1.0));
                }

                // Music tracks: ensure the album QID is also persisted as a
                // wikidata_qid claim on the track asset (see PollAsync Phase 5).
                if (result.MatchedBy == ResolveStrategy.MusicAlbum
                    && !string.IsNullOrWhiteSpace(ctx.ResolvedQid)
                    && !ctx.AdditionalClaims.Any(c => string.Equals(
                        c.Key, BridgeIdKeys.WikidataQid, StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.AdditionalClaims.Add(new ProviderClaim(
                        BridgeIdKeys.WikidataQid, ctx.ResolvedQid, 0.95));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ResolveAsync failed for entity {EntityId}", job.EntityId);
        }

        var allCandidates = new List<WikidataBridgeCandidate>();
        await FinaliseJobAsync(ctx, reconAdapter, allCandidates, ct);

        if (allCandidates.Count > 0)
            await _candidateRepo.InsertBatchAsync(allCandidates, ct);
    }

    internal static (string? TitleHint, string? AuthorHint, string? YearHint, string? AlbumHint, string? ArtistHint, string? SeriesHint) BuildLookupHints(
        MediaType mediaType,
        IReadOnlyList<CanonicalValue> canonicals)
    {
        static string? GetCanonical(IReadOnlyList<CanonicalValue> values, string key)
        {
            return values.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        var titleHint = GetCanonical(canonicals, MetadataFieldConstants.Title);
        var authorHint = GetCanonical(canonicals, MetadataFieldConstants.Author);
        var yearHint = GetCanonical(canonicals, MetadataFieldConstants.Year);
        string? albumHint = null;
        string? artistHint = null;
        string? seriesHint = null;

        if (mediaType == MediaType.TV)
        {
            titleHint = GetCanonical(canonicals, MetadataFieldConstants.ShowName)
                ?? GetCanonical(canonicals, MetadataFieldConstants.Series)
                ?? titleHint;
        }
        else if (mediaType == MediaType.Comics)
        {
            seriesHint = GetCanonical(canonicals, MetadataFieldConstants.Series);
            if (!string.IsNullOrWhiteSpace(seriesHint))
                titleHint = BuildComicTitleHint(seriesHint, titleHint);

            authorHint ??= GetCanonical(canonicals, "writer")
                ?? GetCanonical(canonicals, MetadataFieldConstants.Illustrator);
        }
        else if (mediaType == MediaType.Music)
        {
            albumHint = GetCanonical(canonicals, MetadataFieldConstants.Album);
            artistHint = GetCanonical(canonicals, MetadataFieldConstants.Artist)
                ?? GetCanonical(canonicals, MetadataFieldConstants.Composer)
                ?? authorHint;
            authorHint ??= artistHint;
        }

        return (titleHint, authorHint, yearHint, albumHint, artistHint, seriesHint);
    }

    private static string? BuildComicTitleHint(string seriesHint, string? titleHint)
    {
        if (string.IsNullOrWhiteSpace(titleHint))
            return seriesHint;

        if (TitleAlreadyIncludesSeries(titleHint, seriesHint))
            return titleHint;

        return $"{seriesHint} {titleHint}".Trim();
    }

    private static bool TitleAlreadyIncludesSeries(string title, string series)
    {
        var normalizedTitle = NormalizeComparableText(title);
        var normalizedSeries = NormalizeComparableText(series);

        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedSeries))
            return false;

        return normalizedTitle.Equals(normalizedSeries, StringComparison.Ordinal)
            || normalizedTitle.StartsWith(normalizedSeries + " ", StringComparison.Ordinal)
            || normalizedTitle.Contains(" " + normalizedSeries + " ", StringComparison.Ordinal)
            || normalizedTitle.EndsWith(" " + normalizedSeries, StringComparison.Ordinal);
    }

    private static string NormalizeComparableText(string text)
    {
        var chars = text
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task TryOrganizeRetainedRetailIdentityAsync(
        IdentityJob job,
        CancellationToken ct)
    {
        try
        {
            // Retained retail identity still deserves the same cover-art sidecars
            // as QID-resolved items. Run artwork against the current media path
            // before promotion so AutoOrganize can carry poster/thumb/hero into
            // the final library folder.
            await _coverArt.DownloadAndPersistAsync(job.EntityId, wikidataQid: null, ct);

            await _postPipeline.EvaluateAndOrganizeAsync(
                job.EntityId, job.Id, wikidataQid: null, job.IngestionRunId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Post-bridge organization failed for retained retail identity {EntityId} — pipeline continues",
                job.EntityId);
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
                    EntityId       = entityId,
                    EntityType     = EntityType.MediaAsset,
                    MediaType      = mediaType,
                    Title          = titleHint,
                    PreResolvedQid = qid,
                    HydrationPass  = HydrationPass.Universe,
                }, ct);

            if (fullClaims.Count > 0)
            {
                // Phase 3c: lineage-aware persist for the manual-QID flow.
                WorkLineage? lineage = null;
                try { lineage = await _workRepo.GetLineageByAssetAsync(entityId, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex,
                        "Phase 3c: lineage lookup failed for asset {EntityId} (manual QID {Qid}) — parent mirror skipped",
                        entityId, qid);
                }

                await ScoringHelper.PersistAndScoreWithLineageAsync(
                    entityId, fullClaims, reconAdapter.ProviderId, lineage,
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

    // -------------------------------------------------------------------------
    // Batch gate (D4) — computed before every poll cycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the set of ingestion run IDs that the batch gate is currently
    /// holding back from Stage 2. A run is gated when:
    ///   • <c>batch_gate.enabled</c> is true, AND
    ///   • the run's total file count is above <c>small_batch_threshold</c>, AND
    ///   • the run started less than <c>timeout_seconds</c> ago, AND
    ///   • at least one Stage 1 job (Queued or RetailSearching) still exists
    ///     for that run.
    ///
    /// Ad-hoc jobs (NULL ingestion_run_id) are always excluded from gating by
    /// <see cref="IIdentityJobRepository.LeaseNextAsync"/>, so they never appear
    /// in the pending-count query results.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetGatedRunIdsAsync(CancellationToken ct)
    {
        var gate = _configLoader.LoadCore().Pipeline.BatchGate;

        if (!gate.Enabled)
            return [];

        // Collect distinct run IDs from the current Stage 2 ready pool by
        // temporarily leasing a small probe batch and immediately releasing any
        // that are gated. To avoid that complexity, we instead look at the
        // Stage 1 pending counts directly: any run ID that GetPendingStage1CountsByRunAsync
        // reports as having pending jobs is a candidate for gating.
        //
        // We can't easily enumerate all run IDs without a dedicated query.
        // The practical approach: get the recent running batches from the batch
        // repository and filter them. This is cheap (indexed PK lookup).
        var recentBatches = await _batchRepo.GetRecentAsync(limit: 50, ct);

        // Only "running" batches are candidates — completed/failed batches have no
        // remaining Stage 1 jobs to wait for.
        var runningBatches = recentBatches
            .Where(b => string.Equals(b.Status, "running", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (runningBatches.Count == 0)
            return [];

        var timeoutCutoff = DateTimeOffset.UtcNow.AddSeconds(-gate.TimeoutSeconds);

        // Pre-filter: batches that have already timed out or are too small skip the gate.
        var candidateRunIds = runningBatches
            .Where(b => b.FilesTotal > gate.SmallBatchThreshold)
            .Where(b => b.StartedAt >= timeoutCutoff)
            .Select(b => b.Id.ToString())
            .ToList();

        if (candidateRunIds.Count == 0)
            return [];

        // Ask the job repo which of these candidate runs still have Stage 1 pending jobs.
        var pendingCounts = await _jobRepo.GetPendingStage1CountsByRunAsync(candidateRunIds, ct);

        // Only runs with at least one Stage 1 job still pending get gated.
        var gated = pendingCounts.Keys
            .Where(runId => pendingCounts[runId] > 0)
            .ToList();

        if (gated.Count > 0)
        {
            _logger.LogInformation(
                "Wikidata: gating {Count} batch(es) — Stage 1 still in progress [{RunIds}]",
                gated.Count,
                string.Join(", ", gated));
        }

        return gated;
    }

    // -------------------------------------------------------------------------
    // Phase 3b: lineage-aware Work routing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Routes Wikidata structural facts onto the correct Work row using the
    /// asset → edition → work lineage. The wikidata_qid plus any container-level
    /// bridge IDs (album collection id, etc.) get merged into the parent Work's
    /// <c>external_identifiers</c> JSON; track/episode-level identifiers go to
    /// the asset's own Work. When the claim batch contains a
    /// <c>child_entities_json</c> manifest, this also fans out to
    /// <see cref="CatalogUpsertService"/> to create catalog rows for any
    /// children Wikidata knows about but the library doesn't yet own.
    ///
    /// All work is best-effort: failures are logged but never break the
    /// surrounding pipeline.
    /// </summary>
    private async Task RouteToWorksAsync(
        WorkLineage? lineage,
        Guid assetId,
        MediaType mediaType,
        string? resolvedQid,
        IReadOnlyList<ProviderClaim> claims,
        CancellationToken ct)
    {
        if (lineage is null) return;
        try
        {

            // Build an identifier dict from the resolved QID plus any bridge-id
            // claims that came back with the Wikidata response. The router
            // partitions them by ClaimScope.
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(resolvedQid))
                ids[BridgeIdKeys.WikidataQid] = resolvedQid;

            foreach (var claim in claims)
            {
                if (string.IsNullOrWhiteSpace(claim.Key) ||
                    string.IsNullOrWhiteSpace(claim.Value))
                    continue;

                // Only route well-known external identifier keys; everything
                // else (title, year, genre, etc.) is handled by the existing
                // canonical-value persistence path.
                if (BridgeIdKeys.All.Contains(claim.Key))
                    ids.TryAdd(claim.Key, claim.Value);
            }

            if (ids.Count > 0)
            {
                var (forParent, forSelf) = _claimRouter.SplitBridgeIds(lineage, ids);

                if (forParent.Count > 0)
                    await _workRepo.WriteExternalIdentifiersAsync(
                        lineage.TargetForParentScope, forParent, ct);

                if (forSelf.Count > 0)
                    await _workRepo.WriteExternalIdentifiersAsync(
                        lineage.TargetForSelfScope, forSelf, ct);
            }

            // Catalog upsert: if Wikidata returned a child manifest, create
            // catalog rows for tracks/episodes/issues we don't own yet.
            var childJson = claims
                .FirstOrDefault(c => string.Equals(c.Key,
                    MetadataFieldConstants.ChildEntitiesJson,
                    StringComparison.OrdinalIgnoreCase))?.Value;

            if (!string.IsNullOrWhiteSpace(childJson))
            {
                try
                {
                    var inserted = await _catalogUpsert.UpsertChildrenAsync(
                        lineage.TargetForParentScope, mediaType, childJson, ct);

                    if (inserted > 0)
                        _logger.LogInformation(
                            "Wikidata: catalog upsert added {Count} {MediaType} children under parent Work {ParentWorkId}",
                            inserted, mediaType, lineage.TargetForParentScope);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Catalog upsert failed for parent Work {ParentWorkId}",
                        lineage.TargetForParentScope);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Phase 3b Work routing failed for asset {AssetId}", assetId);
        }
    }

    // -------------------------------------------------------------------------
    // Working DTOs (private to this file)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Per-job working state accumulated across phases 2–5, consumed in phase 6.
    /// </summary>
    private sealed class JobContext
    {
        public IdentityJob Job { get; }
        public MediaType MediaType { get; }
        public IReadOnlyList<BridgeIdEntry> BridgeIds { get; }
        public Dictionary<string, string> BridgeDict { get; }
        public Dictionary<string, string> WikidataProps { get; }
        public string? TitleHint { get; }
        public string? AuthorHint { get; }
        public string? YearHint { get; }
        public string? AlbumHint { get; }
        public string? ArtistHint { get; }
        public string? SeriesHint { get; }

        // Populated during Phase 5 distribution.
        public string? ResolvedQid { get; set; }
        public string? MatchedBy { get; set; }
        public string? PrimaryBridgeIdType { get; set; }
        public List<ProviderClaim> AdditionalClaims { get; } = [];
        public IReadOnlyDictionary<string, string>? CollectedBridgeIds { get; set; }

        // Populated during Phase 6 QID dedup (E1). When set, FinaliseJobAsync uses
        // these claims instead of calling FetchAsync again for this job.
        public IReadOnlyList<ProviderClaim>? PreFetchedClaims { get; set; }

        public JobContext(
            IdentityJob Job,
            MediaType MediaType,
            IReadOnlyList<BridgeIdEntry> BridgeIds,
            Dictionary<string, string> BridgeDict,
            Dictionary<string, string> WikidataProps,
            string? TitleHint,
            string? AuthorHint,
            string? YearHint,
            string? AlbumHint,
            string? ArtistHint,
            string? SeriesHint)
        {
            this.Job           = Job;
            this.MediaType     = MediaType;
            this.BridgeIds     = BridgeIds;
            this.BridgeDict    = BridgeDict;
            this.WikidataProps = WikidataProps;
            this.TitleHint     = TitleHint;
            this.AuthorHint    = AuthorHint;
            this.YearHint      = YearHint;
            this.AlbumHint     = AlbumHint;
            this.ArtistHint    = ArtistHint;
            this.SeriesHint    = SeriesHint;
        }
    }

}
