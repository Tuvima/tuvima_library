using MediaEngine.Api.Services;
using MediaEngine.Api.Services.Libraries;
using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.View;
using MediaEngine.Application.Services;
using MediaEngine.Domain.Capabilities;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Identity;
using MediaEngine.Identity.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Playback;
using MediaEngine.Storage.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaStorageServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaStorage(this IServiceCollection services)
    {
        services.TryAddSingleton<ILibraryAccessEvaluator, LibraryAccessEvaluator>();
        services.AddSingleton<ITransactionJournal, TransactionJournal>();
        services.AddSingleton<IMediaAssetRepository, MediaAssetRepository>();
        services.AddSingleton<ILocalAssetRepository, LocalAssetRepository>();
        services.AddSingleton<IViewProfileRepository, ViewProfileRepository>();
        services.AddSingleton<IViewPersonalSpaceRepository, ViewPersonalSpaceRepository>();
        services.AddSingleton<IViewGalleryRepository, ViewGalleryRepository>();
        services.AddSingleton<ICollectionViewSourceRepository, CollectionViewSourceRepository>();
        services.AddSingleton<ViewLibraryService>();
        services.AddSingleton<IViewScopeStore, ViewScopeStore>();
        services.AddSingleton<IViewScopeResolver, ViewScopeResolver>();
        services.AddSingleton<IViewResourceStore, ViewResourceStore>();
        services.AddSingleton<IViewResourceAuthorizationService, ViewResourceAuthorizationService>();
        services.AddSingleton<IViewAssetQueryBackend, ViewAssetQueryBackend>();
        services.AddSingleton<IViewQueryOrchestrator, ViewQueryOrchestrator>();
        services.AddSingleton<LibraryReorganizationService>();
        services.AddSingleton<IFileHashCacheRepository, FileHashCacheRepository>();
        services.AddSingleton(_ => new TuvimaDataPaths(configuredPath: null));
        services.AddSingleton(sp =>
        {
            var core = sp.GetRequiredService<IConfigurationLoader>().LoadCore();
            return new AssetPathService(core.LibraryRoot, core.StoragePolicy);
        });
        services.AddSingleton<IAssetExportService, AssetExportService>();
        services.AddSingleton<ICollectionRepository, CollectionRepository>();
        services.AddSingleton<ICollectionPlacementRepository, CollectionPlacementRepository>();
        services.AddSingleton<IAudioFingerprintRepository, AudioFingerprintRepository>();
        services.AddSingleton<IProviderConfigurationRepository, ProviderConfigurationRepository>();
        services.AddSingleton<IApiKeyRepository, ApiKeyRepository>();
        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<IProfileWorkPreferencesRepository, ProfileWorkPreferencesRepository>();
        services.AddSingleton<IProfileSequencePreferencesRepository, ProfileSequencePreferencesRepository>();
        services.AddSingleton<ITasteProfileRepository, TasteProfileRepository>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IProfileExternalLoginRepository, ProfileExternalLoginRepository>();
        services.AddSingleton<IProfileExternalLoginService, ProfileExternalLoginService>();

        services.AddSingleton<IMetadataClaimRepository, MetadataClaimRepository>();
        services.AddSingleton<ICanonicalValueRepository, CanonicalValueRepository>();
        services.AddSingleton<IAiFeaturePersistenceRepository>(sp =>
            (IAiFeaturePersistenceRepository)sp.GetRequiredService<ICanonicalValueRepository>());
        services.AddSingleton<IPersonRepository, PersonRepository>();
        services.AddSingleton<IWorkRepository, WorkRepository>();
        services.AddSingleton<ISeriesManifestRepository, SeriesManifestRepository>();
        services.AddSingleton<HierarchyResolver>();
        services.AddSingleton<WorkHierarchyMaintenanceService>();
        services.AddSingleton<IWorkIdentityReconciliationService, WorkIdentityReconciliationService>();
        services.AddSingleton<WorkClaimRouter>();
        services.AddSingleton<CatalogUpsertService>();
        services.AddSingleton<IMediaEntityChainFactory, MediaEntityChainFactory>();
        services.AddSingleton<IQidLabelRepository, QidLabelRepository>();
        services.AddSingleton<IQidLabelResolver, QidLabelResolver>();
        services.AddSingleton<ICanonicalValueArrayRepository, CanonicalValueArrayRepository>();

        services.AddSingleton<IFictionalEntityRepository, FictionalEntityRepository>();
        services.AddSingleton<IEntityRelationshipRepository, EntityRelationshipRepository>();
        services.AddSingleton<INarrativeRootRepository, NarrativeRootRepository>();
        services.AddSingleton<IPluginLoreRepository, PluginLoreRepository>();
        services.AddSingleton<ICharacterPortraitRepository, CharacterPortraitRepository>();
        services.AddSingleton<IEntityAssetRepository, EntityAssetRepository>();
        services.AddSingleton<ITextTrackRepository, TextTrackRepository>();
        services.AddSingleton<IDeferredEnrichmentRepository, DeferredEnrichmentRepository>();
        services.AddSingleton<IBridgeIdRepository, BridgeIdRepository>();
        services.AddSingleton<IEntityTimelineRepository, EntityTimelineRepository>();
        services.AddSingleton<IReviewQueueRepository, ReviewQueueRepository>();
        services.AddSingleton<IIngestionBatchRepository, IngestionBatchRepository>();
        services.AddSingleton<IIngestionBatchArtifactRepository, IngestionBatchArtifactRepository>();
        services.AddSingleton<IMediaOperationRepository, MediaOperationRepository>();
        services.AddSingleton<IMediaOperationEventRepository, MediaOperationEventRepository>();
        services.AddSingleton<IEntityCapabilityStateRepository, EntityCapabilityStateRepository>();
        services.AddSingleton<IMediaOperationTracker, MediaOperationTracker>();
        services.AddSingleton<CapabilityRegistry>();
        services.AddSingleton<CapabilityPlanner>();
        services.AddSingleton<IReviewQueueRouter, ReviewQueueRouter>();
        services.AddSingleton<IPendingPersonSignalRepository, PendingPersonSignalRepository>();
        services.AddSingleton<ILibraryItemRepository, LibraryItemRepository>();
        services.AddSingleton<ISearchIndexRepository, SearchIndexRepository>();
        services.AddSingleton<IPlaybackSegmentRepository, PlaybackSegmentRepository>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<ISearchService>(sp => sp.GetRequiredService<SearchService>());
        services.AddSingleton<RetailMatchPreviewService>();
        services.AddSingleton<IImageCacheRepository, ImageCacheRepository>();
        services.AddSingleton<IProviderResponseCacheRepository, ProviderResponseCacheRepository>();
        services.AddSingleton<ISearchResultsCacheRepository, SearchResultsCacheRepository>();
        services.AddSingleton<IProviderHealthRepository, ProviderHealthRepository>();
        services.AddSingleton<IIdentityJobRepository, IdentityJobRepository>();
        services.AddSingleton<IRetailCandidateRepository, RetailCandidateRepository>();
        services.AddSingleton<IWikidataCandidateRepository, WikidataCandidateRepository>();

        services.AddSingleton<IReaderBookmarkRepository, ReaderBookmarkRepository>();
        services.AddSingleton<IReaderHighlightRepository, ReaderHighlightRepository>();
        services.AddSingleton<IReaderStatisticsRepository, ReaderStatisticsRepository>();
        services.AddSingleton<IAlignmentJobRepository, AlignmentJobRepository>();
        services.AddSingleton<ISystemActivityRepository, SystemActivityRepository>();
        services.AddSingleton<IIngestionLogRepository, IngestionLogRepository>();
        services.AddSingleton<IResolverCacheRepository, ResolverCacheRepository>();
        services.AddSingleton<IUserStateStore, UserStateRepository>();

        services.AddSingleton<CollectionBackfillService>();
        services.AddSingleton<LibraryReconciliationService>();
        services.AddSingleton<IReconciliationService>(sp =>
            sp.GetRequiredService<LibraryReconciliationService>());
        services.AddSingleton<UISettingsCascadeResolver>();
        services.AddSingleton<UISettingsCacheRepository>();
        services.AddSingleton<IStorageMaintenanceService, StorageMaintenanceService>();
        return services;
    }
}
