using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Workers;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that polls <see cref="QuickHydrationWorker"/> for
/// <c>QidResolved</c> identity jobs and runs Quick hydration + post-pipeline evaluation.
/// </summary>
public sealed class QuickHydrationHostedService : PipelineStageHostedService<QuickHydrationWorker>
{
    public QuickHydrationHostedService(
        IServiceScopeFactory scopeFactory,
        IIdentityPipelineSignal signal,
        ILogger<QuickHydrationHostedService> logger)
        : base(scopeFactory, signal, logger)
    {
    }

    protected override IdentityPipelineSignalKind WakeSignal =>
        IdentityPipelineSignalKind.Hydration;

    protected override TimeSpan StuckJobThreshold => TimeSpan.FromMinutes(5);

    protected override Task<int> PollAsync(QuickHydrationWorker worker, CancellationToken stoppingToken) =>
        worker.PollAsync(stoppingToken);

    protected override void LogStarted() =>
        Logger.LogInformation("QuickHydrationHostedService started");

    protected override void LogPollError(Exception exception) =>
        Logger.LogError(exception, "QuickHydrationHostedService poll error");
}
