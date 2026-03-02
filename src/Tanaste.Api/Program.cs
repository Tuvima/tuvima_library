using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Tanaste.Api.Endpoints;
using Tanaste.Api.Hubs;
using Tanaste.Api.Middleware;
using Tanaste.Api.Security;
using Tanaste.Api.Services;
using Tanaste.Domain.Contracts;
using Tanaste.Ingestion;
using Tanaste.Ingestion.Contracts;
using Tanaste.Ingestion.Models;
using Tanaste.Intelligence;
using Tanaste.Intelligence.Contracts;
using Tanaste.Intelligence.Strategies;
using Tanaste.Processors;
using Tanaste.Processors.Contracts;
using Tanaste.Processors.Processors;
using Tanaste.Storage;
using Tanaste.Storage.Contracts;
using Tanaste.Domain.Enums;
using Tanaste.Providers.Adapters;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Providers.Services;
using Tanaste.Identity;
using Tanaste.Identity.Contracts;

var builder = WebApplication.CreateBuilder(args);
var config  = builder.Configuration;

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = config
    .GetSection("Tanaste:Cors:AllowedOrigins")
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
    c.SwaggerDoc("v1", new() { Title = "Tanaste API", Version = "v1" });
});

// ── Storage / Database ────────────────────────────────────────────────────────
string dbPath = config["Tanaste:DatabasePath"] ?? "tanaste.db";
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
// legacy tanaste_master.json on first run. Registered as both IStorageManifest
// (backward compat) and IConfigurationLoader (granular access).
string configDir     = config["Tanaste:ConfigDirectory"] ?? "config";
string manifestPath  = config["Tanaste:ManifestPath"] ?? "tanaste_master.json";
var    configLoader  = new ConfigurationDirectoryLoader(configDir, manifestPath);
builder.Services.AddSingleton<IStorageManifest>(configLoader);
builder.Services.AddSingleton<IConfigurationLoader>(configLoader);

// Bootstrap the default universe config if it doesn't exist yet.
// The ConfigurationDirectoryLoader (in Tanaste.Storage) creates the universe/
// directory but cannot write the Wikidata property map because that model lives
// in Tanaste.Providers. This bootstrap bridges the two projects.
if (configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata") is null)
{
    var defaultUniverse = WikidataSparqlPropertyMap.ExportAsUniverseConfiguration();
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
        var core = configLoader.LoadCore();
        if (!string.IsNullOrWhiteSpace(core.WatchDirectory))       opts.WatchDirectory       = core.WatchDirectory;
        if (!string.IsNullOrWhiteSpace(core.LibraryRoot))          opts.LibraryRoot          = core.LibraryRoot;
        if (!string.IsNullOrWhiteSpace(core.OrganizationTemplate)) opts.OrganizationTemplate = core.OrganizationTemplate;
    }
    catch
    {
        // First run — config may not yet have folder keys; appsettings.json values stand.
    }
});

builder.Services.AddSingleton<IAssetHasher, AssetHasher>();
builder.Services.AddSingleton<IFileWatcher, FileWatcher>();
builder.Services.AddSingleton<DebounceQueue>();
builder.Services.AddSingleton<IFileOrganizer, FileOrganizer>();
builder.Services.AddSingleton<IMetadataTagger, EpubMetadataTagger>();
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

builder.Services.AddHttpClient("apple_books", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("audnexus", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Tanaste/1.0");
});
builder.Services.AddHttpClient("wikidata_api", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tanaste/1.0 (https://github.com/shyfaruqi/tanaste)");
});
builder.Services.AddHttpClient("wikidata_sparql", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tanaste/1.0 (https://github.com/shyfaruqi/tanaste)");
});
builder.Services.AddHttpClient("open_library", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Tanaste/1.0");
});
builder.Services.AddHttpClient("google_books", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Storage repositories (Phase 9 — claim + canonical + person persistence).
builder.Services.AddSingleton<IMetadataClaimRepository,  MetadataClaimRepository>();
builder.Services.AddSingleton<ICanonicalValueRepository, CanonicalValueRepository>();
builder.Services.AddSingleton<IPersonRepository,         PersonRepository>();
builder.Services.AddSingleton<IMediaEntityChainFactory,  MediaEntityChainFactory>();

// Provider adapters — each registered as IExternalMetadataProvider so
// MetadataHarvestingService receives them as IEnumerable<IExternalMetadataProvider>.
builder.Services.AddSingleton<IExternalMetadataProvider>(sp =>
    new AppleBooksAdapter(
        sp.GetRequiredService<IHttpClientFactory>(),
        MediaType.Epub,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AppleBooksAdapter>>()));
builder.Services.AddSingleton<IExternalMetadataProvider>(sp =>
    new AppleBooksAdapter(
        sp.GetRequiredService<IHttpClientFactory>(),
        MediaType.Audiobook,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AppleBooksAdapter>>()));
builder.Services.AddSingleton<IExternalMetadataProvider, AudnexusAdapter>();
builder.Services.AddSingleton<IExternalMetadataProvider, WikidataAdapter>();
builder.Services.AddSingleton<IExternalMetadataProvider, OpenLibraryAdapter>();
builder.Services.AddSingleton<IExternalMetadataProvider, GoogleBooksAdapter>();

builder.Services.AddSingleton<IMetadataHarvestingService, MetadataHarvestingService>();
builder.Services.AddSingleton<IRecursiveIdentityService,  RecursiveIdentityService>();

// ── Phase 7: Sidecar writer + Great Inhale scanner ───────────────────────────
builder.Services.AddSingleton<ISidecarWriter,  SidecarWriter>();
builder.Services.AddSingleton<ILibraryScanner, LibraryScanner>();

// ── Phase 1 (Activity Log): System activity ledger + daily pruning ───────────
builder.Services.AddSingleton<ISystemActivityRepository, SystemActivityRepository>();
builder.Services.AddHostedService<ActivityPruningService>();

// ── UI Settings (three-tier cascade: Global → Device → Profile) ──────────────
builder.Services.AddSingleton<UISettingsCascadeResolver>();
builder.Services.AddSingleton<UISettingsCacheRepository>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── UI Settings cache warm-up ─────────────────────────────────────────────────
// Populate the SQLite cache from config/ui/ files so API reads are fast from
// the first request onward.  Errors are non-fatal — the resolver can still
// read directly from files.
try
{
    var uiCache = app.Services.GetRequiredService<UISettingsCacheRepository>();
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tanaste API v1"));
}

// ── Endpoint registration ─────────────────────────────────────────────────────
app.MapHub<CommunicationHub>("/hubs/intercom");
app.MapSystemEndpoints();
app.MapAdminEndpoints();
app.MapHubEndpoints();
app.MapStreamEndpoints();
app.MapIngestionEndpoints();
app.MapMetadataEndpoints();
app.MapSettingsEndpoints();
app.MapUISettingsEndpoints();
app.MapProfileEndpoints();
app.MapActivityEndpoints();

app.Run();
