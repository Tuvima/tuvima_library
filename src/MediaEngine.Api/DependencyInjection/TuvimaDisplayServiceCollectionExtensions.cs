using MediaEngine.Api.Services;
using MediaEngine.Api.Services.Canonical;
using MediaEngine.Api.Services.Collections;
using MediaEngine.Api.Services.Details;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Metadata;
using MediaEngine.Application.Services;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaDisplayServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaDisplay(this IServiceCollection services)
    {
        services.AddSingleton<IByteStreamer, ByteStreamer>();
        services.AddApiReadServices();
        services.AddSingleton<ILibraryItemCurationStore, LibraryItemCurationStore>();
        services.AddSingleton<IMetadataEndpointDataService, MetadataEndpointDataService>();
        services.AddSingleton<IItemCanonicalDataService, ItemCanonicalDataService>();
        services.AddSingleton<AlbumTrackManifestService>();
        services.AddSingleton<ArtworkScopeService>();
        services.AddSingleton<CanonicalCandidateBuilder>();

        // These services are stateless over immutable/singleton dependencies. Keeping
        // one shared instance avoids manufacturing a graph for every API request.
        services.AddSingleton<IDisplayProjectionRepository, DisplayProjectionRepository>();
        services.AddSingleton<DisplayWorkProjectionReader>();
        services.AddSingleton<DisplayProfilePreferenceProjectionReader>();
        services.AddSingleton<DisplayJourneyProjectionReader>();
        services.AddSingleton<DisplayFavoriteProjectionReader>();
        services.AddSingleton<DisplayHomeCollectionProjectionReader>();
        services.AddSingleton<DisplayLaneGroupPolicy>();
        services.AddSingleton<DisplayCardBuilder>();
        services.AddSingleton<DisplayShelfBuilder>();
        services.AddSingleton<DisplayComposerService>();
        services.AddSingleton<DetailRecommendationService>();
        services.AddSingleton<DetailComposerService>();
        return services;
    }
}
