using System.Security.Claims;
using MediaEngine.Domain.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace MediaEngine.Api.Security;

public sealed class AdministratorElevationRequirement : IAuthorizationRequirement;

public sealed class AdministratorElevationHandler(IIdentityRepository identities,TimeProvider timeProvider) : AuthorizationHandler<AdministratorElevationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,AdministratorElevationRequirement requirement)
    {
        if(!context.User.IsInRole(MediaEngine.Domain.AppRoles.Administrator))return;
        if(!Guid.TryParse(context.User.FindFirstValue(TuvimaClaimTypes.SessionId),out var sessionId))
        {
            context.Succeed(requirement); // Host/API-key administration is already independently authenticated.
            return;
        }
        if(!Guid.TryParse(context.User.FindFirstValue(TuvimaClaimTypes.ActiveProfileId),out var profileId))return;
        if(await identities.GetElevationExpiryAsync(sessionId,profileId,timeProvider.GetUtcNow()).ConfigureAwait(false)is not null)context.Succeed(requirement);
    }
}
