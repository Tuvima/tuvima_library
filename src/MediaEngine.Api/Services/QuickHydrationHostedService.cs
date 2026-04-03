using MediaEngine.Providers.Workers;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that polls <see cref="QuickHydrationWorker"/> for
/// <c>QidResolved</c> identity jobs and runs Quick hydration + post-pipeline evaluation.
/// </summary>
public sealed class QuickHydrationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuickHydrationHostedService> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    public QuickHydrationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<QuickHydrationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QuickHydrationHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<QuickHydrationWorker>();
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
                _logger.LogError(ex, "QuickHydrationHostedService poll error");
                await Task.Delay(IdleInterval, stoppingToken);
            }
        }
    }
}
