using MediaEngine.AI.Configuration;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

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
        var canonicalRepo  = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();
        var descIntel      = scope.ServiceProvider.GetRequiredService<IDescriptionIntelligenceService>();

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
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    foreach (var mood in diResult.Mood)
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "mood",
                            Value        = mood,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Tldr))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "tldr",
                            Value        = diResult.Tldr,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Setting))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "setting",
                            Value        = diResult.Setting,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.TimePeriod))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "time_period",
                            Value        = diResult.TimePeriod,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Audience))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "audience",
                            Value        = diResult.Audience,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    foreach (var warning in diResult.ContentWarnings)
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "content_warnings",
                            Value        = warning,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    if (!string.IsNullOrWhiteSpace(diResult.Pace))
                        aiValues.Add(new CanonicalValue
                        {
                            EntityId     = entityId,
                            Key          = "pace",
                            Value        = diResult.Pace,
                            LastScoredAt = now,
                            WinningProviderId = MetadataFieldConstants.AiProviderId,
                        });

                    if (aiValues.Count > 0)
                    {
                        await canonicalRepo.UpsertBatchAsync(aiValues, ct).ConfigureAwait(false);
                        processed++;

                        _logger.LogDebug(
                            "[DESCRIPTION-INTEL-BATCH] Enriched entity {EntityId} — {Themes} themes, {Mood} mood",
                            entityId, diResult.Themes.Count, diResult.Mood.Count);
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
}
