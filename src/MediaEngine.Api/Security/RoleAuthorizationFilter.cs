using MediaEngine.Domain;

namespace MediaEngine.Api.Security;

/// <summary>
/// Minimal API endpoint filter that restricts access based on the caller's role.
/// Roles come from the authenticated first-party session or a supported API key.
///
/// Usage on individual endpoints:
/// <code>
///   group.MapGet("/", handler).RequireAdmin();
///   group.MapGet("/", handler).RequireAdminOrStandardUser();
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

/// <summary>
/// Endpoint metadata recording which roles a route requires. Attached by every
/// <c>Require*</c> extension in <see cref="RoleFilterExtensions"/> so guardrail
/// tests and diagnostics can discover role requirements from endpoint metadata
/// instead of re-deriving them from the filter pipeline.
/// </summary>
public sealed record RoleRequirementMetadata(IReadOnlyList<string> Roles);

// ── Convenience extension methods ───────────────────────────────────────────

/// <summary>
/// Fluent extensions for applying role-based authorization to Minimal API endpoints.
/// </summary>
public static class RoleFilterExtensions
{
    /// <summary>Restricts the endpoint to Administrators only.</summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.RequireAuthorization(AuthPolicies.Administrator)
               .WithMetadata(new RoleRequirementMetadata([AppRoles.Administrator]));

    /// <summary>Restricts the endpoint to Administrators and Standard Users.</summary>
    public static RouteHandlerBuilder RequireAdminOrStandardUser(this RouteHandlerBuilder builder) =>
        builder.RequireAuthorization(AuthPolicies.StandardOrAdministrator)
               .WithMetadata(new RoleRequirementMetadata([AppRoles.Administrator, AppRoles.StandardUser]));

    /// <summary>Requires any authenticated first-party role.</summary>
    public static RouteHandlerBuilder RequireAnyRole(this RouteHandlerBuilder builder) =>
        builder.RequireAuthorization(AuthPolicies.Authenticated)
               .WithMetadata(new RoleRequirementMetadata(AppRoles.All));

    /// <summary>Restricts every endpoint in the group to Administrators only.</summary>
    public static RouteGroupBuilder RequireAdmin(this RouteGroupBuilder builder) =>
        builder.RequireAuthorization(AuthPolicies.Administrator)
               .WithMetadata(new RoleRequirementMetadata([AppRoles.Administrator]));

    /// <summary>Restricts every endpoint in the group to Administrators and Standard Users.</summary>
    public static RouteGroupBuilder RequireAdminOrStandardUser(this RouteGroupBuilder builder) =>
        builder.RequireAuthorization(AuthPolicies.StandardOrAdministrator)
               .WithMetadata(new RoleRequirementMetadata([AppRoles.Administrator, AppRoles.StandardUser]));

    /// <summary>Requires any authenticated first-party role for every endpoint in the group.</summary>
    public static RouteGroupBuilder RequireAnyRole(this RouteGroupBuilder builder) =>
        builder.RequireAuthorization(AuthPolicies.Authenticated)
               .WithMetadata(new RoleRequirementMetadata(AppRoles.All));
}
