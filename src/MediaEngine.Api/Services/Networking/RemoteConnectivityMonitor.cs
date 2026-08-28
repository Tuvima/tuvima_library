namespace MediaEngine.Api.Services.Networking;

public sealed class RemoteConnectivityMonitor(
    IEnumerable<IRemoteConnectivityProvider> providers,
    NetworkRuntimeState runtime,
    ILogger<RemoteConnectivityMonitor> logger) : BackgroundService
{
    private readonly IReadOnlyList<IRemoteConnectivityProvider> _providers = providers.ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var provider in _providers)
            {
                try
                {
                    runtime.RecordRemoteProvider(await provider.GetStateAsync(stoppingToken));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Remote connectivity provider {Provider} status failed", provider.Key);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
