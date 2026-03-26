using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that detects works with a series name but no position,
/// and uses the AI SeriesAligner to infer their position.
/// Runs on a configurable cron schedule (default: 3 AM daily).
/// </summary>
public sealed class SeriesAlignmentBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiSettings _settings;
    private readonly ILogger<SeriesAlignmentBackgroundService> _logger;

    public SeriesAlignmentBackgroundService(
        IServiceScopeFactory scopeFactory,
        AiSettings settings,
        ILogger<SeriesAlignmentBackgroundService> logger)
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
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAlignmentAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SeriesAlignmentService batch failed");
            }

            var delay = CronScheduler.UntilNext(
                _settings.Scheduling.SeriesCheckCron,
                TimeSpan.FromHours(24));

            _logger.LogInformation("SeriesAlignmentService: next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunAlignmentAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var aligner       = scope.ServiceProvider.GetRequiredService<ISeriesAligner>();
        var canonicalRepo = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();

        _logger.LogInformation("SeriesAlignmentService: scanning for unpositioned series works");

        // Query works that have 'series' canonical value but no 'series_position'.
        // Full query wiring pending — needs a custom repository method.
        _logger.LogInformation("SeriesAlignmentService: scan complete (full query wiring pending)");

        await Task.CompletedTask;
    }
}
