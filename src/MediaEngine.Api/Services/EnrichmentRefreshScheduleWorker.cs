namespace MediaEngine.Api.Services;

/// <summary>Promotes due calendar entries into the ordinary enrichment queues.</summary>
public sealed class EnrichmentRefreshScheduleWorker : BackgroundService
{
    private readonly EnrichmentRefreshScheduleService _schedule;
    private readonly ILogger<EnrichmentRefreshScheduleWorker> _logger;

    public EnrichmentRefreshScheduleWorker(
        EnrichmentRefreshScheduleService schedule,
        ILogger<EnrichmentRefreshScheduleWorker> logger)
    {
        _schedule = schedule;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var queued = await _schedule.QueueDueAsync(50, stoppingToken).ConfigureAwait(false);
                if (queued > 0)
                    _logger.LogInformation("Queued {Count} scheduled enrichment refreshes", queued);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled enrichment refresh sweep failed");
            }
        }
    }
}
