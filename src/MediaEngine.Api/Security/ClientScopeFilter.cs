using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Security;

public sealed record ClientScopeRequirementMetadata(string Scope);

public sealed class ClientScopeFilter(string scope) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (await IsAllowedAsync(context.HttpContext))
        {
            return await next(context);
        }

        context.HttpContext.Response.Headers.WWWAuthenticate =
            $"Bearer error=\"insufficient_scope\", scope=\"{scope}\"";
        return Results.Json(
            new { error = "insufficient_scope", error_description = $"The '{scope}' scope is required." },
            statusCode: StatusCodes.Status403Forbidden);
    }

    internal async ValueTask<bool> IsAllowedAsync(HttpContext context)
    {
        var resolver = context.RequestServices.GetRequiredService<IRequestAuthorityResolver>();
        var authority = await resolver.ResolveAsync(context, context.RequestAborted);
        if (authority.PrincipalKind == PrincipalKind.Human)
        {
            return AuthorityValidity.ValidateHuman(authority) is null;
        }

        if (authority.PrincipalKind is not (PrincipalKind.DelegatedUserClient or PrincipalKind.ServiceApplication))
        {
            return false;
        }

        var evaluator = context.RequestServices.GetRequiredService<IAuthorizationEvaluator>();
        return (await evaluator.EvaluateAsync(
            authority,
            new AuthorizationRequirement(ApplicationPermission: new ApplicationPermissionId(scope)),
            resource: null,
            context.RequestAborted)).IsAllowed;
    }
}

public static class ClientScopeExtensions
{
    public static RouteHandlerBuilder RequireClientScope(this RouteHandlerBuilder builder, string scope) =>
        builder.RequireAuthorization(AuthPolicies.Authenticated)
            .AddEndpointFilter(new ClientScopeFilter(scope))
            .WithMetadata(new ClientScopeRequirementMetadata(scope));
}
