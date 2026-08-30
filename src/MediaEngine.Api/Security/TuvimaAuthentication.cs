using System.Security.Claims;
using System.Text.Encodings.Web;
using MediaEngine.Api.Services;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.Security;

public static class TuvimaAuthDefaults
{
    public const string Scheme = "Tuvima";
    public const string ServiceHeader = "X-Tuvima-Service-Key";
    public const string SessionHeader = "X-Tuvima-Session";
}

public static class TuvimaClaimTypes
{
    public const string ProfileId = "tuvima:profile_id";
    public const string ActiveProfileId = "tuvima:active_profile_id";
    public const string SessionId = "tuvima:session_id";
    public const string AuthenticationMethod = "tuvima:authentication_method";
    public const string DashboardService = "tuvima:dashboard_service";
    public const string DeviceId = "tuvima:device_id";
    public const string DeviceClass = "tuvima:device_class";
    public const string ClientId = "tuvima:client_id";
    public const string ClientVersion = "tuvima:client_version";
    public const string TokenId = "tuvima:token_id";
    public const string Scope = "tuvima:scope";
}

public static class AuthPolicies
{
    public const string Authenticated = "authenticated_user";
    public const string Administrator = "administrator";
    public const string StandardOrAdministrator = "standard_or_administrator";
    public const string DashboardService = "dashboard_service";
    public const string DashboardInteractive = "dashboard_interactive";
    public const string IntercomConnect = "intercom_connect";
}

public sealed class TuvimaAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IFirstPartyIdentityService identity,
    ClientAuthorizationService clientAuthorization,
    IApiKeyLookupCache apiKeys,
    IConfigurationLoader configurationLoader,
    IWebHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.ToString() is { } authorization
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var rawToken = authorization["Bearer ".Length..].Trim();
            var client = await clientAuthorization.ValidateAccessTokenAsync(rawToken, Context.RequestAborted).ConfigureAwait(false);
            if (client is null)
                return AuthenticateResult.Fail("Invalid, expired, or revoked bearer token.");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, client.Device.ProfileId.ToString("D")),
                new(ClaimTypes.Name, client.Device.DeviceName),
                new(ClaimTypes.Role, client.Role),
                new(TuvimaClaimTypes.ProfileId, client.Device.ProfileId.ToString("D")),
                new(TuvimaClaimTypes.ActiveProfileId, client.Device.ProfileId.ToString("D")),
                new(TuvimaClaimTypes.DeviceId, client.Device.Id.ToString("D")),
                new(TuvimaClaimTypes.DeviceClass, client.Device.DeviceClass),
                new(TuvimaClaimTypes.ClientId, client.Device.ClientId),
                new(TuvimaClaimTypes.ClientVersion, client.Device.ClientVersion),
                new(TuvimaClaimTypes.TokenId, client.Token.Id.ToString("D")),
                new(TuvimaClaimTypes.AuthenticationMethod, "device_bearer"),
            };
            claims.AddRange(ClientAuthorizationService.SplitScopes(client.Token.Scopes)
                .Select(scope => new Claim(TuvimaClaimTypes.Scope, scope)));
            return Success(claims);
        }

        if (Request.Headers.TryGetValue(TuvimaAuthDefaults.ServiceHeader, out var serviceValues))
        {
            var serviceToken = serviceValues.ToString();
            if (!await identity.ValidateServiceCredentialAsync(serviceToken, Context.RequestAborted).ConfigureAwait(false))
                return AuthenticateResult.Fail("Invalid Dashboard service credential.");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "service:dashboard"),
                new(ClaimTypes.Name, "Tuvima Dashboard"),
                new(TuvimaClaimTypes.DashboardService, "true"),
            };

            if (Request.Headers.TryGetValue(TuvimaAuthDefaults.SessionHeader, out var sessionValues)
                && !string.IsNullOrWhiteSpace(sessionValues.ToString()))
            {
                var session = await identity.ValidateSessionAsync(sessionValues.ToString(), true, Context.RequestAborted).ConfigureAwait(false);
                if (session is null) return AuthenticateResult.Fail("Invalid or revoked user session.");
                AddSessionClaims(claims, session);
            }

            return Success(claims);
        }

        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKeyValues))
        {
            var raw = apiKeyValues.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return AuthenticateResult.Fail("API key is empty.");
            var match = await apiKeys.FindByHashedKeyAsync(ApiKeyService.HashKey(raw), Context.RequestAborted).ConfigureAwait(false);
            if (match is null) return AuthenticateResult.Fail("Invalid API key.");
            return Success([
                new Claim(ClaimTypes.NameIdentifier, $"api-key:{match.Id:D}"),
                new Claim(ClaimTypes.Name, match.Label),
                new Claim(ClaimTypes.Role, match.Role),
            ]);
        }

        var auth = configurationLoader.LoadCore().Auth;
        if (environment.IsDevelopment() && auth.LocalhostBypass && IsLoopback(Context.Connection.RemoteIpAddress))
        {
            return Success([
                new Claim(ClaimTypes.NameIdentifier, "development:localhost"),
                new Claim(ClaimTypes.Name, "Development localhost"),
                new Claim(ClaimTypes.Role, AppRoles.Administrator),
            ]);
        }

        return AuthenticateResult.NoResult();
    }

    private AuthenticateResult Success(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private static void AddSessionClaims(List<Claim> claims, SessionValidationResult result)
    {
        claims.Add(new Claim(ClaimTypes.NameIdentifier, result.Profile.Id.ToString("D")));
        claims.Add(new Claim(ClaimTypes.Name, result.ActiveProfile.DisplayName));
        claims.Add(new Claim(ClaimTypes.Role, result.ActiveProfile.Role.ToString()));
        claims.Add(new Claim(TuvimaClaimTypes.ProfileId, result.Profile.Id.ToString("D")));
        claims.Add(new Claim(TuvimaClaimTypes.ActiveProfileId, result.ActiveProfile.Id.ToString("D")));
        claims.Add(new Claim(TuvimaClaimTypes.SessionId, result.Session.Id.ToString("D")));
        claims.Add(new Claim(TuvimaClaimTypes.DeviceId, result.Session.DeviceId));
        claims.Add(new Claim(TuvimaClaimTypes.DeviceClass, "web"));
        claims.Add(new Claim(TuvimaClaimTypes.ClientId, result.Session.Client));
        claims.Add(new Claim(TuvimaClaimTypes.AuthenticationMethod, result.Session.AuthenticationMethod));
    }

    private static bool IsLoopback(System.Net.IPAddress? address) =>
        address is not null && System.Net.IPAddress.IsLoopback(address);
}
