using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services.Playback;

public sealed class PlaybackTelemetryMaintenanceService(
    IPlaybackTelemetryRepository repository,
    TimeProvider clock,
    ILogger<PlaybackTelemetryMaintenanceService> logger) : BackgroundService
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(365);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            try
            {
                var now = clock.GetUtcNow();
                await repository.CloseStaleAsync(now - StaleAfter, stoppingToken).ConfigureAwait(false);
                await repository.DeleteHistoryBeforeAsync(now - Retention, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Playback telemetry maintenance failed and will retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
