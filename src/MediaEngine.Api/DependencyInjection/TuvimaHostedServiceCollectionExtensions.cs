using MediaEngine.Api.Services;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.Plugins;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Services;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaHostedServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaHostedServices(this IServiceCollection services)
    {
        services.AddSingleton<EnrichmentPipelineExecutionGate>();

        // IHostedService.StartAsync methods are awaited sequentially before Kestrel
        // accepts requests. Keep the two startup data repairs first.
        services.AddHostedService<UISettingsCacheWarmupHostedService>();
        services.AddHostedService<OrphanReviewQueuePurgeHostedService>();

        services.AddHostedService<EncodeQueueService>();
        services.AddHostedService<FolderHealthService>();
        services.AddHostedService(sp => sp.GetRequiredService<MetadataHarvestingService>());
        services.AddHostedService(sp => sp.GetRequiredService<DeferredEnrichmentService>());
        services.AddHostedService<ProviderActivityBroadcastService>();
        services.AddHostedService(sp => sp.GetRequiredService<ProviderHealthMonitorService>());
        services.AddHostedService<WhisperSyncBackgroundService>();
        services.AddHostedService<ActivityPruningService>();
        services.AddHostedService<MediaOperationRecoveryHostedService>();
        services.AddHostedService<ArtworkRenditionRepairStartupService>();
        services.AddHostedService<RejectedFileCleanupService>();
        services.AddHostedService<RetagSweepWorker>();
        services.AddHostedService<MissingUniverseSweepService>();

        // Recovery must finish before any identity worker can lease jobs.
        services.AddHostedService<HydrationStartupSweepService>();
        services.AddHostedService(sp => sp.GetRequiredService<InitialSweepCommandService>());
        services.AddHostedService(sp => sp.GetRequiredService<LibraryReconciliationService>());
        services.AddHostedService<StorageMaintenanceHostedService>();

        services.AddHostedService<ModelAutoDownloadService>();
        services.AddHostedService<VibeBatchService>();
        services.AddHostedService<SeriesAlignmentBackgroundService>();
        services.AddHostedService<TasteProfileBackgroundService>();
        services.AddHostedService<DescriptionIntelligenceBatchService>();
        services.AddHostedService(sp => sp.GetRequiredService<UniverseEnrichmentService>());
        services.AddHostedService<RetailMatchHostedService>();
        services.AddHostedService<WikidataBridgeHostedService>();
        services.AddHostedService<QuickHydrationHostedService>();
        services.AddHostedService<MusicBrainzEnrichmentHostedService>();
        services.AddHostedService<MusicAlbumManifestHostedService>();
        services.AddHostedService<HardwareBenchmarkBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<PluginScheduledSegmentService>());
        return services;
    }
}
