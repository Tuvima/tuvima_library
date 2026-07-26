using MediaEngine.Api.Services;
using MediaEngine.Api.Services.Canonical;
using MediaEngine.Api.Services.Collections;
using MediaEngine.Api.Services.Details;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Metadata;
using MediaEngine.Application.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Storage;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaDisplayServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaDisplay(this IServiceCollection services)
    {
        services.AddSingleton<IByteStreamer, ByteStreamer>();
        services.AddApiReadServices();
        services.AddSingleton<ILibraryItemCurationRepository, LibraryItemCurationRepository>();
        services.AddSingleton<IMetadataEditorRepository, MetadataEditorRepository>();
        services.AddSingleton<IItemCanonicalRepository, ItemCanonicalRepository>();
        services.AddSingleton<AlbumTrackManifestService>();
        services.AddSingleton<ArtworkScopeService>();
        services.AddSingleton<CanonicalCandidateBuilder>();

        // These services are stateless over immutable/singleton dependencies. Keeping
        // one shared instance avoids manufacturing a graph for every API request.
        services.AddSingleton<IDisplayProjectionReadService, DisplayProjectionReadService>();
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
