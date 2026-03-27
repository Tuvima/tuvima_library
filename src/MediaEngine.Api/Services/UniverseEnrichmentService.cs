using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Stage 3: Universe Enrichment background service.
///
/// Runs on a cron schedule (default: 3 AM daily) to enrich works that have
/// confirmed Wikidata QIDs with fictional entity data, relationship graph
/// edges, and images.  Absorbs the work previously done inline by Pass 2
/// in HydrationPipelineService.
///
/// Per-work pipeline:
///   1. Fictional entity discovery (IRecursiveFictionalEntityService)
///   2. Entity property fetch (ReconciliationAdapter.LookupFictionalEntityAsync)
///   3. Relationship population (IRelationshipPopulationService)
///   4. Image enrichment (IImageEnrichmentService)
///   5. Narrative root resolution (INarrativeRootResolver)
///   6. Lore Delta: stale entity refresh via Wikidata revision checks
/// </summary>
public sealed class UniverseEnrichmentService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UniverseEnrichmentService> _logger;

    /// <summary>Set by <see cref="TriggerManualSweep"/> to skip the next cron wait.</summary>
    private readonly ManualResetEventSlim _manualTrigger = new(initialState: false);

    public UniverseEnrichmentService(
        IServiceScopeFactory scopeFactory,
        ILogger<UniverseEnrichmentService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <summary>
    /// Signals the background loop to start a sweep immediately rather than
    /// waiting for the next cron slot. Used by the manual trigger API endpoint.
    /// </summary>
    public void TriggerManualSweep()
    {
        _logger.LogInformation("[UNIVERSE-ENRICH] Manual sweep triggered");
        _manualTrigger.Set();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay — let ingestion, hydration, and AI services settle.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UNIVERSE-ENRICH] Sweep failed");
            }

            // Reset manual trigger before computing next scheduled delay.
            _manualTrigger.Reset();

            // Reload config each iteration so schedule changes take effect.
            string cron;
            try
            {
                using var configScope = _scopeFactory.CreateScope();
                var configLoader = configScope.ServiceProvider.GetRequiredService<IConfigurationLoader>();
                var config = configLoader.LoadHydration();
                cron = config.Stage3ScheduleCron;
            }
            catch
            {
                cron = "0 3 * * *";
            }

            if (string.IsNullOrWhiteSpace(cron)) cron = "0 3 * * *";

            var delay = CronScheduler.UntilNext(cron, TimeSpan.FromHours(24));
            _logger.LogInformation("[UNIVERSE-ENRICH] Next sweep in {Delay}", delay);

            // Wait for either the cron delay or a manual trigger, whichever comes first.
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(delay);
                await Task.Run(() => _manualTrigger.Wait(cts.Token), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested) break;
                // Cron delay elapsed normally — continue to next sweep.
            }
        }
    }

    private async Task ProcessSweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var configLoader = scope.ServiceProvider.GetRequiredService<IConfigurationLoader>();
        var config = configLoader.LoadHydration();

        if (!config.Stage3Enabled)
        {
            _logger.LogDebug("[UNIVERSE-ENRICH] Stage 3 disabled — skipping sweep");
            return;
        }

        var canonicalRepo  = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();
        var entityRepo     = scope.ServiceProvider.GetRequiredService<IFictionalEntityRepository>();
        var entityService  = scope.ServiceProvider.GetRequiredService<IRecursiveFictionalEntityService>();
        var relationshipSvc = scope.ServiceProvider.GetRequiredService<IRelationshipPopulationService>();
        var narrativeRoot  = scope.ServiceProvider.GetRequiredService<INarrativeRootResolver>();
        var imageService   = scope.ServiceProvider.GetRequiredService<IImageEnrichmentService>();
        var reconAdapter   = scope.ServiceProvider.GetService<ReconciliationAdapter>();

        var maxItems    = config.Stage3MaxItemsPerSweep > 0 ? config.Stage3MaxItemsPerSweep : 50;
        var refreshDays = config.Stage3RefreshDays > 0 ? config.Stage3RefreshDays : 30;
        var maxDepth    = config.Stage3MaxDepth > 0 ? config.Stage3MaxDepth : 2;
        var rateLimitMs = config.Stage3RateLimitMs > 0 ? config.Stage3RateLimitMs : 3000;

        _logger.LogInformation(
            "[UNIVERSE-ENRICH] Starting Stage 3 sweep (max {Max} items, refresh {Days}d, depth {Depth})",
            maxItems, refreshDays, maxDepth);

        // Find works with confirmed Wikidata QIDs.
        // Strategy: get entities that have a "wikidata_qid" canonical value (confirmed identity).
        // We check their fictional entities' enriched_at to decide eligibility.
        var entitiesWithQid = await canonicalRepo.GetEntitiesNeedingEnrichmentAsync(
            hasField:    "wikidata_qid",
            missingField: "__stage3_universe_enriched__",   // Synthetic: no entity has this field yet
            limit:       maxItems * 2,                       // Over-fetch to allow filtering
            ct:          ct).ConfigureAwait(false);

        if (entitiesWithQid.Count == 0)
        {
            _logger.LogDebug("[UNIVERSE-ENRICH] No entities with confirmed QIDs found");
            return;
        }

        int processed = 0;
        int skipped = 0;
        var staleThreshold = DateTimeOffset.UtcNow.AddDays(-refreshDays);

        for (int i = 0; i < entitiesWithQid.Count && processed < maxItems; i++)
        {
            if (ct.IsCancellationRequested) break;

            var entityId = entitiesWithQid[i];

            try
            {
                // Get the work's canonical values to extract QID and title.
                var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct)
                    .ConfigureAwait(false);

                var qidValue = canonicals
                    .FirstOrDefault(c => string.Equals(c.Key, "wikidata_qid", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                if (string.IsNullOrWhiteSpace(qidValue) || !qidValue.StartsWith('Q'))
                {
                    skipped++;
                    continue;
                }

                // Check if this work's universe data was already enriched recently.
                var lastEnriched = canonicals
                    .FirstOrDefault(c => string.Equals(c.Key, "stage3_enriched_at", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                if (!string.IsNullOrWhiteSpace(lastEnriched)
                    && DateTimeOffset.TryParse(lastEnriched, out var lastDate)
                    && lastDate > staleThreshold)
                {
                    skipped++;
                    continue;
                }

                var workTitle = canonicals
                    .FirstOrDefault(c => string.Equals(c.Key, "title", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                _logger.LogDebug(
                    "[UNIVERSE-ENRICH] Processing work {Title} ({QID}) [{Index}/{Total}]",
                    workTitle ?? "(untitled)", qidValue, processed + 1, maxItems);

                // ── Step 1: Narrative root resolution ─────────────────────────
                var root = await narrativeRoot.ResolveAsync(entityId, ct)
                    .ConfigureAwait(false);

                var narrativeRootQid   = root?.Qid;
                var narrativeRootLabel = root?.Label;

                // ── Step 2: Fictional entity discovery ────────────────────────
                if (reconAdapter is not null && !string.IsNullOrWhiteSpace(narrativeRootQid))
                {
                    try
                    {
                        // Discover fictional entity references from the work's claims.
                        var entityRefs = ExtractFictionalEntityRefs(canonicals);

                        if (entityRefs.Count > 0)
                        {
                            await entityService.EnrichAsync(
                                qidValue, workTitle,
                                narrativeRootQid, narrativeRootLabel,
                                entityRefs, ct).ConfigureAwait(false);

                            _logger.LogDebug(
                                "[UNIVERSE-ENRICH] Discovered {Count} fictional entities for {QID}",
                                entityRefs.Count, qidValue);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "[UNIVERSE-ENRICH] Fictional entity discovery failed for {QID}; continuing",
                            qidValue);
                    }
                }

                // ── Step 3: Image enrichment ──────────────────────────────────
                try
                {
                    await imageService.EnrichWorkImagesAsync(entityId, qidValue, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "[UNIVERSE-ENRICH] Image enrichment failed for {QID}; continuing",
                        qidValue);
                }

                // ── Step 4: Mark Stage 3 complete ─────────────────────────────
                await canonicalRepo.UpsertBatchAsync(
                [
                    new Domain.Entities.CanonicalValue
                    {
                        EntityId          = entityId,
                        Key               = "stage3_enriched_at",
                        Value             = DateTimeOffset.UtcNow.ToString("o"),
                        LastScoredAt      = DateTimeOffset.UtcNow,
                        WinningProviderId = Guid.Empty,  // System marker
                    }
                ], ct).ConfigureAwait(false);

                processed++;

                _logger.LogDebug(
                    "[UNIVERSE-ENRICH] Completed {Title} ({QID})",
                    workTitle ?? "(untitled)", qidValue);

                // Rate limit between works.
                if (i < entitiesWithQid.Count - 1 && processed < maxItems)
                    await Task.Delay(rateLimitMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[UNIVERSE-ENRICH] Failed to process entity {Id} — skipping",
                    entityId);
            }
        }

        _logger.LogInformation(
            "[UNIVERSE-ENRICH] Sweep complete — {Processed} works processed, {Skipped} skipped",
            processed, skipped);

        // ── Lore Delta: refresh stale fictional entities ───────────────────
        await ProcessLoreDeltaAsync(scope, entityRepo, refreshDays, maxItems, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Checks fictional entities whose <c>enriched_at</c> is older than
    /// <paramref name="refreshDays"/> days against current Wikidata revision IDs.
    /// Entities with upstream changes have their <c>enriched_at</c> cleared so
    /// the next sweep re-enriches them.  Emits a <c>LoreDeltaDiscovered</c>
    /// SignalR event when changes are found.
    /// </summary>
    private async Task ProcessLoreDeltaAsync(
        IServiceScope scope,
        IFictionalEntityRepository entityRepo,
        int refreshDays,
        int maxItems,
        CancellationToken ct)
    {
        var loreDeltaService = scope.ServiceProvider.GetService<ILoreDeltaService>();
        var eventPublisher   = scope.ServiceProvider.GetService<IEventPublisher>();

        if (loreDeltaService is null)
        {
            _logger.LogDebug("[LORE-DELTA] ILoreDeltaService not registered — skipping stale check");
            return;
        }

        IReadOnlyList<Domain.Entities.FictionalEntity> staleEntities;
        try
        {
            staleEntities = await entityRepo.GetStaleEntitiesAsync(refreshDays, maxItems, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LORE-DELTA] Failed to query stale entities");
            return;
        }

        if (staleEntities.Count == 0)
        {
            _logger.LogDebug("[LORE-DELTA] No stale entities found (threshold: {Days}d)", refreshDays);
            return;
        }

        _logger.LogInformation(
            "[LORE-DELTA] Checking {Count} stale entities for Wikidata revision changes",
            staleEntities.Count);

        // Group by universe so we can batch-call LoreDeltaService per universe.
        var byUniverse = staleEntities
            .Where(e => !string.IsNullOrWhiteSpace(e.FictionalUniverseQid))
            .GroupBy(e => e.FictionalUniverseQid!)
            .ToList();

        int totalChanged = 0;

        foreach (var group in byUniverse)
        {
            if (ct.IsCancellationRequested) break;

            var universeQid = group.Key;

            try
            {
                var deltaResults = await loreDeltaService.CheckForUpdatesAsync(universeQid, ct)
                    .ConfigureAwait(false);

                int changedInUniverse = 0;

                foreach (var result in deltaResults.Where(r => r.HasChanged))
                {
                    // Find the matching entity by QID.
                    var entity = staleEntities.FirstOrDefault(
                        e => string.Equals(e.WikidataQid, result.EntityQid, StringComparison.OrdinalIgnoreCase));

                    if (entity is null) continue;

                    // Mark for re-enrichment by clearing enriched_at.
                    await entityRepo.UpdateEnrichmentAsync(
                        entity.Id,
                        description: entity.Description,
                        imageUrl:    entity.ImageUrl,
                        enrichedAt:  DateTimeOffset.MinValue,   // Signals "needs re-enrichment"
                        ct).ConfigureAwait(false);

                    changedInUniverse++;
                    totalChanged++;

                    _logger.LogDebug(
                        "[LORE-DELTA] Marked {Label} ({Qid}) for re-enrichment (rev {Cached} → {Current})",
                        result.Label, result.EntityQid, result.CachedRevision, result.CurrentRevision);
                }

                // Emit SignalR event if changes were found in this universe.
                if (changedInUniverse > 0 && eventPublisher is not null)
                {
                    try
                    {
                        await eventPublisher.PublishAsync(
                            "LoreDeltaDiscovered",
                            new { UniverseQid = universeQid, ChangedCount = changedInUniverse },
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[LORE-DELTA] Failed to publish LoreDeltaDiscovered event");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[LORE-DELTA] Lore Delta check failed for universe {Qid} — skipping",
                    universeQid);
            }
        }

        if (totalChanged > 0)
        {
            _logger.LogInformation(
                "[LORE-DELTA] {Changed} entities marked for re-enrichment across {Universes} universes",
                totalChanged, byUniverse.Count);
        }
        else
        {
            _logger.LogDebug("[LORE-DELTA] No Wikidata changes detected in {Count} stale entities",
                staleEntities.Count);
        }
    }

    /// <summary>
    /// Extracts fictional entity references from a work's canonical values.
    /// Looks for P674 (characters), P840 (narrative location), and similar
    /// Wikidata property keys that contain QID::Label pairs.
    /// </summary>
    private static IReadOnlyList<Domain.Models.FictionalEntityReference> ExtractFictionalEntityRefs(
        IReadOnlyList<Domain.Entities.CanonicalValue> canonicals)
    {
        var refs = new List<Domain.Models.FictionalEntityReference>();

        foreach (var cv in canonicals)
        {
            string? entitySubType = cv.Key switch
            {
                "character"          => "Character",
                "characters"         => "Character",
                "narrative_location" => "Location",
                "location"           => "Location",
                "organization"       => "Organization",
                _                    => null,
            };

            if (entitySubType is null) continue;
            if (string.IsNullOrWhiteSpace(cv.Value)) continue;

            // Values may be in QID::Label format.
            var parts = cv.Value.Split("::", 2);
            var qid   = parts[0].Trim();
            var label = parts.Length > 1 ? parts[1].Trim() : qid;

            if (!qid.StartsWith('Q')) continue;

            refs.Add(new Domain.Models.FictionalEntityReference(qid, label, entitySubType));
        }

        return refs;
    }
}
