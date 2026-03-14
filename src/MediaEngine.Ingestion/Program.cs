using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Intelligence;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Processors;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

// ─────────────────────────────────────────────────────────────────
// Host builder
// ─────────────────────────────────────────────────────────────────

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Ingestion options ──────────────────────────────────────────
        services.Configure<IngestionOptions>(
            config.GetSection(IngestionOptions.SectionName));

        // ── Storage / Database ─────────────────────────────────
        // DatabaseConnection needs a path to the SQLite file.
        // Conventionally loaded from the manifest; for the Worker host
        // we read it from configuration (Ingestion:DatabasePath).
        string dbPath = config["Ingestion:DatabasePath"] ?? "library.db";
        services.AddSingleton<IDatabaseConnection>(sp =>
        {
            var db = new DatabaseConnection(dbPath);
            db.Open();
            db.InitializeSchema();
            db.RunStartupChecks();
            return db;
        });

        // ── Configuration Directory Loader ───────────────────
        // Reads individual config files from config/ directory.
        // Auto-migrates from legacy manifest on first run.
        string configDir = config["MediaEngine:ConfigDirectory"] ?? "config";
        string manifestPath = config["MediaEngine:ManifestPath"] ?? "legacy_manifest.json";
        var configLoader = new ConfigurationDirectoryLoader(configDir, manifestPath);
        services.AddSingleton<IStorageManifest>(configLoader);
        services.AddSingleton<IConfigurationLoader>(configLoader);

        // PostConfigure: override appsettings.json values with any paths
        // previously saved via PUT /settings/folders (stored in config/core.json).
        // Without this the worker always starts with WatchDirectory = "" even
        // after the user has saved a path via the Settings page.
        services.PostConfigure<IngestionOptions>(opts =>
        {
            try
            {
                var core = configLoader.LoadCore();
                if (!string.IsNullOrWhiteSpace(core.WatchDirectory)) opts.WatchDirectory = core.WatchDirectory;
                if (!string.IsNullOrWhiteSpace(core.LibraryRoot)) opts.LibraryRoot = core.LibraryRoot;
                if (!string.IsNullOrWhiteSpace(core.OrganizationTemplate)) opts.OrganizationTemplate = core.OrganizationTemplate;
                if (core.OrganizationTemplates.Count > 0) opts.OrganizationTemplates = new Dictionary<string, string>(core.OrganizationTemplates, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // First run — config may not yet have folder keys; appsettings.json values stand.
            }

            // Overlay disambiguation thresholds from config/disambiguation.json.
            try
            {
                var disambiguation = configLoader.LoadDisambiguation();
                opts.MediaTypeAutoAssignThreshold = disambiguation.MediaTypeAutoAssignThreshold;
                opts.MediaTypeReviewThreshold     = disambiguation.MediaTypeReviewThreshold;
            }
            catch
            {
                // First run — defaults from IngestionOptions stand.
            }
        });

        // Bootstrap the default universe config if it doesn't exist yet.
        if (configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata") is null)
        {
            var defaultUniverse = WikidataSparqlPropertyMap.ExportAsUniverseConfiguration();
            configLoader.SaveConfig("universe", "wikidata", defaultUniverse);
        }

        services.AddSingleton<ITransactionJournal, TransactionJournal>();
        services.AddSingleton<IMediaAssetRepository, MediaAssetRepository>();

        // ── Storage repositories (Phase 9) ───────────────────
        services.AddSingleton<IHubRepository, HubRepository>();
        services.AddSingleton<IMetadataClaimRepository, MetadataClaimRepository>();
        services.AddSingleton<ICanonicalValueRepository, CanonicalValueRepository>();
        services.AddSingleton<IPersonRepository, PersonRepository>();
        services.AddSingleton<IMediaEntityChainFactory, MediaEntityChainFactory>();
        services.AddSingleton<ISystemActivityRepository, SystemActivityRepository>();

        // ── Ingestion hint cache (sibling-aware priming) ───────
        services.AddSingleton<MediaEngine.Ingestion.Services.IIngestionHintCache, MediaEngine.Ingestion.Services.IngestionHintCache>();

        // ── File watching / debounce ───────────────────────────
        services.AddSingleton<IFileWatcher, FileWatcher>();
        services.AddSingleton<DebounceQueue>();

        // ── Asset hasher ───────────────────────────────────────
        services.AddSingleton<IAssetHasher, AssetHasher>();

        // ── Media processors ───────────────────────────────────
        services.AddSingleton<IVideoMetadataExtractor, StubVideoMetadataExtractor>();

        services.AddSingleton<IProcessorRegistry>(sp =>
        {
            var registry = new MediaProcessorRegistry();

            // Processors ordered by priority (registry sorts internally).
            registry.Register(new EpubProcessor());
            registry.Register(new AudioProcessor());
            registry.Register(new VideoProcessor(
                sp.GetRequiredService<IVideoMetadataExtractor>()));
            registry.Register(new ComicProcessor());
            registry.Register(new GenericFileProcessor());

            return registry;
        });

        // ── Intelligence / Scoring ─────────────────────────────
        services.AddSingleton<IScoringStrategy, ExactMatchStrategy>();
        services.AddSingleton<IScoringStrategy, LevenshteinStrategy>();

        services.AddSingleton<IConflictResolver>(sp =>
            new ConflictResolver(sp.GetServices<IScoringStrategy>()));

        services.AddSingleton<IScoringEngine>(sp =>
            new ScoringEngine(sp.GetRequiredService<IConflictResolver>()));

        services.AddSingleton<IIdentityMatcher>(sp =>
            new IdentityMatcher(sp.GetServices<IScoringStrategy>()));

        services.AddSingleton<IHubArbiter>(sp =>
            new HubArbiter(
                sp.GetRequiredService<IIdentityMatcher>(),
                sp.GetRequiredService<ITransactionJournal>()));

        // ── Event publishing (no-op in the worker host) ───────
        services.AddSingleton<IEventPublisher, NullEventPublisher>();

        // ── File organizer ─────────────────────────────────────
        services.AddSingleton<IFileOrganizer, FileOrganizer>();

        // ── Metadata taggers ───────────────────────────────────
        services.AddSingleton<IMetadataTagger, EpubMetadataTagger>();

        // ── Background worker ──────────────────────────────────
        services.AddSingleton<IBackgroundWorker, BackgroundWorker>();

        // ── HTTP clients (needed by provider adapters) ─────────
        services.AddHttpClient();

        // ── Provider adapters ─────────────────────────────────────
        // Config-driven providers: scan config/providers/ and register
        // each enabled config_driven adapter via the universal adapter.
        foreach (var providerConfig in configLoader.LoadAllProviders())
        {
            if (!providerConfig.Enabled) continue;
            if (!string.Equals(providerConfig.AdapterType, "config_driven",
                    StringComparison.OrdinalIgnoreCase)) continue;

            var name = providerConfig.Name;
            var timeout = providerConfig.HttpClient?.TimeoutSeconds ?? 10;
            var userAgent = providerConfig.HttpClient?.UserAgent;

            services.AddHttpClient(name, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(timeout);
                if (!string.IsNullOrEmpty(userAgent))
                    c.DefaultRequestHeaders.Add("User-Agent", userAgent);
            });

            var cfg = providerConfig;
            services.AddSingleton<IExternalMetadataProvider>(sp =>
                new ConfigDrivenAdapter(
                    cfg,
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<ConfigDrivenAdapter>>(),
                    sp.GetService<IProviderResponseCacheRepository>()));
        }

        // Wikidata stays as a coded adapter (SPARQL cannot be config-driven).
        services.AddSingleton<IExternalMetadataProvider, WikidataAdapter>();

        // ── Metadata harvesting & person enrichment (Phase 9) ──
        services.AddSingleton<IMetadataHarvestingService, MetadataHarvestingService>();
        services.AddSingleton<IRecursiveIdentityService, RecursiveIdentityService>();

        // ── Hydration pipeline + review queue ──────────────────
        // Required by IngestionEngine constructor — missing from the worker host
        // caused a DI crash at startup, preventing the FileSystemWatcher from ever
        // starting (which is why files in the watch folder were not picked up).
        services.AddSingleton<IHydrationPipelineService, HydrationPipelineService>();
        services.AddSingleton<IReviewQueueRepository, ReviewQueueRepository>();

        // ── Sidecar writer + library scanner (Phase 7) ─────────
        services.AddSingleton<ISidecarWriter, SidecarWriter>();
        services.AddSingleton<ILibraryScanner, LibraryScanner>();

        // ── Ingestion engine (BackgroundService + IIngestionEngine) ──
        services.AddSingleton<IngestionEngine>();
        services.AddSingleton<IIngestionEngine>(sp => sp.GetRequiredService<IngestionEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<IngestionEngine>());
    })
    .Build();

await host.RunAsync();
