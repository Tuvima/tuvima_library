using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Hubs;
using MediaEngine.Api.Middleware;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Intelligence;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Strategies;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Processors;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Identity;
using MediaEngine.Identity.Contracts;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager config  = builder.Configuration;

// ── CORS ──────────────────────────────────────────────────────────────────────
string[] allowedOrigins = config
    .GetSection("MediaEngine:Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWasm", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()); // Required for SignalR WebSocket/SSE transports
});

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    options.AddFilter<IntercomAuthFilter>();
});
builder.Services.AddSingleton<IEventPublisher, SignalREventPublisher>();

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Policy: key_generation — 5 requests per minute per IP (API key creation).
    options.AddPolicy("key_generation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window      = TimeSpan.FromMinutes(1),
            }));

    // Policy: streaming — 100 requests per minute per IP (file streaming).
    options.AddPolicy("streaming", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window      = TimeSpan.FromMinutes(1),
            }));

    // Policy: general — 60 requests per minute per IP (everything else).
    options.AddPolicy("general", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window      = TimeSpan.FromMinutes(1),
            }));
});

// ── Data Protection / Secret Store ────────────────────────────────────────────
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ISecretStore, DataProtectionSecretStore>();

// ── OpenAPI / Swagger ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Tuvima Library API", Version = "v1" });
});

// ── Storage / Database ────────────────────────────────────────────────────────
string dbPath = config["MediaEngine:DatabasePath"] ?? "library.db";
builder.Services.AddSingleton<IDatabaseConnection>(sp =>
{
    var db = new DatabaseConnection(dbPath);
    db.Open();
    db.InitializeSchema();
    db.RunStartupChecks();
    return db;
});

// ── Configuration Directory Loader ────────────────────────────────────────────
// Reads individual config files from config/ directory. Auto-migrates from
// legacy manifest on first run. Registered as both IStorageManifest
// (backward compat) and IConfigurationLoader (granular access).
string configDir     = config["MediaEngine:ConfigDirectory"] ?? "config";
string manifestPath  = config["MediaEngine:ManifestPath"] ?? "legacy_manifest.json";
var    configLoader  = new ConfigurationDirectoryLoader(configDir, manifestPath);
builder.Services.AddSingleton<IStorageManifest>(configLoader);
builder.Services.AddSingleton<IConfigurationLoader>(configLoader);

// Bootstrap the default universe config if it doesn't exist yet.
// The ConfigurationDirectoryLoader (in MediaEngine.Storage) creates the universe/
// directory but cannot write the Wikidata property map because that model lives
// in MediaEngine.Providers. This bootstrap bridges the two projects.
if (configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata") is null)
{
    UniverseConfiguration defaultUniverse = WikidataSparqlPropertyMap.ExportAsUniverseConfiguration();
    configLoader.SaveConfig("universe", "wikidata", defaultUniverse);
}
builder.Services.AddSingleton<ITransactionJournal, TransactionJournal>();
builder.Services.AddSingleton<IMediaAssetRepository, MediaAssetRepository>();
builder.Services.AddSingleton<IHubRepository, HubRepository>();
builder.Services.AddSingleton<IProviderConfigurationRepository, ProviderConfigurationRepository>();
builder.Services.AddSingleton<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddSingleton<IProfileRepository, ProfileRepository>();
builder.Services.AddSingleton<IProfileService, ProfileService>();

// ── Processors ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IVideoMetadataExtractor, StubVideoMetadataExtractor>();

builder.Services.AddSingleton<IProcessorRegistry>(sp =>
{
    var registry = new MediaProcessorRegistry();
    registry.Register(new EpubProcessor());
    registry.Register(new AudioProcessor());
    registry.Register(new VideoProcessor(sp.GetRequiredService<IVideoMetadataExtractor>()));
    registry.Register(new ComicProcessor());
    registry.Register(new GenericFileProcessor());
    return registry;
});

builder.Services.AddSingleton<IByteStreamer, ByteStreamer>();

// ── Intelligence ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IScoringStrategy, ExactMatchStrategy>();
builder.Services.AddSingleton<IScoringStrategy, LevenshteinStrategy>();

builder.Services.AddSingleton<IConflictResolver>(sp =>
    new ConflictResolver(sp.GetServices<IScoringStrategy>()));

builder.Services.AddSingleton<IScoringEngine>(sp =>
    new ScoringEngine(sp.GetRequiredService<IConflictResolver>()));

builder.Services.AddSingleton<IIdentityMatcher>(sp =>
    new IdentityMatcher(sp.GetServices<IScoringStrategy>()));

builder.Services.AddSingleton<IHubArbiter>(sp =>
    new HubArbiter(
        sp.GetRequiredService<IIdentityMatcher>(),
        sp.GetRequiredService<ITransactionJournal>()));

// ── Ingestion (for POST /ingestion/scan) ─────────────────────────────────────
builder.Services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.SectionName));

