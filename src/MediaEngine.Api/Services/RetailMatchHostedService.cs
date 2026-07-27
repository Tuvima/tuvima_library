using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Workers;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that polls <see cref="RetailMatchWorker"/> for
/// <c>Queued</c> identity jobs and runs Stage 1 retail identification.
/// </summary>
public sealed class RetailMatchHostedService : PipelineStageHostedService<RetailMatchWorker>
{
    public RetailMatchHostedService(
        IServiceScopeFactory scopeFactory,
        IIdentityPipelineSignal signal,
        EnrichmentPipelineExecutionGate executionGate,
        ILogger<RetailMatchHostedService> logger)
        : base(scopeFactory, signal, executionGate, logger)
    {
    }

    protected override IdentityPipelineSignalKind WakeSignal => IdentityPipelineSignalKind.Retail;

    protected override TimeSpan StuckJobThreshold => TimeSpan.FromMinutes(5);

    protected override IdentityJobState ProcessingState => IdentityJobState.RetailSearching;

    protected override IdentityPipelineSignalKind? DownstreamSignal =>
        IdentityPipelineSignalKind.WikidataBridge;

    protected override Task<int> PollAsync(RetailMatchWorker worker, CancellationToken stoppingToken) =>
        worker.PollAsync(stoppingToken);

    protected override void LogStarted() =>
        Logger.LogInformation("RetailMatchHostedService started");

    protected override void LogPollError(Exception exception) =>
        Logger.LogError(exception, "RetailMatchHostedService poll error");
}
