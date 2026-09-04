using System.Security.Claims;
using System.Net.Mail;
using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Identity.Contracts;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace MediaEngine.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Authentication");

        group.MapGet("/bootstrap/status", async (IFirstPartyIdentityService identity, CancellationToken ct) =>
            Results.Ok(new AuthBootstrapStatusResponse(await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false))))
            .Produces<AuthBootstrapStatusResponse>()
            .RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/login", async (LocalLoginRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            AuthenticationAttemptResult result;
            if (request.ProfileId is { } profileId)
            {
                result = await identity.AuthenticatePinAsync(profileId, request.Pin ?? string.Empty,
                    request.DeviceId, request.DeviceName, request.Client, ct).ConfigureAwait(false);
            }
            else
            {
                result = await identity.AuthenticatePasswordAsync(request.Email ?? string.Empty, request.Password ?? string.Empty,
                    request.DeviceId, request.DeviceName, request.Client, ct).ConfigureAwait(false);
            }

            return result.Succeeded && result.IssuedSession is not null
                ? Results.Ok(ToResponse(result.IssuedSession))
                : ApiErrors.Problem(
                    StatusCodes.Status401Unauthorized,
                    "Authentication failed.",
                    result.LockedOut ? "The credential is temporarily locked." : result.Error ?? "Invalid credentials.");
        })
        .Produces<AuthSessionResponse>()
        .RequireRateLimiting("authentication")
        .RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/external-session", async (
            ExternalSessionRequest request,
            IFirstPartyIdentityService identity,
            IAccountExternalLoginService externalLogins,
            CancellationToken ct) =>
        {
            try
            {
                var linked = await externalLogins.ResolveAsync(request.Provider, request.Issuer, request.Subject, ct).ConfigureAwait(false);
                if (linked is null)
                {
                    return Results.Unauthorized();
                }

                await externalLogins.RecordLoginAsync(linked.Id, ct).ConfigureAwait(false);
                return Results.Ok(ToResponse(await identity.CreateExternalSessionAsync(
                    linked.AccountId, request.Provider, request.DeviceId, request.DeviceName, request.Client, ct).ConfigureAwait(false)));
            }
            catch (KeyNotFoundException ex) { return ApiErrors.NotFound(ex.Message); }
        }).Produces<AuthSessionResponse>().RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/invitations/accept",async(AcceptAccountInvitationRequest request,IFirstPartyIdentityService identity,CancellationToken ct)=>
        {
            try{return Results.Ok(ToResponse(await identity.AcceptInvitationAsync(request.Token,request.Password,request.DeviceId,request.DeviceName,"Tuvima Dashboard",ct).ConfigureAwait(false)));}
            catch(ArgumentException ex){return ApiErrors.BadRequest(ex.Message);}catch(UnauthorizedAccessException){return Results.Unauthorized();}
        }).Produces<AuthSessionResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/session/validate", async (HttpRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var session = await identity.ValidateSessionAsync(request.Headers[TuvimaAuthDefaults.SessionHeader].ToString(), true, ct).ConfigureAwait(false);
            return session is null ? Results.Unauthorized() : Results.Ok(ToValidationResponse(session));
        }).Produces<SessionValidationResponse>().RequireAuthorization(AuthPolicies.DashboardService);

        group.MapGet("/sessions", async (ClaimsPrincipal user, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var accountId = RequiredGuidClaim(user, TuvimaClaimTypes.AccountId);
            var sessions = await identity.GetSessionsAsync(accountId, ct).ConfigureAwait(false);
            return Results.Ok(sessions.Select(session => new DeviceSessionResponse
            {
                Id = session.Id,
                AccountId = session.AccountId,
                ActiveProfileId = session.ActiveProfileId,
                DeviceId = session.DeviceId,
                DeviceName = session.DeviceName,
                Client = session.Client,
                AuthenticationMethod = session.AuthenticationMethod,
                CreatedAt = session.CreatedAt,
                LastSeenAt = session.LastSeenAt,
                ExpiresAt = session.ExpiresAt,
                RevokedAt = session.RevokedAt,
            }).ToList());
        }).Produces<IReadOnlyList<DeviceSessionResponse>>().RequireAuthorization(AuthPolicies.Authenticated);

        group.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal user, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var accountId = RequiredGuidClaim(user, TuvimaClaimTypes.AccountId);
            var owned = (await identity.GetSessionsAsync(accountId, ct).ConfigureAwait(false)).Any(session => session.Id == sessionId);
            if (!owned && !user.IsInRole(MediaEngine.Domain.AppRoles.Administrator))
            {
                return Results.Forbid();
            }

            return await identity.RevokeSessionAsync(sessionId, "user_revoked", ct).ConfigureAwait(false) ? Results.NoContent() : ApiErrors.NotFound("Session not found.");
        }).WithName("RevokeAuthSession").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/password/change", async (ChangePasswordRequest request, ClaimsPrincipal user, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try
            {
                await identity.ChangePasswordAsync(RequiredGuidClaim(user, TuvimaClaimTypes.AccountId), request.CurrentPassword,
                    request.NewPassword, RequiredGuidClaim(user, TuvimaClaimTypes.SessionId), ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).WithName("ChangePassword").Produces(StatusCodes.Status204NoContent).RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/password/recovery-codes", async (RegenerateRecoveryCodesRequest request, ClaimsPrincipal user, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try
            {
                var codes = await identity.RegenerateRecoveryCodesAsync(RequiredGuidClaim(user, TuvimaClaimTypes.AccountId), request.CurrentPassword, ct).ConfigureAwait(false);
                return Results.Ok(new RecoveryCodesResponse(codes));
            }
            catch (InvalidOperationException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).Produces<RecoveryCodesResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/password/recover", async (RecoverPasswordRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try
            {
                var codes = await identity.ResetPasswordWithRecoveryCodeAsync(request.Email, request.RecoveryCode, request.NewPassword, ct).ConfigureAwait(false);
                return Results.Ok(new RecoveryCodesResponse(codes));
            }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).Produces<RecoveryCodesResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/password/reset/begin", async (BeginPasswordResetRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var token = await identity.BeginPasswordResetAsync(request.Email, ct).ConfigureAwait(false);
            return Results.Accepted(value: new BeginPasswordResetResponse(token));
        }).Produces<BeginPasswordResetResponse>(StatusCodes.Status202Accepted).RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/password/reset/complete", async (ResetPasswordTokenRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try { await identity.ResetPasswordWithTokenAsync(request.Token, request.NewPassword, ct).ConfigureAwait(false); return Results.NoContent(); }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).WithName("CompletePasswordReset").Produces(StatusCodes.Status204NoContent).RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/passkeys/login/options", async (BeginPasskeyLoginRequest request,HttpContext context,IAccountRepository accounts,IPasskeyHandler<Account> passkeys,CancellationToken ct) =>
        {
            Account? account=null;
            if(!string.IsNullOrWhiteSpace(request.Email))
            {
                try{account=await accounts.GetByNormalizedEmailAsync(new MailAddress(request.Email.Trim()).Address.ToUpperInvariant(),ct).ConfigureAwait(false);}catch(FormatException){ }
            }
            var result=await passkeys.MakeRequestOptionsAsync(account!,context).ConfigureAwait(false);
            return Results.Ok(new PasskeyOptionsResponse(result.RequestOptionsJson,result.AssertionState??string.Empty));
        }).Produces<PasskeyOptionsResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/passkeys/login/complete",async(CompletePasskeyLoginRequest request,HttpContext context,IPasskeyHandler<Account> passkeys,UserManager<Account> users,IFirstPartyIdentityService identity,CancellationToken ct)=>
        {
            var result=await passkeys.PerformAssertionAsync(new PasskeyAssertionContext{HttpContext=context,CredentialJson=request.CredentialJson,AssertionState=request.State}).ConfigureAwait(false);
            if(!result.Succeeded||result.User is null||result.Passkey is null)return Results.Unauthorized();
            await users.AddOrUpdatePasskeyAsync(result.User,result.Passkey).ConfigureAwait(false);
            return Results.Ok(ToResponse(await identity.CreatePasskeySessionAsync(result.User.Id,request.DeviceId,request.DeviceName,"Tuvima Dashboard",ct).ConfigureAwait(false)));
        }).Produces<AuthSessionResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/passkeys/registration/options",async(ClaimsPrincipal user,HttpContext context,IAccountRepository accounts,IPasskeyHandler<Account> passkeys,CancellationToken ct)=>
        {
            var account=await accounts.GetByIdAsync(RequiredGuidClaim(user,TuvimaClaimTypes.AccountId),ct).ConfigureAwait(false)??throw new UnauthorizedAccessException();
            var entity=new PasskeyUserEntity{Id=account.Id.ToString("D"),Name=account.Email??account.Id.ToString("D"),DisplayName=account.Email??"Tuvima account"};
            var result=await passkeys.MakeCreationOptionsAsync(entity,context).ConfigureAwait(false);
            return Results.Ok(new PasskeyOptionsResponse(result.CreationOptionsJson,result.AttestationState??string.Empty));
        }).Produces<PasskeyOptionsResponse>().RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/passkeys/registration/complete",async(CompletePasskeyRegistrationRequest request,ClaimsPrincipal user,HttpContext context,IAccountRepository accounts,IPasskeyHandler<Account> passkeys,UserManager<Account> users,CancellationToken ct)=>
        {
            var account=await accounts.GetByIdAsync(RequiredGuidClaim(user,TuvimaClaimTypes.AccountId),ct).ConfigureAwait(false)??throw new UnauthorizedAccessException();
            var result=await passkeys.PerformAttestationAsync(new PasskeyAttestationContext{HttpContext=context,CredentialJson=request.CredentialJson,AttestationState=request.State}).ConfigureAwait(false);
            if(!result.Succeeded||result.Passkey is null||result.UserEntity?.Id!=account.Id.ToString("D"))return ApiErrors.BadRequest("Passkey registration could not be verified.");
            result.Passkey.Name=string.IsNullOrWhiteSpace(request.Name)?"Passkey":request.Name.Trim()[..Math.Min(request.Name.Trim().Length,100)];
            var saved=await users.AddOrUpdatePasskeyAsync(account,result.Passkey).ConfigureAwait(false);
            return saved.Succeeded?Results.NoContent():ApiErrors.BadRequest("Passkey registration could not be saved.");
        }).WithName("RegisterPasskey").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.Authenticated);

        group.MapGet("/passkeys",async(ClaimsPrincipal user,IAccountRepository accounts,UserManager<Account> users,CancellationToken ct)=>
        {
            var account=await accounts.GetByIdAsync(RequiredGuidClaim(user,TuvimaClaimTypes.AccountId),ct).ConfigureAwait(false)??throw new UnauthorizedAccessException();
            return Results.Ok((await users.GetPasskeysAsync(account).ConfigureAwait(false)).Select(p=>new PasskeyCredentialResponse(Convert.ToBase64String(p.CredentialId),p.Name??"Passkey",p.CreatedAt,p.IsBackedUp)).ToList());
        }).Produces<List<PasskeyCredentialResponse>>().RequireAuthorization(AuthPolicies.Authenticated);

        group.MapDelete("/passkeys/{credentialId}",async(string credentialId,ClaimsPrincipal user,IAccountRepository accounts,IIdentityRepository identities,IAccountExternalLoginService externalLogins,UserManager<Account> users,CancellationToken ct)=>
        {
            byte[] id;try{id=Convert.FromBase64String(credentialId);}catch(FormatException){return ApiErrors.BadRequest("Credential id is invalid.");}
            var accountId=RequiredGuidClaim(user,TuvimaClaimTypes.AccountId);var account=await accounts.GetByIdAsync(accountId,ct).ConfigureAwait(false)??throw new UnauthorizedAccessException();
            var all=await users.GetPasskeysAsync(account).ConfigureAwait(false);if(!all.Any(p=>p.CredentialId.SequenceEqual(id)))return ApiErrors.NotFound("Passkey not found.");
            if(all.Count==1&&await identities.GetAccountCredentialAsync(accountId,AccountCredentialKind.Password,ct).ConfigureAwait(false)is null&&(await externalLogins.GetByAccountAsync(accountId,ct).ConfigureAwait(false)).Count==0)return ApiErrors.Conflict("Add another sign-in method before removing this passkey.");
            var removed=await users.RemovePasskeyAsync(account,id).ConfigureAwait(false);return removed.Succeeded?Results.NoContent():ApiErrors.BadRequest("Passkey could not be removed.");
        }).WithName("DeletePasskey").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/passkeys/elevation/options",async(ClaimsPrincipal user,HttpContext context,IAccountRepository accounts,IPasskeyHandler<Account> passkeys,CancellationToken ct)=>
        {
            var account=await accounts.GetByIdAsync(RequiredGuidClaim(user,TuvimaClaimTypes.AccountId),ct).ConfigureAwait(false)??throw new UnauthorizedAccessException();
            var result=await passkeys.MakeRequestOptionsAsync(account,context).ConfigureAwait(false);return Results.Ok(new PasskeyOptionsResponse(result.RequestOptionsJson,result.AssertionState??string.Empty));
        }).Produces<PasskeyOptionsResponse>().RequireAuthorization(AuthPolicies.AdministratorRole);

        group.MapPost("/passkeys/elevation/complete",async(CompletePasskeyElevationRequest request,ClaimsPrincipal user,HttpContext context,IPasskeyHandler<Account> passkeys,UserManager<Account> users,IFirstPartyIdentityService identity,CancellationToken ct)=>
        {
            var accountId=RequiredGuidClaim(user,TuvimaClaimTypes.AccountId);var result=await passkeys.PerformAssertionAsync(new PasskeyAssertionContext{HttpContext=context,CredentialJson=request.CredentialJson,AssertionState=request.State}).ConfigureAwait(false);
            if(!result.Succeeded||result.User?.Id!=accountId||result.Passkey is null)return Results.Unauthorized();await users.AddOrUpdatePasskeyAsync(result.User,result.Passkey).ConfigureAwait(false);
            var elevated=await identity.ElevateAdministratorWithPasskeyAsync(context.Request.Headers[TuvimaAuthDefaults.SessionHeader].ToString(),ct).ConfigureAwait(false);return elevated.Succeeded?Results.Ok(new AdministratorElevationResponse(true,elevated.ExpiresAt)):Results.Unauthorized();
        }).Produces<AdministratorElevationResponse>().RequireAuthorization(AuthPolicies.AdministratorRole);

        group.MapPut("/profiles/{profileId:guid}/pin", async (Guid profileId, SetProfilePinRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try { await identity.SetProfilePinAsync(profileId, request.Pin, ct).ConfigureAwait(false); return Results.NoContent(); }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (KeyNotFoundException ex) { return ApiErrors.NotFound(ex.Message); }
        }).WithName("SetProfilePin").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.Administrator);

        group.MapPut("/profiles/{profileId:guid}/administrator-pin", async (Guid profileId, SetProfilePinRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try { await identity.SetAdministratorPinAsync(profileId, request.Pin, ct).ConfigureAwait(false); return Results.NoContent(); }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (KeyNotFoundException ex) { return ApiErrors.NotFound(ex.Message); }
        }).WithName("SetAdministratorPin").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.Administrator);

        group.MapPost("/elevation", async (ElevateAdministratorRequest request, HttpRequest httpRequest, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var result = await identity.ElevateAdministratorAsync(httpRequest.Headers[TuvimaAuthDefaults.SessionHeader].ToString(), request.Secret, ct).ConfigureAwait(false);
            return result.Succeeded ? Results.Ok(new AdministratorElevationResponse(true, result.ExpiresAt)) : Results.Json(new AdministratorElevationResponse(false, null, result.Error), statusCode: StatusCodes.Status401Unauthorized);
        }).Produces<AdministratorElevationResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.AdministratorRole);

        group.MapGet("/elevation", async (HttpRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var expires = await identity.GetAdministratorElevationAsync(request.Headers[TuvimaAuthDefaults.SessionHeader].ToString(), ct).ConfigureAwait(false);
            return Results.Ok(new AdministratorElevationResponse(expires is not null, expires));
        }).Produces<AdministratorElevationResponse>().RequireAuthorization(AuthPolicies.AdministratorRole);

        group.MapDelete("/elevation", async (HttpRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            await identity.ClearAdministratorElevationAsync(request.Headers[TuvimaAuthDefaults.SessionHeader].ToString(), ct).ConfigureAwait(false);
            return Results.NoContent();
        }).WithName("ClearAdministratorElevation").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.AdministratorRole);

        group.MapPost("/session/switch-profile", async (SwitchProfileRequest request, HttpRequest httpRequest, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try
            {
                var result = await identity.SwitchActiveProfileAsync(
                    httpRequest.Headers[TuvimaAuthDefaults.SessionHeader].ToString(), request.ProfileId, request.Secret, ct).ConfigureAwait(false);
                return Results.Ok(ToValidationResponse(result));
            }
            catch (KeyNotFoundException ex) { return ApiErrors.NotFound(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).Produces<SessionValidationResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/intercom-token", (ClaimsPrincipal user, IntercomTokenService tokens) =>
        {
            var sessionId = RequiredGuidClaim(user, TuvimaClaimTypes.SessionId);
            var accountId = RequiredGuidClaim(user, TuvimaClaimTypes.AccountId);
            var created = tokens.Create(sessionId, accountId);
            return Results.Ok(new IntercomTokenResponse(created.Token, created.ExpiresAt));
        }).Produces<IntercomTokenResponse>().RequireRateLimiting("intercom").RequireAuthorization(AuthPolicies.Authenticated);

        return app;
    }

    private static AuthSessionResponse ToResponse(SessionIssueResult issued) => new()
    {
        SessionId = issued.Session.Id,
        SessionToken = issued.PlaintextToken,
        AccountId = issued.Account.Id,
        ActiveProfileId = issued.ActiveProfile.Id,
        DisplayName = issued.ActiveProfile.DisplayName,
        Role = issued.ActiveProfile.Role.ToString(),
        AuthenticationMethod = issued.Session.AuthenticationMethod,
        ExpiresAt = issued.Session.ExpiresAt,
        RecoveryCodes = issued.RecoveryCodes,
    };

    private static SessionValidationResponse ToValidationResponse(SessionValidationResult result) => new()
    {
        SessionId = result.Session.Id,
        AccountId = result.Account.Id,
        ActiveProfileId = result.ActiveProfile.Id,
        DisplayName = result.ActiveProfile.DisplayName,
        Role = result.ActiveProfile.Role.ToString(),
        AuthenticationMethod = result.Session.AuthenticationMethod,
        ExpiresAt = result.Session.ExpiresAt,
    };

    private static Guid RequiredGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value)
            ? value
            : throw new UnauthorizedAccessException($"Required claim '{type}' is missing.");
}
