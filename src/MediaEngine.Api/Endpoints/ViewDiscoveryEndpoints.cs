using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.View;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Contracts.Paging;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Endpoints;

public static class ViewDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapViewDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/view").WithTags("View");

        group.MapGet("/places", async (
            string? scope,
            Guid? scopeProfileId,
            string? q,
            string? cursor,
            int? limit,
            ViewDiscoveryService service,
            CancellationToken ct) =>
        {
            try
            {
                var page = PagedRequest.From(null, limit, defaultLimit: 50, maxLimit: 100);
                var request = new ViewDiscoveryRequest(
                    ParseScope(scope, scopeProfileId),
                    page.Limit,
                    q,
                    cursor);
                return ToResult(await service.GetPlacesAsync(request, ct).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewPlaces")
        .WithSummary("List real location groups from the caller's authorized View scope.")
        .Produces<ViewPlacesPageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/people", async (
            string? scope,
            Guid? scopeProfileId,
            string? q,
            string? cursor,
            int? limit,
            ViewDiscoveryService service,
            CancellationToken ct) =>
        {
            try
            {
                var page = PagedRequest.From(null, limit, defaultLimit: 100, maxLimit: 100);
                var request = new ViewDiscoveryRequest(
                    ParseScope(scope, scopeProfileId),
                    page.Limit,
                    q,
                    cursor);
                return ToResult(await service.GetPeopleAsync(request, ct).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewPeople")
        .WithSummary("List provenance-aware named or reviewed people from the caller's authorized View scope.")
        .Produces<ViewPeoplePageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        return app;
    }

    internal static ViewScopeRequest ParseScope(string? value, Guid? profileId)
    {
        var kind = string.IsNullOrWhiteSpace(value) ? ViewScopeKind.Shared : value.Trim().ToLowerInvariant() switch
        {
            "shared" => ViewScopeKind.Shared,
            "mine" => ViewScopeKind.Mine,
            "profile" => ViewScopeKind.Profile,
            _ => throw new ArgumentException("View scope must be shared, mine, or profile.", nameof(value)),
        };
        if (kind == ViewScopeKind.Profile && profileId is null)
            throw new ArgumentException("Profile scope requires scopeProfileId.", nameof(profileId));
        if (kind != ViewScopeKind.Profile && profileId is not null)
            throw new ArgumentException("scopeProfileId is valid only for profile scope.", nameof(profileId));
        return kind == ViewScopeKind.Profile
            ? ViewScopeRequest.ForProfile(profileId!.Value)
            : kind == ViewScopeKind.Mine ? ViewScopeRequest.Mine : ViewScopeRequest.Shared;
    }

    private static IResult ToResult(ViewPlacesResult result) => result.Outcome switch
    {
        ViewAccessOutcome.Allowed when result.Page is not null => Results.Ok(result.Page),
        ViewAccessOutcome.Unauthenticated => Results.Unauthorized(),
        _ => ApiErrors.NotFound("The requested View scope was not found."),
    };

    private static IResult ToResult(ViewPeopleResult result) => result.Outcome switch
    {
        ViewAccessOutcome.Allowed when result.Page is not null => Results.Ok(result.Page),
        ViewAccessOutcome.Unauthenticated => Results.Unauthorized(),
        _ => ApiErrors.NotFound("The requested View scope was not found."),
    };
}
