using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that checks for pending WhisperSync alignment jobs every 30 seconds.
/// Disabled by default — enable via configuration when Whisper model is available.
/// </summary>
public sealed class WhisperSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhisperSyncBackgroundService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);
    private readonly bool _enabled;

    public WhisperSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<WhisperSyncBackgroundService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _enabled = configuration.GetValue("MediaEngine:WhisperSync:Enabled", false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation(
                "WhisperSync background service is disabled. " +
                "Set MediaEngine:WhisperSync:Enabled=true to enable.");
            return;
        }

        _logger.LogInformation("WhisperSync background service started (poll interval: {Interval}s)",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
                await ProcessPendingJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhisperSync background service encountered an error");
            }
        }

        _logger.LogInformation("WhisperSync background service stopped");
    }

    private async Task ProcessPendingJobAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWhisperSyncService>();

        var processed = await service.ProcessNextPendingAsync(ct);
        if (processed)
        {
            _logger.LogDebug("Processed a pending WhisperSync alignment job");
        }
    }
}
