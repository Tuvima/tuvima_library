using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Storage.Contracts;

namespace Tanaste.Api.Services;

/// <summary>
/// Background service that runs once daily and prunes activity log entries
/// older than the configured retention period.
///
/// Retention period is read from <c>tanaste_master.json → maintenance → activity_retention_days</c>.
/// Default: 60 days.
///
/// Logs an <c>ActivityPruned</c> entry after each successful prune so the
/// maintenance tab can show when the last cleanup happened.
/// </summary>
public sealed class ActivityPruningService : BackgroundService
{
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IStorageManifest          _manifest;
    private readonly ILogger<ActivityPruningService> _logger;

    /// <summary>How often the prune runs. Default: once per day.</summary>
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    public ActivityPruningService(
        ISystemActivityRepository activityRepo,
        IStorageManifest          manifest,
        ILogger<ActivityPruningService> logger)
    {
        ArgumentNullException.ThrowIfNull(activityRepo);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(logger);

        _activityRepo = activityRepo;
        _manifest     = manifest;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ActivityPruningService started — checking every {Hours}h",
            PruneInterval.TotalHours);

        // Initial delay to let the rest of the app start.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = _manifest.Load();
                var retentionDays = config.Maintenance.ActivityRetentionDays;

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

            await Task.Delay(PruneInterval, stoppingToken);
        }
    }
}
