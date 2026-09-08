namespace MediaEngine.Api.Services.View;

public sealed class ViewSharedContributionHostedService(
    ViewSharedContributionService contributions,
    ILogger<ViewSharedContributionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var id in contributions.GetRecoverableIds(stoppingToken))
            await ProcessAsync(id, stoppingToken);

        await foreach (var id in contributions.ReadQueueAsync(stoppingToken))
            await ProcessAsync(id, stoppingToken);
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        try { await contributions.ProcessAsync(id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Shared Library contribution {ContributionId} could not be processed.", id);
        }
    }
}
