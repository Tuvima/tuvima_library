using MediaEngine.Api.Services.Plugins.ApplicationServices;
using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.DependencyInjection;

public static class PluginApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPluginApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(FandomLoreDiscoveryOperation.Permission);
        services.AddSingleton<IPluginApplicationOperation, FandomLoreDiscoveryOperation>();
        services.AddSingleton<PluginApplicationOperationRegistry>();
        services.AddSingleton<IPluginApplicationPermissionAvailability>(provider => provider.GetRequiredService<PluginApplicationOperationRegistry>());
        services.AddScoped<PluginApplicationServiceGateway>();
        return services;
    }
}
