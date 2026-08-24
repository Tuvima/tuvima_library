using System.Threading.RateLimiting;
using MediaEngine.Api.DependencyInjection;
using MediaEngine.Api.DevSupport;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Middleware;
using MediaEngine.Api.Realtime;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Api.Services.HealthChecks;
using MediaEngine.Api.Services.View;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Ingestion.DependencyInjection;
using MediaEngine.Storage;
using MediaEngine.Storage.Configuration;
using MediaEngine.Storage.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
ConfigurationManager config = builder.Configuration;
string configDirectory =
    Environment.GetEnvironmentVariable("TUVIMA_CONFIG_DIR")
    ?? config["MediaEngine:ConfigDirectory"]
    ?? "config";

// -- Serilog ------------------------------------------------------------------
builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine("logs", "tuvima-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}"));

builder.Host.UseWindowsService(options => options.ServiceName = "Tuvima Library");

// -- CORS ---------------------------------------------------------------------
string[] allowedOrigins = config
    .GetSection("MediaEngine:Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
string? envCorsOrigins = Environment.GetEnvironmentVariable("TUVIMA_CORS_ORIGINS");
if (!string.IsNullOrWhiteSpace(envCorsOrigins))
{
    string[] extra = envCorsOrigins.Split(
        ",",
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    allowedOrigins = [.. allowedOrigins, .. extra];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWasm", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// -- SignalR / API infrastructure ---------------------------------------------
builder.Services.AddSignalR(options =>
{
    options.AddFilter<IntercomAuthFilter>();
});
builder.Services.AddSingleton<IEventPublisher, SignalREventPublisher>();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ISecretStore, DataProtectionSecretStore>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IViewRequestProfileContext, HttpViewRequestProfileContext>();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Tuvima Library API", Version = "v1" });
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));
});

// -- Storage bootstrap ---------------------------------------------------------
DapperConfiguration.Configure();
string dbPath;
{
    var environmentDatabasePath = Environment.GetEnvironmentVariable("TUVIMA_DB_PATH");
    if (!string.IsNullOrWhiteSpace(environmentDatabasePath))
    {
        dbPath = environmentDatabasePath;
    }
    else
    {
        var coreJsonPath = Path.Combine(configDirectory, "core.json");
        string? earlyLibraryRoot = null;
        if (File.Exists(coreJsonPath))
        {
            using var stream = File.OpenRead(coreJsonPath);
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("library_root", out var libraryRoot))
            {
                earlyLibraryRoot = libraryRoot.GetString();
            }
        }

        var environmentLibraryRoot =
            Environment.GetEnvironmentVariable("TUVIMA_LIBRARY_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentLibraryRoot))
        {
            earlyLibraryRoot = environmentLibraryRoot;
        }

        dbPath = !string.IsNullOrWhiteSpace(earlyLibraryRoot)
            ? Path.Combine(earlyLibraryRoot, ".data", "database", "library.db")
            : Path.Combine(".data", "database", "library.db");
    }

    var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
    if (!string.IsNullOrEmpty(databaseDirectory))
    {
        Directory.CreateDirectory(databaseDirectory);
    }
}

DatabaseBackupService.ApplyPendingRestore(configDirectory, dbPath);

builder.Services.AddSingleton<IDatabaseConnection>(_ =>
{
    var database = new DatabaseConnection(dbPath);
    database.Open();
    database.InitializeSchema();
    database.RunStartupChecks();
    return database;
});

ConfigurationDirectoryLoader configLoader;
try
{
    configLoader = new ConfigurationDirectoryLoader(configDirectory);
    configLoader.StartWatching();
    configLoader.ConfigurationChanged += (_, change) =>
    {
        if (change.Applied)
        {
            Log.Information("Configuration reloaded: {ConfigFile}", change.RelativePath);
        }
        else
        {
            Log.Warning(
                change.Error,
                "Configuration reload rejected for {ConfigFile}; keeping last-known-good values.",
                change.RelativePath);
        }
    };
}
catch (ConfigValidationException ex)
{
    Log.Fatal(
        ex,
        "Invalid Tuvima Library configuration: {ConfigFile} failed {SchemaName}",
        ex.FilePath,
        ex.SchemaName);
    throw;
}

