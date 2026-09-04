#if DEBUG
using MediaEngine.Api.DevSupport;
#endif
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Realtime;
using MediaEngine.Domain;

namespace MediaEngine.Api.DependencyInjection;

public static class ApiEndpointRouteBuilderExtensions
{
    public static WebApplication MapEngineEndpoints(this WebApplication app)
    {
        app.MapHub<Intercom>(SignalREvents.IntercomPath)
            .RequireRateLimiting("intercom")
            .AllowAnonymous();
        app.MapSystemEndpoints();
        app.MapSetupEndpoints();
        app.MapAuthenticationEndpoints();
        app.MapAccountEndpoints();
        app.MapClientAuthorizationEndpoints();
        app.MapMaintenanceEndpoints();
        app.MapAdminEndpoints();
        app.MapCollectionEndpoints();
        app.MapLibraryEndpoints();
        app.MapStreamEndpoints();
        app.MapHlsStreamEndpoints();
        app.MapViewEndpoints();
        app.MapViewDiscoveryEndpoints();
        app.MapPlaybackEndpoints();
        app.MapPlayerEndpoints();
        app.MapPlaybackSegmentEndpoints();
        app.MapReadEndpoints();
        app.MapReaderEndpoints();
        app.MapIngestionEndpoints();
        app.MapEnrichmentRefreshEndpoints();
        app.MapOperationsEndpoints();
        app.MapCapabilityEndpoints();
        app.MapMetadataEndpoints();
        app.MapReviewEndpoints();
        app.MapSettingsEndpoints();
        app.MapNetworkEndpoints();
        app.MapServerFolderEndpoints();
        app.MapLibraryReorganizationEndpoints();
        app.MapProviderCatalogueEndpoints();
        app.MapUISettingsEndpoints();
        app.MapProfileEndpoints();
        app.MapPersonEndpoints();
        app.MapWorkEndpoints();
        app.MapProgressEndpoints();
        app.MapActivityEndpoints();
        app.MapDisplayEndpoints();
        app.MapDetailEndpoints();
        app.MapUniverseGraphEndpoints();
        app.MapUniverseLoreEndpoints();
        app.MapCharacterEndpoints();
        app.MapCanonEndpoints();
        app.MapDeferredEnrichmentEndpoints();
        app.MapLibraryItemEndpoints();
        app.MapItemCanonicalEndpoints();
        app.MapTimelineEndpoints();
        app.MapSearchEndpoints();
        app.MapReportEndpoints();
        app.MapAiEndpoints();
        app.MapPluginEndpoints();

        return app;
    }

    public static WebApplication MapDevelopmentEngineEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
#if DEBUG
            app.MapDebugEndpoints();
            app.MapDevSeedEndpoints();
            app.MapIntegrationTestEndpoints();
#endif
        }

        return app;
    }
}
