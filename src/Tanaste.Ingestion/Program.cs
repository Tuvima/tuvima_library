using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Enums;
using Tanaste.Ingestion;
using Tanaste.Ingestion.Contracts;
using Tanaste.Ingestion.Models;
using Tanaste.Intelligence;
using Tanaste.Intelligence.Contracts;
using Tanaste.Intelligence.Models;
using Tanaste.Intelligence.Strategies;
using Tanaste.Processors;
using Tanaste.Processors.Contracts;
using Tanaste.Processors.Processors;
using Tanaste.Providers.Adapters;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Providers.Services;
using Tanaste.Storage;
using Tanaste.Storage.Contracts;

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

        // ── Ingestion options ──────────────────────────────────
        services.Configure<IngestionOptions>(
            config.GetSection(IngestionOptions.SectionName));

        // ── Storage / Database ─────────────────────────────────
        // DatabaseConnection needs a path to the SQLite file.
        // Conventionally loaded from the manifest; for the Worker host
        // we read it from configuration (Ingestion:DatabasePath).
        string dbPath = config["Ingestion:DatabasePath"] ?? "tanaste.db";
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
        // Auto-migrates from legacy tanaste_master.json on first run.
        string configDir    = config["Tanaste:ConfigDirectory"] ?? "config";
        string manifestPath = config["Tanaste:ManifestPath"] ?? "tanaste_master.json";
        var    configLoader = new ConfigurationDirectoryLoader(configDir, manifestPath);
        services.AddSingleton<IStorageManifest>(configLoader);
        services.AddSingleton<IConfigurationLoader>(configLoader);

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

        // ── Provider adapters (Phase 9) ────────────────────────
        // Each adapter is registered as IExternalMetadataProvider so
        // MetadataHarvestingService receives them via DI enumeration.
        services.AddSingleton<IExternalMetadataProvider>(sp =>
            new AppleBooksAdapter(
                sp.GetRequiredService<IHttpClientFactory>(),
                MediaType.Epub,
                sp.GetRequiredService<ILogger<AppleBooksAdapter>>()));
        services.AddSingleton<IExternalMetadataProvider>(sp =>
            new AppleBooksAdapter(
                sp.GetRequiredService<IHttpClientFactory>(),
                MediaType.Audiobook,
                sp.GetRequiredService<ILogger<AppleBooksAdapter>>()));
        services.AddSingleton<IExternalMetadataProvider, AudnexusAdapter>();
        services.AddSingleton<IExternalMetadataProvider, WikidataAdapter>();

        // ── Metadata harvesting & person enrichment (Phase 9) ──
        services.AddSingleton<IMetadataHarvestingService, MetadataHarvestingService>();
        services.AddSingleton<IRecursiveIdentityService, RecursiveIdentityService>();

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
