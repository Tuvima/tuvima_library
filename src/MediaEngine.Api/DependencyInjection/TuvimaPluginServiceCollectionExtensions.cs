using MediaEngine.Api.Services.Plugins;
using MediaEngine.Plugin.CommercialSkip;
using MediaEngine.Plugin.FandomLore;
using MediaEngine.Plugin.MediaSegments;
using MediaEngine.Plugins;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaPluginServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaPlugins(this IServiceCollection services)
    {
        services.AddSingletonImplementations<ITuvimaPlugin>(
            expectedCount: 6,
            typeof(CommercialSkipPlugin).Assembly,
            typeof(FandomLorePlugin).Assembly,
            typeof(IntroSkipPlugin).Assembly);
        services.AddSingleton<PluginSettingsService>();
        services.AddSingleton<PluginCatalog>();
        services.AddSingleton<ApprovedPluginCatalogService>();
        services.AddSingleton<PluginUniverseLoreService>();
        services.AddSingleton<PluginJobStateService>();
        services.AddSingleton<PluginScheduledSegmentService>();
        services.AddSingleton<IPluginToolRuntime, PluginToolRuntime>();
        services.AddSingleton<IPluginAiClient, PluginAiClient>();
        services.AddSingleton<PluginSegmentDetectionService>();
        return services;
    }
}
