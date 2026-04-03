using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Hubs;
using MediaEngine.Api.Middleware;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Intelligence;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Services;
using MediaEngine.Intelligence.Strategies;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Extractors;
using MediaEngine.Processors.Processors;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Services;
using MediaEngine.Providers.Workers;
using MediaEngine.Identity;
using MediaEngine.Identity.Contracts;
using Microsoft.Extensions.Http.Resilience;
using Serilog;
using MediaEngine.Api.DevSupport;
using MediaEngine.Api.Services.HealthChecks;
using Tuvima.Wikidata.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager config  = builder.Configuration;

// ── Serilog ──────────────────────────────────────────────────────────────────
// Structured logging with rolling file output for headless Engine operation.
// Console output preserved for Docker / development.  Rolling files auto-delete
// after the configured retention period (default: 14 days).
builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine("logs", "tuvima-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 50 * 1024 * 1024,   // 50 MB per file
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}"));

// ── Windows Service hosting ────────────────────────────────────────────────────
// Integrates with the Windows Service Control Manager when the Engine is installed
// as a Windows service via the .exe installer.  Completely ignored on Linux / Docker.
builder.Host.UseWindowsService(options => options.ServiceName = "Tuvima Library");

// ── CORS ──────────────────────────────────────────────────────────────────────
string[] allowedOrigins = config
    .GetSection("MediaEngine:Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
// TUVIMA_CORS_ORIGINS: comma-separated extra origins appended at runtime.
// Useful for Docker deployments where the Dashboard URL differs from localhost.
// Example: "http://192.168.1.50:5016,https://tuvima.local"
string? envCorsOrigins = Environment.GetEnvironmentVariable("TUVIMA_CORS_ORIGINS");
if (!string.IsNullOrWhiteSpace(envCorsOrigins))
{
    string[] extra = envCorsOrigins.Split(",", StringSplitOptions.RemoveEmptyEntries
                                               | StringSplitOptions.TrimEntries);
    allowedOrigins  = [.. allowedOrigins, .. extra];
}

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

// Rate limiting is registered below, after the config loader is available.

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
// Register Dapper type handlers for Guid ↔ string and DateTimeOffset ↔ ISO-8601
// conversions.  Must run before any Dapper queries execute.
DapperConfiguration.Configure();

// TUVIMA_DB_PATH overrides the config value — used by Docker and the installer
// to pin the database to a persistent volume outside the container image.
// When a library_root is configured, the default resolves to {LibraryRoot}/.data/database/library.db.
string dbPath;
{
    var envDb = Environment.GetEnvironmentVariable("TUVIMA_DB_PATH");
    if (!string.IsNullOrWhiteSpace(envDb))
    {
        dbPath = envDb;
    }
    else
    {
        // Peek at core.json to get library_root early (before config loader is created).
        var earlyConfigDir = config["MediaEngine:ConfigDirectory"] ?? "config";
        var coreJsonPath = Path.Combine(earlyConfigDir, "core.json");
        string? earlyLibraryRoot = null;
        if (File.Exists(coreJsonPath))
        {
            try
            {
                using var fs = File.OpenRead(coreJsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("library_root", out var lr))
                    earlyLibraryRoot = lr.GetString();
            }
            catch { /* non-fatal — fall back to default */ }
        }
        // Also check environment variable override.
        var envLibRoot = Environment.GetEnvironmentVariable("TUVIMA_LIBRARY_ROOT");
        if (!string.IsNullOrWhiteSpace(envLibRoot))
            earlyLibraryRoot = envLibRoot;

        if (!string.IsNullOrWhiteSpace(earlyLibraryRoot))
            dbPath = Path.Combine(earlyLibraryRoot, ".data", "database", "library.db");
        else
            dbPath = Path.Combine(".data", "database", "library.db");
    }
    // Ensure the database directory exists before opening the connection.
    var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
    if (!string.IsNullOrEmpty(dbDir))
        Directory.CreateDirectory(dbDir);
}
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
// TUVIMA_CONFIG_DIR overrides the config directory — used by Docker to point
// to a mounted volume so configuration survives container image updates.
string configDir     = Environment.GetEnvironmentVariable("TUVIMA_CONFIG_DIR")
                    ?? config["MediaEngine:ConfigDirectory"]
                    ?? "config";
string manifestPath  = config["MediaEngine:ManifestPath"] ?? "legacy_manifest.json";
var    configLoader  = new ConfigurationDirectoryLoader(configDir, manifestPath);
builder.Services.AddSingleton<IStorageManifest>(configLoader);
builder.Services.AddSingleton<IConfigurationLoader>(configLoader);

// ── Rate Limiting ─────────────────────────────────────────────────────────────
// Policy parameters are loaded from config/core.json (rate_limiting section)
// so they can be tuned without recompiling.  Defaults match the previously
// hardcoded values: key_generation=5/min, streaming=100/min, general=60/min.
{
    var rateLimits = configLoader.LoadCore().RateLimiting;
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Policy: key_generation — strict per-IP limit for API key creation.
        options.AddPolicy("key_generation", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.KeyGeneration.PermitLimit,
                    Window      = TimeSpan.FromMinutes(rateLimits.KeyGeneration.WindowMinutes),
                }));

        // Policy: streaming — higher per-IP limit for file streaming/media playback.
        options.AddPolicy("streaming", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.Streaming.PermitLimit,
                    Window      = TimeSpan.FromMinutes(rateLimits.Streaming.WindowMinutes),
                }));

        // Policy: general — default per-IP limit for all other endpoints.
        options.AddPolicy("general", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.General.PermitLimit,
                    Window      = TimeSpan.FromMinutes(rateLimits.General.WindowMinutes),
                }));
    });
}

