using System.Security.Claims;

namespace MediaEngine.Api.Security;

public sealed record ClientScopeRequirementMetadata(string Scope);

public sealed class ClientScopeFilter(string scope) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.HasClaim(TuvimaClaimTypes.DashboardService, "true")
            || user.HasClaim(TuvimaClaimTypes.Scope, scope))
        {
            return await next(context);
        }

        context.HttpContext.Response.Headers.WWWAuthenticate =
            $"Bearer error=\"insufficient_scope\", scope=\"{scope}\"";
        return Results.Json(
            new { error = "insufficient_scope", error_description = $"The '{scope}' scope is required." },
            statusCode: StatusCodes.Status403Forbidden);
    }
}

public static class ClientScopeExtensions
{
    public static RouteHandlerBuilder RequireClientScope(this RouteHandlerBuilder builder, string scope) =>
        builder.RequireAuthorization(AuthPolicies.Authenticated)
            .AddEndpointFilter(new ClientScopeFilter(scope))
            .WithMetadata(new ClientScopeRequirementMetadata(scope));
}
