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
        services.AddSingleton<IProfileOverviewReadService, ProfileOverviewReadService>();
        services.AddSingleton<IPersonCreditReadService, PersonCreditReadService>();
        services.AddSingleton<ILibraryOverviewReadService, LibraryOverviewReadService>();
        services.AddSingleton<IMetadataClaimHistoryReadService, MetadataClaimHistoryReadService>();
        services.AddSingleton<ICollectionBrowseReadService, CollectionBrowseReadService>();
        services.AddSingleton<ICollectionSearchReadService, CollectionSearchReadService>();
        services.AddSingleton<ICollectionMediaLookupReadService, CollectionMediaLookupReadService>();
        services.AddSingleton<IReviewQueueReadService, ReviewQueueReadService>();
        services.AddSingleton<MediaEditorNavigationReadService>();
        services.AddSingleton<IMediaEditorNavigationReadService>(sp => sp.GetRequiredService<MediaEditorNavigationReadService>());
        services.AddSingleton<IMediaEditorMembershipReadService>(sp => sp.GetRequiredService<MediaEditorNavigationReadService>());
        return services;
    }
}