builder.Services.AddSingleton<ITransactionJournal, TransactionJournal>();
builder.Services.AddSingleton<IMediaAssetRepository, MediaAssetRepository>();
builder.Services.AddSingleton<IHubRepository, HubRepository>();
builder.Services.AddSingleton<IHubPlacementRepository, HubPlacementRepository>();
builder.Services.AddSingleton<IAudioFingerprintRepository, MediaEngine.Storage.AudioFingerprintRepository>();
builder.Services.AddSingleton<IProviderConfigurationRepository, ProviderConfigurationRepository>();
builder.Services.AddSingleton<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddSingleton<IProfileRepository, ProfileRepository>();
builder.Services.AddSingleton<IProfileService, ProfileService>();

// ── FFmpeg Service ────────────────────────────────────────────────────────────
// Auto-detects ffmpeg/ffprobe from tools/ffmpeg/ → PATH → config override.
// Logs a warning (not error) when binaries are absent — transcoding is optional.
builder.Services.AddSingleton<IFFmpegService, FFmpegService>();

// ── Processors ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IVideoMetadataExtractor, FFmpegVideoMetadataExtractor>();

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
builder.Services.AddSingleton<IFuzzyMatchingService, FuzzyMatchingService>();

// ScoringConfiguration is loaded from config/scoring.json and exposed as a
// DI singleton so every consumer reads the same config-driven thresholds.
// Converts from the storage-layer ScoringSettings (JSON-friendly mutable record)
// to the intelligence-layer ScoringConfiguration (immutable snapshot).
builder.Services.AddSingleton<MediaEngine.Intelligence.Models.ScoringConfiguration>(sp =>
{
    var loader = sp.GetRequiredService<IConfigurationLoader>();
    MediaEngine.Storage.Models.ScoringSettings s;
    try   { s = loader.LoadScoring(); }
    catch { s = new MediaEngine.Storage.Models.ScoringSettings(); }
    return new MediaEngine.Intelligence.Models.ScoringConfiguration
    {
        AutoLinkThreshold       = s.AutoLinkThreshold,
        ConflictThreshold       = s.ConflictThreshold,
        ConflictEpsilon         = s.ConflictEpsilon,
        StaleClaimDecayDays     = s.StaleClaimDecayDays,
        StaleClaimDecayFactor   = s.StaleClaimDecayFactor,
    };
});

builder.Services.AddSingleton<IScoringEngine, PriorityCascadeEngine>();
builder.Services.AddSingleton<IRetailMatchScoringService, RetailMatchScoringService>();
builder.Services.AddSingleton<ILocalMatchService, LocalMatchService>();

builder.Services.AddSingleton<IIdentityMatcher>(sp =>
    new IdentityMatcher(sp.GetRequiredService<IFuzzyMatchingService>()));

builder.Services.AddSingleton<IHubArbiter>(sp =>
    new HubArbiter(
        sp.GetRequiredService<IIdentityMatcher>(),
        sp.GetRequiredService<ITransactionJournal>()));

builder.Services.AddSingleton<IParentHubResolver>(sp =>
    new ParentHubResolver(
        sp.GetRequiredService<IHubRepository>(),
        sp.GetRequiredService<ILogger<ParentHubResolver>>()));

// ── Ingestion (for POST /ingestion/scan) ─────────────────────────────────────
builder.Services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.SectionName));

