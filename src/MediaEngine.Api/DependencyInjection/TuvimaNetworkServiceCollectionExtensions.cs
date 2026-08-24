using MediaEngine.Api.Services.Networking;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaNetworkServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaNetworking(this IServiceCollection services)
    {
        services.AddSingleton<NetworkRuntimeState>();
        services.AddSingleton<NetworkConnectionClassifier>();
        services.AddSingleton<INetworkEnvironmentService, NetworkEnvironmentService>();
        services.AddSingleton<IGatewayDiscoveryService, GatewayDiscoveryService>();
        services.AddSingleton<NetworkStatusService>();
        services.AddSingleton<IRouterPortMapper, PcpRouterPortMapper>();
        services.AddSingleton<IRouterPortMapper, NatPmpRouterPortMapper>();
        services.AddHttpClient<UpnpRouterPortMapper>(client => client.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<IRouterPortMapper>(provider => provider.GetRequiredService<UpnpRouterPortMapper>());
        services.AddSingleton<RouterPortMappingCoordinator>();
        services.AddHostedService(provider => provider.GetRequiredService<RouterPortMappingCoordinator>());
        services.AddHostedService<LocalDiscoveryHostedService>();
        services.AddHttpClient<INetworkDiagnosticsService, NetworkDiagnosticsService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        return services;
    }
}
