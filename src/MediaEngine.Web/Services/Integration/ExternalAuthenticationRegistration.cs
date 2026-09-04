using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Domain.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace MediaEngine.Web.Services.Integration;

public sealed record RegisteredExternalAuthProvider(
    string Id,
    string DisplayName,
    string Kind,
    string AuthenticationScheme,
    string CallbackPath);

public static partial class ExternalAuthenticationRegistration
{
    public static IReadOnlyList<RegisteredExternalAuthProvider> AddTuvimaExternalProviders(
        this AuthenticationBuilder authentication,
        IReadOnlyList<ExternalAuthProviderSettings> providers)
    {
        var registrations = new List<RegisteredExternalAuthProvider>(providers.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            Validate(provider, ids);
            var scheme = $"Tuvima.External.{provider.Id}";
            var callbackPath = $"/signin-tuvima-{provider.Id}";

            if (provider.Kind.Equals(ExternalAuthProviderKinds.OpenIdConnect, StringComparison.OrdinalIgnoreCase))
            {
                AddOpenIdConnect(authentication, scheme, callbackPath, provider);
            }
            else
            {
                AddOAuth(authentication, scheme, callbackPath, provider);
            }

            registrations.Add(new RegisteredExternalAuthProvider(
                provider.Id,
                provider.DisplayName,
                provider.Kind,
                scheme,
                callbackPath));
        }

        return registrations;
    }