// PostConfigure reads saved folder paths from the core configuration and
// overrides the IngestionOptions values bound from appsettings.json.  This means
// a path saved via PUT /settings/folders survives an Engine restart without
// touching appsettings.json — the config directory is the persistent source of truth.
builder.Services.PostConfigure<IngestionOptions>(opts =>
{
    // ── Environment variable overrides (highest priority) ─────────────────────
    // These allow Docker / Unraid users to set paths via container environment
    // variables without ever editing a config file.
    //   TUVIMA_WATCH_FOLDER   → where to pick up new files
    //   TUVIMA_LIBRARY_ROOT   → where organised files are stored
    //   Note: staging area is always {LibraryRoot}/.data/staging/ — not independently configurable
    {
        string? envWatch   = Environment.GetEnvironmentVariable("TUVIMA_WATCH_FOLDER");
        string? envLibrary = Environment.GetEnvironmentVariable("TUVIMA_LIBRARY_ROOT");
        if (!string.IsNullOrWhiteSpace(envWatch))   { opts.WatchDirectory  = envWatch; }
        if (!string.IsNullOrWhiteSpace(envLibrary)) { opts.LibraryRoot     = envLibrary; }
    }

    try
    {
        CoreConfiguration core = configLoader.LoadCore();
        if (!string.IsNullOrWhiteSpace(core.WatchDirectory))       { opts.WatchDirectory       = core.WatchDirectory; }
        if (!string.IsNullOrWhiteSpace(core.LibraryRoot))          { opts.LibraryRoot          = core.LibraryRoot; opts.AutoOrganize = true; }
        if (!string.IsNullOrWhiteSpace(core.OrganizationTemplate)) { opts.OrganizationTemplate = core.OrganizationTemplate; }
        if (core.OrganizationTemplates.Count > 0) { opts.OrganizationTemplates = new Dictionary<string, string>(core.OrganizationTemplates, StringComparer.OrdinalIgnoreCase); }
        opts.ConfiguredLanguage = core.Language.Metadata;

    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[WARN] Failed to load core configuration for ingestion options — using defaults: {ex.Message}");
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

    // Load library folder priors from config/libraries.json.
    // These give the ingestion engine a strong media type prior when a file arrives
    // from a folder whose content category is already known (e.g. Books folder → Audiobook).
    try
    {
        var libraries = configLoader.LoadLibraries();
        opts.LibraryFolders = libraries.Libraries
            .Where(l => !string.IsNullOrWhiteSpace(l.SourcePath))
            .Select(l => new MediaEngine.Ingestion.Models.LibraryFolderEntry
            {
                SourcePath = l.SourcePath,
                MediaTypes = l.MediaTypes
                    .Select(s => ParseMediaTypeFromConfig(s))
                    .Where(mt => mt != MediaEngine.Domain.Enums.MediaType.Unknown)
                    .ToList(),
            })
            .Where(e => e.MediaTypes.Count > 0)
            .ToList();

    }
    catch
    {
        // No libraries.json or parse failure — library folder priors will not be applied.
    }
});

// Auto-create media type subfolders in each configured watch directory.
// Called after DI is configured but before the host starts so the directories
// exist before the FileSystemWatcher attaches.
try
{
    var libsForSubfolders = configLoader.LoadLibraries();
    var mediaTypeSubfolders = new[] { "Books", "Audiobooks", "Movies", "TV", "Music", "Comics", "Podcasts" };
    foreach (var lib in libsForSubfolders.Libraries.Where(l => !string.IsNullOrWhiteSpace(l.SourcePath)))
    {
        if (Directory.Exists(lib.SourcePath))
        {
            foreach (var sub in mediaTypeSubfolders)
                Directory.CreateDirectory(Path.Combine(lib.SourcePath, sub));
        }
    }
}
catch
{
    // No libraries.json or directory creation failed — non-fatal; directories will be created on demand.
}

// Auto-create the .data/ subdirectories under LibraryRoot at startup.
// These consolidate staging, images, and database under a single hidden folder.
try
{
    var coreForDataDir = configLoader.LoadCore();
    if (!string.IsNullOrWhiteSpace(coreForDataDir.LibraryRoot))
    {
        Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "staging"));
        Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "images"));
        Directory.CreateDirectory(Path.Combine(coreForDataDir.LibraryRoot, ".data", "database"));
    }
}
catch
{
    // LibraryRoot not yet configured or directory creation failed — non-fatal.
}

