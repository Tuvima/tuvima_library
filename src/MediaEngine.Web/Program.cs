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
using System.Security.Claims;
using MediaEngine.Contracts.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    ForwardedHeaderConfiguration.Configure(
        options,
        networkSettings.Remote,
        Environment.GetEnvironmentVariable("TUVIMA_TAILSCALE_URL"));
});

var authSettings = dashboardConfig.LoadCore().Auth;
var ssoEnabled =
    authSettings.Oidc.Enabled &&
    (string.Equals(authSettings.Mode, "Oidc", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(authSettings.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase));
builder.Services.AddSingleton(new DashboardAuthUiOptions(ssoEnabled));
builder.Services.AddScoped<DashboardCookieEvents>();
var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "Tuvima.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/auth/denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.EventsType = typeof(DashboardCookieEvents);
    });

if (ssoEnabled)
{
    authentication.AddOpenIdConnect(options =>
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

            options.Events.OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirstValue("sub")
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    context.Fail("The identity provider did not return a subject identifier.");
                    return;
                }

                var deviceId = context.Request.Cookies["Tuvima.Device"];
                if (!Guid.TryParse(deviceId, out _))
                {
                    deviceId = Guid.NewGuid().ToString("D");
                    context.Response.Cookies.Append("Tuvima.Device", deviceId, new CookieOptions
                    {
                        HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
                        Secure = context.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddYears(2),
                    });
                }

                var issued = await context.HttpContext.RequestServices
                    .GetRequiredService<DashboardIdentityClient>()
                    .CreateExternalSessionAsync(new ExternalSessionRequest
                    {
                        Provider = authSettings.Oidc.Authority.Trim(), Subject = subject,
                        DeviceId = deviceId!, DeviceName = context.Request.Headers.UserAgent.ToString(),
                        Client = "Tuvima Dashboard OIDC",
                    }, context.HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                if (issued is null)
                {
                    context.Fail("This external identity is not linked to a Tuvima profile.");
                    return;
                }

                context.Principal = DashboardPrincipalFactory.Create(issued);
                context.Properties ??= new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
                context.Properties.IsPersistent = true;
                context.Properties.ExpiresUtc = issued.ExpiresAt;
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAssertion(context =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                return true;
            }

            if (context.Resource is not HttpContext http)
            {
                return false;
            }

            var path = http.Request.Path;
            return path.StartsWithSegments("/setup")
                || path.StartsWithSegments("/_blazor")
                || path.StartsWithSegments("/_framework")
                || path.StartsWithSegments("/_content")
                || path.StartsWithSegments("/assets")
                || path.StartsWithSegments("/js")
                || path.StartsWithSegments("/app.css")
                || path.StartsWithSegments("/MediaEngine.Web.styles.css")
                || path.StartsWithSegments("/favicon");
        })
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

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
var configuredMediaGrantKey = builder.Configuration["View:MediaGrantKey"];
var mediaGrantKey = !string.IsNullOrWhiteSpace(configuredMediaGrantKey)
    ? SHA256.HashData(Encoding.UTF8.GetBytes(configuredMediaGrantKey))
    : RandomNumberGenerator.GetBytes(32);
var mediaGrantLifetime = TimeSpan.FromSeconds(Math.Clamp(
    builder.Configuration.GetValue<int?>("View:MediaGrantLifetimeSeconds") ?? 600,
    30,
    900));

// Shared setup for every HttpClient that talks to the Engine — BaseAddress plus the
// X-Api-Key header — so this configuration exists exactly once instead of once per client.
void ConfigureEngineClient(IServiceProvider services, HttpClient client)
{
    client.BaseAddress = new Uri(apiBase);
    client.DefaultRequestHeaders.Remove(DashboardEngineAuthenticationHandler.ServiceHeader);
    client.DefaultRequestHeaders.Add(
        DashboardEngineAuthenticationHandler.ServiceHeader,
        services.GetRequiredService<DashboardServiceCredentialProvider>().GetToken());
}

