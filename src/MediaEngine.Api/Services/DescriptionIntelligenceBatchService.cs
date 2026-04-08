using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that runs Description Intelligence (LLM-powered structured
/// analysis of descriptions) for entities that have a description or plot summary
/// but have not yet been enriched with themes, mood, and other vocabulary.
///
/// Runs on a configurable cron schedule (default: every 15 minutes).
/// Initial delay of 3 minutes to let ingestion and hydration settle first.
/// </summary>
public sealed class DescriptionIntelligenceBatchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiSettings _settings;
    private readonly ILogger<DescriptionIntelligenceBatchService> _logger;

    public DescriptionIntelligenceBatchService(
        IServiceScopeFactory scopeFactory,
        AiSettings settings,
        ILogger<DescriptionIntelligenceBatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _settings     = settings;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay — let ingestion and hydration pipelines settle.
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DESCRIPTION-INTEL-BATCH] Batch run failed");
            }

            var delay = CronScheduler.UntilNext(
                _settings.Scheduling.DescriptionIntelligenceCron,
                TimeSpan.FromMinutes(15));

            _logger.LogInformation("[DESCRIPTION-INTEL-BATCH] Next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        if (!_settings.Features.DescriptionIntelligence)
        {
            _logger.LogDebug("[DESCRIPTION-INTEL-BATCH] Feature disabled — skipping batch");
            return;
        }

        using var scope = _scopeFactory.CreateScope();

        // Check hardware tier and select the best model available right now.
        var features = HardwareTierPolicy.GetFeatures(_settings.HardwareProfile.Tier);

        // Check if resources allow enrichment right now.
        var resourceMonitor = scope.ServiceProvider.GetRequiredService<ResourceMonitorService>();
        var modelSize = features.ScholarAvailable
            ? _settings.Models.TextScholar.SizeMB
            : _settings.Models.TextQuality.SizeMB;
        var recommendation = resourceMonitor.CanLoadModel(modelSize);
        if (!recommendation.CanLoad)
        {
            _logger.LogInformation("[DESCRIPTION-INTEL-BATCH] Deferring: {Reason}", recommendation.Reason);
            return;
        }

        _logger.LogDebug(
            "[DESCRIPTION-INTEL-BATCH] Using {Model} for enrichment (ScholarAvailable={Scholar})",
            features.ScholarAvailable ? "text_scholar (8B)" : "text_quality (3B)",
            features.ScholarAvailable);
        var canonicalRepo  = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();
        var workRepo       = scope.ServiceProvider.GetRequiredService<IWorkRepository>();
        var descIntel      = scope.ServiceProvider.GetRequiredService<IDescriptionIntelligenceService>();
        var personRecon    = scope.ServiceProvider.GetService<IPersonReconciliationService>();
        var claimRepo      = scope.ServiceProvider.GetRequiredService<IMetadataClaimRepository>();
        var scoringEngine  = scope.ServiceProvider.GetRequiredService<IScoringEngine>();
        var configLoader   = scope.ServiceProvider.GetRequiredService<IConfigurationLoader>();
        var arrayRepo      = scope.ServiceProvider.GetRequiredService<ICanonicalValueArrayRepository>();
        var searchIndex    = scope.ServiceProvider.GetRequiredService<ISearchIndexRepository>();
        var providers      = scope.ServiceProvider.GetServices<IExternalMetadataProvider>();
        var identityService = scope.ServiceProvider.GetRequiredService<IRecursiveIdentityService>();
        var harvesting     = scope.ServiceProvider.GetRequiredService<IMetadataHarvestingService>();

        var batchSize = _settings.EnrichmentBatchSize > 0 ? _settings.EnrichmentBatchSize : 10;

        // Find entities that have a description (or plot_summary) but no themes yet.
        var entityIds = await canonicalRepo.GetEntitiesNeedingEnrichmentAsync(
            hasField:    "description",
            missingField: "themes",
            limit:       batchSize,
            ct:          ct).ConfigureAwait(false);

        if (entityIds.Count == 0)
        {
            _logger.LogDebug("[DESCRIPTION-INTEL-BATCH] No entities needing enrichment");
            return;
        }

        _logger.LogInformation(
            "[DESCRIPTION-INTEL-BATCH] Processing {Count} entities needing description enrichment",
            entityIds.Count);

        int processed = 0;

        for (int i = 0; i < entityIds.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var entityId = entityIds[i];

            try
            {
                // Determine media category from canonicals for vocabulary selection.
                var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct)
                    .ConfigureAwait(false);

                var mediaCategory = canonicals
                    .FirstOrDefault(c => string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? "books";

                var diResult = await descIntel.AnalyzeAsync(entityId, mediaCategory, ct)
                    .ConfigureAwait(false);

                if (diResult is not null)
                {
                    var aiValues = new List<CanonicalValue>();
                    var now      = DateTimeOffset.UtcNow;

                    foreach (var theme in diResult.Themes)
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "themes",
                            Value        = theme,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    foreach (var mood in diResult.Mood)
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "mood",
                            Value        = mood,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Tldr))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "tldr",
                            Value        = diResult.Tldr,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Setting))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "setting",
                            Value        = diResult.Setting,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.TimePeriod))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "time_period",
                            Value        = diResult.TimePeriod,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Audience))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "audience",
                            Value        = diResult.Audience,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    foreach (var warning in diResult.ContentWarnings)
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "content_warnings",
                            Value        = warning,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Pace))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "pace",
                            Value        = diResult.Pace,
                            LastScoredAt = now,
                            WinningProviderId = WellKnownProviders.AiProvider,
                        });

                    if (aiValues.Count > 0)
                    {
                        await canonicalRepo.UpsertBatchAsync(aiValues, ct).ConfigureAwait(false);
                        processed++;

                        _logger.LogDebug(
                            "[DESCRIPTION-INTEL-BATCH] Enriched entity {EntityId} — {Themes} themes, {Mood} mood",
                            entityId, diResult.Themes.Count, diResult.Mood.Count);
                    }

                    // ── AI person signal fallback ──────────────────────────────
                    // For each person extracted from the description, check if a
                    // companion QID claim already exists. If not, run standalone
                    // person reconciliation at confidence 0.75 (lowest tier).
                    if (personRecon is not null && diResult.People.Count > 0)
                    {
                        try
                        {
                            // Get the work title for notable-work matching.
                            var titleCanonical = canonicals
                                .FirstOrDefault(c => string.Equals(c.Key, "title", StringComparison.OrdinalIgnoreCase));
                            var workTitleForSearch = titleCanonical?.Value;

                            // Get existing QID claims to avoid redundant searches.
                            var existingClaims = await claimRepo.GetByEntityAsync(entityId, ct)
                                .ConfigureAwait(false);
                            var existingQidKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var claim in existingClaims)
                            {
                                if (claim.ClaimKey.EndsWith("_qid", StringComparison.OrdinalIgnoreCase)
                                    && !string.IsNullOrWhiteSpace(claim.ClaimValue))
                                {
                                    existingQidKeys.Add(claim.ClaimKey);
                                }
                            }

                            var reconAdapter = providers
                                .OfType<ReconciliationAdapter>()
                                .FirstOrDefault();
                            var wikidataProviderId = reconAdapter?.ProviderId
                                ?? WellKnownProviders.Wikidata;

                            var aiPersonClaims = new List<ProviderClaim>();

                            foreach (var person in diResult.People)
                            {
                                if (string.IsNullOrWhiteSpace(person.Name) || person.Confidence < 0.50)
                                    continue;

                                // Normalize the AI role to match PersonReference roles.
                                var normalizedRole = NormalizeAiRole(person.Role);
                                if (normalizedRole is null)
                                    continue;

                                // Map role to QID claim key.
                                var qidKey = normalizedRole switch
                                {
                                    "Author"       => "author_qid",
                                    "Narrator"     => "narrator_qid",
                                    "Director"     => "director_qid",
                                    "Screenwriter" => "screenwriter_qid",
                                    "Composer"     => "composer_qid",
                                    "Actor"        => "cast_member_qid",
                                    _              => null,
                                };

                                if (qidKey is null) continue;

                                // Skip if a QID already exists for this role (from structured properties
                                // or Phase 3 standalone search — higher confidence tiers).
                                if (existingQidKeys.Contains(qidKey))
                                {
                                    _logger.LogDebug(
                                        "[DESCRIPTION-INTEL-BATCH] AI person '{Name}' ({Role}) skipped — {QidKey} already resolved for entity {EntityId}",
                                        person.Name, person.Role, qidKey, entityId);
                                    continue;
                                }

                                try
                                {
                                    var searchResult = await personRecon.SearchPersonAsync(
                                        person.Name, normalizedRole, workTitleForSearch, ct)
                                        .ConfigureAwait(false);

                                    if (searchResult is not null)
                                    {
                                        aiPersonClaims.Add(new ProviderClaim(
                                            qidKey,
                                            $"{searchResult.WikidataQid}::{searchResult.Name}",
                                            0.75));

                                        // Also deposit the name claim if not already present.
                                        var nameKey = qidKey.Replace("_qid", "");
                                        var hasNameClaim = existingClaims.Any(c =>
                                            string.Equals(c.ClaimKey, nameKey, StringComparison.OrdinalIgnoreCase));
                                        if (!hasNameClaim)
                                        {
                                            aiPersonClaims.Add(new ProviderClaim(nameKey, person.Name, 0.75));
                                        }

                                        _logger.LogInformation(
                                            "[DESCRIPTION-INTEL-BATCH] AI person fallback resolved: '{Name}' ({Role}) → {QID} '{WikiName}' (score={Score:F2}) for entity {EntityId}",
                                            person.Name, normalizedRole, searchResult.WikidataQid,
                                            searchResult.Name, searchResult.Score, entityId);
                                    }
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    _logger.LogWarning(ex,
                                        "[DESCRIPTION-INTEL-BATCH] AI person search failed for '{Name}' ({Role}); continuing",
                                        person.Name, person.Role);
                                }
                            }

                            // Persist AI-resolved person QID claims and create Person records.
                            if (aiPersonClaims.Count > 0)
                            {
                                // Phase 3c: lineage-aware persist so cast_member_qid /
                                // director_qid for TV episodes mirror onto the show Work.
                                WorkLineage? lineage = null;
                                try { lineage = await workRepo.GetLineageByAssetAsync(entityId, ct).ConfigureAwait(false); }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex,
                                        "[DESCRIPTION-INTEL-BATCH] Phase 3c lineage lookup failed for entity {EntityId} — parent mirror skipped",
                                        entityId);
                                }

                                await ScoringHelper.PersistAndScoreWithLineageAsync(
                                    entityId, aiPersonClaims, wikidataProviderId, lineage,
                                    claimRepo, canonicalRepo, scoringEngine, configLoader,
                                    providers, ct, arrayRepo, _logger, searchIndex).ConfigureAwait(false);

                                // Create Person records for the newly resolved QIDs.
                                var personRefs = aiPersonClaims
                                    .Where(c => c.Key.EndsWith("_qid", StringComparison.OrdinalIgnoreCase))
                                    .Select(c =>
                                    {
                                        var parts = c.Value.Split("::", 2);
                                        var qid = parts[0];
                                        var name = parts.Length > 1 ? parts[1] : qid;
                                        var role = c.Key.Replace("_qid", "") switch
                                        {
                                            "author"       => "Author",
                                            "narrator"     => "Narrator",
                                            "director"     => "Director",
                                            "screenwriter" => "Screenwriter",
                                            "composer"     => "Composer",
                                            "cast_member"  => "Actor",
                                            _              => "Author",
                                        };
                                        return new PersonReference(role, name, qid);
                                    })
                                    .ToList();

                                if (personRefs.Count > 0)
                                {
                                    var personRequests = await identityService.EnrichAsync(
                                        entityId, personRefs, ct).ConfigureAwait(false);

                                    foreach (var personReq in personRequests)
                                    {
                                        try
                                        {
                                            await harvesting.ProcessSynchronousAsync(personReq, ct)
                                                .ConfigureAwait(false);
                                        }
                                        catch (Exception ex) when (ex is not OperationCanceledException)
                                        {
                                            _logger.LogWarning(ex,
                                                "[DESCRIPTION-INTEL-BATCH] Person enrichment failed for AI-resolved person {Id}; continuing",
                                                personReq.EntityId);
                                        }
                                    }
                                }

                                _logger.LogInformation(
                                    "[DESCRIPTION-INTEL-BATCH] AI person fallback: {ClaimCount} claims, {PersonCount} persons for entity {EntityId}",
                                    aiPersonClaims.Count, personRefs.Count, entityId);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex,
                                "[DESCRIPTION-INTEL-BATCH] AI person fallback failed for entity {EntityId}; continuing",
                                entityId);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DESCRIPTION-INTEL-BATCH] Failed to process entity {Id} — skipping",
                    entityId);
            }

            // Yield resources between entities to avoid saturating the LLM.
            if (i < entityIds.Count - 1)
                await Task.Delay(2000, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "[DESCRIPTION-INTEL-BATCH] Processed {Processed}/{Total} entities",
            processed, entityIds.Count);
    }

    /// <summary>
    /// Maps AI-extracted role strings (which may be lowercase, abbreviated, or variant)
    /// to the canonical PersonReference role names.
    /// Returns null for unrecognized roles.
    /// </summary>
    private static string? NormalizeAiRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;

        return role.Trim().ToLowerInvariant() switch
        {
            "author" or "writer" or "novelist"                      => "Author",
            "narrator" or "reader" or "voice"                       => "Narrator",
            "director" or "filmmaker"                               => "Director",
            "screenwriter" or "screenplay" or "writer (screenplay)" => "Screenwriter",
            "composer" or "music" or "score"                        => "Composer",
            "actor" or "actress" or "cast" or "cast member" or "star" => "Actor",
            _                                                        => null,
        };
    }
}
