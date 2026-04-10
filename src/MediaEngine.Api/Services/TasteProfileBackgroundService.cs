using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that builds and updates user taste profiles
/// from library composition data. Runs weekly (default: Sunday 5 AM).
/// </summary>
public sealed class TasteProfileBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiSettings _settings;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<TasteProfileBackgroundService> _logger;

    public TasteProfileBackgroundService(
        IServiceScopeFactory scopeFactory,
        AiSettings settings,
        IConfigurationLoader configLoader,
        ILogger<TasteProfileBackgroundService> logger)
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
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BuildProfilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TasteProfileService failed");
            }

            var maintenance = _configLoader.LoadMaintenance();
            var cron = maintenance.Schedules.TryGetValue("taste_profile_update", out var s) ? s : "0 5 * * 0";
            var delay = CronScheduler.UntilNext(cron, TimeSpan.FromDays(7));

            _logger.LogInformation("TasteProfileService: next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task BuildProfilesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var profiler = scope.ServiceProvider.GetRequiredService<ITasteProfiler>();

        _logger.LogInformation("TasteProfileService: starting taste profile generation");

        // Query all user profiles and rebuild taste data.
        // Full implementation pending user_taste_profiles migration.
        _logger.LogInformation("TasteProfileService: complete (migration M-058 pending)");

        await Task.CompletedTask;
    }
}