// ── Image Path Service ────────────────────────────────────────────────────────
// Centralizes image path resolution. All artwork lives under {libraryRoot}/.data/images/
// organized by entity type and QID. Registered as a singleton so all services that
// read or write cover art and hero banners use the same root path.
builder.Services.AddSingleton(sp =>
{
    var core = sp.GetRequiredService<IConfigurationLoader>().LoadCore();
    var libraryRoot = core.LibraryRoot;
    if (string.IsNullOrWhiteSpace(libraryRoot))
    {
        // LibraryRoot not yet configured (first run) — use a temp sentinel path.
        // Services that write images guard against this with their own checks.
        libraryRoot = Path.Combine(Path.GetTempPath(), "tuvima_images_unset");
    }
    return new MediaEngine.Domain.Services.ImagePathService(libraryRoot);
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
})
.AddStandardResilienceHandler();

// Named HttpClient for the ReconciliationAdapter (wikidata.reconci.link + Wikimedia Commons).
// 30-second timeout to accommodate batch SPARQL-style data extension queries.
builder.Services.AddHttpClient("wikidata_reconciliation", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tuvima Library/1.0 (https://github.com/Tuvima/tuvima_library)");
})
.AddStandardResilienceHandler();

// Image download clients used by SynchronousIdentityPipelineService (cover art) and
// MetadataHarvestingService (person headshots).  Wikimedia Commons requires a
// descriptive User-Agent header and honours HTTP redirects automatically.
builder.Services.AddHttpClient("cover_download", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tuvima Library/1.0 (https://github.com/Tuvima/tuvima_library)");
})
.AddStandardResilienceHandler();
builder.Services.AddHttpClient("headshot_download", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Tuvima Library/1.0 (https://github.com/Tuvima/tuvima_library)");
})
.AddStandardResilienceHandler();

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
    })
    .AddStandardResilienceHandler();

    // Capture for closure.
    ProviderConfiguration cfg = providerConfig;
    builder.Services.AddSingleton<IExternalMetadataProvider>(sp =>
        new ConfigDrivenAdapter(
            cfg,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<ConfigDrivenAdapter>>(),
            sp.GetRequiredService<IProviderHealthMonitor>(),
            sp.GetRequiredService<IProviderResponseCacheRepository>()));
}

// Storage repositories (Phase 9 — claim + canonical + person persistence).
builder.Services.AddSingleton<IMetadataClaimRepository,  MetadataClaimRepository>();
builder.Services.AddSingleton<ICanonicalValueRepository, CanonicalValueRepository>();
builder.Services.AddSingleton<IPersonRepository,         PersonRepository>();
builder.Services.AddSingleton<IWorkRepository,           WorkRepository>();
builder.Services.AddSingleton<IMediaEntityChainFactory,  MediaEntityChainFactory>();

// QID label cache and multi-value array storage (QID-first architecture).
builder.Services.AddSingleton<IQidLabelRepository,            QidLabelRepository>();
builder.Services.AddSingleton<IQidLabelResolver,              QidLabelResolver>();
builder.Services.AddSingleton<ICanonicalValueArrayRepository, CanonicalValueArrayRepository>();

// ── WikidataReconciler — unified Wikidata/Wikipedia API client ────────────────
// Provides reconciliation, entity fetching, property extraction, Wikipedia summaries,
// and image URLs — all with built-in maxlag, retry, and concurrency control.
// MIT license — Tuvima.Wikidata NuGet package.
{
    var coreConfig = configLoader.LoadCore();
    var reconcilerOptions = new Tuvima.Wikidata.WikidataReconcilerOptions
    {
        UserAgent = "Tuvima Library/1.0 (https://github.com/Tuvima/tuvima_library)",
        Language  = coreConfig.Language.Metadata,
        // MaxLag: Wikidata's query-service lag frequently exceeds 5s, causing all
        // action=query&list=search calls (including haswbstatement bridge resolution)
        // to fail silently. Setting to 0 disables the maxlag check — acceptable for
        // a single-user personal tool making infrequent requests.
        MaxLag    = 0,
        // P279 subclass walking: depth 3 matches our former custom walk. The library's
        // internal BFS walker with ConcurrentDictionary cache replaces our _learnedClasses.
        TypeHierarchyDepth = 3,
        // Include Wikipedia sitelink titles in the label scoring pool so common names
        // like "Frankenstein" score higher than the formal label.
        IncludeSitelinkLabels = true,
        // Add ISBN properties to the unique ID set so reconciliation scores 100 on exact
        // ISBN match — replaces our manual +100 ISBN scoring in FilterByMediaTypeAsync.
        UniqueIdProperties = new HashSet<string>
        {
            "P213",  // ISNI
            "P214",  // VIAF ID
            "P227",  // GND ID
            "P244",  // Library of Congress authority ID
            "P268",  // BnF ID
            "P269",  // IdRef ID
            "P349",  // National Diet Library ID
            "P496",  // ORCID iD
            "P906",  // SELIBR ID
            "P1006", // NTA ID (Netherlands)
            "P1015", // NORAF ID
            "P1566", // GeoNames ID
            "P2427", // GRID ID
            // Media-specific identifiers for Tuvima Library
            "P212",  // ISBN-13
            "P957",  // ISBN-10
            "P345",  // IMDb ID
            "P4947", // TMDB movie ID
            "P5749", // Amazon Standard Identification Number (ASIN)
            "P434",  // MusicBrainz artist ID
            "P436",  // MusicBrainz release group ID
        },
    };

    builder.Services.AddHttpClient("WikidataReconciliation", client =>
    {
        client.Timeout = reconcilerOptions.Timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(reconcilerOptions.UserAgent);
    })
    .AddStandardResilienceHandler();

    builder.Services.AddSingleton(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>()
            .CreateClient("WikidataReconciliation");
        return new Tuvima.Wikidata.WikidataReconciler(httpClient, reconcilerOptions);
    });
}

