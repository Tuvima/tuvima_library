using MediaEngine.Domain;

namespace MediaEngine.Api.Security;

/// <summary>
/// Minimal API endpoint filter that restricts access based on the caller's role.
/// The role is set by <see cref="Middleware.ApiKeyMiddleware"/> in
/// <c>HttpContext.Items["ApiKeyRole"]</c>.
///
/// Usage on individual endpoints:
/// <code>
///   group.MapGet("/", handler).RequireAdmin();
///   group.MapGet("/", handler).RequireAdminOrCurator();
///   group.MapGet("/", handler).RequireAnyRole();
/// </code>
/// </summary>
public sealed class RoleAuthorizationFilter : IEndpointFilter
{
    private readonly string[] _allowedRoles;

    private RoleAuthorizationFilter(string[] allowedRoles) => _allowedRoles = allowedRoles;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var httpCtx = ctx.HttpContext;

        // If ApiKeyMiddleware did not set a role, the request is unauthenticated.
        if (!httpCtx.Items.TryGetValue("ApiKeyRole", out var roleObj) ||
            roleObj is not string role)
        {
            return Results.Json(
                new { error = "Authentication required." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!_allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { error = $"Access denied. Required role: {string.Join(" or ", _allowedRoles)}." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(ctx);
    }

    /// <summary>Creates a filter that requires the caller to have one of the specified roles.</summary>
    public static RoleAuthorizationFilter RequireRole(params string[] roles) => new(roles);
}

// ── Convenience extension methods ───────────────────────────────────────────

/// <summary>
/// Fluent extensions for applying role-based authorization to Minimal API endpoints.
/// </summary>
public static class RoleFilterExtensions
{
    /// <summary>Restricts the endpoint to Administrators only.</summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(RoleAuthorizationFilter.RequireRole(AppRoles.Administrator));

    /// <summary>Restricts the endpoint to Administrators and Curators.</summary>
    public static RouteHandlerBuilder RequireAdminOrCurator(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(RoleAuthorizationFilter.RequireRole(AppRoles.Administrator, AppRoles.Curator));

    /// <summary>Requires any authenticated role (Administrator, Curator, or Consumer).</summary>
    public static RouteHandlerBuilder RequireAnyRole(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(RoleAuthorizationFilter.RequireRole(AppRoles.All));
}
