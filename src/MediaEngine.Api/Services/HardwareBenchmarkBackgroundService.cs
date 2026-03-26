using MediaEngine.AI.Infrastructure;

namespace MediaEngine.Api.Services;

/// <summary>
/// One-shot background service that runs the hardware benchmark 15 seconds after the
/// Engine starts.  The delay allows model auto-download to begin and the DI container
/// to fully settle before inference is attempted.
///
/// Results are cached in AiSettings.HardwareProfile so the benchmark is skipped on
/// subsequent restarts unless the tier is still set to "auto".
/// </summary>
public sealed class HardwareBenchmarkBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly HardwareBenchmarkService              _benchmark;
    private readonly ILogger<HardwareBenchmarkBackgroundService> _logger;

    public HardwareBenchmarkBackgroundService(
        HardwareBenchmarkService                        benchmark,
        ILogger<HardwareBenchmarkBackgroundService>     logger)
    {
        _benchmark = benchmark;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "HardwareBenchmarkBackgroundService: waiting {Seconds}s before running benchmark",
            StartupDelay.TotalSeconds);

        await Task.Delay(StartupDelay, stoppingToken);

        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            await _benchmark.BenchmarkAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown before benchmark completed — no action needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HardwareBenchmarkBackgroundService: benchmark failed unexpectedly");
        }

        // Service is done — runs once on startup only.
    }
}
