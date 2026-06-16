using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Events;
using MediaEngine.Providers.Services;

namespace MediaEngine.Api.Services;

public sealed class ProviderActivityBroadcastService : BackgroundService
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1);

    private readonly IProviderRateLimiterCoordinator _rateLimiter;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<ProviderActivityBroadcastService> _logger;
    private string? _lastSignature;

    public ProviderActivityBroadcastService(
        IProviderRateLimiterCoordinator rateLimiter,
        IEventPublisher publisher,
        ILogger<ProviderActivityBroadcastService> logger)
    {
        _rateLimiter = rateLimiter;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BroadcastInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await BroadcastIfChangedAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task BroadcastIfChangedAsync(CancellationToken ct)
    {
        var snapshots = _rateLimiter.GetSnapshots()
            .OrderBy(snapshot => snapshot.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (snapshots.Count == 0)
            return;

        var signature = string.Join("|", snapshots.Select(snapshot =>
            string.Join(':',
                snapshot.ProviderName,
                snapshot.ActiveRequests,
                snapshot.WaitingRequests,
                snapshot.RequestsTotal,
                snapshot.RequestsLastMinute,
                snapshot.ErrorsTotal,
                snapshot.ErrorsLastMinute,
                snapshot.ThrottleWaitMsTotal,
                snapshot.WaitMsLastMinute,
                Math.Round(snapshot.AverageWaitMs, 1),
                snapshot.MaxActiveLastMinute,
                snapshot.LastSuccessAt?.ToUnixTimeSeconds() ?? 0,
                snapshot.LastRequestAt?.ToUnixTimeSeconds() ?? 0)));
        var hasLiveProviderWork = snapshots.Any(snapshot => snapshot.ActiveRequests > 0 || snapshot.WaitingRequests > 0);
        if (!hasLiveProviderWork && string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            return;

        _lastSignature = signature;

        try
        {
            await _publisher.PublishAsync(
                SignalREvents.ProviderActivity,
                new ProviderActivityEvent(
                    snapshots.Select(ToEvent).ToList(),
                    DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Provider activity broadcast failed; continuing");
        }
    }

    private static ProviderActivityItemEvent ToEvent(ProviderActivitySnapshot snapshot) => new(
        snapshot.ProviderName,
        snapshot.ActiveRequests,
        snapshot.WaitingRequests,
        snapshot.RequestsTotal,
        snapshot.RequestsLastMinute,
        snapshot.MaxActiveLastMinute,
        snapshot.ErrorsTotal,
        snapshot.ErrorsLastMinute,
        snapshot.ThrottleWaitMsTotal,
        snapshot.WaitMsLastMinute,
        snapshot.AverageWaitMs,
        snapshot.AverageLatencyMs,
        snapshot.LastSuccessAt,
        snapshot.LastRequestAt,
        snapshot.LastError);
}
