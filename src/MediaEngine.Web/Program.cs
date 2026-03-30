using MudBlazor.Services;
using MediaEngine.Web.Components;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Theming;
using MediaEngine.Web.Services.Narration;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

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

// ── Theming ───────────────────────────────────────────────────────────────────
// Singleton: dark-mode-only theme shared across all connections.
builder.Services.AddSingleton<ThemeService>();

// ── Narration ─────────────────────────────────────────────────────────────────
// Singleton: config-driven phrase templates for hero subtitles and section headings.
builder.Services.AddSingleton<IPhraseTemplateService, PhraseTemplateService>();

// ── Engine API HTTP Client ────────────────────────────────────────────────────
// TUVIMA_ENGINE_URL: override the Engine address — essential for Docker where
// the Dashboard and Engine run as separate processes or in separate containers.
// Example: "http://engine:61495" (service name in docker-compose) or
//          "http://192.168.1.50:61495" (fixed LAN IP for Unraid).
var apiBase = Environment.GetEnvironmentVariable("TUVIMA_ENGINE_URL")
           ?? builder.Configuration["Engine:BaseUrl"]
           ?? "http://localhost:61495";
var apiKey  = builder.Configuration["Engine:ApiKey"]  ?? string.Empty;

// AddHttpClient<IClient, TClient> wires the interface directly to the typed-client
// factory so the HttpClient it receives has the correct BaseAddress and default headers.
// A separate AddScoped<IClient, TClient> would resolve HttpClient via the default
// (unconfigured, no BaseAddress) registration, causing every Engine call to fail silently.
builder.Services.AddHttpClient<IEngineApiClient, EngineApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBase);
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// Named "EngineApi" client — same base address and API key as the typed client above.
// Used by ad-hoc pages (e.g. the Enrichment Tester) that need direct HttpClient access
// without routing through IEngineApiClient.
builder.Services.AddHttpClient("EngineApi", client =>
{
    client.BaseAddress = new Uri(apiBase);
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// ── State + Orchestration (scoped = one per SignalR circuit) ──────────────────
builder.Services.AddScoped<UniverseStateContainer>();
builder.Services.AddScoped<UIOrchestratorService>();

// ── Provider Catalogue (singleton = loaded once, shared across all circuits) ──
// Caches provider UI metadata from GET /providers/catalogue. Replaces hardcoded
// provider accent colours, icons, and chip labels spread across Dashboard files.
builder.Services.AddSingleton<ProviderCatalogueService>();

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
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseAntiforgery();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
