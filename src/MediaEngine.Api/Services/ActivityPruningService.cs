using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that runs once daily and prunes activity log entries
/// older than the configured retention period.
///
/// Retention period is read from <c>config/maintenance.json → activity_retention_days</c>.
/// Default: 60 days.
///
/// Logs an <c>ActivityPruned</c> entry after each successful prune so the
/// maintenance tab can show when the last cleanup happened.
/// </summary>
public sealed class ActivityPruningService : BackgroundService
{
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IConfigurationLoader      _configLoader;
    private readonly ILogger<ActivityPruningService> _logger;

    /// <summary>Cron expression for the prune schedule. Default: 3 AM daily.</summary>
    private const string DefaultSchedule = "0 3 * * *";

    public ActivityPruningService(
        ISystemActivityRepository activityRepo,
        IConfigurationLoader      configLoader,
        ILogger<ActivityPruningService> logger)
    {
        ArgumentNullException.ThrowIfNull(activityRepo);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);

        _activityRepo = activityRepo;
        _configLoader = configLoader;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ActivityPruningService started — schedule: {Schedule}", DefaultSchedule);

        // Initial delay to let the rest of the app start.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var maintenance = _configLoader.LoadMaintenance();
                var retentionDays = maintenance.ActivityRetentionDays;

                if (retentionDays > 0)
                {
                    var deleted = await _activityRepo.PruneOlderThanAsync(retentionDays, stoppingToken);

                    if (deleted > 0)
                    {
                        _logger.LogInformation(
                            "Activity prune: removed {Count} entries older than {Days} days",
                            deleted, retentionDays);

                        // Log the prune itself so the maintenance tab shows when it happened.
                        await _activityRepo.LogAsync(new SystemActivityEntry
                        {
                            ActionType  = SystemActionType.ActivityPruned,
                            Detail      = $"Pruned {deleted} entries older than {retentionDays} days",
                            ChangesJson = $"{{\"deleted\":{deleted},\"retention_days\":{retentionDays}}}",
                        }, stoppingToken);
                    }
                    else
                    {
                        _logger.LogDebug("Activity prune: nothing to remove (retention={Days}d)",
                            retentionDays);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Activity prune failed; will retry next cycle");
            }

            var schedule = DefaultSchedule; // future: read from config/maintenance.json
            var delay = CronScheduler.UntilNext(schedule, TimeSpan.FromHours(24));
            _logger.LogInformation("Next activity prune at {NextRun}", DateTimeOffset.Now.Add(delay));
            await Task.Delay(delay, stoppingToken);
        }
    }
}
