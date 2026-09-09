namespace MediaEngine.Api.Services.View;

public sealed class ViewSharedContributionHostedService(
    IViewSharedContributionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ViewSharedContributionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<Guid> recoverable;
        using (var scope = scopeFactory.CreateScope())
        {
            recoverable = scope.ServiceProvider.GetRequiredService<ViewSharedContributionService>()
                .GetRecoverableIds(stoppingToken);
        }

        foreach (var id in recoverable)
        {
            await ProcessAsync(id, stoppingToken);
        }

        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(id, stoppingToken);
        }
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ViewSharedContributionService>()
                .ProcessAsync(id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Shared Library contribution {ContributionId} could not be processed.", id);
        }
    }
}
