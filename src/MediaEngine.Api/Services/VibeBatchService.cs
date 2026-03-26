using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Contracts;

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
    private readonly ILogger<VibeBatchService> _logger;

    public VibeBatchService(
        IServiceScopeFactory scopeFactory,
        AiSettings settings,
        ILogger<VibeBatchService> logger)
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

            var delay = CronScheduler.UntilNext(
                _settings.Scheduling.VibeBatchCron,
                TimeSpan.FromHours(24));

            _logger.LogInformation("VibeBatchService: next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tagger       = scope.ServiceProvider.GetRequiredService<IVibeTagger>();
        var canonicalRepo = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();

        _logger.LogInformation("VibeBatchService: starting vibe tag generation batch");

        // Query entities that have a description but no vibe tags.
        // Full implementation would query the database for untagged entities.
        // For now, log that the batch ran.
        _logger.LogInformation("VibeBatchService: batch complete (full query wiring pending)");

        await Task.CompletedTask;
    }
}
