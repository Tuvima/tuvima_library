using System.Threading.Channels;
using MediaEngine.Ingestion.Services;

namespace MediaEngine.Api.Services;

public interface IInitialSweepCommandService
{
    bool IsPendingOrRunning { get; }

    /// <summary>
    /// Schedules one sweep when no sweep is already queued or executing.
    /// </summary>
    bool TrySchedule();
}

/// <summary>
/// Owns manually requested initial sweeps for the lifetime of the Engine host.
/// A single-flight gate prevents overlapping full-library scans.
/// </summary>
public sealed class InitialSweepCommandService(
    IInitialSweepService sweep,
    ILogger<InitialSweepCommandService> logger) : BackgroundService, IInitialSweepCommandService
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private int _pendingOrRunning;

    public bool IsPendingOrRunning => Volatile.Read(ref _pendingOrRunning) != 0;

    public bool TrySchedule()
    {
        if (Interlocked.CompareExchange(ref _pendingOrRunning, 1, 0) != 0)
            return false;

        if (_requests.Writer.TryWrite(true))
            return true;

        Volatile.Write(ref _pendingOrRunning, 0);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in _requests.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await sweep.RunAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Manually requested initial sweep failed");
                }
                finally
                {
                    Volatile.Write(ref _pendingOrRunning, 0);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _requests.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
