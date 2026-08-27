using MudBlazor.Services;
using MediaEngine.Web.Components;
using MediaEngine.Web.Services.Branding;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Theming;
using MediaEngine.Web.Services.Narration;
using MediaEngine.Web.Services.MediaTiles;
using MediaEngine.Web.Services.Playback;
using MediaEngine.Web.Services.Navigation;
using MediaEngine.Web.Services.Configuration;
using MediaEngine.Web.Services.Integration.Clients;
using MediaEngine.Domain.Models;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
SettingsNav.ConfigureEnvironment(builder.Environment.IsProduction());

// ── Windows Service hosting ────────────────────────────────────────────────────
// Integrates with the Windows Service Control Manager when the Dashboard is
// installed as a Windows service via the .exe installer.  No-op on Linux / Docker.
builder.Host.UseWindowsService(options => options.ServiceName = "Tuvima Library Dashboard");

// ── Blazor ────────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Localization ──────────────────────────────────────────────────────────────
// Supported cultures match the curated list in ServerGeneralTab.
var supportedCultures = new[]
{
    "ar", "zh", "zh-TW", "cs", "da", "nl", "en", "fi", "fr", "de",
    "el", "he", "hi", "hu", "id", "it", "ja", "ko", "ms", "no",
    "pl", "pt", "pt-BR", "ro", "ru", "es", "sv", "th", "tr", "uk", "vi",
}.Select(c => new CultureInfo(c)).ToList();

builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    opts.DefaultRequestCulture = new RequestCulture("en");
    opts.SupportedCultures     = supportedCultures;
    opts.SupportedUICultures   = supportedCultures;
    // Cookie provider first so the Dashboard language selection takes effect.
    opts.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider());
});

// ── MudBlazor ─────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();
builder.Services.AddMemoryCache();

// ── Theming ───────────────────────────────────────────────────────────────────
// Singleton: dark-mode-only theme shared across all connections.
builder.Services.AddSingleton<ThemeService>();

// ── Colour Palette ────────────────────────────────────────────────────────────
// Resolve Dashboard config the same way the Engine does so UI palette tokens
// stay aligned with config/ui/palette.json in local, service, and Docker runs.
string configDir = Environment.GetEnvironmentVariable("TUVIMA_CONFIG_DIR")
                ?? builder.Configuration["MediaEngine:ConfigDirectory"]
                ?? "config";
string dataProtectionDir = Environment.GetEnvironmentVariable("TUVIMA_DATA_PROTECTION_DIR")
                        ?? Path.Combine(configDir, ".keys");
Directory.CreateDirectory(dataProtectionDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDir))
    .SetApplicationName("Tuvima.Library");
var dashboardConfig = new DashboardConfigurationReader(configDir);
builder.Services.AddSingleton(dashboardConfig);
var networkSettings = dashboardConfig.LoadNetwork();

// Installed/service deployments use the user-facing network port from the shared
// configuration. Explicit host configuration (launchSettings, ASPNETCORE_URLS,
// container settings) remains authoritative for development and orchestration.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{networkSettings.Local.Port}");
}

if (networkSettings.Remote.TrustedProxies.Count > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        foreach (var address in networkSettings.Remote.TrustedProxies)
        {
            if (IPAddress.TryParse(address, out var proxy))
                options.KnownProxies.Add(proxy);
        }
    });
}

var authSettings = dashboardConfig.LoadCore().Auth;
var ssoEnabled =
    authSettings.Oidc.Enabled &&
    (string.Equals(authSettings.Mode, "Oidc", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(authSettings.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase));
builder.Services.AddSingleton(new DashboardAuthUiOptions(ssoEnabled));

if (ssoEnabled)
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "Tuvima.Auth";
            options.SlidingExpiration = true;
        })
        .AddOpenIdConnect(options =>
        {
            options.Authority = authSettings.Oidc.Authority;
            options.ClientId = authSettings.Oidc.ClientId;
            if (!string.IsNullOrWhiteSpace(authSettings.Oidc.ClientSecret))
                options.ClientSecret = authSettings.Oidc.ClientSecret;

            options.ResponseType = "code";
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false;

            options.Scope.Clear();
            foreach (var scope in authSettings.Oidc.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
                options.Scope.Add(scope);
        });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
}

PaletteProvider.Initialize(dashboardConfig.LoadPalette());
builder.Services.AddHostedService<DashboardPaletteReloadService>();

