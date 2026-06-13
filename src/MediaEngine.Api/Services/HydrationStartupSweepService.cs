using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Services;

namespace MediaEngine.Api.Services;

/// <summary>
/// One-shot startup recovery for durable identity jobs interrupted by an Engine
/// restart. The database is the queue; this service only clears leases from the
/// previous process and wakes the current workers.
/// </summary>
public sealed class HydrationStartupSweepService : BackgroundService
{
    private readonly IIdentityJobRepository _jobs;
    private readonly IIngestionBatchRepository _batches;
    private readonly BatchProgressService _batchProgress;
    private readonly IIdentityPipelineSignal _signal;
    private readonly ILogger<HydrationStartupSweepService> _logger;

    public HydrationStartupSweepService(
        IIdentityJobRepository jobs,
        IIngestionBatchRepository batches,
        BatchProgressService batchProgress,
        IIdentityPipelineSignal signal,
        ILogger<HydrationStartupSweepService> logger)
    {
        _jobs = jobs;
        _batches = batches;
        _batchProgress = batchProgress;
        _signal = signal;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var recovered = await _jobs.RecoverInterruptedJobsAsync(cancellationToken).ConfigureAwait(false);
            var recentRunningBatches = (await _batches.GetRecentAsync(50, cancellationToken).ConfigureAwait(false))
                .Where(batch => string.Equals(batch.Status, "running", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var batch in recentRunningBatches)
            {
                await _batchProgress.EmitProgressAsync(batch.Id, isFinal: false, ct: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (recovered == 0)
            {
                _logger.LogInformation("Identity startup recovery found no interrupted jobs.");
                var abandoned = await _batches.AbandonRunningAsync(cancellationToken).ConfigureAwait(false);
                if (abandoned > 0)
                {
                    _logger.LogInformation(
                        "Identity startup recovery marked {Count} stale running batch(es) abandoned.",
                        abandoned);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Identity startup recovery released {Count} interrupted job lease(s).",
                    recovered);

                _signal.Signal(IdentityPipelineSignalKind.Retail);
                _signal.Signal(IdentityPipelineSignalKind.WikidataBridge);
                _signal.Signal(IdentityPipelineSignalKind.Hydration);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Identity startup recovery failed.");
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
