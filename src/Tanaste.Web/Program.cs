using MudBlazor.Services;
using Tanaste.Web.Components;
using Tanaste.Web.Services.Integration;
using Tanaste.Web.Services.Theming;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor ────────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── MudBlazor ─────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();

// ── Theming ───────────────────────────────────────────────────────────────────
// Singleton: one theme instance shared across all connections; toggle is per-connection
// (components hold their own _isDark flag synced via ThemeService.OnThemeChanged).
builder.Services.AddSingleton<ThemeService>();

// ── Tanaste API HTTP Client ───────────────────────────────────────────────────
var apiBase = builder.Configuration["TanasteApi:BaseUrl"] ?? "http://localhost:61495";
var apiKey  = builder.Configuration["TanasteApi:ApiKey"]  ?? string.Empty;

// AddHttpClient<IClient, TClient> wires the interface directly to the typed-client
// factory so the HttpClient it receives has the correct BaseAddress and default headers.
// A separate AddScoped<IClient, TClient> would resolve HttpClient via the default
// (unconfigured, no BaseAddress) registration, causing every Engine call to fail silently.
builder.Services.AddHttpClient<ITanasteApiClient, TanasteApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBase);
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// ── State + Orchestration (scoped = one per SignalR circuit) ──────────────────
builder.Services.AddScoped<UniverseStateContainer>();
builder.Services.AddScoped<UIOrchestratorService>();

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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