    private static void AddOpenIdConnect(
        AuthenticationBuilder authentication,
        string scheme,
        string callbackPath,
        ExternalAuthProviderSettings provider)
    {
        authentication.AddOpenIdConnect(scheme, provider.DisplayName, options =>
        {
            options.Authority = provider.Authority.TrimEnd('/');
            options.ClientId = provider.ClientId;
            if (!string.IsNullOrWhiteSpace(provider.ClientSecret))
            {
                options.ClientSecret = provider.ClientSecret;
            }

            options.CallbackPath = callbackPath;
            options.ResponseType = "code";
            options.UsePkce = provider.UsePkce;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.Scope.Clear();
            foreach (var scope in provider.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Distinct(StringComparer.Ordinal))
            {
                options.Scope.Add(scope);
            }

            options.Events.OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirstValue("sub")
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var issuer = context.SecurityToken?.Issuer
                    ?? context.Principal?.FindFirstValue("iss")
                    ?? provider.Issuer;
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer))
                {
                    context.Fail("The identity provider did not return an issuer and subject identifier.");
                    return;
                }

                var issued = await IssueTuvimaSessionAsync(
                    context.HttpContext,
                    provider,
                    issuer,
                    subject,
                    "OIDC").ConfigureAwait(false);
                if (issued is null)
                {
                    context.Fail("This external identity is not linked to a Tuvima account.");
                    return;
                }

                context.Principal = DashboardPrincipalFactory.Create(issued);
                context.Properties ??= new AuthenticationProperties();
                context.Properties.IsPersistent = true;
                context.Properties.ExpiresUtc = issued.ExpiresAt;
            };
        });
    }

    private static void AddOAuth(
        AuthenticationBuilder authentication,
        string scheme,
        string callbackPath,
        ExternalAuthProviderSettings provider)
    {
        authentication.AddOAuth(scheme, provider.DisplayName, options =>
        {
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
            options.CallbackPath = callbackPath;
            options.AuthorizationEndpoint = provider.AuthorizationEndpoint;
            options.TokenEndpoint = provider.TokenEndpoint;
            options.UserInformationEndpoint = provider.UserInformationEndpoint;
            options.UsePkce = provider.UsePkce;
            options.SaveTokens = false;
            options.Scope.Clear();
            foreach (var scope in provider.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Distinct(StringComparer.Ordinal))
            {
                options.Scope.Add(scope);
            }

            options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, provider.IdClaim);
            options.ClaimActions.MapJsonKey(ClaimTypes.Name, provider.NameClaim);
            options.ClaimActions.MapJsonKey(ClaimTypes.Email, provider.EmailClaim);
            options.Events.OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.UserAgent.ParseAdd("Tuvima-Library/1.0");
                using var response = await context.Backchannel.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    context.HttpContext.RequestAborted).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
                using var payload = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: context.HttpContext.RequestAborted).ConfigureAwait(false);
                context.RunClaimActions(payload.RootElement);

                var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    context.Fail("The identity provider did not return a stable user identifier.");
                    return;
                }

                var issued = await IssueTuvimaSessionAsync(
                    context.HttpContext,
                    provider,
                    provider.Issuer,
                    subject,
                    "OAuth").ConfigureAwait(false);
                if (issued is null)
                {
                    context.Fail("This external identity is not linked to a Tuvima account.");
                    return;
                }

                context.Principal = DashboardPrincipalFactory.Create(issued);
                context.Properties.IsPersistent = true;
                context.Properties.ExpiresUtc = issued.ExpiresAt;
            };
        });
    }

    private static async Task<AuthSessionResponse?> IssueTuvimaSessionAsync(
        HttpContext context,
        ExternalAuthProviderSettings provider,
        string issuer,
        string subject,
        string protocol)
    {
        var deviceId = context.Request.Cookies["Tuvima.Device"];
        if (!Guid.TryParse(deviceId, out _))
        {
            deviceId = Guid.NewGuid().ToString("D");
            context.Response.Cookies.Append("Tuvima.Device", deviceId, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddYears(2),
            });
        }

        return await context.RequestServices
            .GetRequiredService<DashboardIdentityClient>()
            .CreateExternalSessionAsync(new ExternalSessionRequest
            {
                Provider = provider.Id,
                Issuer = NormalizeIssuer(issuer),
                Subject = subject.Trim(),
                DeviceId = deviceId!,
                DeviceName = context.Request.Headers.UserAgent.ToString(),
                Client = $"Tuvima Dashboard {protocol}",
            }, context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static void Validate(ExternalAuthProviderSettings provider, HashSet<string> ids)
    {
        if (!ProviderIdRegex().IsMatch(provider.Id) || !ids.Add(provider.Id))
        {
            throw new InvalidOperationException($"External authentication provider id '{provider.Id}' is invalid or duplicated.");
        }

        if (string.IsNullOrWhiteSpace(provider.DisplayName) || string.IsNullOrWhiteSpace(provider.ClientId))
        {
            throw new InvalidOperationException($"External authentication provider '{provider.Id}' requires a display name and client ID.");
        }

        if (provider.Kind.Equals(ExternalAuthProviderKinds.OpenIdConnect, StringComparison.OrdinalIgnoreCase))
        {
            RequireHttps(provider.Authority, provider.Id, "authority");
            if (!provider.Scopes.Contains("openid", StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"OIDC provider '{provider.Id}' must request the openid scope.");
            }

            return;
        }

        if (!provider.Kind.Equals(ExternalAuthProviderKinds.OAuth, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"External authentication provider '{provider.Id}' has unsupported kind '{provider.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(provider.ClientSecret))
        {
            throw new InvalidOperationException(
                $"OAuth provider '{provider.Id}' requires client_secret in config/.secrets/auth-providers.json.");
        }

        RequireHttps(provider.Issuer, provider.Id, "issuer");
        RequireHttps(provider.AuthorizationEndpoint, provider.Id, "authorization_endpoint");
        RequireHttps(provider.TokenEndpoint, provider.Id, "token_endpoint");
        RequireHttps(provider.UserInformationEndpoint, provider.Id, "user_information_endpoint");
    }

    private static void RequireHttps(string value, string providerId, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"External authentication provider '{providerId}' requires an absolute HTTPS {field}.");
        }
    }

    // Preserve the exact issuer supplied by the validated token/provider. Issuer
    // comparison is security-sensitive and a trailing slash can be significant.
    private static string NormalizeIssuer(string issuer) => issuer.Trim();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderIdRegex();
}
