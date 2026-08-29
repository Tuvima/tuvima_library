using System.Security.Claims;
using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Identity.Contracts;

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
                result = await identity.AuthenticatePasswordAsync(request.Username ?? string.Empty, request.Password ?? string.Empty,
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
            IProfileExternalLoginService externalLogins,
            CancellationToken ct) =>
        {
            try
            {
                var linked = await externalLogins.ResolveAsync(request.Provider, request.Subject, ct).ConfigureAwait(false);
                if (linked is null)
                {
                    return Results.Unauthorized();
                }

                await externalLogins.RecordLoginAsync(linked.Id, ct).ConfigureAwait(false);
                return Results.Ok(ToResponse(await identity.CreateExternalSessionAsync(
                    linked.ProfileId, request.Provider, request.DeviceId, request.DeviceName, request.Client, ct).ConfigureAwait(false)));
            }
            catch (KeyNotFoundException ex) { return ApiErrors.NotFound(ex.Message); }
        }).Produces<AuthSessionResponse>().RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPost("/session/validate", async (HttpRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var session = await identity.ValidateSessionAsync(request.Headers[TuvimaAuthDefaults.SessionHeader].ToString(), true, ct).ConfigureAwait(false);
            return session is null ? Results.Unauthorized() : Results.Ok(ToValidationResponse(session));
        }).Produces<SessionValidationResponse>().RequireAuthorization(AuthPolicies.DashboardService);

        group.MapGet("/sessions", async (ClaimsPrincipal user, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            var profileId = RequiredGuidClaim(user, TuvimaClaimTypes.ProfileId);
            var sessions = await identity.GetSessionsAsync(profileId, ct).ConfigureAwait(false);
            return Results.Ok(sessions.Select(session => new DeviceSessionResponse
            {
                Id = session.Id,
                ProfileId = session.ProfileId,
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
            var profileId = RequiredGuidClaim(user, TuvimaClaimTypes.ProfileId);
            var owned = (await identity.GetSessionsAsync(profileId, ct).ConfigureAwait(false)).Any(session => session.Id == sessionId);
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
                await identity.ChangePasswordAsync(RequiredGuidClaim(user, TuvimaClaimTypes.ProfileId), request.CurrentPassword,
                    request.NewPassword, RequiredGuidClaim(user, TuvimaClaimTypes.SessionId), ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).WithName("ChangePassword").Produces(StatusCodes.Status204NoContent).RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("/password/recover", async (RecoverPasswordRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try
            {
                var codes = await identity.ResetPasswordWithRecoveryCodeAsync(request.Username, request.RecoveryCode, request.NewPassword, ct).ConfigureAwait(false);
                return Results.Ok(new RecoveryCodesResponse(codes));
            }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        }).Produces<RecoveryCodesResponse>().RequireRateLimiting("authentication").RequireAuthorization(AuthPolicies.DashboardService);

        group.MapPut("/profiles/{profileId:guid}/pin", async (Guid profileId, SetProfilePinRequest request, IFirstPartyIdentityService identity, CancellationToken ct) =>
        {
            try { await identity.SetProfilePinAsync(profileId, request.Pin, ct).ConfigureAwait(false); return Results.NoContent(); }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (KeyNotFoundException ex) { return ApiErrors.NotFound(ex.Message); }
        }).WithName("SetProfilePin").Produces(StatusCodes.Status204NoContent).RequireAuthorization(AuthPolicies.Administrator);

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
            var profileId = RequiredGuidClaim(user, TuvimaClaimTypes.ProfileId);
            var created = tokens.Create(sessionId, profileId);
            return Results.Ok(new IntercomTokenResponse(created.Token, created.ExpiresAt));
        }).Produces<IntercomTokenResponse>().RequireRateLimiting("intercom").RequireAuthorization(AuthPolicies.Authenticated);

        return app;
    }

    private static AuthSessionResponse ToResponse(SessionIssueResult issued) => new()
    {
        SessionId = issued.Session.Id,
        SessionToken = issued.PlaintextToken,
        ProfileId = issued.Profile.Id,
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
        ProfileId = result.Profile.Id,
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
