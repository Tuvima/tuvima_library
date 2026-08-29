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
            {
                return Results.Redirect(SafeReturnUrl(returnUrl));
            }

            var bootstrap = await identity.GetBootstrapStatusAsync(context.RequestAborted).ConfigureAwait(false);
            var tokens = antiforgery.GetAndStoreTokens(context);
            EnsureDeviceCookie(context);
            return Results.Content(
                LoginPage(
                    tokens.RequestToken ?? string.Empty,
                    bootstrap?.AdministratorConfigured == true,
                    oidcEnabled,
                    SafeReturnUrl(returnUrl),
                    IsLoopbackRequest(context)),
                "text/html",
                Encoding.UTF8);
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
                if (codes is null)
                {
                    return Results.Content(
                        LoginFailurePage("Recovery failed. Check the username, code, and new password."),
                        "text/html",
                        Encoding.UTF8,
                        StatusCodes.Status400BadRequest);
                }

                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
                return Results.Content(
                    RecoveryCodesPage(codes, "/auth/login", "Continue to sign in"),
                    "text/html",
                    Encoding.UTF8);
            }

            AuthSessionResponse? issued;
            if (action.Equals("bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsLoopbackRequest(context))
                {
                    return Results.Content(
                        LoginFailurePage(
                            "First-run administrator setup is available only from localhost on the computer running Tuvima Library."),
                        "text/html",
                        Encoding.UTF8,
                        StatusCodes.Status403Forbidden);
                }

                issued = await identity.BootstrapAsync(new BootstrapAdministratorRequest
                {
                    Username = form["username"].ToString(),
                    Password = form["password"].ToString(),
                    DisplayName = form["displayName"].ToString(),
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Client = "Tuvima Dashboard",
                }, context.RequestAborted).ConfigureAwait(false);
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
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Client = "Tuvima Dashboard",
                }, context.RequestAborted).ConfigureAwait(false);
            }

            if (issued is null)
            {
                return Results.Content(LoginFailurePage("Sign in failed. Check your credentials and try again."), "text/html", Encoding.UTF8, StatusCodes.Status401Unauthorized);
            }

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
            {
                return Results.Content(
                    RecoveryCodesPage(issued.RecoveryCodes, "/", "Continue to Tuvima Library"),
                    "text/html",
                    Encoding.UTF8);
            }

            return Results.Redirect(returnUrl);
        }).AllowAnonymous();

        app.MapPost("/auth/logout", async (HttpContext context, DashboardIdentityClient identity, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            if (Guid.TryParse(context.User.FindFirstValue("tuvima:session_id"), out var sessionId))
            {
                await identity.RevokeSessionAsync(sessionId, context.RequestAborted).ConfigureAwait(false);
            }

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
        {
            return existing;
        }

        var value = Guid.NewGuid().ToString("D");
        context.Response.Cookies.Append("Tuvima.Device", value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(2),
        });
        return value;
    }

    private static string SafeReturnUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
            ? value
            : "/";

    private static string SanitizeDeviceName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Browser" : value.Length <= 100 ? value : value[..100];

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }

    private static string LoginPage(
        string token,
        bool configured,
        bool oidcEnabled,
        string returnUrl,
        bool localBootstrap)
    {
        string form;
        if (configured)
        {
            form = $"""
              <p class="eyebrow">Tuvima Library</p>
              <h1>Sign in to Tuvima Library</h1>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="login"><input type="hidden" name="returnUrl" value="{H(returnUrl)}">
              <label>Username<input name="username" autocomplete="username" required autofocus></label>
              <label>Password<input type="password" name="password" autocomplete="current-password" required></label><button>Sign in</button></form>
              <details><summary>Forgot password?</summary>
              <p class="supporting">Use one of the one-time recovery codes saved when the administrator was created.</p>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="recover"><label>Username<input name="username" autocomplete="username" required></label><label>Recovery code<input name="recoveryCode" autocomplete="off" spellcheck="false" required></label><label>New password<input type="password" name="newPassword" minlength="8" autocomplete="new-password" required><small>Use at least 8 characters.</small></label><button>Reset with recovery code</button></form>
              <p class="supporting">No recovery code? Run <code>tuvima-admin auth reset-password</code> with administrator privileges on the computer running Tuvima Library.</p>
              </details>
              <details><summary>Sign in with a profile PIN</summary><form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="login"><input type="hidden" name="returnUrl" value="{H(returnUrl)}"><label>Profile ID<input name="profileId" required></label><label>PIN<input type="password" inputmode="numeric" name="pin" required></label><button>Unlock profile</button></form></details>
              {(oidcEnabled ? "<p><a class=\"button\" href=\"/auth/oidc?returnUrl=" + Uri.EscapeDataString(returnUrl) + "\">Continue with OpenID Connect</a></p>" : string.Empty)}
              """;
        }
        else if (!localBootstrap)
        {
            form = """
              <p class="eyebrow">First-run setup</p>
              <h1>Finish setup on the Tuvima computer</h1>
              <p class="lede">For security, the first administrator can be created only from <strong>localhost</strong> on the computer running Tuvima Library.</p>
              <div class="notice">Open <code>http://localhost:5016</code> on that computer to continue.</div>
              """;
        }
        else
        {
            form = $"""
              <p class="eyebrow">First-run setup</p>
              <h1>Create your administrator</h1>
              <p class="lede">Create a local Tuvima user for this library. The account is stored and used only by this Tuvima installation.</p>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="bootstrap"><input type="hidden" name="returnUrl" value="/">
              <label>Display name<input name="displayName" value="Administrator" autocomplete="name" required autofocus></label>
              <label>Username<input name="username" autocomplete="username" spellcheck="false" required><small>Used only to sign in to this Tuvima Library.</small></label>
              <label>Password<input type="password" name="password" minlength="8" autocomplete="new-password" required><small>Use at least 8 characters.</small></label>
              <button>Create administrator</button></form>
              """;
        }

        return Shell(form);
    }

    private static string SecurityPage(IReadOnlyList<DeviceSessionResponse> sessions, string? currentId, string token)
    {
        var rows = string.Join("", sessions.Select(session => $"<tr><td>{H(session.DeviceName)}</td><td>{H(session.AuthenticationMethod)}</td><td>{session.LastSeenAt:g}</td><td>{session.ExpiresAt:g}</td><td>{(session.Id.ToString("D").Equals(currentId, StringComparison.OrdinalIgnoreCase) ? "Current" : $"<form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"revoke\"><input type=\"hidden\" name=\"sessionId\" value=\"{session.Id:D}\"><button>Revoke</button></form>")}</td></tr>"));
        return Shell($"<h1>Account security</h1><p><a href=\"/\">Back to library</a></p><h2>Your sessions</h2><table><thead><tr><th>Device</th><th>Method</th><th>Last used</th><th>Expires</th><th></th></tr></thead><tbody>{rows}</tbody></table><h2>Change password</h2><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"password\"><label>Current password<input type=\"password\" name=\"currentPassword\" required></label><label>New password<input type=\"password\" name=\"newPassword\" minlength=\"8\" required><small>Use at least 8 characters.</small></label><button>Change password</button></form>");
    }

    private static string RecoveryCodesPage(
        IReadOnlyList<string> codes,
        string continueHref,
        string continueLabel) =>
        Shell($"<h1>Save your recovery codes</h1><p>Each code works once. Store them somewhere safe before continuing.</p><pre>{H(string.Join(Environment.NewLine, codes))}</pre><p><a class=\"button\" href=\"{H(continueHref)}\">{H(continueLabel)}</a></p>");
    private static string LoginFailurePage(string message) => Shell($"<h1>Unable to continue</h1><p>{H(message)}</p><p><a href=\"/auth/login\">Return to sign in</a></p>");
    private static string Shell(string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width">
          <title>Tuvima Library</title>
          <style>
            :root { color-scheme: dark; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 1.25rem; background: radial-gradient(circle at 15% 5%, #281849 0, #100b1c 38%, #08060d 75%); color: #f8f6ff; }
            main { width: min(34rem, 100%); padding: clamp(1.5rem, 5vw, 2.5rem); background: rgba(22, 16, 33, .96); border: 1px solid #58427d; border-radius: 1.25rem; box-shadow: 0 1.5rem 5rem rgba(0, 0, 0, .55); }
            h1, h2, p { margin-top: 0; }
            h1 { margin-bottom: .75rem; color: #ffffff; font-size: clamp(1.8rem, 5vw, 2.35rem); line-height: 1.1; letter-spacing: -.025em; }
            h2 { margin-top: 2rem; color: #ffffff; }
            p, td, th, summary { color: #d8d0e8; }
            .eyebrow { margin-bottom: .65rem; color: #ad8cff; font-size: .75rem; font-weight: 800; letter-spacing: .14em; text-transform: uppercase; }
            .lede { margin-bottom: 1.6rem; color: #c9bfd9; font-size: 1rem; line-height: 1.6; }
            .supporting { margin-top: .8rem; color: #c9bfd9; font-size: .9rem; line-height: 1.5; }
            form { display: grid; gap: 1rem; margin: 1.25rem 0; }
            label { display: grid; gap: .45rem; color: #f4efff; font-size: .9rem; font-weight: 700; }
            small { color: #aa9fbc; font-size: .78rem; font-weight: 500; }
            input, button, .button { width: 100%; min-height: 3rem; border-radius: .7rem; font: inherit; }
            input { padding: .78rem .9rem; border: 1px solid #65557d; outline: none; background: #0e0a16; color: #ffffff; caret-color: #b596ff; }
            input:hover { border-color: #8970ad; }
            input:focus { border-color: #a982ff; box-shadow: 0 0 0 .2rem rgba(124, 77, 255, .24); }
            button, .button { display: inline-grid; place-items: center; padding: .8rem 1rem; border: 1px solid #9d7bff; background: linear-gradient(135deg, #7040ef, #8c5cff); color: #ffffff; font-weight: 800; text-decoration: none; cursor: pointer; }
            button:hover, .button:hover { background: linear-gradient(135deg, #8051f5, #9c70ff); }
            button:focus-visible, .button:focus-visible, summary:focus-visible, a:focus-visible { outline: .2rem solid rgba(181, 150, 255, .75); outline-offset: .15rem; }
            a { color: #c6aaff; }
            details { margin: 1.25rem 0; }
            summary { cursor: pointer; }
            table { width: 100%; border-collapse: collapse; }
            td, th { padding: .65rem; border-bottom: 1px solid #392b4c; text-align: left; }
            pre, .notice { padding: 1rem; border: 1px solid #44345c; border-radius: .7rem; background: #0d0914; color: #ece5f7; }
            pre { white-space: pre-wrap; }
            code { color: #d2bcff; font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
            @media (max-width: 35rem) { body { align-items: start; padding: .75rem; } main { margin-top: .75rem; padding: 1.35rem; border-radius: 1rem; } }
          </style>
        </head>
        <body><main>{{body}}</main></body>
        </html>
        """;
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