// ReconciliationAdapter — Wikidata Reconciliation API + Data Extension API.
// Registered as both its concrete type and IExternalMetadataProvider so the
// hydration pipeline can inject the concrete type for direct method calls.
{
    var reconConfig = configLoader.LoadConfig<MediaEngine.Storage.Models.ReconciliationProviderConfig>(
        "providers", "wikidata_reconciliation");
    if (reconConfig is not null)
    {
        builder.Services.AddSingleton<ReconciliationAdapter>(sp =>
            new ReconciliationAdapter(
                reconConfig,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<ReconciliationAdapter>>(),
                sp.GetRequiredService<IFuzzyMatchingService>(),
                sp.GetRequiredService<IProviderResponseCacheRepository>(),
                sp.GetRequiredService<IConfigurationLoader>(),
                sp.GetService<Tuvima.Wikidata.WikidataReconciler>()));
        builder.Services.AddSingleton<IExternalMetadataProvider>(
            sp => sp.GetRequiredService<ReconciliationAdapter>());
    }
    else
    {
        Console.Error.WriteLine(
            "[WARN] config/providers/wikidata_reconciliation.json not found — " +
            "ReconciliationAdapter will not be registered.");
    }
}

builder.Services.AddSingleton<IMetadataHarvestingService, MetadataHarvestingService>();
builder.Services.AddSingleton<IRecursiveIdentityService,  RecursiveIdentityService>();
builder.Services.AddSingleton<IPersonReconciliationService, PersonReconciliationService>();
builder.Services.AddSingleton<ICanonDiscrepancyService,   CanonDiscrepancyService>();

// ── Universe graph (fictional entities, relationships, narrative roots) ──────
builder.Services.AddSingleton<IFictionalEntityRepository,      FictionalEntityRepository>();
builder.Services.AddSingleton<IEntityRelationshipRepository,    EntityRelationshipRepository>();
builder.Services.AddSingleton<INarrativeRootRepository,         NarrativeRootRepository>();
builder.Services.AddSingleton<INarrativeRootResolver,           NarrativeRootResolver>();
builder.Services.AddSingleton<IRecursiveFictionalEntityService, RecursiveFictionalEntityService>();
builder.Services.AddSingleton<IRelationshipPopulationService,   RelationshipPopulationService>();
builder.Services.AddSingleton<IUniverseGraphWriterService,      UniverseGraphWriterService>();
builder.Services.AddSingleton<IUniverseGraphQueryService,       UniverseGraphQueryService>();
builder.Services.AddSingleton<ICharacterPortraitRepository,      CharacterPortraitRepository>();
builder.Services.AddSingleton<IEntityAssetRepository,            EntityAssetRepository>();
builder.Services.AddSingleton<ILoreDeltaService,                LoreDeltaService>();
builder.Services.AddSingleton<IEraActorResolverService,         EraActorResolverService>();
builder.Services.AddSingleton<IImageEnrichmentService,           MediaEngine.Providers.Services.ImageEnrichmentService>();

