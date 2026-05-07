using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Application.Services;

namespace MediaEngine.Api.DependencyInjection;

public static class ApiReadServiceCollectionExtensions
{
    public static IServiceCollection AddApiReadServices(this IServiceCollection services)
    {
        services.AddSingleton<IJourneyReadService, JourneyReadService>();
        services.AddSingleton<IIngestionBatchReadService, IngestionBatchReadService>();
        services.AddSingleton<IPersonAliasReadService, PersonAliasReadService>();
        services.AddSingleton<IPersonPresenceReadService, PersonPresenceReadService>();
        services.AddSingleton<IPersonWorksReadService, PersonWorksReadService>();
        services.AddSingleton<IPersonAssetScopeReadService, PersonAssetScopeReadService>();
        services.AddSingleton<IOrphanImageReferenceReadService, OrphanImageReferenceReadService>();
        return services;
    }
}
