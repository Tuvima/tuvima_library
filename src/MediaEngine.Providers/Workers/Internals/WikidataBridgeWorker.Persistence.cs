using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Domain.Configuration;
using Microsoft.Extensions.Logging;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Workers;

public sealed partial class WikidataBridgeWorker
{
    private async Task TryOrganizeRetainedRetailIdentityAsync(
        IdentityJob job,
        CancellationToken ct)
    {
        try
        {
            if (_personEnrichment is not null)
            {
                try
                {
                    await _personEnrichment.EnrichFromClaimsAsync(job.EntityId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex,
                        "Retained retail identity person enrichment failed for {EntityId}; continuing with artwork and organization",
                        job.EntityId);
                }
            }

            // Retained retail identity still deserves the same cover-art sidecars
            // as QID-resolved items. Run artwork against the current media path
            // before promotion so AutoOrganize can carry poster/thumb/hero into
            // the final library folder.
            await _coverArt.DownloadAndPersistAsync(job.EntityId, wikidataQid: null, ct);

            var organized = await _postPipeline.EvaluateAndOrganizeAsync(
                job.EntityId, job.Id, wikidataQid: null, job.IngestionRunId, ct,
                retainedRetailIdentity: true);
            if (organized)
            {
                if (_collectionFinalization is not null)
                {
                    await _collectionFinalization.FinalizeAsync(
                        job.EntityId,
                        CollectionFinalizationReason.RetainedRetailIdentity,
                        job.IngestionRunId,
                        ct).ConfigureAwait(false);
                }

                await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.ReadyWithoutUniverse, ct: ct);
            }
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
        var languageHint = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Language,
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
                    FileLanguage   = languageHint,
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
                    arrayRepo: _arrayRepo, logger: _logger);
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

