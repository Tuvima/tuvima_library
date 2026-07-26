using MediaEngine.Domain.Contracts;
using MediaEngine.Storage;
using MediaEngine.Storage.Services;

namespace MediaEngine.Api.Services;

public sealed class UISettingsCacheWarmupHostedService(
    UISettingsCacheRepository cache,
    IConfigurationLoader configurationLoader,
    ILogger<UISettingsCacheWarmupHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.RebuildFromFilesAsync(configurationLoader, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "UI settings cache warm-up failed; resolver will fall back to files.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OrphanReviewQueuePurgeHostedService(
    IReviewQueueRepository reviewQueueRepository,
    ILogger<OrphanReviewQueuePurgeHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var purged = await reviewQueueRepository.PurgeOrphanedAsync(cancellationToken);
            if (purged > 0)
            {
                logger.LogInformation(
                    "Purged {Count} orphaned review queue entries",
                    purged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Orphaned review queue purge failed; counts may be inflated until next restart.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