// ── Narration ─────────────────────────────────────────────────────────────────
// Singleton: config-driven phrase templates for hero subtitles and section headings.
builder.Services.AddSingleton<IPhraseTemplateService, PhraseTemplateService>();
builder.Services.AddSingleton<StreamingServiceLogoResolver>();

// ── Engine API HTTP Client ────────────────────────────────────────────────────
// TUVIMA_ENGINE_URL: override the Engine address — essential for Docker where
// the Dashboard and Engine run as separate processes or in separate containers.
// Example: "http://engine:61495" (service name in docker-compose) or
//          "http://192.168.1.50:61495" (fixed LAN IP for Unraid).
var apiBase = Environment.GetEnvironmentVariable("TUVIMA_ENGINE_URL")
           ?? builder.Configuration["Engine:BaseUrl"]
           ?? "http://localhost:61495";
var apiKey  = builder.Configuration["Engine:ApiKey"]  ?? string.Empty;
var configuredMediaGrantKey = builder.Configuration["View:MediaGrantKey"];
var mediaGrantKey = !string.IsNullOrWhiteSpace(configuredMediaGrantKey)
    ? SHA256.HashData(Encoding.UTF8.GetBytes(configuredMediaGrantKey))
    : !string.IsNullOrWhiteSpace(apiKey)
        ? SHA256.HashData(Encoding.UTF8.GetBytes($"tuvima:view-media-grant:v1\n{apiKey}"))
        : RandomNumberGenerator.GetBytes(32);
var mediaGrantLifetime = TimeSpan.FromSeconds(Math.Clamp(
    builder.Configuration.GetValue<int?>("View:MediaGrantLifetimeSeconds") ?? 600,
    30,
    900));

// Shared setup for every HttpClient that talks to the Engine — BaseAddress plus the
// X-Api-Key header — so this configuration exists exactly once instead of once per client.
void ConfigureEngineClient(HttpClient client)
{
    client.BaseAddress = new Uri(apiBase);
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
}

// The circuit-scoped client owns a circuit-scoped active-profile accessor. Its
// server-only handler signs eligible View/Collections requests after the final URI
// is known; the Engine API key is never serialized into browser state.
builder.Services.AddScoped<ActiveProfileAccessor>();
builder.Services.AddScoped<IActiveProfileAccessor>(services => services.GetRequiredService<ActiveProfileAccessor>());
builder.Services.AddSingleton(new ViewMediaGrantService(mediaGrantKey, mediaGrantLifetime));
builder.Services.AddScoped<IEngineApiClient>(services =>
{
    var assertionHandler = new ViewProfileAssertionHandler(
        services.GetRequiredService<IActiveProfileAccessor>(),
        apiKey)
    {
        InnerHandler = new HttpClientHandler(),
    };
    var client = new HttpClient(assertionHandler);
    ConfigureEngineClient(client);
    return ActivatorUtilities.CreateInstance<EngineApiClient>(services, client);
});
builder.Services.AddScoped<EngineApiFailureState>();
builder.Services.AddScoped<IViewMediaEngineClient>(services =>
{
    var assertionHandler = new ViewProfileAssertionHandler(
        services.GetRequiredService<IActiveProfileAccessor>(),
        apiKey)
    {
        InnerHandler = new HttpClientHandler(),
    };
    var client = new HttpClient(assertionHandler);
    ConfigureEngineClient(client);
    return new ViewMediaEngineClient(client);
});
builder.Services.AddScoped<ICollectionPersonalMediaClient>(services =>
{
    var assertionHandler = new ViewProfileAssertionHandler(
        services.GetRequiredService<IActiveProfileAccessor>(),
        apiKey)
    {
        InnerHandler = new HttpClientHandler(),
    };
    var client = new HttpClient(assertionHandler);
    ConfigureEngineClient(client);
    return ActivatorUtilities.CreateInstance<CollectionPersonalMediaClient>(services, client);
});

// Named "EngineApi" client — same base address and API key as the scoped client above.
// Used by ad-hoc pages (e.g. the Enrichment Tester) that need direct HttpClient access
// without routing through IEngineApiClient.
builder.Services.AddHttpClient("EngineApi", ConfigureEngineClient);
builder.Services.AddHealthChecks()
    .AddCheck<DashboardEngineHealthCheck>("engine_liveness", tags: ["readiness"]);