    private async Task<MediaOperation?> EnsureBridgeOperationAsync(IdentityJob job, string stage, CancellationToken ct)
    {
        if (_operationTracker is null)
            return null;

        try
        {
            return await _operationTracker.EnsureQueuedAsync(new MediaOperation
            {
                OperationType = MediaOperationType.IdentityWikidataBridge,
                OperationKind = MediaOperationKind.Identity,
                EntityId = job.EntityId,
                EntityKind = "asset",
                BatchId = job.IngestionRunId,
                CapabilityId = CapabilityId.IdentityWikidataBridge,
                CapabilityVersion = WikidataLibraryInfo.PackageVersion,
                ProviderId = "wikidata",
                Status = MediaOperationStatus.Queued,
                Stage = stage,
                QueueName = "identity",
                IdempotencyKey = $"identity:{job.EntityId}:wikidata_bridge:{WikidataLibraryInfo.PackageVersion}"
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not ensure Wikidata bridge operation for job {JobId}", job.Id);
            return null;
        }
    }

    private async Task UpdateBridgeOperationStageAsync(
        MediaOperation? operation,
        string stage,
        int progressPercent,
        string message,
        CancellationToken ct,
        object? detail = null)
    {
        if (_operationTracker is null || operation is null)
            return;

        try
        {
            await _operationTracker.UpdateStageAsync(operation.Id, stage, progressPercent, message, detail, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not update Wikidata bridge operation {OperationId}", operation.Id);
        }
    }

    private async Task MarkBridgeSucceededAsync(MediaOperation? operation, IdentityJob job, string qid, CancellationToken ct)
    {
        if (_operationTracker is not null && operation is not null)
        {
            try
            {
                await _operationTracker.MarkSucceededAsync(operation.Id, $"Resolved QID {qid}", new { qid }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not complete Wikidata bridge operation {OperationId}", operation.Id);
            }
        }

        if (_capabilityStates is not null)
        {
            await _capabilityStates.MarkSucceededAsync(job.EntityId, CapabilityId.IdentityWikidataBridge, null,
                new CapabilityStateResult(
                    Source: "wikidata",
                    Confidence: 1.0,
                    ArtifactCount: 1,
                    ArtifactSummary: qid,
                    ResultSummary: $"Resolved QID {qid}",
                    OperationId: operation?.Id), ct).ConfigureAwait(false);
        }
    }

    private async Task MarkBridgeNoResultAsync(MediaOperation? operation, IdentityJob job, string reason, CancellationToken ct)
    {
        if (_operationTracker is not null && operation is not null)
        {
            try { await _operationTracker.MarkNoResultAsync(operation.Id, reason, null, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not mark Wikidata bridge operation no-result {OperationId}", operation.Id);
            }
        }

        if (_capabilityStates is not null)
            await _capabilityStates.MarkNoResultAsync(job.EntityId, CapabilityId.IdentityWikidataBridge, null, reason, ct).ConfigureAwait(false);
    }

    private async Task MarkBridgeBlockedAsync(MediaOperation? operation, IdentityJob job, string reason, CancellationToken ct)
    {
        if (_operationTracker is not null && operation is not null)
        {
            try { await _operationTracker.MarkBlockedAsync(operation.Id, reason, null, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not mark Wikidata bridge operation blocked {OperationId}", operation.Id);
            }
        }

        if (_capabilityStates is not null)
            await _capabilityStates.MarkBlockedAsync(job.EntityId, CapabilityId.IdentityWikidataBridge, null, reason, ct).ConfigureAwait(false);
    }

    private async Task MarkBridgeFailedAsync(MediaOperation? operation, IdentityJob job, string error, bool terminal, CancellationToken ct)
    {
        if (_operationTracker is not null && operation is not null)
        {
            try
            {
                await _operationTracker.MarkFailedAsync(operation.Id, new InvalidOperationException(error), terminal, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not mark Wikidata bridge operation failed {OperationId}", operation.Id);
            }
        }

        if (_capabilityStates is not null)
            await _capabilityStates.MarkFailedAsync(job.EntityId, CapabilityId.IdentityWikidataBridge, null, error, terminal, ct).ConfigureAwait(false);
    }

    private async Task MarkBridgeCapabilityQueuedAsync(IdentityJob job, MediaOperation? operation, CancellationToken ct)
    {
        if (_capabilityStates is null)
            return;

        await _capabilityStates.EnsureAsync(new EntityCapabilityState
        {
            EntityId = job.EntityId,
            EntityKind = "asset",
            MediaType = job.MediaType,
            CapabilityId = CapabilityId.IdentityWikidataBridge,
            CapabilityKind = MediaOperationKind.Identity,
            CapabilityVersion = WikidataLibraryInfo.PackageVersion,
            Status = EntityCapabilityStatus.Queued,
            Requiredness = CapabilityRequiredness.Optional,
            LastOperationId = operation?.Id
        }, ct).ConfigureAwait(false);

        if (operation is not null)
            await _capabilityStates.MarkQueuedAsync(job.EntityId, CapabilityId.IdentityWikidataBridge, null, operation.Id, ct).ConfigureAwait(false);
    }

    private async Task MarkBridgeCapabilityRunningAsync(IdentityJob job, MediaOperation? operation, CancellationToken ct)
    {
        if (_capabilityStates is not null && operation is not null)
            await _capabilityStates.MarkRunningAsync(job.EntityId, CapabilityId.IdentityWikidataBridge, null, operation.Id, ct).ConfigureAwait(false);
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
                if (BridgeIdKeys.All.Contains(claim.Key)
                    && IsBridgeIdCompatibleWithMediaType(claim.Key, mediaType))
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

    private static bool IsBridgeIdCompatibleWithMediaType(string key, MediaType mediaType)
    {
        if (string.Equals(key, BridgeIdKeys.ComicVineId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, BridgeIdKeys.ComicVineVolumeId, StringComparison.OrdinalIgnoreCase))
        {
            return mediaType == MediaType.Comics;
        }

        if (string.Equals(key, BridgeIdKeys.TmdbEpisodeId, StringComparison.OrdinalIgnoreCase))
            return mediaType == MediaType.TV;

        return true;
    }

    private static Guid ResolveBridgeIdEntityId(WorkLineage? lineage, Guid assetId, string key)
    {
        if (lineage is null)
            return assetId;

        return ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : lineage.TargetForSelfScope;
    }

    private async Task RunPostIdentityPersonPassAsync(Guid entityId, string qid, CancellationToken ct)
    {
        if (_personEnrichment is null)
            return;

        try
        {
            await _personEnrichment.EnrichFromClaimsAsync(entityId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Post-identity person enrichment failed for entity {EntityId} ({Qid})",
                entityId,
                qid);
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
        public string? LanguageHint { get; }
        public int? SeasonNumber { get; }
        public int? EpisodeNumber { get; }
        public string? IssueNumber { get; }

        // Populated during Phase 5 distribution.
        public string? ResolvedQid { get; set; }
        public string? MatchedBy { get; set; }
        public string? PrimaryBridgeIdType { get; set; }
        public List<ProviderClaim> AdditionalClaims { get; } = [];
        public IReadOnlyDictionary<string, string>? CollectedBridgeIds { get; set; }
        public MediaOperation? Operation { get; set; }

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
            string? SeriesHint,
            string? LanguageHint,
            int? SeasonNumber,
            int? EpisodeNumber,
            string? IssueNumber)
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
            this.LanguageHint  = LanguageHint;
            this.SeasonNumber  = SeasonNumber;
            this.EpisodeNumber = EpisodeNumber;
            this.IssueNumber   = IssueNumber;
        }
    }

}