// ── Hydration pipeline (three-stage orchestrator) + review queue ─────────────
builder.Services.AddSingleton<IOrganizationGate,             OrganizationGate>();
builder.Services.AddSingleton<IAutoOrganizeService,          AutoOrganizeService>();
builder.Services.AddSingleton<IDeferredEnrichmentRepository, DeferredEnrichmentRepository>();
builder.Services.AddSingleton<IHydrationPipelineService,     SynchronousIdentityPipelineService>();
builder.Services.AddSingleton<IDeferredEnrichmentService,    DeferredEnrichmentService>();
builder.Services.AddSingleton<IBridgeIdRepository,           BridgeIdRepository>();
builder.Services.AddSingleton<IEntityTimelineRepository,     EntityTimelineRepository>();
builder.Services.AddSingleton<IReviewQueueRepository,        ReviewQueueRepository>();
builder.Services.AddSingleton<IIngestionBatchRepository,     IngestionBatchRepository>();
builder.Services.AddSingleton<IPendingPersonSignalRepository, PendingPersonSignalRepository>();
builder.Services.AddSingleton<IRegistryRepository,           RegistryRepository>();
builder.Services.AddSingleton<ISearchIndexRepository,        SearchIndexRepository>();
builder.Services.AddSingleton<ISearchService,                SearchService>();
builder.Services.AddSingleton<IImageCacheRepository,              ImageCacheRepository>();
builder.Services.AddSingleton<IProviderResponseCacheRepository,  ProviderResponseCacheRepository>();
builder.Services.AddSingleton<ISearchResultsCacheRepository,     SearchResultsCacheRepository>();
builder.Services.AddSingleton<IProviderHealthRepository,         ProviderHealthRepository>();

// ── Identity Pipeline (v2 — durable job model) ─────────────────────────
builder.Services.AddSingleton<IIdentityJobRepository, IdentityJobRepository>();
builder.Services.AddSingleton<IRetailCandidateRepository, RetailCandidateRepository>();
builder.Services.AddSingleton<IWikidataCandidateRepository, WikidataCandidateRepository>();

// Pipeline helpers
builder.Services.AddSingleton<BridgeIdHelper>();
builder.Services.AddSingleton<StageOutcomeFactory>();
builder.Services.AddSingleton<TimelineRecorder>();
builder.Services.AddSingleton<BatchProgressService>();

// Enrichment workers
builder.Services.AddSingleton<CoverArtWorker>();
builder.Services.AddSingleton<PersonEnrichmentWorker>();
builder.Services.AddSingleton<ChildEntityWorker>();
builder.Services.AddSingleton<FictionalEntityWorker>();
builder.Services.AddSingleton<DescriptionEnrichmentWorker>();

// Enrichment orchestrator
builder.Services.AddSingleton<IEnrichmentService, EnrichmentService>();

// Pipeline workers
builder.Services.AddSingleton<RetailMatchWorker>();
builder.Services.AddSingleton<WikidataBridgeWorker>();
builder.Services.AddSingleton<QuickHydrationWorker>();
builder.Services.AddSingleton<MediaEngine.Providers.Services.PostPipelineService>();

// ── Provider Health Monitor: active probes, recovery flush, SignalR events ────
builder.Services.AddSingleton<ProviderHealthMonitorService>();
builder.Services.AddSingleton<IProviderHealthMonitor>(sp => sp.GetRequiredService<ProviderHealthMonitorService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderHealthMonitorService>());

// ── Great Inhale scanner ──────────────────────────────────────────────────────
builder.Services.AddSingleton<ILibraryScanner, LibraryScanner>();

// ── Hero banner generation (cover art → cinematic hero.jpg) ──────────────────
builder.Services.AddSingleton<IHeroBannerGenerator, HeroBannerGenerator>();

// -- EPUB reader content service (chapter serving, TOC, search) ---------------
builder.Services.AddSingleton<IEpubContentService, EpubContentService>();

// -- EPUB reader data repositories (bookmarks, highlights, statistics, alignment) --
builder.Services.AddSingleton<IReaderBookmarkRepository, ReaderBookmarkRepository>();
builder.Services.AddSingleton<IReaderHighlightRepository, ReaderHighlightRepository>();
builder.Services.AddSingleton<IReaderStatisticsRepository, ReaderStatisticsRepository>();
builder.Services.AddSingleton<IAlignmentJobRepository, AlignmentJobRepository>();
builder.Services.AddSingleton<IWhisperSyncService, WhisperSyncService>();
builder.Services.AddHostedService<WhisperSyncBackgroundService>();

// ── Phase 1 (Activity Log): System activity ledger + daily pruning ───────────
builder.Services.AddSingleton<ISystemActivityRepository, SystemActivityRepository>();
builder.Services.AddSingleton<IIngestionLogRepository, IngestionLogRepository>();
builder.Services.AddSingleton<IResolverCacheRepository, ResolverCacheRepository>();
builder.Services.AddHostedService<ActivityPruningService>();
builder.Services.AddHostedService<RejectedFileCleanupService>();
builder.Services.AddHostedService<MissingUniverseSweepService>();
builder.Services.AddHostedService<HydrationStartupSweepService>();
builder.Services.AddHostedService<EditionRecheckService>();