// The circuit-scoped client owns a circuit-scoped active-profile accessor. Its
// server-only handler signs eligible View/Collections requests after the final URI
// is known; the Engine API key is never serialized into browser state.
builder.Services.AddScoped<ActiveProfileAccessor>();
builder.Services.AddScoped<IActiveProfileAccessor>(services => services.GetRequiredService<ActiveProfileAccessor>());
builder.Services.AddSingleton(new DashboardServiceCredentialProviderOptions(configDir));
builder.Services.AddSingleton<DashboardServiceCredentialProvider>();
builder.Services.AddScoped<DashboardSessionAccessor>();
builder.Services.AddTransient<DashboardEngineAuthenticationHandler>();
builder.Services.AddScoped<DashboardIdentityClient>();
builder.Services.AddSingleton(new ViewMediaGrantService(mediaGrantKey, mediaGrantLifetime));
builder.Services.AddScoped<IEngineApiClient>(services =>
{
    var serviceToken = services.GetRequiredService<DashboardServiceCredentialProvider>().GetToken();
    var authenticationHandler = ActivatorUtilities.CreateInstance<DashboardEngineAuthenticationHandler>(services);
    authenticationHandler.InnerHandler = new HttpClientHandler();
    var assertionHandler = new ViewProfileAssertionHandler(
        services.GetRequiredService<IActiveProfileAccessor>(),
        serviceToken)
    {
        InnerHandler = authenticationHandler,
    };
    var client = new HttpClient(assertionHandler);
    ConfigureEngineClient(services, client);
    return ActivatorUtilities.CreateInstance<EngineApiClient>(services, client);
});
builder.Services.AddScoped<EngineApiFailureState>();
builder.Services.AddScoped<IViewMediaEngineClient>(services =>
{
    var serviceToken = services.GetRequiredService<DashboardServiceCredentialProvider>().GetToken();
    var authenticationHandler = ActivatorUtilities.CreateInstance<DashboardEngineAuthenticationHandler>(services);
    authenticationHandler.InnerHandler = new HttpClientHandler();
    var assertionHandler = new ViewProfileAssertionHandler(
        services.GetRequiredService<IActiveProfileAccessor>(),
        serviceToken)
    {
        InnerHandler = authenticationHandler,
    };
    var client = new HttpClient(assertionHandler);
    ConfigureEngineClient(services, client);
    return new ViewMediaEngineClient(client);
});
builder.Services.AddScoped<ICollectionPersonalMediaClient>(services =>
{
    var serviceToken = services.GetRequiredService<DashboardServiceCredentialProvider>().GetToken();
    var authenticationHandler = ActivatorUtilities.CreateInstance<DashboardEngineAuthenticationHandler>(services);
    authenticationHandler.InnerHandler = new HttpClientHandler();
    var assertionHandler = new ViewProfileAssertionHandler(
        services.GetRequiredService<IActiveProfileAccessor>(),
        serviceToken)
    {
        InnerHandler = authenticationHandler,
    };
    var client = new HttpClient(assertionHandler);
    ConfigureEngineClient(services, client);
    return ActivatorUtilities.CreateInstance<CollectionPersonalMediaClient>(services, client);
});

// Named "EngineApi" client — same base address and API key as the scoped client above.
// Used by ad-hoc pages (e.g. the Enrichment Tester) that need direct HttpClient access
// without routing through IEngineApiClient.
builder.Services.AddHttpClient("EngineApi", ConfigureEngineClient)
    .AddHttpMessageHandler<DashboardEngineAuthenticationHandler>();
// Artwork requests are already authorized at the same-origin Dashboard route.
// Forward only the server-held service credential so a shelf of images does not
// repeat user-session validation and a SQLite lookup for every image.
builder.Services.AddHttpClient("EngineArtwork", ConfigureEngineClient);
builder.Services.AddHttpClient("EngineIdentity", ConfigureEngineClient)
    .AddHttpMessageHandler<DashboardEngineAuthenticationHandler>();
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

// Forwarded scheme/client information must be established before HSTS,
// redirection, authentication, and URL generation. Only loopback plus the
// explicitly configured proxy addresses/networks are trusted.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.Use(async (context, next) =>
{
    if (!context.Request.IsHttps && !ForwardedHeaderConfiguration.IsLocalNetworkClient(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await context.Response.WriteAsync("Remote access requires a verified HTTPS or tunnel path.");
        return;
    }

    await next();
});
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
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

app.MapGet("/_tuvima/remote-probe", (HttpContext context, string? nonce) => Results.Ok(new
{
    product = "Tuvima Library",
    nonce = nonce ?? string.Empty,
    secure = context.Request.IsHttps,
})).AllowAnonymous();

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

app.MapMethods("/engine-image/{**enginePath}", [HttpMethods.Get, HttpMethods.Head], ProxyEngineImageAsync)
    .WithName("ProxyEngineImage")
    .WithSummary("Proxies authenticated Engine artwork through the Dashboard origin.")
    .RequireAuthorization();

app.MapViewMediaProxy();

app.MapDashboardAuthenticationEndpoints(ssoEnabled);

if (ssoEnabled)
{
    app.MapGet("/auth/oidc", (string? returnUrl) =>
        Results.Challenge(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl,
            },
            [OpenIdConnectDefaults.AuthenticationScheme]))
        .AllowAnonymous();

}

app.MapStaticAssets().AllowAnonymous();
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

static async Task ProxyEngineImageAsync(
    string enginePath,
    HttpContext ctx,
    IHttpClientFactory httpFactory,
    CancellationToken ct)
{
    var upstreamPath = $"/{enginePath.TrimStart('/')}";
    if (!EngineImageProxyPath.IsAllowedEnginePath(upstreamPath))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var client = httpFactory.CreateClient("EngineArtwork");
    using var request = new HttpRequestMessage(
        HttpMethods.IsHead(ctx.Request.Method) ? HttpMethod.Head : HttpMethod.Get,
        $"{upstreamPath}{ctx.Request.QueryString}");
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    ctx.Response.StatusCode = (int)response.StatusCode;
    CopyResponseHeaders(response, ctx.Response);

    if (!HttpMethods.IsHead(ctx.Request.Method))
    {
        await response.Content.CopyToAsync(ctx.Response.Body, ct);
    }
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
