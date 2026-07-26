using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Owns the common lifecycle for one durable identity-pipeline stage.
/// The database remains the source of truth; signals only shorten the idle wait.
/// </summary>
/// <typeparam name="TWorker">The scoped worker that processes one pipeline stage.</typeparam>
public abstract class PipelineStageHostedService<TWorker> : BackgroundService
    where TWorker : notnull
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReclaimInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIdentityPipelineSignal _signal;
    private DateTimeOffset _nextReclaimAt = DateTimeOffset.UtcNow;

    protected PipelineStageHostedService(
        IServiceScopeFactory scopeFactory,
        IIdentityPipelineSignal signal,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    protected abstract IdentityPipelineSignalKind WakeSignal { get; }

    protected abstract TimeSpan StuckJobThreshold { get; }

    protected virtual IdentityPipelineSignalKind? DownstreamSignal => null;

    protected abstract Task<int> PollAsync(TWorker worker, CancellationToken stoppingToken);

    protected abstract void LogStarted();

    protected abstract void LogPollError(Exception exception);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // Reclaim jobs stuck in intermediate states every 30 seconds.
                if (DateTimeOffset.UtcNow >= _nextReclaimAt)
                {
                    var jobRepository = scope.ServiceProvider.GetRequiredService<IIdentityJobRepository>();
                    var reclaimed = await jobRepository.ReclaimStuckJobsAsync(
                        StuckJobThreshold,
                        stoppingToken);
                    if (reclaimed > 0)
                    {
                        Logger.LogInformation(
                            "{Service}: reclaimed {Count} stuck job(s)",
                            GetType().Name,
                            reclaimed);
                    }

                    _nextReclaimAt = DateTimeOffset.UtcNow.Add(ReclaimInterval);
                }

                var worker = scope.ServiceProvider.GetRequiredService<TWorker>();
                var processed = await PollAsync(worker, stoppingToken);
                if (processed > 0 && DownstreamSignal is { } downstreamSignal)
                    _signal.Signal(downstreamSignal);

                // Back off when idle.
                var delay = processed > 0 ? PollInterval : IdleInterval;
                await _signal.WaitAsync(WakeSignal, delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPollError(exception);
                await _signal.WaitAsync(WakeSignal, IdleInterval, stoppingToken);
            }
        }
    }
}