// ── Library Reconciliation: periodic scan for missing files ──────────────────
builder.Services.AddSingleton<LibraryReconciliationService>();
builder.Services.AddSingleton<IReconciliationService>(sp =>
    sp.GetRequiredService<LibraryReconciliationService>());
builder.Services.AddHostedService<LibraryReconciliationService>(sp =>
    sp.GetRequiredService<LibraryReconciliationService>());

// ── User State: progress tracking for media playback ─────────────────────────
builder.Services.AddSingleton<IUserStateStore, UserStateRepository>();

// ── UI Settings (three-tier cascade: Global → Device → Profile) ──────────────
builder.Services.AddSingleton<UISettingsCascadeResolver>();
builder.Services.AddSingleton<UISettingsCacheRepository>();

// ── AI Services ──────────────────────────────────────────────────────────────
// Load AI settings from config/ai.json (uses the generic loader to avoid circular refs).
var aiSettings = configLoader.LoadAi<MediaEngine.AI.Configuration.AiSettings>()
    ?? new MediaEngine.AI.Configuration.AiSettings();

// Resolve the models directory from environment variable or config default.
var modelsDir = Environment.GetEnvironmentVariable("TUVIMA_MODELS_DIR");
if (!string.IsNullOrEmpty(modelsDir))
    aiSettings.ModelsDirectory = modelsDir;

builder.Services.AddSingleton(aiSettings);
builder.Services.AddSingleton<MediaEngine.AI.Infrastructure.ModelInventory>();
builder.Services.AddSingleton<IModelDownloadManager, MediaEngine.AI.Infrastructure.ModelDownloadManager>();
builder.Services.AddSingleton<IModelLifecycleManager, MediaEngine.AI.Infrastructure.ModelLifecycleManager>();
builder.Services.AddHostedService<MediaEngine.Api.Services.ModelAutoDownloadService>();

// AI background services.
builder.Services.AddHostedService<MediaEngine.Api.Services.VibeBatchService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.SeriesAlignmentBackgroundService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.TasteProfileBackgroundService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.DescriptionIntelligenceBatchService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.UniverseEnrichmentService>();

// Pipeline hosted services.
builder.Services.AddHostedService<MediaEngine.Api.Services.RetailMatchHostedService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.WikidataBridgeHostedService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.QuickHydrationHostedService>();

// AI inference and feature services.
builder.Services.AddSingleton<MediaEngine.AI.Llama.LlamaInferenceService>();
builder.Services.AddSingleton<MediaEngine.AI.Llama.ILlamaInferenceService>(
    sp => sp.GetRequiredService<MediaEngine.AI.Llama.LlamaInferenceService>());
builder.Services.AddSingleton<MediaEngine.AI.Whisper.WhisperInferenceService>();
builder.Services.AddSingleton<MediaEngine.AI.Whisper.AudioPreprocessor>();

// Sprint 2: Ingestion features.
builder.Services.AddSingleton<ISmartLabeler, MediaEngine.AI.Features.SmartLabeler>();
builder.Services.AddSingleton<IMediaTypeAdvisor, MediaEngine.AI.Features.MediaTypeAdvisor>();

// Sprint 3: Alignment features.
builder.Services.AddSingleton<IQidDisambiguator, MediaEngine.AI.Features.QidDisambiguator>();
builder.Services.AddSingleton<ISeriesAligner, MediaEngine.AI.Features.SeriesAligner>();
builder.Services.AddSingleton<IWatchingOrderAdvisor, MediaEngine.AI.Features.WatchingOrderAdvisor>();

// Sprint 4: Enrichment features.
builder.Services.AddSingleton<IVibeTagger, MediaEngine.AI.Features.VibeTagger>();
builder.Services.AddSingleton<ITldrGenerator, MediaEngine.AI.Features.TldrGenerator>();
builder.Services.AddSingleton<ICoverArtValidator, MediaEngine.AI.Features.CoverArtValidator>();
builder.Services.AddSingleton<IAudioSimilarityService, MediaEngine.AI.Features.AudioSimilarityService>();
builder.Services.AddSingleton<ICoverArtHashService, MediaEngine.AI.Features.CoverArtHashService>();

// Sprint 6: Personalization features.
builder.Services.AddSingleton<ITasteProfiler, MediaEngine.AI.Features.TasteProfiler>();
builder.Services.AddSingleton<IWhyExplainer, MediaEngine.AI.Features.WhyExplainer>();

// Sprint 7: Discovery features.
builder.Services.AddSingleton<IIntentSearchParser, MediaEngine.AI.Features.IntentSearchParser>();

