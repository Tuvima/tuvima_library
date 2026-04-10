using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that generates vibe/mood tags for entities
/// using Wikipedia summaries and per-category controlled vocabulary.
/// Runs on a configurable cron schedule (default: 4 AM daily).
/// </summary>
public sealed class VibeBatchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiSettings _settings;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<VibeBatchService> _logger;

    public VibeBatchService(
        IServiceScopeFactory scopeFactory,
        AiSettings settings,
        IConfigurationLoader configLoader,
        ILogger<VibeBatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _settings     = settings;
        _configLoader = configLoader;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay — let the Engine fully start.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VibeBatchService batch failed");
            }

            var maintenance = _configLoader.LoadMaintenance();
            var cron = maintenance.Schedules.TryGetValue("vibe_batch", out var s) ? s : "0 4 * * *";
            var delay = CronScheduler.UntilNext(cron, TimeSpan.FromHours(24));

            _logger.LogInformation("VibeBatchService: next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tagger        = scope.ServiceProvider.GetRequiredService<IVibeTagger>();
        var canonicalRepo = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();
        var registryRepo  = scope.ServiceProvider.GetRequiredService<IRegistryRepository>();

        _logger.LogInformation("VibeBatchService: scanning for entities needing vibe tags");

        // Page through the registry to find entities (up to 200 candidates per batch).
        // We load a larger set then filter in-memory for those missing vibe tags.
        var page = await registryRepo.GetPageAsync(
            new RegistryQuery(Offset: 0, Limit: 200, Sort: "newest"), ct);

        int tagged = 0;

        foreach (var item in page.Items)
        {
            if (ct.IsCancellationRequested) break;
            if (tagged >= 50) break; // Batch cap.

            var canonicals = await canonicalRepo.GetByEntityAsync(item.EntityId, ct);

            // Skip if already has vibe tags.
            if (canonicals.Any(c => string.Equals(c.Key, "vibe", StringComparison.OrdinalIgnoreCase)))
                continue;

            // Need a description to generate vibes.
            var description = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "description", StringComparison.OrdinalIgnoreCase));
            if (description is null) continue;

            var genres = canonicals
                .Where(c => string.Equals(c.Key, "genre", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value)
                .ToList();

            var mediaType = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase));

            var title = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "title", StringComparison.OrdinalIgnoreCase));

            try
            {
                var tags = await tagger.TagAsync(
                    title?.Value ?? "Unknown",
                    description.Value,
                    genres,
                    mediaType?.Value ?? "books",
                    ct);

                if (tags.Count > 0)
                {
                    // Store each tag as a separate vibe canonical value.
                    var vibeValues = tags.Select(tag => new CanonicalValue
                    {
                        EntityId     = item.EntityId,
                        Key          = "vibe",
                        Value        = tag,
                        LastScoredAt = DateTimeOffset.UtcNow,
                    }).ToList();

                    await canonicalRepo.UpsertBatchAsync(vibeValues, ct);
                    tagged++;

                    _logger.LogDebug(
                        "VibeBatchService: tagged entity {EntityId} with {Count} vibe tag(s)",
                        item.EntityId, tags.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VibeBatchService: failed to tag entity {Id}", item.EntityId);
            }
        }

        _logger.LogInformation("VibeBatchService: tagged {Count} entities", tagged);
    }
}
