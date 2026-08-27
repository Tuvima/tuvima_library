using System.Security.Claims;
using MediaEngine.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MediaEngine.Web.Services.Integration;

public sealed class DashboardCookieEvents(DashboardIdentityClient identity) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var token = context.Principal?.FindFirstValue(DashboardEngineAuthenticationHandler.SessionTokenClaim);
        if (string.IsNullOrWhiteSpace(token))
        {
            context.RejectPrincipal();
            return;
        }

        var validated = await identity.ValidateAsync(token, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (validated is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return;
        }

        var currentRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
        var currentActive = context.Principal?.FindFirstValue("tuvima:active_profile_id");
        if (!string.Equals(currentRole, validated.Role, StringComparison.Ordinal)
            || !string.Equals(currentActive, validated.ActiveProfileId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            context.ReplacePrincipal(DashboardPrincipalFactory.Create(validated, token));
            context.ShouldRenew = true;
        }
    }
}

public static class DashboardPrincipalFactory
{
    public static ClaimsPrincipal Create(AuthSessionResponse response) =>
        CreateCore(response.SessionId, response.ProfileId, response.ActiveProfileId, response.DisplayName,
            response.Role, response.AuthenticationMethod, response.SessionToken);

    public static ClaimsPrincipal Create(SessionValidationResponse response, string token) =>
        CreateCore(response.SessionId, response.ProfileId, response.ActiveProfileId, response.DisplayName,
            response.Role, response.AuthenticationMethod, token);

    private static ClaimsPrincipal CreateCore(Guid sessionId, Guid profileId, Guid activeProfileId, string name, string role, string method, string token)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, profileId.ToString("D")),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role),
            new Claim("tuvima:profile_id", profileId.ToString("D")),
            new Claim("tuvima:active_profile_id", activeProfileId.ToString("D")),
            new Claim("tuvima:session_id", sessionId.ToString("D")),
            new Claim("tuvima:authentication_method", method),
            new Claim(DashboardEngineAuthenticationHandler.SessionTokenClaim, token),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