// Sprint 8: Advanced features.
builder.Services.AddSingleton<IUrlMetadataExtractor, MediaEngine.AI.Features.UrlMetadataExtractor>();

// Sprint 9: Description Intelligence.
builder.Services.AddSingleton<IDescriptionIntelligenceService, MediaEngine.AI.Features.DescriptionIntelligenceService>();

// Sprint 3: GPU backend detection (Vulkan → CUDA → CPU probe chain).
builder.Services.AddSingleton<MediaEngine.AI.Infrastructure.GpuBackendDetector>();

// Resource monitor — checks RAM, CPU pressure, and transcoding activity before loading models.
builder.Services.AddSingleton<MediaEngine.AI.Infrastructure.ResourceMonitorService>();

// Sprint 2: Hardware auto-profiling.
builder.Services.AddSingleton<MediaEngine.AI.Infrastructure.HardwareBenchmarkService>();
builder.Services.AddHostedService<MediaEngine.Api.Services.HardwareBenchmarkBackgroundService>();

// ── Health Checks ────────────────────────────────────────────────────────────
// Standard /health endpoint for Docker HEALTHCHECK, monitoring tools, etc.
builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite", tags: ["db"])
    .AddCheck<LibraryRootHealthCheck>("library_root", tags: ["storage"])
    .AddCheck<WatchFolderHealthCheck>("watch_folder", tags: ["storage"]);

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

// ── Purge orphaned review queue entries ──────────────────────────────────────
// Review queue rows referencing deleted media assets can accumulate and inflate
// the badge count. Purge them once at startup before the Engine accepts requests.
try
{
    var reviewRepo = app.Services.GetRequiredService<IReviewQueueRepository>();
    var purged = await reviewRepo.PurgeOrphanedAsync();
    if (purged > 0)
        app.Logger.LogInformation("Purged {Count} orphaned review queue entries", purged);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Orphaned review queue purge failed; counts may be inflated until next restart.");
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseCors("BlazorWasm");
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tuvima Library API v1"));
}

// ── Endpoint registration ─────────────────────────────────────────────────────
app.MapHub<CommunicationHub>(SignalREvents.HubPath);
app.MapSystemEndpoints();
app.MapAdminEndpoints();
app.MapHubEndpoints();
app.MapLibraryEndpoints();
app.MapStreamEndpoints();
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
app.MapProgressEndpoints();
app.MapActivityEndpoints();
app.MapUniverseGraphEndpoints();
app.MapCharacterEndpoints();
app.MapCanonEndpoints();
app.MapDeferredEnrichmentEndpoints();
app.MapRegistryEndpoints();
app.MapTimelineEndpoints();
app.MapSearchEndpoints();
app.MapReportEndpoints();
app.MapDebugEndpoints();
app.MapAiEndpoints();
app.MapAiEnrichmentEndpoints();

// ── Development-only seed endpoints ──────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapDevSeedEndpoints();
    app.MapIntegrationTestEndpoints();
}

// Seed default managed hubs (System Lists, Mixes, Smart Hubs) on first run
{
    var hubRepo = app.Services.GetRequiredService<IHubRepository>();
    MediaEngine.Api.Services.HubSeeder.SeedManagedHubsAsync(hubRepo).GetAwaiter().GetResult();
}

app.Run();

// ── Local helpers ────────────────────────────────────────────────────────────

/// <summary>
/// Maps config/libraries.json media type strings to the <see cref="MediaType"/> enum.
/// Config uses short names ("Epub", "Audiobook") that differ from enum values
/// ("Books", "Audiobooks"). This mapping bridges them.
/// </summary>
static MediaEngine.Domain.Enums.MediaType ParseMediaTypeFromConfig(string configValue)
{
    // First try direct enum parse (handles "Books", "Movies", "TV", "Music", "Podcasts", "Comic").
    if (Enum.TryParse<MediaEngine.Domain.Enums.MediaType>(configValue, ignoreCase: true, out var mt))
        return mt;

    // Map config aliases to enum values.
    return configValue.ToLowerInvariant() switch
    {
        "epub"      => MediaEngine.Domain.Enums.MediaType.Books,
        "ebook"     => MediaEngine.Domain.Enums.MediaType.Books,
        "audiobook" => MediaEngine.Domain.Enums.MediaType.Audiobooks,
        "comics"    => MediaEngine.Domain.Enums.MediaType.Comics,
        "podcast"   => MediaEngine.Domain.Enums.MediaType.Podcasts,
        "movie"     => MediaEngine.Domain.Enums.MediaType.Movies,
        _           => MediaEngine.Domain.Enums.MediaType.Unknown,
    };
}
