using System.Net.Http.Json;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Api.Services.Networking;

public sealed class RemoteAccessReadinessService
{
    private readonly IRemoteAuthenticationReadiness _authentication;
    private readonly INetworkTopologyService _topology;
    private readonly IReadOnlyDictionary<string, IRemoteConnectivityProvider> _providers;
    private readonly HttpClient _http;
    private readonly ILogger<RemoteAccessReadinessService> _logger;

    public RemoteAccessReadinessService(
        IRemoteAuthenticationReadiness authentication,
        INetworkTopologyService topology,
        IEnumerable<IRemoteConnectivityProvider> providers,
        HttpClient http,
        ILogger<RemoteAccessReadinessService> logger)
    {
        _authentication = authentication;
        _topology = topology;
        _providers = providers.ToDictionary(provider => provider.Key, StringComparer.OrdinalIgnoreCase);
        _http = http;
        _logger = logger;
    }

    public async Task<RemoteAccessReadinessDto> EvaluateAsync(RemoteNetworkSettings settings, CancellationToken ct)
    {
        var checks = new List<NetworkTestCheckDto>();
        var authentication = await _authentication.GetAsync(ct).ConfigureAwait(false);
        var administratorConfigured = authentication.AdministratorConfigured;
        checks.Add(Check(
            "authentication",
            "Tuvima sign-in",
            administratorConfigured,
            administratorConfigured
                ? "An administrator account is configured and Dashboard authentication is required."
                : "Complete first-run administrator setup from the Dashboard on the Tuvima host before enabling remote access."));

        var bypassDisabled = authentication.LocalhostBypassDisabled;
        checks.Add(Check(
            "authentication-bypass",
            "Authentication bypass",
            bypassDisabled,
            bypassDisabled
                ? "The localhost authentication bypass is disabled."
                : "Disable the localhost authentication bypass before enabling remote access."));

        switch (settings.ConnectionMode)
        {
            case NetworkConnectionModes.Tailscale:
                checks.Add(await CheckTailscaleAsync(ct).ConfigureAwait(false));
                break;
            case NetworkConnectionModes.Custom:
                checks.Add(await CheckHttpsEndpointAsync(settings.PublicHostname, ct).ConfigureAwait(false));
                break;
            case NetworkConnectionModes.DirectOnly:
                var topology = _topology.GetSnapshot();
                checks.Add(Check(
                    "router-topology",
                    "Router topology",
                    topology.SupportsRouterDiscovery,
                    topology.Detail));
                checks.Add(Check(
                    "tls-terminator",
                    "Local TLS terminator",
                    settings.TlsTerminationPort is not null,
                    settings.TlsTerminationPort is null
                        ? "Configure the local HTTPS reverse-proxy port. Tuvima will never map the Dashboard HTTP listener directly."
                        : $"Router mapping targets the HTTPS reverse proxy on port {settings.TlsTerminationPort}."));
                checks.Add(await CheckHttpsEndpointAsync(settings.PublicHostname, ct).ConfigureAwait(false));
                break;
            default:
                checks.Add(Check(
                    "secure-path",
                    "Secure remote path",
                    false,
                    "Choose Tailscale or a verified HTTPS reverse proxy before enabling remote access."));
                break;
        }

        return new RemoteAccessReadinessDto
        {
            Ready = checks.All(check => check.Status == "passed"),
            Checks = checks,
        };
    }

    private async Task<NetworkTestCheckDto> CheckTailscaleAsync(CancellationToken ct)
    {
        if (!_providers.TryGetValue("tailscale", out var provider))
            return Check("tailscale", "Tailscale Serve", false, "The Tailscale deployment provider is unavailable.");

        var state = await provider.TestAsync(ct).ConfigureAwait(false);
        return Check(
            "tailscale",
            "Tailscale Serve",
            state.State == RemoteProviderState.Connected && state.SecureHttps,
            state.Message);
    }

    private async Task<NetworkTestCheckDto> CheckHttpsEndpointAsync(string? value, CancellationToken ct)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps)
            return Check("https-endpoint", "HTTPS reverse proxy", false, "Configure an absolute HTTPS address.");

        var nonce = Guid.NewGuid().ToString("N");
        var probeUri = new Uri(origin, $"/_tuvima/remote-probe?nonce={nonce}");
        try
        {
            using var response = await _http.GetAsync(probeUri, ct).ConfigureAwait(false);
            var body = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<RemoteProbeResponse>(cancellationToken: ct).ConfigureAwait(false)
                : null;
            var verified = body is { Product: "Tuvima Library", Secure: true } && body.Nonce == nonce;
            return Check(
                "https-endpoint",
                "HTTPS reverse proxy",
                verified,
                verified
                    ? $"Verified Tuvima through {origin.GetLeftPart(UriPartial.Authority)} with trusted HTTPS."
                    : "The HTTPS address responded, but it did not prove that it securely reaches this Tuvima Dashboard.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _logger.LogInformation(ex, "Remote HTTPS readiness probe failed for {Host}", origin.Host);
            return Check(
                "https-endpoint",
                "HTTPS reverse proxy",
                false,
                "The configured HTTPS address could not be verified. Check DNS, the certificate, and reverse-proxy routing.");
        }
    }

    private static NetworkTestCheckDto Check(string key, string label, bool passed, string detail) => new()
    {
        Key = key,
        Label = label,
        Status = passed ? "passed" : "failed",
        Detail = detail,
    };

    private sealed class RemoteProbeResponse
    {
        public string Product { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public bool Secure { get; set; }
    }
}

public sealed class RemoteAuthenticationReadiness(
    IFirstPartyIdentityService identity,
    MediaEngine.Domain.Contracts.IConfigurationLoader configuration) : IRemoteAuthenticationReadiness
{
    public async Task<RemoteAuthenticationSnapshot> GetAsync(CancellationToken ct) => new(
        await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false),
        !configuration.LoadCore().Auth.LocalhostBypass);
}
