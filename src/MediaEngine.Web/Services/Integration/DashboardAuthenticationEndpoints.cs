using System.Net;
using System.Security.Claims;
using System.Text;
using MediaEngine.Contracts.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MediaEngine.Web.Services.Integration;

public static class DashboardAuthenticationEndpoints
{
    public static WebApplication MapDashboardAuthenticationEndpoints(this WebApplication app, bool oidcEnabled)
    {
        app.MapGet("/auth/login", async (HttpContext context, DashboardIdentityClient identity, IAntiforgery antiforgery, string? returnUrl) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
                return Results.Redirect(SafeReturnUrl(returnUrl));

            var bootstrap = await identity.GetBootstrapStatusAsync(context.RequestAborted).ConfigureAwait(false);
            var tokens = antiforgery.GetAndStoreTokens(context);
            EnsureDeviceCookie(context);
            return Results.Content(LoginPage(tokens.RequestToken ?? string.Empty, bootstrap?.AdministratorConfigured == true, oidcEnabled, SafeReturnUrl(returnUrl)), "text/html", Encoding.UTF8);
        }).AllowAnonymous();

        app.MapPost("/auth/login", async (HttpContext context, DashboardIdentityClient identity, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["action"].ToString();
            var deviceId = EnsureDeviceCookie(context);
            var deviceName = SanitizeDeviceName(context.Request.Headers.UserAgent.ToString());
            var returnUrl = SafeReturnUrl(form["returnUrl"].ToString());

            if (action.Equals("recover", StringComparison.OrdinalIgnoreCase))
            {
                var codes = await identity.RecoverAsync(new RecoverPasswordRequest
                {
                    Username = form["username"].ToString(),
                    RecoveryCode = form["recoveryCode"].ToString(),
                    NewPassword = form["newPassword"].ToString(),
                }, context.RequestAborted).ConfigureAwait(false);
                return codes is null
                    ? Results.Content(LoginFailurePage("Recovery failed. Check the username, code, and new password."), "text/html", Encoding.UTF8, StatusCodes.Status400BadRequest)
                    : Results.Content(RecoveryCodesPage(codes), "text/html", Encoding.UTF8);
            }

            AuthSessionResponse? issued;
            if (action.Equals("bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                issued = await identity.BootstrapAsync(new BootstrapAdministratorRequest
                {
                    Username = form["username"].ToString(), Password = form["password"].ToString(),
                    DisplayName = form["displayName"].ToString(), DeviceId = deviceId,
                    DeviceName = deviceName, Client = "Tuvima Dashboard",
                }, form["setupCode"].ToString(), context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                Guid? profileId = Guid.TryParse(form["profileId"].ToString(), out var parsed) ? parsed : null;
                issued = await identity.LoginAsync(new LocalLoginRequest
                {
                    Username = profileId is null ? form["username"].ToString() : null,
                    Password = profileId is null ? form["password"].ToString() : null,
                    ProfileId = profileId,
                    Pin = profileId is null ? null : form["pin"].ToString(),
                    DeviceId = deviceId, DeviceName = deviceName, Client = "Tuvima Dashboard",
                }, context.RequestAborted).ConfigureAwait(false);
            }

            if (issued is null)
                return Results.Content(LoginFailurePage("Sign in failed. Check your credentials and try again."), "text/html", Encoding.UTF8, StatusCodes.Status401Unauthorized);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                DashboardPrincipalFactory.Create(issued),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true,
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = issued.ExpiresAt,
                    RedirectUri = returnUrl,
                }).ConfigureAwait(false);

            if (issued.RecoveryCodes.Count > 0)
                return Results.Content(RecoveryCodesPage(issued.RecoveryCodes), "text/html", Encoding.UTF8);
            return Results.Redirect(returnUrl);
        }).AllowAnonymous();

        app.MapPost("/auth/logout", async (HttpContext context, DashboardIdentityClient identity, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            if (Guid.TryParse(context.User.FindFirstValue("tuvima:session_id"), out var sessionId))
                await identity.RevokeSessionAsync(sessionId, context.RequestAborted).ConfigureAwait(false);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return Results.Redirect("/auth/login");
        });

        app.MapGet("/account/security", async (HttpContext context, DashboardIdentityClient identity, IAntiforgery antiforgery) =>
        {
            var sessions = await identity.GetSessionsAsync(context.RequestAborted).ConfigureAwait(false);
            var token = antiforgery.GetAndStoreTokens(context).RequestToken ?? string.Empty;
            var current = context.User.FindFirstValue("tuvima:session_id");
            return Results.Content(SecurityPage(sessions, current, token), "text/html", Encoding.UTF8);
        });

        app.MapPost("/account/security", async (HttpContext context, DashboardIdentityClient identity, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["action"].ToString();
            var success = action switch
            {
                "revoke" when Guid.TryParse(form["sessionId"].ToString(), out var id) => await identity.RevokeSessionAsync(id, context.RequestAborted).ConfigureAwait(false),
                "password" => await identity.ChangePasswordAsync(new ChangePasswordRequest
                {
                    CurrentPassword = form["currentPassword"].ToString(),
                    NewPassword = form["newPassword"].ToString(),
                }, context.RequestAborted).ConfigureAwait(false),
                _ => false,
            };
            if (action == "password" && success)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
                return Results.Redirect("/auth/login");
            }
            return Results.Redirect("/account/security");
        });

        return app;
    }

    private static string EnsureDeviceCookie(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue("Tuvima.Device", out var existing) && Guid.TryParse(existing, out _))
            return existing;
        var value = Guid.NewGuid().ToString("D");
        context.Response.Cookies.Append("Tuvima.Device", value, new CookieOptions
        {
            HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddYears(2),
        });
        return value;
    }

    private static string SafeReturnUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
            ? value
            : "/";

    private static string SanitizeDeviceName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Browser" : value.Length <= 100 ? value : value[..100];

    private static string LoginPage(string token, bool configured, bool oidcEnabled, string returnUrl)
    {
        var form = configured
            ? $"""
              <h1>Sign in to Tuvima Library</h1>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="login"><input type="hidden" name="returnUrl" value="{H(returnUrl)}">
              <label>Username<input name="username" autocomplete="username" required autofocus></label>
              <label>Password<input type="password" name="password" autocomplete="current-password" required></label><button>Sign in</button></form>
              <details><summary>Sign in with a profile PIN</summary><form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="login"><input type="hidden" name="returnUrl" value="{H(returnUrl)}"><label>Profile ID<input name="profileId" required></label><label>PIN<input type="password" inputmode="numeric" name="pin" required></label><button>Unlock profile</button></form></details>
              {(oidcEnabled ? "<p><a class=\"button\" href=\"/auth/oidc?returnUrl=" + Uri.EscapeDataString(returnUrl) + "\">Continue with OpenID Connect</a></p>" : string.Empty)}
              <details><summary>Recover password</summary><form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="recover"><label>Username<input name="username" required></label><label>Recovery code<input name="recoveryCode" required></label><label>New password<input type="password" name="newPassword" minlength="12" required></label><button>Reset password</button></form></details>
              """
            : $"""
              <h1>Claim this Tuvima Library</h1><p>Enter the one-time claim code shown in the Engine log, then create the administrator password.</p>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="bootstrap"><input type="hidden" name="returnUrl" value="/">
              <label>Claim code<input name="setupCode" autocomplete="one-time-code" required autofocus></label><label>Display name<input name="displayName" value="Owner" required></label><label>Username<input name="username" autocomplete="username" required></label><label>Password<input type="password" name="password" minlength="12" autocomplete="new-password" required></label><button>Create administrator</button></form>
              """;
        return Shell(form);
    }

    private static string SecurityPage(IReadOnlyList<DeviceSessionResponse> sessions, string? currentId, string token)
    {
        var rows = string.Join("", sessions.Select(session => $"<tr><td>{H(session.DeviceName)}</td><td>{H(session.AuthenticationMethod)}</td><td>{session.LastSeenAt:g}</td><td>{session.ExpiresAt:g}</td><td>{(session.Id.ToString("D").Equals(currentId, StringComparison.OrdinalIgnoreCase) ? "Current" : $"<form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"revoke\"><input type=\"hidden\" name=\"sessionId\" value=\"{session.Id:D}\"><button>Revoke</button></form>")}</td></tr>"));
        return Shell($"<h1>Account security</h1><p><a href=\"/\">Back to library</a></p><h2>Your sessions</h2><table><thead><tr><th>Device</th><th>Method</th><th>Last used</th><th>Expires</th><th></th></tr></thead><tbody>{rows}</tbody></table><h2>Change password</h2><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"password\"><label>Current password<input type=\"password\" name=\"currentPassword\" required></label><label>New password<input type=\"password\" name=\"newPassword\" minlength=\"12\" required></label><button>Change password</button></form>");
    }

    private static string RecoveryCodesPage(IReadOnlyList<string> codes) =>
        Shell($"<h1>Save your recovery codes</h1><p>Each code works once. Store them somewhere safe before continuing.</p><pre>{H(string.Join(Environment.NewLine, codes))}</pre><p><a class=\"button\" href=\"/\">Continue to Tuvima Library</a></p>");
    private static string LoginFailurePage(string message) => Shell($"<h1>Unable to continue</h1><p>{H(message)}</p><p><a href=\"/auth/login\">Return to sign in</a></p>");
    private static string Shell(string body) => $$"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Tuvima Library</title><style>color-scheme:dark;body{font-family:system-ui;background:#09070f;color:#f5f2ff;margin:0;min-height:100vh;display:grid;place-items:center}main{width:min(42rem,calc(100% - 2rem));background:#151020;padding:2rem;border:1px solid #49386a;border-radius:1rem;box-shadow:0 1rem 4rem #0008}form{display:grid;gap:1rem;margin:1rem 0}label{display:grid;gap:.35rem}input,button,.button{font:inherit;padding:.8rem;border-radius:.55rem;border:1px solid #655080}button,.button{background:#7c4dff;color:white;text-decoration:none;cursor:pointer;text-align:center}details{margin:1.25rem 0}table{width:100%;border-collapse:collapse}td,th{padding:.6rem;border-bottom:1px solid #332744;text-align:left}pre{white-space:pre-wrap;background:#09070f;padding:1rem;border-radius:.5rem}</style></head><body><main>{{body}}</main></body></html>""";
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
