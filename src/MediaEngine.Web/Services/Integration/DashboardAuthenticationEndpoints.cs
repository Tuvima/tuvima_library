using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MediaEngine.Contracts.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MediaEngine.Web.Services.Integration;

public static class DashboardAuthenticationEndpoints
{
    public static WebApplication MapDashboardAuthenticationEndpoints(
        this WebApplication app,
        IReadOnlyList<RegisteredExternalAuthProvider> externalProviders)
    {
        app.MapGet("/auth/login", async (HttpContext context, DashboardIdentityClient identity, PasswordResetEmailSender emailSender, IAntiforgery antiforgery, string? returnUrl) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                return Results.Redirect(SafeReturnUrl(returnUrl));
            }

            var bootstrap = await identity.GetBootstrapStatusAsync(context.RequestAborted).ConfigureAwait(false);
            if (bootstrap?.AdministratorConfigured != true)
            {
                return Results.Redirect("/setup");
            }

            var tokens = antiforgery.GetAndStoreTokens(context);
            var deviceId=EnsureDeviceCookie(context);
            return Results.Content(
                LoginPage(
                    tokens.RequestToken ?? string.Empty,
                    externalProviders,
                    emailSender.IsConfigured,
                    deviceId,
                    SafeReturnUrl(returnUrl)),
                "text/html",
                Encoding.UTF8);
        }).AllowAnonymous();

        app.MapPost("/auth/login", async (HttpContext context, DashboardIdentityClient identity, PasswordResetEmailSender emailSender, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["action"].ToString();
            var deviceId = EnsureDeviceCookie(context);
            var deviceName = SanitizeDeviceName(context.Request.Headers.UserAgent.ToString());
            var returnUrl = SafeReturnUrl(form["returnUrl"].ToString());

            if (action.Equals("email-reset", StringComparison.OrdinalIgnoreCase))
            {
                var email=form["email"].ToString();
                var resetToken=await identity.BeginPasswordResetAsync(email,context.RequestAborted).ConfigureAwait(false);
                if(resetToken is not null) await emailSender.SendAsync(email,resetToken,context.RequestAborted).ConfigureAwait(false);
                return Results.Content(Shell("<h1>Check your email</h1><p>If that address belongs to an eligible account, a password reset link has been sent. The link expires in 30 minutes.</p><p><a href=\"/auth/login\">Return to sign in</a></p>"),"text/html",Encoding.UTF8);
            }

            if (action.Equals("recover", StringComparison.OrdinalIgnoreCase))
            {
                var codes = await identity.RecoverAsync(new RecoverPasswordRequest
                {
                    Email = form["email"].ToString(),
                    RecoveryCode = form["recoveryCode"].ToString(),
                    NewPassword = form["newPassword"].ToString(),
                }, context.RequestAborted).ConfigureAwait(false);
                if (codes is null)
                {
                    return Results.Content(
                        LoginFailurePage("Recovery failed. Check the email, code, and new password."),
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

            if (action.Equals("bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Redirect("/setup");
            }

            Guid? profileId = Guid.TryParse(form["profileId"].ToString(), out var parsed) ? parsed : null;
            var issued = await identity.LoginAsync(new LocalLoginRequest
            {
                Email = profileId is null ? form["email"].ToString() : null,
                Password = profileId is null ? form["password"].ToString() : null,
                ProfileId = profileId,
                Pin = profileId is null ? null : form["pin"].ToString(),
                DeviceId = deviceId,
                DeviceName = deviceName,
                Client = "Tuvima Dashboard",
            }, context.RequestAborted).ConfigureAwait(false);

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

        app.MapGet("/auth/reset", (HttpContext context,IAntiforgery antiforgery,string? token) =>
        {
            if(string.IsNullOrWhiteSpace(token))return Results.Redirect("/auth/login");
            var anti=antiforgery.GetAndStoreTokens(context).RequestToken??string.Empty;
            return Results.Content(Shell($"<h1>Choose a new password</h1><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(anti)}\"><input type=\"hidden\" name=\"token\" value=\"{H(token)}\"><label>New password<input type=\"password\" name=\"newPassword\" minlength=\"8\" autocomplete=\"new-password\" required></label><button>Reset password</button></form>"),"text/html",Encoding.UTF8);
        }).AllowAnonymous();

        app.MapPost("/auth/reset", async (HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);var form=await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var ok=await identity.CompletePasswordResetAsync(form["token"].ToString(),form["newPassword"].ToString(),context.RequestAborted).ConfigureAwait(false);
            return Results.Content(ok?Shell("<h1>Password changed</h1><p><a class=\"button\" href=\"/auth/login\">Sign in</a></p>"):LoginFailurePage("That reset link is invalid or expired."),"text/html",Encoding.UTF8,ok?StatusCodes.Status200OK:StatusCodes.Status400BadRequest);
        }).AllowAnonymous();

        app.MapGet("/auth/invite",(HttpContext context,IAntiforgery antiforgery,string? token)=>
        {
            if(string.IsNullOrWhiteSpace(token))return Results.Redirect("/auth/login");var anti=antiforgery.GetAndStoreTokens(context).RequestToken??string.Empty;var device=EnsureDeviceCookie(context);
            return Results.Content(Shell($"<p class=\"eyebrow\">Tuvima Library invitation</p><h1>Create your sign-in</h1><p class=\"supporting\">This invitation grants access only to the profiles chosen by the server administrator.</p><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(anti)}\"><input type=\"hidden\" name=\"token\" value=\"{H(token)}\"><input type=\"hidden\" name=\"deviceId\" value=\"{H(device)}\"><label>Password<input type=\"password\" name=\"password\" minlength=\"8\" autocomplete=\"new-password\" required></label><button>Accept invitation</button></form>"),"text/html",Encoding.UTF8);
        }).AllowAnonymous();
        app.MapPost("/auth/invite",async(HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery)=>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);var form=await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);var issued=await identity.AcceptInvitationAsync(new AcceptAccountInvitationRequest(form["token"].ToString(),form["password"].ToString(),form["deviceId"].ToString(),SanitizeDeviceName(context.Request.Headers.UserAgent.ToString())),context.RequestAborted).ConfigureAwait(false);
            if(issued is null)return Results.Content(LoginFailurePage("That invitation is invalid, expired, or already used."),"text/html",Encoding.UTF8,StatusCodes.Status400BadRequest);await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,DashboardPrincipalFactory.Create(issued),new AuthenticationProperties{IsPersistent=true,ExpiresUtc=issued.ExpiresAt}).ConfigureAwait(false);return Results.Redirect("/");
        }).AllowAnonymous();

        app.MapPost("/auth/passkeys/login/options",async(BeginPasskeyLoginRequest request,DashboardIdentityClient identity,CancellationToken ct)=>
            await identity.GetPasskeyLoginOptionsAsync(request.Email,ct).ConfigureAwait(false) is {} result?Results.Ok(result):Results.BadRequest()).AllowAnonymous();

        app.MapPost("/auth/passkeys/login/complete",async(CompletePasskeyLoginRequest request,HttpContext context,DashboardIdentityClient identity,CancellationToken ct)=>
        {
            var issued=await identity.CompletePasskeyLoginAsync(request,ct).ConfigureAwait(false);if(issued is null)return Results.Unauthorized();
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,DashboardPrincipalFactory.Create(issued),new AuthenticationProperties{IsPersistent=true,AllowRefresh=true,IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=issued.ExpiresAt}).ConfigureAwait(false);
            return Results.Ok(issued);
        }).AllowAnonymous();

        app.MapPost("/auth/passkeys/registration/options",async(HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery,CancellationToken ct)=>
        {await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);return await identity.GetPasskeyRegistrationOptionsAsync(ct).ConfigureAwait(false) is {} result?Results.Ok(result):Results.BadRequest();});

        app.MapPost("/auth/passkeys/registration/complete",async(CompletePasskeyRegistrationRequest request,HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery,CancellationToken ct)=>
        {await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);return await identity.CompletePasskeyRegistrationAsync(request,ct).ConfigureAwait(false)?Results.NoContent():Results.BadRequest();});

        app.MapPost("/auth/passkeys/elevation/options",async(HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery,CancellationToken ct)=>
        {await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);return await identity.GetPasskeyElevationOptionsAsync(ct).ConfigureAwait(false)is{} result?Results.Ok(result):Results.BadRequest();});
        app.MapPost("/auth/passkeys/elevation/complete",async(CompletePasskeyElevationRequest request,HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery,CancellationToken ct)=>
        {await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);return await identity.CompletePasskeyElevationAsync(request,ct).ConfigureAwait(false)is{} result?Results.Ok(result):Results.Unauthorized();});

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

        app.MapGet("/account/elevate",(HttpContext context,IAntiforgery antiforgery,string? returnUrl)=>
        {
            var token=antiforgery.GetAndStoreTokens(context).RequestToken??string.Empty;var target=SafeReturnUrl(returnUrl);
            var script=$"<script>document.getElementById('passkey-elevate').addEventListener('click',async()=>{{const message=document.getElementById('elevation-message');try{{if(!window.PublicKeyCredential||!PublicKeyCredential.parseRequestOptionsFromJSON)throw new Error('This browser does not support passkeys.');const headers={{'Content-Type':'application/json','RequestVerificationToken':{JsonSerializer.Serialize(token)}}};const start=await fetch('/auth/passkeys/elevation/options',{{method:'POST',headers,body:'{{}}'}});if(!start.ok)throw new Error('No passkey is available.');const data=await start.json();const credential=await navigator.credentials.get({{publicKey:PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(data.options_json))}});const finish=await fetch('/auth/passkeys/elevation/complete',{{method:'POST',headers,body:JSON.stringify({{credential_json:JSON.stringify(credential.toJSON()),state:data.state}})}});if(!finish.ok)throw new Error('Passkey verification failed.');location.href={JsonSerializer.Serialize(target)};}}catch(error){{message.textContent=error.message;}}}});</script>";
            return Results.Content(Shell($"<p class=\"eyebrow\">Administrator access</p><h1>Confirm it’s you</h1><p class=\"supporting\">Enter your administrator PIN or account password. Elevated access lasts 30 minutes and is cleared when you switch profiles.</p><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"returnUrl\" value=\"{H(target)}\"><label>PIN or password<input type=\"password\" name=\"secret\" autocomplete=\"current-password\" required autofocus></label><button>Unlock administrator access</button></form><button id=\"passkey-elevate\" type=\"button\">Use passkey or Windows Hello</button><p id=\"elevation-message\" class=\"supporting\"></p>{script}"),"text/html",Encoding.UTF8);
        });

        app.MapPost("/account/elevate",async(HttpContext context,DashboardIdentityClient identity,IAntiforgery antiforgery)=>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);var form=await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);var target=SafeReturnUrl(form["returnUrl"].ToString());
            var elevated=await identity.ElevateAsync(form["secret"].ToString(),context.RequestAborted).ConfigureAwait(false);
            return elevated?.Elevated==true?Results.Redirect(target):Results.Content(LoginFailurePage("The administrator PIN or password was incorrect."),"text/html",Encoding.UTF8,StatusCodes.Status401Unauthorized);
        });

        app.MapGet("/account/security", async (HttpContext context, DashboardIdentityClient identity, PasswordResetEmailSender emailSender, IAntiforgery antiforgery, string? emailTest) =>
        {
            var sessions = await identity.GetSessionsAsync(context.RequestAborted).ConfigureAwait(false);
            var account = await identity.GetAccountAsync(context.RequestAborted).ConfigureAwait(false);
            var linkedLogins = await identity.GetExternalLoginsAsync(context.RequestAborted).ConfigureAwait(false);
            var passkeys = await identity.GetPasskeysAsync(context.RequestAborted).ConfigureAwait(false);
            var token = antiforgery.GetAndStoreTokens(context).RequestToken ?? string.Empty;
            var current = context.User.FindFirstValue("tuvima:session_id");
            return Results.Content(SecurityPage(account, sessions, linkedLogins, passkeys, externalProviders, current, token, context.User.IsInRole(MediaEngine.Domain.AppRoles.Administrator), emailSender.IsConfigured, emailTest), "text/html", Encoding.UTF8);
        });

        app.MapPost("/account/security", async (HttpContext context, DashboardIdentityClient identity, PasswordResetEmailSender emailSender, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["action"].ToString();
            if (action == "test-email")
            {
                var account = await identity.GetAccountAsync(context.RequestAborted).ConfigureAwait(false);
                var sent = account?.Email is { Length: > 0 } email
                    && await emailSender.SendTestAsync(email, context.RequestAborted).ConfigureAwait(false);
                return Results.Redirect($"/account/security?emailTest={(sent ? "sent" : "failed")}");
            }
            if (action == "recovery-codes")
            {
                var codes = await identity.RegenerateRecoveryCodesAsync(form["currentPassword"].ToString(), context.RequestAborted).ConfigureAwait(false);
                return codes is null
                    ? Results.Content(LoginFailurePage("The current password was incorrect."), "text/html", Encoding.UTF8, StatusCodes.Status401Unauthorized)
                    : Results.Content(RecoveryCodesPage(codes, "/account/security", "Return to Account Security"), "text/html", Encoding.UTF8);
            }
            if (action == "administrator-pin")
            {
                var elevation = await identity.GetElevationAsync(context.RequestAborted).ConfigureAwait(false);
                if (elevation?.Elevated != true)
                {
                    return Results.Redirect("/account/elevate?returnUrl=/account/security");
                }
            }
            var success = action switch
            {
                "revoke" when Guid.TryParse(form["sessionId"].ToString(), out var id) => await identity.RevokeSessionAsync(id, context.RequestAborted).ConfigureAwait(false),
                "unlink-external" when Guid.TryParse(form["loginId"].ToString(),out var loginId)=>await identity.UnlinkExternalLoginAsync(loginId,context.RequestAborted).ConfigureAwait(false),
                "remove-passkey" => await identity.RemovePasskeyAsync(form["credentialId"].ToString(), context.RequestAborted).ConfigureAwait(false),
                "password" => await identity.ChangePasswordAsync(new ChangePasswordRequest
                {
                    CurrentPassword = form["currentPassword"].ToString(),
                    NewPassword = form["newPassword"].ToString(),
                }, context.RequestAborted).ConfigureAwait(false),
                "administrator-pin" when Guid.TryParse(context.User.FindFirstValue("tuvima:active_profile_id"),out var activeProfileId)=>await identity.SetAdministratorPinAsync(activeProfileId,form["pin"].ToString(),context.RequestAborted).ConfigureAwait(false),
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

    private static string LoginPage(
        string token,
        IReadOnlyList<RegisteredExternalAuthProvider> externalProviders,
        bool emailResetEnabled,
        string deviceId,
        string returnUrl)
    {
        var externalButtons = string.Join(
            string.Empty,
            externalProviders.Select(provider =>
                $"<p><a class=\"button\" href=\"/auth/external/{Uri.EscapeDataString(provider.Id)}?returnUrl={Uri.EscapeDataString(returnUrl)}\">Continue with {H(provider.DisplayName)}</a></p>"));
        var emailReset = emailResetEnabled ? $"<form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"email-reset\"><label>Email<input type=\"email\" name=\"email\" autocomplete=\"username\" required></label><button>Email reset link</button></form>" : "<p class=\"supporting\">Email delivery is not configured. Use a saved recovery code, or ask the server administrator to run the host recovery command.</p>";
        var passkeyScript = $$$"""
              <script>
              document.getElementById('passkey-login').addEventListener('click',async()=>{const message=document.getElementById('passkey-message');try{if(!window.PublicKeyCredential||!PublicKeyCredential.parseRequestOptionsFromJSON)throw new Error('This browser does not support passkeys.');const start=await fetch('/auth/passkeys/login/options',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:document.getElementById('signin-email').value||null})});if(!start.ok)throw new Error('Passkey sign-in is unavailable.');const data=await start.json();const credential=await navigator.credentials.get({publicKey:PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(data.options_json))});const finish=await fetch('/auth/passkeys/login/complete',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({credential_json:JSON.stringify(credential.toJSON()),state:data.state,device_id:{{{JsonSerializer.Serialize(deviceId)}}},device_name:navigator.userAgent})});if(!finish.ok)throw new Error('Passkey sign-in failed.');location.href={{{JsonSerializer.Serialize(returnUrl)}}};}catch(error){message.textContent=error.message;}});
              </script>
              """;
        var form = $"""
              <p class="eyebrow">Tuvima Library</p>
              <h1>Sign in to Tuvima Library</h1>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="login"><input type="hidden" name="returnUrl" value="{H(returnUrl)}">
              <label>Email<input id="signin-email" type="email" name="email" autocomplete="username" required autofocus></label>
              <label>Password<input type="password" name="password" autocomplete="current-password" required></label><button>Sign in</button></form>
              <button type="button" id="passkey-login">Sign in with a passkey</button><p id="passkey-message" class="supporting"></p>
              <details><summary>Forgot password?</summary>
              {emailReset}
              <p class="supporting">Use one of the one-time recovery codes saved when the account was created.</p>
              <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="recover"><label>Email<input type="email" name="email" autocomplete="username" required></label><label>Recovery code<input name="recoveryCode" autocomplete="off" spellcheck="false" required></label><label>New password<input type="password" name="newPassword" minlength="8" autocomplete="new-password" required><small>Use at least 8 characters.</small></label><button>Reset with recovery code</button></form>
              <p class="supporting">No recovery code? Run <code>tuvima-admin auth reset-password</code> with administrator privileges on the computer running Tuvima Library.</p>
              </details>
              <details><summary>Sign in with a profile PIN</summary><form method="post"><input type="hidden" name="__RequestVerificationToken" value="{H(token)}"><input type="hidden" name="action" value="login"><input type="hidden" name="returnUrl" value="{H(returnUrl)}"><label>Profile ID<input name="profileId" required></label><label>PIN<input type="password" inputmode="numeric" name="pin" required></label><button>Unlock profile</button></form></details>
              {externalButtons}
              {passkeyScript}
              """;

        return Shell(form);
    }

    private static string SecurityPage(AccountResponse? account,IReadOnlyList<DeviceSessionResponse> sessions,IReadOnlyList<AccountExternalLoginDto> linkedLogins,IReadOnlyList<PasskeyCredentialResponse> passkeys,IReadOnlyList<RegisteredExternalAuthProvider> externalProviders,string? currentId, string token,bool isAdministrator,bool emailConfigured,string? emailTest)
    {
        var rows = string.Join("", sessions.Select(session => $"<tr><td>{H(session.DeviceName)}</td><td>{H(session.AuthenticationMethod)}</td><td>{session.LastSeenAt:g}</td><td>{session.ExpiresAt:g}</td><td>{(session.Id.ToString("D").Equals(currentId, StringComparison.OrdinalIgnoreCase) ? "Current" : $"<form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"revoke\"><input type=\"hidden\" name=\"sessionId\" value=\"{session.Id:D}\"><button>Revoke</button></form>")}</td></tr>"));
        var links=string.Join("",linkedLogins.Select(login=>$"<li>{H(login.Provider)} · {H(login.Email??login.DisplayName??"Linked identity")} <form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"unlink-external\"><input type=\"hidden\" name=\"loginId\" value=\"{login.Id:D}\"><button>Unlink</button></form></li>"));
        var providerLinks=string.Join("",externalProviders.Select(provider=>$"<p><a class=\"button\" href=\"/auth/external/{Uri.EscapeDataString(provider.Id)}?returnUrl=/account/security\">Link {H(provider.DisplayName)}</a></p>"));
        var passkeyRows=string.Join("",passkeys.Select(passkey=>$"<li>{H(passkey.Name)} · added {passkey.CreatedAt:g} <form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"remove-passkey\"><input type=\"hidden\" name=\"credentialId\" value=\"{H(passkey.CredentialId)}\"><button>Remove</button></form></li>"));
        var administratorPin=isAdministrator?$"<h2>Administrator PIN</h2><p class=\"supporting\">Use a separate 4–12 digit PIN for 30-minute administrator elevation.</p><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"administrator-pin\"><label>New administrator PIN<input type=\"password\" inputmode=\"numeric\" pattern=\"[0-9]{{4,12}}\" name=\"pin\" required></label><button>Set administrator PIN</button></form>":string.Empty;
        var emailTestStatus=emailTest switch{"sent"=>"<p class=\"success\">Test email sent.</p>","failed"=>"<p class=\"error\">The test email could not be sent. Check the server logs and SMTP settings.</p>",_=>string.Empty};
        var emailTestForm=emailConfigured&&account?.Email is not null?$"<h2>Email delivery</h2>{emailTestStatus}<form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"test-email\"><button>Send test email to {H(account.Email)}</button></form>":string.Empty;
        var passkeyScript=$"<script>document.getElementById('register-passkey').addEventListener('click',async()=>{{const message=document.getElementById('passkey-register-message');try{{if(!window.PublicKeyCredential||!PublicKeyCredential.parseCreationOptionsFromJSON)throw new Error('This browser does not support passkeys.');const headers={{'Content-Type':'application/json','RequestVerificationToken':{JsonSerializer.Serialize(token)}}};const start=await fetch('/auth/passkeys/registration/options',{{method:'POST',headers,body:'{{}}'}});if(!start.ok)throw new Error('Could not begin passkey registration.');const data=await start.json();const credential=await navigator.credentials.create({{publicKey:PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(data.options_json))}});const finish=await fetch('/auth/passkeys/registration/complete',{{method:'POST',headers,body:JSON.stringify({{credential_json:JSON.stringify(credential.toJSON()),state:data.state,name:'Passkey'}})}});if(!finish.ok)throw new Error('Passkey registration failed.');location.reload();}}catch(error){{message.textContent=error.message;}}}});</script>";
        return Shell($"<h1>Account security</h1><p><a href=\"/\">Back to library</a></p><h2>Account</h2><p>{H(account?.Email??"Local-only account")}</p>{emailTestForm}<h2>Passkeys</h2><ul>{passkeyRows}</ul><button type=\"button\" id=\"register-passkey\">Add passkey or Windows Hello</button><p id=\"passkey-register-message\" class=\"supporting\"></p><h2>Sign-in providers</h2><ul>{links}</ul>{providerLinks}{administratorPin}<h2>Your sessions</h2><table><thead><tr><th>Device</th><th>Method</th><th>Last used</th><th>Expires</th><th></th></tr></thead><tbody>{rows}</tbody></table><h2>Recovery codes</h2><p class=\"supporting\">Generating a new set immediately invalidates every previous recovery code.</p><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"recovery-codes\"><label>Current password<input type=\"password\" name=\"currentPassword\" required></label><button>Generate new recovery codes</button></form><h2>Change password</h2><form method=\"post\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(token)}\"><input type=\"hidden\" name=\"action\" value=\"password\"><label>Current password<input type=\"password\" name=\"currentPassword\" required></label><label>New password<input type=\"password\" name=\"newPassword\" minlength=\"8\" required><small>Use at least 8 characters.</small></label><button>Change password</button></form>{passkeyScript}");
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
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <meta name="theme-color" content="#0B1020">
          <meta name="description" content="Sign in to your local Tuvima Library.">
          <meta name="mobile-web-app-capable" content="yes">
          <meta name="apple-mobile-web-app-capable" content="yes">
          <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
          <meta name="apple-mobile-web-app-title" content="Tuvima Library">
          <link rel="manifest" href="/manifest.webmanifest">
          <link rel="apple-touch-icon" sizes="192x192" href="/icons/tuvima-192.png">
          <title>Tuvima Library</title>
          <style>
            :root { color-scheme: dark; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100dvh; display: grid; place-items: center; padding: max(1.25rem, env(safe-area-inset-top)) max(1.25rem, env(safe-area-inset-right)) max(1.25rem, env(safe-area-inset-bottom)) max(1.25rem, env(safe-area-inset-left)); background: radial-gradient(circle at 15% 5%, #281849 0, #100b1c 38%, #08060d 75%); color: #f8f6ff; }
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
            summary { min-height: 3rem; display: flex; align-items: center; cursor: pointer; touch-action: manipulation; }
            table { width: 100%; border-collapse: collapse; }
            td, th { padding: .65rem; border-bottom: 1px solid #392b4c; text-align: left; }
            pre, .notice { padding: 1rem; border: 1px solid #44345c; border-radius: .7rem; background: #0d0914; color: #ece5f7; }
            pre { white-space: pre-wrap; }
            code { color: #d2bcff; font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
            @media (max-width: 40rem) { body { align-items: start; padding: max(.75rem, env(safe-area-inset-top)) max(.75rem, env(safe-area-inset-right)) max(.75rem, env(safe-area-inset-bottom)) max(.75rem, env(safe-area-inset-left)); } main { margin-top: .75rem; padding: 1.35rem; border-radius: 1rem; } }
          </style>
        </head>
        <body><main>{{body}}</main><script>if ('serviceWorker' in navigator) window.addEventListener('load', function () { navigator.serviceWorker.register('/service-worker.js', { scope: '/' }); });</script></body>
        </html>
        """;
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
