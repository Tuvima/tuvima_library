using MediaEngine.Api.DevSupport;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Realtime;
using MediaEngine.Domain;

namespace MediaEngine.Api.DependencyInjection;

public static class ApiEndpointRouteBuilderExtensions
{
    public static WebApplication MapEngineEndpoints(this WebApplication app)
    {
        app.MapHub<Intercom>(SignalREvents.IntercomPath);
        app.MapSystemEndpoints();
        app.MapMaintenanceEndpoints();
        app.MapAdminEndpoints();
        app.MapCollectionEndpoints();
        app.MapLibraryEndpoints();
        app.MapStreamEndpoints();
        app.MapPlaybackEndpoints();
        app.MapPlaybackSegmentEndpoints();
        app.MapReadEndpoints();
        app.MapReaderEndpoints();
        app.MapIngestionEndpoints();
        app.MapMetadataEndpoints();
        app.MapReviewEndpoints();
        app.MapSettingsEndpoints();
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
        app.MapCharacterEndpoints();
        app.MapCanonEndpoints();
        app.MapDeferredEnrichmentEndpoints();
        app.MapLibraryItemEndpoints();
        app.MapItemCanonicalEndpoints();
        app.MapTimelineEndpoints();
        app.MapSearchEndpoints();
        app.MapReportEndpoints();
        app.MapDebugEndpoints();
        app.MapAiEndpoints();
        app.MapAiEnrichmentEndpoints();
        app.MapPluginEndpoints();

        return app;
    }

    public static WebApplication MapDevelopmentEngineEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapDevSeedEndpoints();
            app.MapIntegrationTestEndpoints();
        }

        return app;
    }
}
