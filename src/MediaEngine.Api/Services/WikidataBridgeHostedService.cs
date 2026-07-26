using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Workers;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that polls <see cref="WikidataBridgeWorker"/> for
/// <c>RetailMatched</c> identity jobs and runs Stage 2 Wikidata bridge resolution.
/// </summary>
public sealed class WikidataBridgeHostedService : PipelineStageHostedService<WikidataBridgeWorker>
{
    public WikidataBridgeHostedService(
        IServiceScopeFactory scopeFactory,
        IIdentityPipelineSignal signal,
        ILogger<WikidataBridgeHostedService> logger)
        : base(scopeFactory, signal, logger)
    {
    }

    protected override IdentityPipelineSignalKind WakeSignal =>
        IdentityPipelineSignalKind.WikidataBridge;

    protected override TimeSpan StuckJobThreshold => TimeSpan.FromMinutes(10);

    protected override IdentityPipelineSignalKind? DownstreamSignal =>
        IdentityPipelineSignalKind.Hydration;

    protected override Task<int> PollAsync(WikidataBridgeWorker worker, CancellationToken stoppingToken) =>
        worker.PollAsync(stoppingToken);

    protected override void LogStarted() =>
        Logger.LogInformation("WikidataBridgeHostedService started");

    protected override void LogPollError(Exception exception) =>
        Logger.LogError(exception, "WikidataBridgeHostedService poll error");
}