// ── State + Orchestration (scoped = one per SignalR circuit) ──────────────────
builder.Services.AddScoped<UniverseStateContainer>();
builder.Services.AddScoped<ActiveProfileSessionService>();
builder.Services.AddScoped<ViewWorkspaceService>();
builder.Services.AddScoped<ViewAssetDragService>();
builder.Services.AddScoped<UIOrchestratorService>();
builder.Services.AddScoped<IngestionLiveDashboardState>();
builder.Services.AddScoped<MediaEditorLauncherService>();
builder.Services.AddScoped<CollectionEditorLauncherService>();
builder.Services.AddScoped<MediaTileComposerService>();
builder.Services.AddScoped<WatchlistService>();
builder.Services.AddScoped<FavoriteService>();
builder.Services.AddScoped<MediaReactionService>();
builder.Services.AddSingleton(dashboardConfig.LoadPlaybackClientSettings());
builder.Services.AddScoped<PlaybackSessionController>();
builder.Services.AddScoped<ShellActivityState>();
builder.Services.AddScoped<ListenAudioDragService>();
builder.Services.AddScoped<ListenPageState>();
builder.Services.AddScoped<IUserPlaybackPreferencesAccessor, UserPlaybackPreferencesAccessor>();

// Provider Catalogue (scoped = one API client per SignalR circuit).
// Caches provider UI metadata from GET /providers/catalogue in IMemoryCache while
// keeping the circuit-scoped Engine API failure state out of the root provider.
builder.Services.AddScoped<ProviderCatalogueService>();
builder.Services.AddScoped<MetadataSettingsStateService>();

// ── Device Context (scoped = per-tab; a TV in television mode won't affect a mobile session) ──
// Generalised device-context model supporting web, mobile, television, and automotive classes.
builder.Services.AddScoped<DeviceContextService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (networkSettings.Remote.TrustedProxies.Count > 0)
    app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRequestLocalization();
if (ssoEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("readiness"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(report.Status.ToString().ToLowerInvariant());
    },
}).AllowAnonymous();

// ── Culture cookie setter ─────────────────────────────────────────────────────
// Sets the ASP.NET Core culture cookie and redirects back to the requested page.
// Called by ServerGeneralTab via forceLoad navigation after saving a new language.
app.MapGet("/culture/set", (string culture, string redirectUri, HttpContext ctx) =>
{
    if (supportedCultures.Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
    }
    return Results.Redirect(redirectUri);
}).AllowAnonymous();

app.MapMethods("/engine-stream/{assetId:guid}", [HttpMethods.Get, HttpMethods.Head], ProxyEngineStreamAsync)
    .WithName("ProxyEngineMediaStream")
    .WithSummary("Proxies Engine media bytes through the Dashboard origin for browser media playback.");

app.MapViewMediaProxy();

if (ssoEnabled)
{
    app.MapGet("/auth/login", (string? returnUrl) =>
        Results.Challenge(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl,
            },
            [OpenIdConnectDefaults.AuthenticationScheme]))
        .AllowAnonymous();

    app.MapPost("/auth/logout", () =>
        Results.SignOut(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task ProxyEngineStreamAsync(
    Guid assetId,
    HttpContext ctx,
    IHttpClientFactory httpFactory,
    CancellationToken ct)
{
    var client = httpFactory.CreateClient("EngineApi");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/stream/{assetId:D}");
    CopyRequestHeader(ctx, request, "Range");
    CopyRequestHeader(ctx, request, "If-Range");

    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    ctx.Response.StatusCode = (int)response.StatusCode;
    CopyResponseHeaders(response, ctx.Response);

    if (HttpMethods.IsHead(ctx.Request.Method))
    {
        return;
    }

    await response.Content.CopyToAsync(ctx.Response.Body, ct);
}

static void CopyRequestHeader(HttpContext ctx, HttpRequestMessage request, string headerName)
{
    if (ctx.Request.Headers.TryGetValue(headerName, out var values))
    {
        request.Headers.TryAddWithoutValidation(headerName, values.ToArray());
    }
}

static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
{
    foreach (var header in source.Headers)
    {
        target.Headers[header.Key] = header.Value.ToArray();
    }

    foreach (var header in source.Content.Headers)
    {
        target.Headers[header.Key] = header.Value.ToArray();
    }

    target.Headers.Remove("transfer-encoding");
}

sealed class DashboardEngineHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("EngineApi");
            using var response = await client.GetAsync("/health/live", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("The Dashboard can reach the Engine liveness endpoint.")
                : HealthCheckResult.Unhealthy($"The Engine liveness endpoint returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The Dashboard cannot reach the Engine liveness endpoint.", ex);
        }
    }
}
