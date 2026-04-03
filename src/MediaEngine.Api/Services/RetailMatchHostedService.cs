using MediaEngine.Providers.Workers;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that polls <see cref="RetailMatchWorker"/> for
/// <c>Queued</c> identity jobs and runs Stage 1 retail identification.
/// Only active when <c>identity_pipeline_v2_enabled</c> is true.
/// </summary>
public sealed class RetailMatchHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetailMatchHostedService> _logger;
    private readonly bool _enabled;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    public RetailMatchHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RetailMatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Read feature flag — only poll when v2 is enabled
        _enabled = configuration.GetValue<bool>("identity_pipeline_v2_enabled");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("RetailMatchHostedService disabled — identity_pipeline_v2_enabled is false");
            return;
        }

        _logger.LogInformation("RetailMatchHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<RetailMatchWorker>();
                var processed = await worker.PollAsync(stoppingToken);

                // Back off when idle
                var delay = processed > 0 ? PollInterval : IdleInterval;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetailMatchHostedService poll error");
                await Task.Delay(IdleInterval, stoppingToken);
            }
        }
    }
}