// PostConfigure reads saved folder paths from the core configuration and
// overrides the IngestionOptions values bound from appsettings.json.  This means
// a path saved via PUT /settings/folders survives an Engine restart without
// touching appsettings.json — the config directory is the persistent source of truth.
builder.Services.PostConfigure<IngestionOptions>(opts =>
{
    try
    {
        CoreConfiguration core = configLoader.LoadCore();
        if (!string.IsNullOrWhiteSpace(core.WatchDirectory))       { opts.WatchDirectory       = core.WatchDirectory; }
        if (!string.IsNullOrWhiteSpace(core.LibraryRoot))          { opts.LibraryRoot          = core.LibraryRoot; }
        if (!string.IsNullOrWhiteSpace(core.StagingDirectory))     { opts.StagingDirectory     = core.StagingDirectory; }
        if (!string.IsNullOrWhiteSpace(core.OrganizationTemplate)) { opts.OrganizationTemplate = core.OrganizationTemplate; }
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

builder.Services.AddSingleton<IAssetHasher, AssetHasher>();
builder.Services.AddSingleton<IFileWatcher, FileWatcher>();
builder.Services.AddSingleton<DebounceQueue>();
builder.Services.AddSingleton<IFileOrganizer, FileOrganizer>();
builder.Services.AddSingleton<IMetadataTagger, EpubMetadataTagger>();
builder.Services.AddSingleton<IMetadataTagger, AudioMetadataTagger>();
builder.Services.AddSingleton<IMetadataTagger, VideoMetadataTagger>();
builder.Services.AddSingleton<IMetadataTagger, ComicMetadataTagger>();
builder.Services.AddSingleton<IWriteBackService, MediaEngine.Ingestion.Services.WriteBackService>();
builder.Services.AddSingleton<IBackgroundWorker, BackgroundWorker>();

// IngestionEngine registered as a singleton, IIngestionEngine interface, AND a
// hosted service.  When WatchDirectory is configured, the background watcher loop
// starts automatically.  When WatchDirectory is empty at startup, the engine
// idles until the user sets a Watch Folder via PUT /settings/folders.
builder.Services.AddSingleton<IngestionEngine>();
builder.Services.AddSingleton<IIngestionEngine>(sp => sp.GetRequiredService<IngestionEngine>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestionEngine>());

// ── Folder Health Monitor (Phase 10 — Settings & Management) ─────────────────
// Periodic background check on Watch Folder + Library Root accessibility.
// Broadcasts FolderHealthChanged via SignalR when status changes.
builder.Services.AddHostedService<FolderHealthService>();

// ── External Metadata Providers (Phase 9 — Zero-Key) ─────────────────────────
// Named HttpClients: lifecycle managed by IHttpClientFactory.
// Short-lived probe client used by GET /settings/providers to test reachability.
// 3-second timeout is intentionally tight — the settings page should respond quickly.
builder.Services.AddHttpClient("settings_probe", c =>
{
    c.Timeout = TimeSpan.FromSeconds(5); // outer cap; each probe uses a 3-second CTS
});

// Wikidata keeps its two named HttpClients (coded adapter, not config-driven).
builder.Services.AddHttpClient("wikidata_api", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tuvima Library/1.0 (https://github.com/Tuvima/tuvima_library)");
});
builder.Services.AddHttpClient("wikidata_sparql", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tuvima Library/1.0 (https://github.com/Tuvima/tuvima_library)");
});

// Config-driven providers: scan config/providers/ and register each one.
// Named HttpClients + ConfigDrivenAdapter instances are created from config.
// All providers are registered regardless of Enabled state — the pipeline services
// check Enabled before using them, but the Settings UI needs adapter access for
// testing and configuring disabled providers.
foreach (ProviderConfiguration providerConfig in configLoader.LoadAllProviders())
{
    if (!string.Equals(providerConfig.AdapterType, "config_driven", StringComparison.OrdinalIgnoreCase))
    { continue; }

    string name = providerConfig.Name;
    int timeout = providerConfig.HttpClient?.TimeoutSeconds ?? 10;
    string? userAgent = providerConfig.HttpClient?.UserAgent;

    builder.Services.AddHttpClient(name, c =>
    {
        c.Timeout = TimeSpan.FromSeconds(timeout);
        if (!string.IsNullOrEmpty(userAgent))
        { c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent); }
    });

    // Capture for closure.
    ProviderConfiguration cfg = providerConfig;
    builder.Services.AddSingleton<IExternalMetadataProvider>(sp =>
        new ConfigDrivenAdapter(
            cfg,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<ConfigDrivenAdapter>>()));
}

// Storage repositories (Phase 9 — claim + canonical + person persistence).
builder.Services.AddSingleton<IMetadataClaimRepository,  MetadataClaimRepository>();
builder.Services.AddSingleton<ICanonicalValueRepository, CanonicalValueRepository>();
builder.Services.AddSingleton<IPersonRepository,         PersonRepository>();
builder.Services.AddSingleton<IMediaEntityChainFactory,  MediaEntityChainFactory>();

// Wikidata stays as a coded adapter — SPARQL cannot be expressed as URL templates.
builder.Services.AddSingleton<IExternalMetadataProvider, WikidataAdapter>();

builder.Services.AddSingleton<IMetadataHarvestingService, MetadataHarvestingService>();
builder.Services.AddSingleton<IRecursiveIdentityService,  RecursiveIdentityService>();

// ── Hydration pipeline (three-stage orchestrator) + review queue ─────────────
builder.Services.AddSingleton<IAutoOrganizeService,       AutoOrganizeService>();
builder.Services.AddSingleton<IHydrationPipelineService,  HydrationPipelineService>();
builder.Services.AddSingleton<IReviewQueueRepository,     ReviewQueueRepository>();
builder.Services.AddSingleton<IImageCacheRepository,      ImageCacheRepository>();

// ── Phase 7: Sidecar writer + Great Inhale scanner ───────────────────────────
builder.Services.AddSingleton<ISidecarWriter,  SidecarWriter>();
builder.Services.AddSingleton<ILibraryScanner, LibraryScanner>();

// ── Phase 1 (Activity Log): System activity ledger + daily pruning ───────────
builder.Services.AddSingleton<ISystemActivityRepository, SystemActivityRepository>();
builder.Services.AddHostedService<ActivityPruningService>();

// ── Library Reconciliation: periodic scan for missing files ──────────────────
builder.Services.AddSingleton<LibraryReconciliationService>();
builder.Services.AddSingleton<IReconciliationService>(sp =>
    sp.GetRequiredService<LibraryReconciliationService>());
builder.Services.AddHostedService<LibraryReconciliationService>(sp =>
    sp.GetRequiredService<LibraryReconciliationService>());

// ── UI Settings (three-tier cascade: Global → Device → Profile) ──────────────
builder.Services.AddSingleton<UISettingsCascadeResolver>();
builder.Services.AddSingleton<UISettingsCacheRepository>();

// ── Build ─────────────────────────────────────────────────────────────────────
WebApplication app = builder.Build();

// ── UI Settings cache warm-up ─────────────────────────────────────────────────
// Populate the SQLite cache from config/ui/ files so API reads are fast from
// the first request onward.  Errors are non-fatal — the resolver can still
// read directly from files.
try
{
    UISettingsCacheRepository uiCache = app.Services.GetRequiredService<UISettingsCacheRepository>();
    uiCache.RebuildFromFiles(configLoader);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "UI settings cache warm-up failed; resolver will fall back to files.");
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseCors("BlazorWasm");
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tuvima Library API v1"));
}

// ── Endpoint registration ─────────────────────────────────────────────────────
app.MapHub<CommunicationHub>("/hubs/intercom");
app.MapSystemEndpoints();
app.MapAdminEndpoints();
app.MapHubEndpoints();
app.MapStreamEndpoints();
app.MapIngestionEndpoints();
app.MapMetadataEndpoints();
app.MapReviewEndpoints();
app.MapSettingsEndpoints();
app.MapUISettingsEndpoints();
app.MapProfileEndpoints();
app.MapActivityEndpoints();

app.Run();