builder.Services.AddSingleton<IConfigurationLoader>(configLoader);

// -- Rate limiting -------------------------------------------------------------
{
    var rateLimits = configLoader.LoadCore().RateLimiting;
    string[] globalLimiterExemptPaths = ["/system/status", "/health"];
    string[] globalLimiterExemptPrefixes =
    [
        "/swagger",
        SignalREvents.IntercomPath,
        "/stream",
        "/read",
        "/playback",
        "/admin/api-keys",
    ];

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (rejectedContext, _) =>
        {
            if (rejectedContext.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfter))
            {
                rejectedContext.HttpContext.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return ValueTask.CompletedTask;
        };
        options.AddPolicy("key_generation", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.KeyGeneration.PermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimits.KeyGeneration.WindowMinutes),
                }));
        options.AddPolicy("streaming", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.Streaming.PermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimits.Streaming.WindowMinutes),
                }));
        options.AddPolicy("general", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.General.PermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimits.General.WindowMinutes),
                }));
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var isExempt =
                globalLimiterExemptPaths.Any(exempt =>
                    path.Equals(exempt, StringComparison.OrdinalIgnoreCase))
                || globalLimiterExemptPrefixes.Any(prefix =>
                    path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
            if (isExempt)
            {
                return RateLimitPartition.GetNoLimiter("exempt");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.General.PermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimits.General.WindowMinutes),
                });
        });
    });
}

// -- Composition roots ---------------------------------------------------------
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddSingleton<IApiKeyLookupCache, ApiKeyLookupCache>();
builder.Services.AddSingleton<ILibraryAccessEvaluator, LibraryAccessEvaluator>();
builder.Services.AddTuvimaStorage();
builder.Services.AddTuvimaPlayback();
builder.Services.AddMediaEngineIngestion(config, configLoader);
builder.Services.AddSingleton<DevHarnessResetService>();
builder.Services.AddSingleton<AssetStoreCleanupService>();
builder.Services.AddSingleton(sp => new DatabaseBackupService(
    sp.GetRequiredService<IDatabaseConnection>(),
    configDirectory));
builder.Services.AddTuvimaDisplay();
builder.Services.AddTuvimaIntelligence();
builder.Services.AddTuvimaProviders(configLoader);
builder.Services.AddTuvimaAi(configLoader);
builder.Services.AddTuvimaPlugins();
builder.Services.AddTuvimaHostedServices();

builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite", tags: ["db"])
    .AddCheck<LibraryRootHealthCheck>("library_root", tags: ["storage"])
    .AddCheck<WatchFolderHealthCheck>("watch_folder", tags: ["storage"]);

WebApplication app = builder.Build();

// -- Middleware pipeline -------------------------------------------------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var traceId = context.TraceIdentifier;
        var isDevelopment = app.Environment.IsDevelopment();
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "The Engine hit an unexpected error.",
            Detail = isDevelopment
                ? feature?.Error.Message
                : "The request failed. Check Engine logs with the trace id for details.",
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;

        app.Logger.LogError(feature?.Error, "Unhandled API exception for {Path} (trace {TraceId})",
            context.Request.Path, traceId);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});
app.UseCors("BlazorWasm");
// SECURITY: rate limiting must run before authentication. ApiKeyMiddleware performs
// a database lookup for every X-Api-Key header it sees, so an unauthenticated flood
// of requests must be throttled here first — otherwise the flood reaches the DB
// lookup on every single request before any limiter has a chance to reject it.
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tuvima Library API v1"));
}

// -- Endpoint registration -----------------------------------------------------
app.MapEngineEndpoints();
app.MapDevelopmentEngineEndpoints();

app.Run();
