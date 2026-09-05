using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Services;

namespace MediaEngine.Api.Services;

/// <summary>
/// Reconciles active ingestion batches from durable operation state even when
/// no Dashboard client is connected. This keeps batch completion independent
/// of the page that happens to be open.
/// </summary>
public sealed class IngestionBatchProgressHostedService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IIngestionBatchRepository _batches;
    private readonly BatchProgressService _progress;
    private readonly ILogger<IngestionBatchProgressHostedService> _logger;

    public IngestionBatchProgressHostedService(
        IIngestionBatchRepository batches,
        BatchProgressService progress,
        ILogger<IngestionBatchProgressHostedService> logger)
    {
        _batches = batches;
        _progress = progress;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var activeBatches = await _batches.GetActiveAsync(stoppingToken).ConfigureAwait(false);
                foreach (var batch in activeBatches)
                {
                    await _progress.EmitProgressAsync(batch.Id, isFinal: false, stoppingToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion batch progress reconciliation stopped unexpectedly.");
        }
    }
}
