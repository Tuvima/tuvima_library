using MediaEngine.Providers.Workers;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that polls <see cref="WikidataBridgeWorker"/> for
/// <c>RetailMatched</c> identity jobs and runs Stage 2 Wikidata bridge resolution.
/// </summary>
public sealed class WikidataBridgeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WikidataBridgeHostedService> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    public WikidataBridgeHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WikidataBridgeHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WikidataBridgeHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<WikidataBridgeWorker>();
                var processed = await worker.PollAsync(stoppingToken);

                var delay = processed > 0 ? PollInterval : IdleInterval;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WikidataBridgeHostedService poll error");
                await Task.Delay(IdleInterval, stoppingToken);
            }
        }
    }
}
